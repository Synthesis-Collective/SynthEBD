using System;
using System.Collections.Generic;
using System.IO;
using HelixToolkit;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using Pfim;
using SysVector3 = System.Numerics.Vector3;

namespace SynthEBD;

/// <summary>
/// Strategy for uploading decoded pixel data to the GPU via HelixToolkit.
/// </summary>
public enum TextureLoadStrategy
{
    /// <summary>
    /// Encode pixels as BMP in a MemoryStream, use TextureModel(Stream) → WIC decoder.
    /// This is the default and confirmed working path.
    /// </summary>
    BmpStream,

    /// <summary>
    /// Pass raw BGRA byte[] directly via TextureModel(byte[], Format, width, height) → ByteArrayLoader.
    /// This path was found to silently fail to render in HelixToolkit v3.1.2.
    /// </summary>
    ByteArray,

    /// <summary>
    /// Pass the resolved DDS file path directly via TextureModel(string) → TextureFileLoader.
    /// Uses HelixToolkit's internal DDS decoder; known to fail silently for Skyrim DDS (BC7/DXT).
    /// </summary>
    FilePath,
}

/// <summary>
/// Loads DDS textures from game assets and applies them to HelixToolkit
/// PhongMaterial on mesh models. Uses direct DDS byte loading for diffuse
/// textures (HelixToolkit natively supports DDS) and Pfim for CPU-side
/// MSN normal map sampling.
/// </summary>
public class NifTextureLoader
{
    private readonly GameAssetResolver _assetResolver;
    private readonly Logger _logger;

    /// <summary>
    /// Controls which GPU upload path is used for decoded textures.
    /// </summary>
    public TextureLoadStrategy LoadStrategy { get; set; } = TextureLoadStrategy.BmpStream;

    public NifTextureLoader(GameAssetResolver assetResolver, Logger logger)
    {
        _assetResolver = assetResolver;
        _logger = logger;
    }

    /// <summary>
    /// Applies textures from a NIF shape's BSShaderTextureSet to a model's PhongMaterial.
    /// <paramref name="texturePaths"/> is keyed by slot index (0=diffuse, 1=normal, 7=specular).
    /// </summary>
    public void ApplyTexturesToModel(MeshGeometryModel3D model, Dictionary<int, string> texturePaths)
    {
        if (model.Material is not PhongMaterial material) return;

        // Slot 0: Diffuse
        if (texturePaths.TryGetValue(0, out string? diffusePath))
        {
            var texture = LoadDdsTexture(diffusePath);
            if (texture != null)
            {
                material.DiffuseMap = texture;
                material.DiffuseColor = new HelixToolkit.Maths.Color4(1f, 1f, 1f, 1f);
            }
        }
    }

    /// <summary>
    /// Applies texture overrides from <see cref="FilePathReplacement"/> entries
    /// (as used in ForcedSubgroups) to the appropriate mesh model and texture slot.
    /// For normal map overrides (slot 1) on MSN shapes, also resamples vertex normals.
    /// </summary>
    /// <param name="modelsByBodyPart">Maps body part name ("Head", "Body", "Hands", "Feet") to its model.</param>
    /// <param name="overrides">FilePathReplacement entries with Source (texture path) and Destination (slot descriptor).</param>
    /// <param name="builtMeshesByBodyPart">Optional: built mesh data for MSN normal resampling on override.</param>
    public void ApplyTextureOverrides(
        Dictionary<string, MeshGeometryModel3D> modelsByBodyPart,
        IEnumerable<FilePathReplacement> overrides,
        Dictionary<string, NifMeshBuilder.BuiltMesh>? builtMeshesByBodyPart = null)
    {
        foreach (var replacement in overrides)
        {
            string dest = replacement.Destination;
            if (string.IsNullOrWhiteSpace(dest) || string.IsNullOrWhiteSpace(replacement.Source))
            {
                continue;
            }

            string? bodyPart = ParseBodyPart(dest);
            int? slot = ParseTextureSlot(dest);

            if (bodyPart == null || slot == null)
            {
                _logger.LogMessage("CharacterViewer: Could not parse body part or slot from override dest '" + dest + "'");
                continue;
            }

            if (!modelsByBodyPart.TryGetValue(bodyPart, out var model))
            {
                _logger.LogMessage("CharacterViewer: No model found for body part '" + bodyPart + "' in override");
                continue;
            }

            if (model.Material is not PhongMaterial material)
            {
                continue;
            }

            if (slot.Value == 0)
            {
                // Diffuse override
                var texture = LoadDdsTexture(replacement.Source);
                if (texture == null) continue;

                material.DiffuseMap = texture;
                material.DiffuseColor = new HelixToolkit.Maths.Color4(1f, 1f, 1f, 1f);
                _logger.LogMessage("CharacterViewer: Override diffuse '" + replacement.Source +
                    "' -> " + bodyPart);
            }
            else if (slot.Value == 1)
            {
                // Normal map override — resample vertex normals if this is an MSN shape
                if (builtMeshesByBodyPart != null &&
                    builtMeshesByBodyPart.TryGetValue(bodyPart, out var builtMesh) &&
                    builtMesh.IsModelSpaceNormals &&
                    model.Geometry is HelixToolkit.SharpDX.MeshGeometry3D geo)
                {
                    var newNormals = SampleMsnNormalsAtVertices(
                        replacement.Source,
                        builtMesh.TextureCoordinates);
                    if (newNormals != null)
                    {
                        geo.Normals = newNormals;
                        geo.UpdateOctree();
                        _logger.LogMessage("CharacterViewer: Override MSN normals '" + replacement.Source +
                            "' -> " + bodyPart + " (" + newNormals.Count + " vertices resampled)");
                    }
                }
                else
                {
                    _logger.LogMessage("CharacterViewer: Normal map override '" + replacement.Source +
                        "' -> " + bodyPart + " (skipped — not MSN or no built mesh data)");
                }
            }
            else
            {
                _logger.LogMessage("CharacterViewer: Skipping override for slot " + slot.Value +
                    " on " + bodyPart + " (not yet supported)");
            }
        }
    }

    /// <summary>
    /// Loads a DDS texture from a game-relative path using the current <see cref="LoadStrategy"/>.
    /// </summary>
    public TextureModel? LoadDdsTexture(string relativeGamePath)
    {
        string? resolved = _assetResolver.ResolveAssetPath(relativeGamePath);
        if (resolved == null)
        {
            _logger.LogMessage("CharacterViewer: Texture not found: '" + relativeGamePath + "'");
            return null;
        }

        if (!File.Exists(resolved))
        {
            _logger.LogMessage("CharacterViewer: Resolved path does not exist on disk: '" + resolved + "'");
            return null;
        }

        if (LoadStrategy == TextureLoadStrategy.FilePath)
        {
            _logger.LogMessage("CharacterViewer: [FilePath strategy] Loading '" + relativeGamePath + "' via TextureModel(filePath)");
            return new TextureModel(resolved);
        }

        return LoadDdsTextureViaPfim(relativeGamePath, resolved);
    }

    private TextureModel? LoadDdsTextureViaPfim(string relativeGamePath, string resolved)
    {
        try
        {
            using var image = Pfimage.FromFile(resolved);

            int width = image.Width;
            int height = image.Height;
            byte[] pixelData;

            switch (image.Format)
            {
                case Pfim.ImageFormat.Rgba32:
                {
                    int rowBytes = width * 4;
                    int expectedSize = height * rowBytes;

                    // Always copy — Pfim may reclaim image.Data on Dispose (ArrayPool)
                    pixelData = new byte[expectedSize];
                    if (image.Stride == rowBytes)
                    {
                        Buffer.BlockCopy(image.Data, 0, pixelData, 0, expectedSize);
                    }
                    else
                    {
                        for (int y = 0; y < height; y++)
                            Buffer.BlockCopy(image.Data, y * image.Stride, pixelData, y * rowBytes, rowBytes);
                    }

                    return CreateTextureModelFromPixels(pixelData, width, height, relativeGamePath);
                }

                case Pfim.ImageFormat.Rgb24:
                {
                    pixelData = new byte[width * height * 4];
                    int srcStride = image.Stride;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = y * srcStride + x * 3;
                            int dstIdx = (y * width + x) * 4;
                            pixelData[dstIdx] = image.Data[srcIdx];         // B
                            pixelData[dstIdx + 1] = image.Data[srcIdx + 1]; // G
                            pixelData[dstIdx + 2] = image.Data[srcIdx + 2]; // R
                            pixelData[dstIdx + 3] = 255;                    // A
                        }
                    }

                    return CreateTextureModelFromPixels(pixelData, width, height, relativeGamePath);
                }

                default:
                    _logger.LogError("CharacterViewer: Unsupported Pfim format " + image.Format +
                        " for '" + relativeGamePath + "'");
                    return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer: Failed to decode '" + relativeGamePath +
                "': " + ex.Message);
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FACE TINT BLENDING
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads a diffuse texture and blends a face tint overlay onto it on the CPU.
    /// The face tint DDS is decoded and blended per-pixel:
    ///   finalColor = mix(baseColor, baseColor * tintSample.rgb, tintSample.a)
    /// Returns the blended texture as a TextureModel, or falls back to the
    /// unblended diffuse if the tint texture cannot be loaded.
    /// </summary>
    public TextureModel? LoadDdsTextureWithFaceTint(string diffusePath, string faceTintPath)
    {
        var diffusePixels = LoadDdsPixels(diffusePath, out int dw, out int dh);
        if (diffusePixels == null)
            return null;

        var tintPixels = LoadDdsPixels(faceTintPath, out int tw, out int th);
        if (tintPixels == null)
        {
            _logger.LogMessage("CharacterViewer: FaceTint texture not found or failed to decode '" +
                faceTintPath + "', using unblended diffuse");
            return CreateTextureModelFromPixels(diffusePixels, dw, dh, diffusePath);
        }

        if (dw != tw || dh != th)
        {
            _logger.LogMessage("CharacterViewer: FaceTint size mismatch — diffuse=" +
                dw + "x" + dh + " tint=" + tw + "x" + th + ", using unblended diffuse");
            return CreateTextureModelFromPixels(diffusePixels, dw, dh, diffusePath);
        }

        // Blend: finalColor = mix(baseColor, baseColor * tintSample.rgb, tintSample.a)
        // In byte terms: out = base + (base * tint/255 - base) * tintAlpha/255
        //              = base * (1 - tintAlpha/255) + base * tint/255 * tintAlpha/255
        for (int i = 0; i < diffusePixels.Length; i += 4)
        {
            byte baseB = diffusePixels[i];
            byte baseG = diffusePixels[i + 1];
            byte baseR = diffusePixels[i + 2];

            byte tintB = tintPixels[i];
            byte tintG = tintPixels[i + 1];
            byte tintR = tintPixels[i + 2];
            byte tintA = tintPixels[i + 3];

            if (tintA == 0) continue; // No tint contribution

            float a = tintA / 255f;
            float oneMinusA = 1f - a;

            diffusePixels[i]     = (byte)Math.Clamp((int)(baseB * oneMinusA + baseB * tintB / 255f * a), 0, 255);
            diffusePixels[i + 1] = (byte)Math.Clamp((int)(baseG * oneMinusA + baseG * tintG / 255f * a), 0, 255);
            diffusePixels[i + 2] = (byte)Math.Clamp((int)(baseR * oneMinusA + baseR * tintR / 255f * a), 0, 255);
            // Alpha stays from diffuse
        }

        _logger.LogMessage("CharacterViewer: FaceTint blended onto diffuse (" + dw + "x" + dh + ")");
        return CreateTextureModelFromPixels(diffusePixels, dw, dh, diffusePath);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HAIR TINT
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads a greyscale hair diffuse texture and applies the hair tint color on the CPU.
    /// For greyscale-to-palette shaders, the diffuse texture's red channel acts as intensity:
    ///   outColor.rgb = pixel.rrr * tintColor
    /// </summary>
    public TextureModel? LoadDdsTextureWithHairTint(string diffusePath, float tintR, float tintG, float tintB)
    {
        var pixels = LoadDdsPixels(diffusePath, out int width, out int height);
        if (pixels == null) return null;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            // BGRA byte order — R is at offset +2
            float intensity = pixels[i + 2] / 255f; // red channel = greyscale intensity

            pixels[i]     = (byte)Math.Clamp((int)(intensity * tintB * 255f), 0, 255); // B
            pixels[i + 1] = (byte)Math.Clamp((int)(intensity * tintG * 255f), 0, 255); // G
            pixels[i + 2] = (byte)Math.Clamp((int)(intensity * tintR * 255f), 0, 255); // R
            // Alpha unchanged
        }

        _logger.LogMessage("CharacterViewer: Hair tint applied to '" + Path.GetFileName(diffusePath) +
            "' (" + width + "x" + height + ") tint=(" +
            tintR.ToString("F2") + "," + tintG.ToString("F2") + "," + tintB.ToString("F2") + ")");
        return CreateTextureModelFromPixels(pixels, width, height, diffusePath);
    }

    /// <summary>
    /// Decodes a DDS texture via Pfim and returns the raw BGRA pixel data.
    /// Returns null if the texture cannot be resolved or decoded.
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
            byte[] pixelData;

            switch (image.Format)
            {
                case Pfim.ImageFormat.Rgba32:
                    pixelData = new byte[expectedSize];
                    if (image.Stride == rowBytes)
                        Buffer.BlockCopy(image.Data, 0, pixelData, 0, expectedSize);
                    else
                    {
                        for (int y = 0; y < height; y++)
                            Buffer.BlockCopy(image.Data, y * image.Stride, pixelData, y * rowBytes, rowBytes);
                    }
                    return pixelData;

                case Pfim.ImageFormat.Rgb24:
                    pixelData = new byte[expectedSize];
                    int srcStride = image.Stride;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = y * srcStride + x * 3;
                            int dstIdx = (y * width + x) * 4;
                            pixelData[dstIdx] = image.Data[srcIdx];
                            pixelData[dstIdx + 1] = image.Data[srcIdx + 1];
                            pixelData[dstIdx + 2] = image.Data[srcIdx + 2];
                            pixelData[dstIdx + 3] = 255;
                        }
                    }
                    return pixelData;

                default:
                    _logger.LogMessage("CharacterViewer: Unsupported Pfim format " + image.Format +
                        " for '" + relativeGamePath + "'");
                    return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogMessage("CharacterViewer: Failed to decode pixels from '" + relativeGamePath +
                "': " + ex.Message);
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MSN NORMAL MAP SAMPLING
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Samples a model-space normal (MSN) texture at each vertex's UV coordinate
    /// and returns a new <see cref="Vector3Collection"/> of vertex normals.
    /// This converts the MSN data from NIF Z-up (with DirectX green-channel
    /// inversion) into HelixToolkit's Y-up coordinate system.
    ///
    /// Returns null if the texture cannot be loaded or decoded.
    /// </summary>
    public Vector3Collection? SampleMsnNormalsAtVertices(
        string normalMapRelativePath,
        Vector2Collection uvs)
    {
        string? resolved = _assetResolver.ResolveAssetPath(normalMapRelativePath);
        if (resolved == null)
        {
            _logger.LogMessage("CharacterViewer: MSN texture not found: '" + normalMapRelativePath + "'");
            return null;
        }

        try
        {
            using var image = Pfimage.FromFile(resolved);

            int width = image.Width;
            int height = image.Height;
            byte[] data = image.Data;
            int stride = image.Stride;
            int bytesPerPixel;

            switch (image.Format)
            {
                case Pfim.ImageFormat.Rgba32:
                    bytesPerPixel = 4;
                    break;
                case Pfim.ImageFormat.Rgb24:
                    bytesPerPixel = 3;
                    break;
                default:
                    _logger.LogError("CharacterViewer: MSN texture '" + normalMapRelativePath +
                        "' has unsupported pixel format " + image.Format +
                        " (expected Rgba32 or Rgb24)");
                    return null;
            }

            _logger.LogMessage("CharacterViewer: Sampling MSN texture '" + normalMapRelativePath +
                "' (" + width + "x" + height + ", " + image.Format + ") for " + uvs.Count + " vertices");

            var normals = new Vector3Collection(uvs.Count);
            int failedSamples = 0;

            for (int i = 0; i < uvs.Count; i++)
            {
                var uv = uvs[i];

                // Wrap UVs to [0,1) range (Skyrim meshes can have UVs outside 0-1)
                float u = uv.X % 1f;
                float v = uv.Y % 1f;
                if (u < 0) u += 1f;
                if (v < 0) v += 1f;

                // Convert UV to texel coordinates
                int tx = Math.Clamp((int)(u * (width - 1)), 0, width - 1);
                int ty = Math.Clamp((int)(v * (height - 1)), 0, height - 1);

                int offset = ty * stride + tx * bytesPerPixel;
                if (offset + 2 >= data.Length)
                {
                    normals.Add(new SysVector3(0, 1, 0));
                    failedSamples++;
                    continue;
                }

                // Pfim returns BGR/BGRA byte order
                float b = data[offset] / 255f;
                float g = data[offset + 1] / 255f;
                float r = data[offset + 2] / 255f;

                // Unpack from [0,1] to [-1,1]: normal = rgb * 2.0 - 1.0
                float nx = r * 2f - 1f;
                float ny = g * 2f - 1f;
                float nz = b * 2f - 1f;

                // Invert green channel for DirectX convention (matches reference shader)
                ny *= -1f;

                // Convert from NIF model space (Z-up) to HelixToolkit (Y-up):
                // X stays, Y = Z_nif, Z = -Y_nif
                float finalX = nx;
                float finalY = nz;
                float finalZ = -ny;

                var normal = new SysVector3(finalX, finalY, finalZ);
                float len = normal.Length();
                normals.Add(len > 0.0001f ? normal / len : new SysVector3(0, 1, 0));
            }

            if (failedSamples > 0)
            {
                _logger.LogMessage("CharacterViewer: MSN sampling had " + failedSamples +
                    " out-of-bounds samples (used fallback normal)");
            }

            _logger.LogMessage("CharacterViewer: MSN normals sampled successfully for " +
                uvs.Count + " vertices from '" + normalMapRelativePath + "'");
            return normals;
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer: Failed to sample MSN texture '" + normalMapRelativePath +
                "': " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Dispatches to the correct TextureModel creation method based on <see cref="LoadStrategy"/>.
    /// </summary>
    private TextureModel CreateTextureModelFromPixels(byte[] pixelData, int width, int height, string label)
    {
        switch (LoadStrategy)
        {
            case TextureLoadStrategy.ByteArray:
            {
                var texture = new TextureModel(pixelData, SharpDX.DXGI.Format.B8G8R8A8_UNorm, width, height);
                _logger.LogMessage("CharacterViewer: [ByteArray strategy] TextureModel for '" +
                    Path.GetFileName(label) + "' (" + width + "x" + height + ")");
                return texture;
            }
            case TextureLoadStrategy.BmpStream:
            default:
            {
                return CreateTextureModelViaBmp(pixelData, width, height, label);
            }
        }
    }

    /// <summary>
    /// Creates a TextureModel by encoding BGRA pixel data as a 32-bit BMP in a MemoryStream,
    /// then using TextureModel(Stream). This routes through HelixToolkit's WIC decoder path
    /// (TextureLoader.FromMemoryAsShaderResource) instead of ByteArrayLoader, which silently
    /// fails to render real texture data despite accepting it without error.
    /// </summary>
    private TextureModel CreateTextureModelViaBmp(byte[] pixelData, int width, int height, string label)
    {
        int rowBytes = width * 4;
        int pixelDataSize = height * rowBytes;
        int fileSize = 54 + pixelDataSize;

        var ms = new MemoryStream(fileSize);
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            // BITMAPFILEHEADER (14 bytes)
            bw.Write((byte)'B');
            bw.Write((byte)'M');
            bw.Write(fileSize);
            bw.Write(0);              // reserved
            bw.Write(54);             // pixel data offset

            // BITMAPINFOHEADER (40 bytes)
            bw.Write(40);             // header size
            bw.Write(width);
            bw.Write(height);         // positive = bottom-up row order
            bw.Write((short)1);       // planes
            bw.Write((short)32);      // bits per pixel (BGRA)
            bw.Write(0);              // compression (BI_RGB)
            bw.Write(pixelDataSize);
            bw.Write(0);              // x ppm
            bw.Write(0);              // y ppm
            bw.Write(0);              // colors used
            bw.Write(0);              // important colors

            // Pixel rows: BMP is bottom-up, pixelData is top-down
            for (int y = height - 1; y >= 0; y--)
            {
                bw.Write(pixelData, y * rowBytes, rowBytes);
            }
        }

        ms.Position = 0;
        var texture = new TextureModel(ms);
        _logger.LogMessage("CharacterViewer: Created BMP-stream TextureModel for '" +
            Path.GetFileName(label) + "' (" + width + "x" + height +
            ", stream=" + ms.Length + " bytes)");
        return texture;
    }

    /// <summary>
    /// Parses a FilePathDestinationMap destination string to determine the target body part.
    /// </summary>
    private static string? ParseBodyPart(string destination)
    {
        if (destination.StartsWith("HeadTexture", StringComparison.OrdinalIgnoreCase))
        {
            return "Head";
        }

        if (destination.Contains("SkinTexture", StringComparison.OrdinalIgnoreCase) ||
            destination.Contains("WorldModel", StringComparison.OrdinalIgnoreCase))
        {
            if (destination.Contains("BipedObjectFlag.Body", StringComparison.OrdinalIgnoreCase))
                return "Body";
            if (destination.Contains("BipedObjectFlag.Hands", StringComparison.OrdinalIgnoreCase))
                return "Hands";
            if (destination.Contains("BipedObjectFlag.Feet", StringComparison.OrdinalIgnoreCase))
                return "Feet";
        }

        return null;
    }

    /// <summary>
    /// Parses a FilePathDestinationMap destination string to determine the texture slot index.
    /// </summary>
    private static int? ParseTextureSlot(string destination)
    {
        // Check from most specific to least to avoid substring false matches
        if (destination.Contains("BacklightMaskOrSpecular", StringComparison.OrdinalIgnoreCase))
            return 7;
        if (destination.Contains("NormalOrGloss", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (destination.Contains("GlowOrDetailMap", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (destination.Contains("Diffuse", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (destination.Contains("Height", StringComparison.OrdinalIgnoreCase))
            return 3;

        return null;
    }
}
