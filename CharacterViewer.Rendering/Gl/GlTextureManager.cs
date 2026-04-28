using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;

namespace CharacterViewer.Rendering;

/// <summary>
/// Manages OpenGL texture loading from game assets. DDS decoding and asset-path
/// resolution are delegated to <see cref="CharacterPreviewCache"/> so the pixel
/// payload is shared across viewer instances; this class is responsible for
/// per-context GL texture handles, face/hair tint blending, and caching of
/// uploaded handles within a single viewer's GL context.
/// </summary>
public class GlTextureManager : IDisposable
{
    private readonly CharacterPreviewCache _previewCache;
    private readonly ICharacterViewerLogger _logger;
    private readonly Dictionary<string, int> _textureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<int> _allTextures = new();

    // Game-paths that LoadTexture was asked for but couldn't decode (resolver
    // returned no on-disk file, or the DDS load itself failed). Cleared by
    // the host (VM_CharacterViewer) at the start of each scene load and
    // surfaced after load via VM_CharacterViewer.MissingTexturePaths so
    // hosts can flag the affected shapes (rendered as wireframe instead of
    // a flat-white billboard) and list the unresolved texture paths in a
    // tooltip. Path comparison is case-insensitive (Skyrim assets routinely
    // mix case in the same NIF).
    private readonly HashSet<string> _missingTexturePaths = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> MissingTexturePaths => _missingTexturePaths;

    /// <summary>Resets the per-load missing-texture diagnostics.
    /// Called by <see cref="VM_CharacterViewer.LoadAsync"/> alongside
    /// <see cref="VM_CharacterViewer.MissingMeshPaths"/>.</summary>
    public void ClearMissingTexturePaths() => _missingTexturePaths.Clear();

    /// <summary>A 1x1 white texture used as a fallback when no texture is available.</summary>
    public int WhiteTexture { get; private set; }

    public GlTextureManager(CharacterPreviewCache previewCache, ICharacterViewerLogger logger)
    {
        _previewCache = previewCache;
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
    /// Results are cached by path. The upload does not mutate the decoded pixel
    /// array, so handing the shared buffer directly to GL is safe.
    /// </summary>
    public int LoadTexture(string relativeGamePath)
    {
        if (string.IsNullOrWhiteSpace(relativeGamePath))
            return WhiteTexture;

        if (_textureCache.TryGetValue(relativeGamePath, out int cached))
            return cached;

        var pixels = _previewCache.GetOrLoadDdsPixels(relativeGamePath);
        if (pixels == null)
        {
            // Track for the post-load missing-texture overlay. Only counts
            // when the host actually asked for a path — empty/null paths
            // (above) are normal "shape doesn't use this slot" cases.
            _missingTexturePaths.Add(relativeGamePath);
            return WhiteTexture;
        }

        int handle = UploadTexture(pixels.Value.Data, pixels.Value.Width, pixels.Value.Height);
        _textureCache[relativeGamePath] = handle;
        return handle;
    }

    /// <summary>
    /// Loads a diffuse texture with face tint blended on the CPU.
    /// Not cached — caller should cache the result if needed.
    /// </summary>
    public int LoadTextureWithFaceTint(string diffusePath, string faceTintPath)
    {
        var diffuseSource = _previewCache.GetOrLoadDdsPixels(diffusePath);
        if (diffuseSource == null) return WhiteTexture;

        int dw = diffuseSource.Value.Width;
        int dh = diffuseSource.Value.Height;
        // CLONE — the cached array is shared across viewers and this method mutates
        // it in place during the blend. Skipping the clone corrupts subsequent loads.
        byte[] diffusePixels = (byte[])diffuseSource.Value.Data.Clone();

        var tintSource = _previewCache.GetOrLoadDdsPixels(faceTintPath);
        if (tintSource == null)
        {
            _logger.LogMessage("GlTextures: Face tint not found '" + faceTintPath + "', using unblended diffuse");
            return UploadTexture(diffusePixels, dw, dh);
        }

        int tw = tintSource.Value.Width;
        int th = tintSource.Value.Height;
        byte[] tintPixels = tintSource.Value.Data;

        if (dw != tw || dh != th)
        {
            // BilinearResample always allocates a fresh array, so the resampled buffer
            // is already owner-exclusive. The read-only-from-cache invariant is
            // preserved because the shared tintPixels is only read, never written.
            tintPixels = BilinearResample(tintPixels, tw, th, dw, dh);
        }

        // Blend: finalColor = mix(base, base * tint.rgb, tint.a). Writes only go to
        // diffusePixels (the clone above); tintPixels is read-only here.
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
        var source = _previewCache.GetOrLoadDdsPixels(diffusePath);
        if (source == null) return WhiteTexture;

        int width = source.Value.Width;
        int height = source.Value.Height;
        // CLONE — see LoadTextureWithFaceTint: the cached buffer is shared, and we
        // mutate per-pixel below.
        byte[] pixels = (byte[])source.Value.Data.Clone();

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

        var pixels = _previewCache.GetOrLoadDdsPixels(relativeGamePath);
        if (pixels == null)
        {
            _logger.LogMessage("GlTextures: Env map not found '" + relativeGamePath + "'");
            return 0;
        }

        int handle = UploadTexture(pixels.Value.Data, pixels.Value.Width, pixels.Value.Height);
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

    /// <summary>
    /// Drops all texture-handle references without issuing any GL calls. Mirrors
    /// <see cref="GlRenderer.ForgetResourcesFromDeadContext"/>: when the owning UC is
    /// recreated during navigation, the GL context that minted these texture IDs is
    /// destroyed, so <see cref="GL.DeleteTexture(int)"/> on them in the new context is
    /// invalid. The textures themselves are reclaimed with the old context; next
    /// <see cref="Initialize"/> re-uploads into the new context.
    /// </summary>
    public void ForgetResourcesFromDeadContext()
    {
        _textureCache.Clear();
        _allTextures.Clear();
        WhiteTexture = 0;
    }
}
