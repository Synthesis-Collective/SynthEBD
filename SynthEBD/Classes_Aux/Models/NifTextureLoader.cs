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
    /// Set to true to override all diffuse textures with a solid magenta test texture.
    /// Use to verify that the material/rendering pipeline works independently of
    /// actual texture data. If magenta appears on mesh, pipeline is OK.
    /// </summary>
    public bool UseDiagnosticTexture { get; set; } = false;

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
        if (model.Material is not PhongMaterial material)
        {
            _logger.LogMessage("CharacterViewer: ApplyTexturesToModel skipped — material is not PhongMaterial");
            return;
        }

        _logger.LogMessage("CharacterViewer: ApplyTexturesToModel — material type: " +
            material.GetType().Name + ", Core type: " + (material.Core?.GetType().Name ?? "NULL"));

        // Slot 0: Diffuse
        if (texturePaths.TryGetValue(0, out string? diffusePath))
        {
            var texture = LoadDdsTexture(diffusePath);
            if (texture != null)
            {
                material.DiffuseMap = texture;
                material.DiffuseColor = new HelixToolkit.Maths.Color4(1f, 1f, 1f, 1f);

                // Diagnostic: verify the Core received the DiffuseMap
                if (material.Core is HelixToolkit.SharpDX.Model.PhongMaterialCore core)
                {
                    _logger.LogMessage("CharacterViewer: DIAG — Core.DiffuseMap is " +
                        (core.DiffuseMap != null ? "SET (Guid=" + core.DiffuseMap.Guid + ")" : "NULL") +
                        ", Core.RenderDiffuseMap=" + core.RenderDiffuseMap.ToString() +
                        ", Core.DiffuseColor=" + core.DiffuseColor.ToString());
                }
                else
                {
                    _logger.LogMessage("CharacterViewer: DIAG — Core is NOT PhongMaterialCore (type: " +
                        (material.Core?.GetType().Name ?? "NULL") + ")");
                }

                _logger.LogMessage("CharacterViewer: Applied diffuse texture '" + diffusePath + "'");
            }
        }
        else
        {
            _logger.LogMessage("CharacterViewer: No diffuse texture (slot 0) in texture paths");
        }

        // DIAGNOSTIC: force a magenta test texture to verify the rendering pipeline.
        // If the mesh turns magenta, the pipeline works and the issue is in texture data.
        // If the mesh stays the same color, the pipeline itself is broken.
        // Set UseDiagnosticTexture = true (below) to activate.
        if (UseDiagnosticTexture)
        {
            var diag = CreateDiagnosticTexture();
            material.DiffuseMap = diag;
            material.DiffuseColor = new HelixToolkit.Maths.Color4(1f, 1f, 1f, 1f);
            _logger.LogMessage("CharacterViewer: DIAG — Overriding with diagnostic magenta texture");

            if (material.Core is HelixToolkit.SharpDX.Model.PhongMaterialCore diagCore)
            {
                _logger.LogMessage("CharacterViewer: DIAG — Core.DiffuseMap is " +
                    (diagCore.DiffuseMap != null ? "SET" : "NULL") +
                    ", RenderDiffuseMap=" + diagCore.RenderDiffuseMap.ToString());
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

    /// <summary>
    /// Creates a small solid-color diagnostic texture. If this texture displays
    /// on the mesh, the material/rendering pipeline is working and the issue
    /// lies in real texture data or loading. If it does NOT display, the
    /// pipeline itself is broken.
    /// </summary>
    public TextureModel CreateDiagnosticTexture()
    {
        // 4×4 solid magenta (BGRA byte order)
        byte[] pixels = new byte[4 * 4 * 4];
        for (int i = 0; i < 4 * 4; i++)
        {
            pixels[i * 4 + 0] = 255; // B
            pixels[i * 4 + 1] = 0;   // G
            pixels[i * 4 + 2] = 255; // R
            pixels[i * 4 + 3] = 255; // A
        }
        var texture = new TextureModel(pixels, SharpDX.DXGI.Format.B8G8R8A8_UNorm, 4, 4);
        _logger.LogMessage("CharacterViewer: Created 4x4 DIAGNOSTIC magenta texture (BGRA raw)");
        return texture;
    }

    private TextureModel? LoadDdsTextureViaPfim(string relativeGamePath, string resolved)
    {
        try
        {
            using var image = Pfimage.FromFile(resolved);

            int width = image.Width;
            int height = image.Height;

            int expectedBaseSize = width * height * (image.Format == Pfim.ImageFormat.Rgba32 ? 4 : 3);
            double dataRatio = image.Data.Length / (double)expectedBaseSize;
            _logger.LogMessage("CharacterViewer: Pfim decoded '" + relativeGamePath +
                "': " + width + "x" + height + " format=" + image.Format +
                " stride=" + image.Stride + " dataLen=" + image.Data.Length +
                " expectedBase=" + expectedBaseSize +
                " ratio=" + dataRatio.ToString("F3") +
                " (1.333=mipchain) strideExpected=" + (width * (image.Format == Pfim.ImageFormat.Rgba32 ? 4 : 3)));

            byte[] pixelData;

            switch (image.Format)
            {
                case Pfim.ImageFormat.Rgba32:
                {
                    // Pfim Rgba32 → GDI+ Format32bppArgb → byte order B,G,R,A → DXGI B8G8R8A8_UNorm
                    int rowBytes = width * 4;
                    int expectedSize = height * rowBytes;

                    _logger.LogMessage("CharacterViewer: Pfim Rgba32 copy: rowBytes=" + rowBytes +
                        " expectedSize=" + expectedSize + " image.Stride=" + image.Stride +
                        " image.Data.Length=" + image.Data.Length +
                        " copyPath=" + (image.Stride == rowBytes ? "flat" : "row-by-row"));

                    // Always copy — Pfim may reclaim image.Data on Dispose (ArrayPool)
                    pixelData = new byte[expectedSize];
                    if (image.Stride == rowBytes)
                    {
                        Buffer.BlockCopy(image.Data, 0, pixelData, 0, expectedSize);
                    }
                    else
                    {
                        _logger.LogMessage("CharacterViewer: Pfim stride mismatch — stride=" +
                            image.Stride + " vs rowBytes=" + rowBytes + ", copying row-by-row");
                        for (int y = 0; y < height; y++)
                        {
                            Buffer.BlockCopy(image.Data, y * image.Stride, pixelData, y * rowBytes, rowBytes);
                        }
                    }

                    // Verify copy: check first 8 bytes match between source and dest
                    if (image.Data.Length >= 8 && pixelData.Length >= 8)
                    {
                        bool copyMatch = true;
                        for (int ci = 0; ci < 8; ci++)
                        {
                            if (image.Data[ci] != pixelData[ci]) { copyMatch = false; break; }
                        }
                        _logger.LogMessage("CharacterViewer: Pfim copy verification: first 8 bytes match=" + copyMatch);
                    }

                    LogPixelDataDiagnostics(pixelData, width, height, relativeGamePath);

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

                    LogPixelDataDiagnostics(pixelData, width, height, relativeGamePath);

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
            _logger.LogError("CharacterViewer: Pfim fallback also failed for '" + relativeGamePath +
                "' (resolved: '" + resolved + "'): " + ex.Message);
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
    /// Comprehensive pixel data diagnostics. Logs sample pixels at multiple locations,
    /// alpha channel statistics, zero-data check, and data variation analysis.
    /// </summary>
    private void LogPixelDataDiagnostics(byte[] pixelData, int width, int height, string label)
    {
        if (pixelData.Length < 4) return;

        int totalPixels = pixelData.Length / 4;
        string shortName = Path.GetFileName(label);

        // Sample pixels at key positions (first, 25%, mid, 75%, last)
        int[] sampleOffsets = { 0, totalPixels / 4, totalPixels / 2, totalPixels * 3 / 4, totalPixels - 1 };
        foreach (int pixIdx in sampleOffsets)
        {
            int byteIdx = pixIdx * 4;
            if (byteIdx + 3 >= pixelData.Length) continue;
            _logger.LogMessage("CharacterViewer: PIXDIAG '" + shortName +
                "' px[" + pixIdx + "] byte[0]=" + pixelData[byteIdx].ToString("X2") +
                " byte[1]=" + pixelData[byteIdx + 1].ToString("X2") +
                " byte[2]=" + pixelData[byteIdx + 2].ToString("X2") +
                " byte[3]=" + pixelData[byteIdx + 3].ToString("X2") +
                " (BGRA: B=" + pixelData[byteIdx] + " G=" + pixelData[byteIdx + 1] +
                " R=" + pixelData[byteIdx + 2] + " A=" + pixelData[byteIdx + 3] + ")");
        }

        // Alpha channel statistics
        int alphaZero = 0, alpha255 = 0, alphaOther = 0;
        // Zero-pixel check (all four bytes zero)
        int zeroPixels = 0;
        // Track if all pixels are identical (uniform)
        byte firstB = pixelData[0], firstG = pixelData[1], firstR = pixelData[2], firstA = pixelData[3];
        bool allIdentical = true;
        // Channel min/max
        byte minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0;

        for (int i = 0; i < pixelData.Length; i += 4)
        {
            byte b = pixelData[i], g = pixelData[i + 1], r = pixelData[i + 2], a = pixelData[i + 3];

            if (a == 0) alphaZero++;
            else if (a == 255) alpha255++;
            else alphaOther++;

            if (b == 0 && g == 0 && r == 0 && a == 0) zeroPixels++;
            if (b != firstB || g != firstG || r != firstR || a != firstA) allIdentical = false;

            if (r < minR) minR = r; if (r > maxR) maxR = r;
            if (g < minG) minG = g; if (g > maxG) maxG = g;
            if (b < minB) minB = b; if (b > maxB) maxB = b;
        }

        _logger.LogMessage("CharacterViewer: PIXDIAG '" + shortName +
            "' alpha: zero=" + alphaZero + " full=" + alpha255 + " partial=" + alphaOther +
            " of " + totalPixels + " total pixels");
        _logger.LogMessage("CharacterViewer: PIXDIAG '" + shortName +
            "' zeroPixels(BGRA all 0)=" + zeroPixels + "/" + totalPixels +
            ", allIdentical=" + allIdentical);
        _logger.LogMessage("CharacterViewer: PIXDIAG '" + shortName +
            "' channel ranges: R=[" + minR + ".." + maxR +
            "] G=[" + minG + ".." + maxG + "] B=[" + minB + ".." + maxB + "]");

        // First 32 raw bytes as hex for byte-level inspection
        int hexLen = Math.Min(32, pixelData.Length);
        var hex = new System.Text.StringBuilder(hexLen * 3);
        for (int i = 0; i < hexLen; i++)
        {
            if (i > 0 && i % 4 == 0) hex.Append(" | ");
            else if (i > 0) hex.Append(' ');
            hex.Append(pixelData[i].ToString("X2"));
        }
        _logger.LogMessage("CharacterViewer: PIXDIAG '" + shortName +
            "' first 32 bytes (BGRA groups): " + hex.ToString());

        // Flag critical issues
        if (alphaZero == totalPixels)
        {
            _logger.LogError("CharacterViewer: PIXDIAG '" + shortName +
                "' *** ALL PIXELS HAVE ALPHA=0 — texture will be fully transparent! ***");
        }
        else if (alphaZero > totalPixels * 0.9)
        {
            _logger.LogError("CharacterViewer: PIXDIAG '" + shortName +
                "' *** " + (alphaZero * 100 / totalPixels) + "% of pixels have alpha=0 ***");
        }
        if (zeroPixels == totalPixels)
        {
            _logger.LogError("CharacterViewer: PIXDIAG '" + shortName +
                "' *** ALL PIXEL DATA IS ZERO — decode likely failed ***");
        }
    }

    /// <summary>
    /// Saves BGRA pixel data to disk as a 32-bit BMP file for visual inspection.
    /// BMP uses bottom-up row order, so rows are flipped during write.
    /// Files are written to the user's temp folder under SynthEBD_TexDebug.
    /// </summary>
    private void SaveDebugPixelDump(byte[] pixelData, int width, int height, string label)
    {
        try
        {
            string debugDir = Path.Combine(Path.GetTempPath(), "SynthEBD_TexDebug");
            Directory.CreateDirectory(debugDir);

            string safeName = Path.GetFileNameWithoutExtension(label)
                .Replace('\\', '_').Replace('/', '_').Replace(' ', '_');
            string bmpPath = Path.Combine(debugDir, safeName + ".bmp");

            int rowBytes = width * 4;
            int pixelDataSize = height * rowBytes;
            int fileSize = 54 + pixelDataSize; // 14 (file header) + 40 (info header) + pixels

            using var fs = new FileStream(bmpPath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            // BITMAPFILEHEADER (14 bytes)
            bw.Write((byte)'B');
            bw.Write((byte)'M');
            bw.Write(fileSize);       // file size
            bw.Write(0);              // reserved
            bw.Write(54);             // pixel data offset

            // BITMAPINFOHEADER (40 bytes)
            bw.Write(40);             // header size
            bw.Write(width);          // width
            bw.Write(height);         // height (positive = bottom-up)
            bw.Write((short)1);       // planes
            bw.Write((short)32);      // bits per pixel
            bw.Write(0);              // compression (BI_RGB)
            bw.Write(pixelDataSize);  // image size
            bw.Write(0);              // x pixels per meter
            bw.Write(0);              // y pixels per meter
            bw.Write(0);              // colors used
            bw.Write(0);              // important colors

            // Pixel data: BMP is bottom-up, our data is top-down — write rows in reverse
            for (int y = height - 1; y >= 0; y--)
            {
                bw.Write(pixelData, y * rowBytes, rowBytes);
            }

            _logger.LogMessage("CharacterViewer: Debug BMP saved to '" + bmpPath + "' (" +
                width + "x" + height + ", " + fileSize + " bytes)");
        }
        catch (Exception ex)
        {
            _logger.LogMessage("CharacterViewer: Could not save debug BMP: " + ex.Message);
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
