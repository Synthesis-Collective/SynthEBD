using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using Pfim;

namespace SynthEBD;

/// <summary>
/// Manages OpenGL texture loading from game assets via Pfim DDS decoding.
/// Includes caching, face tint blending, and hair tint application.
/// </summary>
public class GlTextureManager : IDisposable
{
    private readonly GameAssetResolver _assetResolver;
    private readonly Logger _logger;
    private readonly Dictionary<string, int> _textureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<int> _allTextures = new();

    /// <summary>A 1x1 white texture used as a fallback when no texture is available.</summary>
    public int WhiteTexture { get; private set; }

    public GlTextureManager(GameAssetResolver assetResolver, Logger logger)
    {
        _assetResolver = assetResolver;
        _logger = logger;
    }

    /// <summary>
    /// Creates the fallback white texture. Must be called after GL context is ready.
    /// </summary>
    public void Initialize()
    {
        WhiteTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, WhiteTexture);
        byte[] white = { 255, 255, 255, 255 };
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
            1, 1, 0, PixelFormat.Bgra, PixelType.UnsignedByte, white);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _allTextures.Add(WhiteTexture);
    }

    /// <summary>
    /// Loads a DDS texture from a game-relative path. Returns the GL texture handle,
    /// or <see cref="WhiteTexture"/> if the texture can't be loaded.
    /// Results are cached by path.
    /// </summary>
    public int LoadTexture(string relativeGamePath)
    {
        if (string.IsNullOrWhiteSpace(relativeGamePath))
            return WhiteTexture;

        if (_textureCache.TryGetValue(relativeGamePath, out int cached))
            return cached;

        var pixels = LoadDdsPixels(relativeGamePath, out int width, out int height);
        if (pixels == null)
            return WhiteTexture;

        int handle = UploadTexture(pixels, width, height);
        _textureCache[relativeGamePath] = handle;
        return handle;
    }

    /// <summary>
    /// Loads a diffuse texture with face tint blended on the CPU.
    /// Not cached — caller should cache the result if needed.
    /// </summary>
    public int LoadTextureWithFaceTint(string diffusePath, string faceTintPath)
    {
        var diffusePixels = LoadDdsPixels(diffusePath, out int dw, out int dh);
        if (diffusePixels == null) return WhiteTexture;

        var tintPixels = LoadDdsPixels(faceTintPath, out int tw, out int th);
        if (tintPixels == null)
        {
            _logger.LogMessage("GlTextures: Face tint not found '" + faceTintPath + "', using unblended diffuse");
            return UploadTexture(diffusePixels, dw, dh);
        }

        if (dw != tw || dh != th)
        {
            tintPixels = BilinearResample(tintPixels, tw, th, dw, dh);
        }

        // Blend: finalColor = mix(base, base * tint.rgb, tint.a)
        for (int i = 0; i < diffusePixels.Length; i += 4)
        {
            byte baseB = diffusePixels[i], baseG = diffusePixels[i + 1], baseR = diffusePixels[i + 2];
            byte tintB = tintPixels[i], tintG = tintPixels[i + 1], tintR = tintPixels[i + 2];
            byte tintA = tintPixels[i + 3];
            if (tintA == 0) continue;

            float a = tintA / 255f;
            float oneMinusA = 1f - a;
            diffusePixels[i]     = (byte)Math.Clamp((int)(baseB * oneMinusA + baseB * tintB / 255f * a), 0, 255);
            diffusePixels[i + 1] = (byte)Math.Clamp((int)(baseG * oneMinusA + baseG * tintG / 255f * a), 0, 255);
            diffusePixels[i + 2] = (byte)Math.Clamp((int)(baseR * oneMinusA + baseR * tintR / 255f * a), 0, 255);
        }

        _logger.LogMessage("GlTextures: Face tint blended (" + dw + "x" + dh + ")");
        return UploadTexture(diffusePixels, dw, dh);
    }

    /// <summary>
    /// Loads a greyscale hair diffuse and applies tint color on the CPU.
    /// Not cached — caller should cache the result if needed.
    /// </summary>
    public int LoadTextureWithHairTint(string diffusePath, float tintR, float tintG, float tintB)
    {
        var pixels = LoadDdsPixels(diffusePath, out int width, out int height);
        if (pixels == null) return WhiteTexture;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            float intensity = pixels[i + 2] / 255f; // red channel = greyscale intensity (BGRA)
            pixels[i]     = (byte)Math.Clamp((int)(intensity * tintB * 255f), 0, 255);
            pixels[i + 1] = (byte)Math.Clamp((int)(intensity * tintG * 255f), 0, 255);
            pixels[i + 2] = (byte)Math.Clamp((int)(intensity * tintR * 255f), 0, 255);
        }

        return UploadTexture(pixels, width, height);
    }

    /// <summary>
    /// Loads an environment map texture (spherical 2D mapping).
    /// Skyrim environment maps are DDS files loaded as standard 2D textures;
    /// the shader converts reflection vectors to spherical UV coordinates.
    /// Returns 0 if the texture can't be loaded.
    /// </summary>
    public int LoadCubemap(string relativeGamePath)
    {
        if (string.IsNullOrWhiteSpace(relativeGamePath))
            return 0;

        if (_textureCache.TryGetValue("env:" + relativeGamePath, out int cached))
            return cached;

        var pixels = LoadDdsPixels(relativeGamePath, out int width, out int height);
        if (pixels == null)
        {
            _logger.LogMessage("GlTextures: Env map not found '" + relativeGamePath + "'");
            return 0;
        }

        int handle = UploadTexture(pixels, width, height);
        _textureCache["env:" + relativeGamePath] = handle;
        return handle;
    }

    /// <summary>
    /// Uploads BGRA pixel data to a new OpenGL texture with mipmaps.
    /// </summary>
    private int UploadTexture(byte[] pixelData, int width, int height)
    {
        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, handle);

        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
            width, height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, pixelData);

        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
            (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.Repeat);

        // Anisotropic filtering if available
        float maxAniso = GL.GetFloat((GetPName)0x84FF); // GL_MAX_TEXTURE_MAX_ANISOTROPY
        if (maxAniso > 1f)
            GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)0x84FE,
                Math.Min(maxAniso, 8f));

        _allTextures.Add(handle);
        return handle;
    }

    /// <summary>
    /// Decodes a DDS texture via Pfim and returns raw BGRA pixel data.
    /// </summary>
    private byte[]? LoadDdsPixels(string relativeGamePath, out int width, out int height)
    {
        width = 0;
        height = 0;

        string? resolved = _assetResolver.ResolveAssetPath(relativeGamePath);
        if (resolved == null || !File.Exists(resolved)) return null;

        try
        {
            using var image = Pfimage.FromFile(resolved);
            width = image.Width;
            height = image.Height;

            int rowBytes = width * 4;
            int expectedSize = height * rowBytes;

            switch (image.Format)
            {
                case Pfim.ImageFormat.Rgba32:
                {
                    byte[] pixelData = new byte[expectedSize];
                    if (image.Stride == rowBytes)
                        System.Buffer.BlockCopy(image.Data, 0, pixelData, 0, expectedSize);
                    else
                        for (int y = 0; y < height; y++)
                            System.Buffer.BlockCopy(image.Data, y * image.Stride, pixelData, y * rowBytes, rowBytes);
                    return pixelData;
                }
                case Pfim.ImageFormat.Rgb24:
                {
                    byte[] pixelData = new byte[expectedSize];
                    int srcStride = image.Stride;
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = y * srcStride + x * 3;
                            int dstIdx = (y * width + x) * 4;
                            pixelData[dstIdx] = image.Data[srcIdx];
                            pixelData[dstIdx + 1] = image.Data[srcIdx + 1];
                            pixelData[dstIdx + 2] = image.Data[srcIdx + 2];
                            pixelData[dstIdx + 3] = 255;
                        }
                    return pixelData;
                }
                default:
                    _logger.LogMessage("GlTextures: Unsupported format " + image.Format + " for '" + relativeGamePath + "'");
                    return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogMessage("GlTextures: Failed to decode '" + relativeGamePath + "': " + ex.Message);
            return null;
        }
    }

    private static byte[] BilinearResample(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        byte[] dst = new byte[dstW * dstH * 4];
        float xRatio = (float)srcW / dstW;
        float yRatio = (float)srcH / dstH;

        for (int dy = 0; dy < dstH; dy++)
        {
            float srcY = dy * yRatio;
            int y0 = (int)srcY;
            int y1 = Math.Min(y0 + 1, srcH - 1);
            float fy = srcY - y0;

            for (int dx = 0; dx < dstW; dx++)
            {
                float srcX = dx * xRatio;
                int x0 = (int)srcX;
                int x1 = Math.Min(x0 + 1, srcW - 1);
                float fx = srcX - x0;

                int i00 = (y0 * srcW + x0) * 4;
                int i10 = (y0 * srcW + x1) * 4;
                int i01 = (y1 * srcW + x0) * 4;
                int i11 = (y1 * srcW + x1) * 4;
                int iDst = (dy * dstW + dx) * 4;

                float w00 = (1f - fx) * (1f - fy);
                float w10 = fx * (1f - fy);
                float w01 = (1f - fx) * fy;
                float w11 = fx * fy;

                for (int c = 0; c < 4; c++)
                    dst[iDst + c] = (byte)(src[i00 + c] * w00 + src[i10 + c] * w10 +
                                            src[i01 + c] * w01 + src[i11 + c] * w11 + 0.5f);
            }
        }
        return dst;
    }

    /// <summary>
    /// Deletes all cached textures and clears the cache.
    /// </summary>
    public void ClearCache()
    {
        foreach (int tex in _allTextures)
        {
            if (tex != WhiteTexture)
                GL.DeleteTexture(tex);
        }
        _textureCache.Clear();
        _allTextures.RemoveAll(t => t != WhiteTexture);
    }

    public void Dispose()
    {
        foreach (int tex in _allTextures)
            GL.DeleteTexture(tex);
        _allTextures.Clear();
        _textureCache.Clear();
    }
}
