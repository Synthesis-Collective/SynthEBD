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
    // Gate for the verbose per-texture resolution trace (game path -> disk path ->
    // resident-hit/upload/missing + GL handle). Off by default; the host's
    // "Verbose Log" toggle / force-regen _Mugshot.txt flips it on so a poisoned
    // re-render's texture provenance can be diffed against a post-restart render.
    private readonly CharacterViewerLogGate? _logGate;

    private void LogVerbose(string message)
    {
        if (_logGate != null && _logGate.Verbose) _logger?.LogMessage(message);
    }
    // Optional render-context-owned cache shared across renders (offscreen path).
    // When present, uploaded textures are owned by it (keyed on resolved disk
    // path) and survive between renders rather than being deleted with this VM.
    // Null for the live preview, which keeps strictly per-VM texture ownership.
    private readonly ResidentTextureCache? _resident;
    // Per-VM game-path -> handle map. Dedupes repeated requests within a single
    // render and, when a resident cache is in play, caches the resolved resident
    // handle so re-lookups this render skip the disk-path resolve.
    private readonly Dictionary<string, int> _textureCache = new(StringComparer.OrdinalIgnoreCase);
    // Handles OWNED BY THIS VM (deleted on Dispose/ClearCache). Resident-cache
    // handles are deliberately NOT added here — the resident cache owns them.
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

    public GlTextureManager(CharacterPreviewCache previewCache, ICharacterViewerLogger logger,
        ResidentTextureCache? resident = null, CharacterViewerLogGate? logGate = null)
    {
        _previewCache = previewCache;
        _logger = logger;
        _resident = resident;
        _logGate = logGate;
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
        {
            LogVerbose($"[GlTex] LoadTexture '{relativeGamePath}' VM-CACHE handle={cached}");
            return cached;
        }

        // Resident (offscreen) path: share the GL texture across renders, keyed on
        // the resolved disk path (correct under the strict per-mod scope chain).
        string? diskPath = _resident != null ? _previewCache.ResolveAssetPath(relativeGamePath) : null;
        if (diskPath != null)
        {
            int residentHandle = _resident!.TryGet(diskPath);
            if (residentHandle != -1)
            {
                _textureCache[relativeGamePath] = residentHandle;
                LogVerbose($"[GlTex] LoadTexture '{relativeGamePath}' -> '{diskPath}' RESIDENT-HIT handle={residentHandle}");
                return residentHandle;
            }
        }

        var pixels = _previewCache.GetOrLoadDdsPixels(relativeGamePath);
        if (pixels == null)
        {
            // Track for the post-load missing-texture overlay. Only counts
            // when the host actually asked for a path — empty/null paths
            // (above) are normal "shape doesn't use this slot" cases.
            _missingTexturePaths.Add(relativeGamePath);
            LogVerbose($"[GlTex] LoadTexture '{relativeGamePath}' -> '{diskPath ?? "(unresolved)"}' " +
                "MISSING pixels (WhiteTexture; shape flagged wireframe)");
            return WhiteTexture;
        }

        int handle = UploadTexture2DOwned(pixels.Value.Data, pixels.Value.Width, pixels.Value.Height, diskPath);
        _textureCache[relativeGamePath] = handle;
        LogVerbose($"[GlTex] LoadTexture '{relativeGamePath}' -> '{diskPath ?? "(per-VM)"}' " +
            $"UPLOAD handle={handle} {pixels.Value.Width}x{pixels.Value.Height} resident={diskPath != null}");
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
            return UploadTexture2DOwned(diffusePixels, dw, dh, null);
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
        return UploadTexture2DOwned(diffusePixels, dw, dh, null);
    }

    /// <summary>
    /// Loads a greyscale hair diffuse and applies tint color on the CPU.
    /// Not cached — caller should cache the result if needed.
    /// </summary>
    public int LoadTextureWithHairTint(string diffusePath, float tintR, float tintG, float tintB)
    {
        var source = _previewCache.GetOrLoadDdsPixels(diffusePath);
        if (source == null)
        {
            LogVerbose($"[GlTex] HairTint '{diffusePath}' MISSING pixels (WhiteTexture)");
            return WhiteTexture;
        }

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

        // Per-VM (residentDiskPath=null): the tinted diffuse is uploaded fresh every
        // render and carries the alpha the hair alpha-test samples. Logging its
        // handle + dimensions lets a poisoned re-render be compared against a
        // post-restart one — the alpha payload here is what drives hair coverage.
        int handle = UploadTexture2DOwned(pixels, width, height, null);
        LogVerbose($"[GlTex] HairTint '{diffusePath}' UPLOAD handle={handle} {width}x{height} " +
            $"tint=({tintR:0.###},{tintG:0.###},{tintB:0.###}) [per-VM]");
        return handle;
    }

    /// <summary>
    /// Loads an environment-map DDS as a real GL_TEXTURE_CUBE_MAP if the file
    /// is a complete cubemap, or returns <c>(handle, isCube=false)</c> as a
    /// 2D texture if the file isn't a cubemap (mod-shipped 2D sphere-maps).
    /// Returns <c>(0, false)</c> if the texture can't be loaded at all.
    ///
    /// The caller (<see cref="GlMesh"/> + the shader) chooses the sampling
    /// path based on <see cref="GlMesh.IsEnvMap2D"/>: cube-mapped reflections
    /// for proper cubemaps, the legacy spherical-2D math for the 2D fallback.
    /// </summary>
    public (int Handle, bool IsCube) LoadEnvMap(string relativeGamePath)
    {
        if (string.IsNullOrWhiteSpace(relativeGamePath))
            return (0, false);

        if (_textureCache.TryGetValue("envcube:" + relativeGamePath, out int cubeCached))
            return (cubeCached, true);
        if (_textureCache.TryGetValue("env2d:" + relativeGamePath, out int flatCached))
            return (flatCached, false);

        // Resident path keyed on disk path, prefixed so a file's cubemap and 2D
        // forms can't be confused (a given file is deterministically one or the
        // other, but the prefixes keep the handles unambiguous).
        string? diskPath = _resident != null ? _previewCache.ResolveAssetPath(relativeGamePath) : null;
        if (diskPath != null)
        {
            int rc = _resident!.TryGet("cube:" + diskPath);
            if (rc != -1) { _textureCache["envcube:" + relativeGamePath] = rc; return (rc, true); }
            int r2 = _resident.TryGet("2d:" + diskPath);
            if (r2 != -1) { _textureCache["env2d:" + relativeGamePath] = r2; return (r2, false); }
        }

        var cubemap = _previewCache.GetOrLoadDdsCubemap(relativeGamePath);
        if (cubemap != null)
        {
            int cubeHandle = UploadCubemapOwned(cubemap.Value.Faces, cubemap.Value.Width, cubemap.Value.Height,
                diskPath != null ? "cube:" + diskPath : null);
            _textureCache["envcube:" + relativeGamePath] = cubeHandle;
            return (cubeHandle, true);
        }

        // Not a cubemap (or not a readable DDS): fall through to the legacy
        // 2D sphere-map path so mod-shipped panoramic envmaps still render.
        var pixels = _previewCache.GetOrLoadDdsPixels(relativeGamePath);
        if (pixels == null)
        {
            _logger.LogMessage("GlTextures: Env map not found '" + relativeGamePath + "'");
            return (0, false);
        }

        int flatHandle = UploadTexture2DOwned(pixels.Value.Data, pixels.Value.Width, pixels.Value.Height,
            diskPath != null ? "2d:" + diskPath : null);
        _textureCache["env2d:" + relativeGamePath] = flatHandle;
        return (flatHandle, false);
    }

    /// <summary>
    /// Uploads six BGRA32 face buffers as a GL_TEXTURE_CUBE_MAP. Faces are in
    /// standard order +X, -X, +Y, -Y, +Z, -Z. ClampToEdge wrap on all three
    /// axes is required for cubemap seam continuity (Repeat would produce
    /// visible seams at face boundaries).
    /// </summary>
    private int UploadCubemap(byte[][] faces, int width, int height)
    {
        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.TextureCubeMap, handle);

        // DDS cubemap face order matches GL's TextureCubeMap{Positive,Negative}{X,Y,Z}.
        var targets = new[]
        {
            TextureTarget.TextureCubeMapPositiveX,
            TextureTarget.TextureCubeMapNegativeX,
            TextureTarget.TextureCubeMapPositiveY,
            TextureTarget.TextureCubeMapNegativeY,
            TextureTarget.TextureCubeMapPositiveZ,
            TextureTarget.TextureCubeMapNegativeZ,
        };
        for (int i = 0; i < 6; i++)
        {
            GL.TexImage2D(targets[i], 0, PixelInternalFormat.Rgba8,
                width, height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, faces[i]);
        }

        GL.GenerateMipmap(GenerateMipmapTarget.TextureCubeMap);

        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS,
            (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR,
            (int)TextureWrapMode.ClampToEdge);

        return handle;
    }

    // Rough VRAM footprint of an RGBA8 texture incl. its mip chain (~+1/3).
    private static long EstimateTextureBytes(int width, int height) => (long)width * height * 4 * 4 / 3;

    /// <summary>Uploads a 2D texture and assigns ownership: to the resident cache
    /// (keyed by <paramref name="residentDiskPath"/>) when both are present, else
    /// to this VM's per-instance list. On a GL_OUT_OF_MEMORY upload it deletes the
    /// handle, shrinks the resident budget, and returns <see cref="WhiteTexture"/>
    /// so the render degrades gracefully instead of crashing on low-VRAM GPUs.</summary>
    private int UploadTexture2DOwned(byte[] pixelData, int width, int height, string? residentDiskPath)
    {
        int handle = UploadTexture(pixelData, width, height);
        if (_resident != null)
        {
            // Single GetError drains the queue for this upload. OutOfMemory degrades
            // to WhiteTexture; any OTHER error is a silent-corruption suspect (the
            // 16-shared-mod / heavy-4K-texture batch is exactly where a driver can
            // report a partial/failed upload the cache would otherwise reuse as a
            // valid-but-garbage handle) — surface it under the verbose gate.
            ErrorCode err = GL.GetError();
            if (err == ErrorCode.OutOfMemory)
            {
                GL.DeleteTexture(handle);
                _resident.ReduceBudgetAfterOom();
                return WhiteTexture;
            }
            if (err != ErrorCode.NoError)
                LogVerbose($"[GlTex] UPLOAD GL error {err} handle={handle} {width}x{height} " +
                    $"disk='{residentDiskPath ?? "(per-VM)"}'");
            if (residentDiskPath != null)
            {
                _resident.Add(residentDiskPath, handle, EstimateTextureBytes(width, height));
                return handle;
            }
        }
        _allTextures.Add(handle);
        return handle;
    }

    /// <summary>Cubemap counterpart to <see cref="UploadTexture2DOwned"/>. Returns
    /// 0 (no env map — the shader handles its absence) on GL_OUT_OF_MEMORY.</summary>
    private int UploadCubemapOwned(byte[][] faces, int width, int height, string? residentDiskPath)
    {
        int handle = UploadCubemap(faces, width, height);
        if (_resident != null)
        {
            if (GL.GetError() == ErrorCode.OutOfMemory)
            {
                GL.DeleteTexture(handle);
                _resident.ReduceBudgetAfterOom();
                return 0;
            }
            if (residentDiskPath != null)
            {
                _resident.Add(residentDiskPath, handle, EstimateTextureBytes(width, height) * 6);
                return handle;
            }
        }
        _allTextures.Add(handle);
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
