using System;
using System.Collections.Generic;
using System.IO;
using Pfim;

namespace CharacterViewer.Rendering;

/// <summary>
/// BGRA32 pixel payload decoded from a DDS on disk. Data length == Width*Height*4.
/// Treated as immutable by the cache — callers that need to mutate (face/hair tint
/// blending) must clone the array before writing, or cache corruption follows.
/// </summary>
public readonly record struct DdsPixels(byte[] Data, int Width, int Height);

/// <summary>
/// BGRA32 pixel payload for a cubemap DDS — six square faces in standard order
/// +X, -X, +Y, -Y, +Z, -Z. Each face's array length == Width*Height*4. Pfim
/// 0.11.4 reads only the first face of a cubemap DDS, so cubemaps are detected
/// by parsing the DDS header (Caps2 cubemap bits) directly and fed face-by-face
/// through Pfim with the cubemap flags cleared so each face decodes as a 2D
/// image. See <see cref="CharacterPreviewCache.GetOrLoadDdsCubemap"/>.
/// </summary>
public readonly record struct DdsCubemapPixels(byte[][] Faces, int Width, int Height);

/// <summary>
/// Process-wide cache of expensive read-only inputs to the 3D preview pipeline.
///
/// Each VM_CharacterViewer is short-lived (e.g. the BodySlide menu disposes the
/// previous viewer on every preset switch to release its GL context), which
/// throws away the per-instance NIF parse cache, resolved-mesh-path data, and
/// decoded DDS pixel buffers. Hoisting all three into a singleton lets a
/// freshly-constructed viewer skip the link-cache traversal, NIF re-parse, and
/// Pfim DDS re-decode when it's loading the same preview NPC the previous
/// viewer just released.
///
/// Three layers:
///   * <see cref="MeshBuilder"/> — owns the parsed-NIF LRU keyed on
///     (nifPath, mtime, skeletonPath, mtime). Shared across all viewers.
///   * <see cref="GetOrResolveMeshPaths"/> — caches the data source's resolved
///     paths keyed on (invalidation-token identity, <see cref="NpcIdentity"/>).
///     The head-override path is applied by the caller after retrieval since
///     it's a per-load decoration, not part of the resolved record chain.
///   * <see cref="GetOrLoadDdsPixels"/> — LRU cache of BGRA32 pixel arrays keyed
///     on game-relative texture path. GL texture handles are still created per
///     viewer (context-specific), but the Pfim decode + Rgb24→Rgba32 conversion
///     happens once. Preset switching is the hot path — a typical NPC pulls
///     ~20 textures and decoding dominated the ~3.6s latency per switch.
///
/// Invalidation: when the data source's <see cref="INpcMeshDataSource.CurrentInvalidationToken"/>
/// reference changes (env reload), the path cache is dropped on next access. The
/// mesh LRU self-invalidates via file mtimes. The pixel cache doesn't track
/// mtimes — call <see cref="Clear"/> from an explicit env-refresh hook if
/// textures may have been edited on disk between sessions. In normal viewer use
/// the files don't change during a run.
/// </summary>
public class CharacterPreviewCache
{
    private readonly ICharacterViewerLogger _logger;
    private readonly CharacterViewerLogGate _logGate;
    private readonly INpcMeshDataSource _dataSource;
    private readonly GameAssetResolver _assetResolver;

    public NifMeshBuilder MeshBuilder { get; }

    private const int MeshPathsCacheMaxEntries = 32;
    private readonly Dictionary<NpcIdentity, ResolvedNpcMeshPaths?> _meshPathsCache = new();
    private readonly LinkedList<NpcIdentity> _meshPathsLru = new();
    private readonly object _meshPathsLock = new();
    private object? _meshPathsInvalidationToken;

    // Decoded BGRA32 pixel buffers, evicted by a dynamic byte budget rather than a
    // fixed entry count: a 4K texture is ~16x the bytes of a 1K one, so a count cap
    // could mean anywhere from a few hundred MB to several GB of resident pixels.
    // The budget tracks free system RAM (see SystemMemoryBudget) so the cache grows
    // to use spare memory on a big machine and shrinks on a constrained one. This is
    // the dominant in-RAM cache, so it gets the largest share of free RAM, and its
    // ceiling is a share of total RAM (not a fixed cap) so a high-RAM host running
    // batched 4K/8K renders isn't throttled.
    private const long PixelCacheMinBudgetBytes = 64L * 1024 * 1024;        // 64 MB floor
    private const double PixelCacheMaxFractionOfTotal = 0.6;                // ceiling: 60% of RAM
    private const double PixelCacheFreeRamFraction = 0.5;
    private const int PixelCacheRepollEveryAdds = 32;
    private readonly Dictionary<string, DdsPixels?> _pixelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _pixelLru = new();
    private readonly object _pixelLock = new();
    private long _pixelBytes;
    private long _pixelBudgetBytes;
    private int _pixelAddsSinceRepoll;

    // path -> "decodes to a fully-transparent image" verdict (see
    // IsFullyTransparent). A texture's alpha content is fixed for the session,
    // so this is cached independently of the pixel LRU and never evicted —
    // re-deciding after a pixel-cache eviction would needlessly re-decode. One
    // bool per unique path, so the footprint is negligible.
    private readonly Dictionary<string, bool> _fullyTransparentCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _transparencyLock = new();

    // Parallel cache for cubemap DDS payloads (six face buffers each). Kept
    // separate from _pixelCache because the value type differs and a single
    // texture path can't legitimately be both at once. Byte-budgeted like the
    // pixel cache but with a much smaller share of free RAM: a typical NPC pulls
    // one envmap and many share the default cubemap, so the working set is tiny.
    private const long CubemapCacheMinBudgetBytes = 16L * 1024 * 1024;       // 16 MB floor
    private const double CubemapCacheMaxFractionOfTotal = 0.1;               // ceiling: 10% of RAM
    private const double CubemapCacheFreeRamFraction = 0.1;
    private const int CubemapCacheRepollEveryAdds = 8;
    private readonly Dictionary<string, DdsCubemapPixels?> _cubemapCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _cubemapLru = new();
    private readonly object _cubemapLock = new();
    private long _cubemapBytes;
    private long _cubemapBudgetBytes;
    private int _cubemapAddsSinceRepoll;

    // Cumulative wall-clock spent in actual DDS decode (cache misses only — hits
    // don't reach the decode call). Lets a profiling host snapshot the delta
    // around a render to split the "install" phase into decode (CPU, cacheable /
    // parallelizable) vs GL upload (must be on the render thread). Interlocked
    // because the live preview can decode off the render thread.
    private long _decodeTicks;

    // Per-thread decode accumulator. Since the prewarm pipeline decodes on worker
    // threads concurrently with the render thread's own decode-on-miss, the
    // process-wide _decodeTicks can no longer attribute decode to a single render
    // (a render's install span would also count whatever prewarm workers decoded
    // meanwhile, inflating it past the install wall-time). ThreadStatic so the
    // render thread snapshots ONLY its own decode for per-render timings.
    [ThreadStatic] private static long _threadDecodeTicks;

    /// <summary>Cumulative milliseconds spent decoding DDS pixels on cache
    /// misses since process start (or the last <see cref="Clear"/>... not reset
    /// by Clear — it's a monotonic profiling counter). Process-wide across all
    /// threads. Snapshot before/after a span and subtract to attribute decode cost.</summary>
    public double TotalDecodeMs => _decodeTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>Like <see cref="TotalDecodeMs"/> but accumulated PER CALLING THREAD
    /// (ThreadStatic). The offscreen render thread snapshots this around its install
    /// span to measure its OWN decode-on-miss without counting decode that prewarm
    /// workers perform on other threads concurrently. Monotonic per thread.</summary>
    public double ThreadDecodeMs => _threadDecodeTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    public CharacterPreviewCache(
        INpcMeshDataSource dataSource,
        GameAssetResolver assetResolver,
        ICharacterViewerLogger logger,
        CharacterViewerLogGate logGate)
    {
        _dataSource = dataSource;
        _assetResolver = assetResolver;
        _logger = logger;
        _logGate = logGate;
        MeshBuilder = new NifMeshBuilder(logger, logGate, assetResolver);

        _pixelBudgetBytes = SystemMemoryBudget.Compute(
            0, PixelCacheFreeRamFraction, PixelCacheMinBudgetBytes, PixelCacheMaxFractionOfTotal);
        _cubemapBudgetBytes = SystemMemoryBudget.Compute(
            0, CubemapCacheFreeRamFraction, CubemapCacheMinBudgetBytes, CubemapCacheMaxFractionOfTotal);
    }

    /// <summary>
    /// Returns cached <see cref="ResolvedNpcMeshPaths"/> for this NPC under the
    /// data source's current invalidation token, or resolves and caches if absent.
    /// Null results are NOT cached — under the strict-scopes contract a miss
    /// can legitimately differ across scopes (one render's vanilla-only scope
    /// failing to resolve doesn't tell us what a later mod-scoped render would
    /// produce). Treating misses as fresh-each-time avoids cross-scope cache
    /// poisoning.
    /// </summary>
    /// <summary>Resolves a game-relative path to its on-disk file under the
    /// current scope chain (extracting from a BSA into the cache dir if needed),
    /// or null if unresolved. Exposed so the resident GL texture cache can key on
    /// the resolved disk path — correct across the strict per-mod scopes, where
    /// the same game-path can map to different files.</summary>
    public string? ResolveAssetPath(string relativeGamePath) =>
        _assetResolver.ResolveAssetPath(relativeGamePath);

    public ResolvedNpcMeshPaths? GetOrResolveMeshPaths(NpcIdentity identity)
    {
        var token = _dataSource.CurrentInvalidationToken;

        lock (_meshPathsLock)
        {
            // Drop everything if the data source's invalidation token has been
            // replaced (e.g. env reload). Reference identity is sufficient — a
            // new environment build always produces a new token instance.
            if (!ReferenceEquals(_meshPathsInvalidationToken, token))
            {
                _meshPathsCache.Clear();
                _meshPathsLru.Clear();
                _meshPathsInvalidationToken = token;
            }
            else if (_meshPathsCache.TryGetValue(identity, out var cached))
            {
                _meshPathsLru.Remove(identity);
                _meshPathsLru.AddFirst(identity);
                if (_logGate != null && _logGate.Verbose)
                    _logger?.LogMessage("CharacterPreviewCache: NpcMeshPaths cache hit for " + identity.CacheKey);
                return cached;
            }
        }

        var resolved = _dataSource.Resolve(identity);

        // Don't cache misses — see method-level remark.
        if (resolved == null) return null;

        lock (_meshPathsLock)
        {
            // Re-check the token in case another thread invalidated mid-resolve.
            if (!ReferenceEquals(_meshPathsInvalidationToken, token)) return resolved;

            _meshPathsCache[identity] = resolved;
            _meshPathsLru.AddFirst(identity);
            while (_meshPathsLru.Count > MeshPathsCacheMaxEntries)
            {
                var oldest = _meshPathsLru.Last!.Value;
                _meshPathsLru.RemoveLast();
                _meshPathsCache.Remove(oldest);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Returns BGRA32 pixel data for the given game-relative DDS path, decoding
    /// through Pfim on cache miss and caching successful results for subsequent
    /// viewers. Null results are NOT cached — under the strict-scopes contract
    /// the same path can resolve differently across scopes, so a miss observed
    /// in one render must not poison a later render whose scope chain would
    /// have found the file. Re-decoding a legitimately-broken texture is the
    /// expected (rare) cost; the missing-texture wireframe path bounds the
    /// visual impact.
    ///
    /// The returned <see cref="DdsPixels.Data"/> array is treated as immutable
    /// by the cache — callers that blend tints on the CPU must clone before
    /// mutating. <see cref="GlTextureManager"/>'s tint paths do exactly that.
    /// </summary>
    public DdsPixels? GetOrLoadDdsPixels(string relativeGamePath)
    {
        if (string.IsNullOrWhiteSpace(relativeGamePath)) return null;

        lock (_pixelLock)
        {
            if (_pixelCache.TryGetValue(relativeGamePath, out var cached))
            {
                _pixelLru.Remove(relativeGamePath);
                _pixelLru.AddFirst(relativeGamePath);
                if (_logGate != null && _logGate.Verbose)
                    _logger?.LogMessage("CharacterPreviewCache: DdsPixels cache hit for '" + relativeGamePath + "'");
                return cached;
            }
        }

        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var decoded = DecodeDds(relativeGamePath);
        long dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        System.Threading.Interlocked.Add(ref _decodeTicks, dt);
        _threadDecodeTicks += dt;

        // Don't cache misses — see method-level remark.
        if (decoded == null) return null;

        lock (_pixelLock)
        {
            // Re-check in case a parallel caller already populated the entry.
            if (_pixelCache.TryGetValue(relativeGamePath, out var racedCached))
            {
                _pixelLru.Remove(relativeGamePath);
                _pixelLru.AddFirst(relativeGamePath);
                return racedCached;
            }

            _pixelCache[relativeGamePath] = decoded;
            _pixelLru.AddFirst(relativeGamePath);
            _pixelBytes += decoded.Value.Data.Length;

            // Periodically re-evaluate the budget against current free RAM so the
            // cache expands into spare memory and contracts when it tightens.
            if (++_pixelAddsSinceRepoll >= PixelCacheRepollEveryAdds)
            {
                _pixelAddsSinceRepoll = 0;
                _pixelBudgetBytes = SystemMemoryBudget.Compute(
                    _pixelBytes, PixelCacheFreeRamFraction,
                    PixelCacheMinBudgetBytes, PixelCacheMaxFractionOfTotal);
            }

            // Evict LRU until within budget, but always keep the entry just added
            // (a single texture larger than the whole budget must not loop forever).
            while (_pixelBytes > _pixelBudgetBytes && _pixelLru.Count > 1)
            {
                var oldestKey = _pixelLru.Last!.Value;
                _pixelLru.RemoveLast();
                if (_pixelCache.TryGetValue(oldestKey, out var evicted) && evicted.HasValue)
                    _pixelBytes -= evicted.Value.Data.Length;
                _pixelCache.Remove(oldestKey);
            }
        }

        return decoded;
    }

    /// <summary>
    /// True if the texture at <paramref name="relativeGamePath"/> decodes to a
    /// fully-transparent image — every texel's alpha is exactly 0.
    ///
    /// Used to cull SMP/physics collision-proxy shapes: SMP-enabled hair (and
    /// some armor) ship invisible collision bodies textured with a zero-alpha
    /// placeholder (e.g. "0alfa.dds": white RGB, alpha 0) and carrying NO
    /// NiAlphaProperty. In-game the physics system detaches them from the render
    /// graph; with no physics here they would otherwise rasterize as opaque white
    /// over the face/body. Only RGBA-format DDS can satisfy this — <see
    /// cref="PfimageToBgra32"/> forces alpha to 255 for the non-alpha formats — so
    /// the check never fires on ordinary opaque skin/armor diffuse.
    ///
    /// Returns false for an empty path or a texture that can't be decoded. The
    /// verdict is cached for the session.
    /// </summary>
    public bool IsFullyTransparent(string? relativeGamePath)
    {
        if (string.IsNullOrWhiteSpace(relativeGamePath)) return false;

        lock (_transparencyLock)
            if (_fullyTransparentCache.TryGetValue(relativeGamePath, out bool cached))
                return cached;

        var pixels = GetOrLoadDdsPixels(relativeGamePath);
        bool verdict = pixels != null && IsAllAlphaZero(pixels.Value.Data);

        lock (_transparencyLock)
            _fullyTransparentCache[relativeGamePath] = verdict;
        return verdict;
    }

    // BGRA32: alpha is every 4th byte (offset +3). The early-out on the first
    // non-transparent texel keeps this O(1) for ordinary opaque diffuse, where
    // the very first texel is already opaque.
    private static bool IsAllAlphaZero(byte[] bgra)
    {
        for (int i = 3; i < bgra.Length; i += 4)
            if (bgra[i] != 0) return false;
        return true;
    }

    /// <summary>
    /// Decodes a DDS via Pfim into BGRA32. Handles the formats Pfim emits for
    /// Skyrim assets — Rgba32 (blittable), Rgb24 (needs padding to 4-byte
    /// stride), and Rgb8 (8-bit greyscale broadcast to BGR). Matches the
    /// behavior previously in GlTextureManager.LoadDdsPixels so swapping
    /// callers from the direct Pfim path to this cached path yields pixel-
    /// identical output.
    /// </summary>
    /// <summary>Total bytes held by a cubemap payload: the sum of its six face
    /// buffers. Used for the cubemap cache's byte-budget accounting.</summary>
    private static long CubemapByteSize(DdsCubemapPixels cubemap)
    {
        long total = 0;
        foreach (var face in cubemap.Faces)
            total += face?.Length ?? 0;
        return total;
    }

    private DdsPixels? DecodeDds(string relativeGamePath)
    {
        string? resolved = _assetResolver.ResolveAssetPath(relativeGamePath);
        if (resolved == null || !File.Exists(resolved)) return null;

        try
        {
            using var image = Pfimage.FromFile(resolved);
            byte[]? bgra = PfimageToBgra32(image, relativeGamePath);
            if (bgra == null) return null;
            return new DdsPixels(bgra, image.Width, image.Height);
        }
        catch (Exception ex)
        {
            if (_logGate != null && _logGate.Verbose)
                _logger?.LogMessage("CharacterPreviewCache: Failed to decode '" +
                    relativeGamePath + "': " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Returns six BGRA32 face buffers for the given game-relative cubemap DDS,
    /// or null if the file is missing, unreadable, or not a complete cubemap.
    /// Caching mirrors <see cref="GetOrLoadDdsPixels"/> — successful results
    /// are cached, failures are not.
    /// </summary>
    public DdsCubemapPixels? GetOrLoadDdsCubemap(string relativeGamePath)
    {
        if (string.IsNullOrWhiteSpace(relativeGamePath)) return null;

        lock (_cubemapLock)
        {
            if (_cubemapCache.TryGetValue(relativeGamePath, out var cached))
            {
                _cubemapLru.Remove(relativeGamePath);
                _cubemapLru.AddFirst(relativeGamePath);
                if (_logGate != null && _logGate.Verbose)
                    _logger?.LogMessage("CharacterPreviewCache: DdsCubemap cache hit for '" + relativeGamePath + "'");
                return cached;
            }
        }

        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var decoded = DecodeDdsCubemap(relativeGamePath);
        long dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        System.Threading.Interlocked.Add(ref _decodeTicks, dt);
        _threadDecodeTicks += dt;
        if (decoded == null) return null;

        lock (_cubemapLock)
        {
            if (_cubemapCache.TryGetValue(relativeGamePath, out var racedCached))
            {
                _cubemapLru.Remove(relativeGamePath);
                _cubemapLru.AddFirst(relativeGamePath);
                return racedCached;
            }

            _cubemapCache[relativeGamePath] = decoded;
            _cubemapLru.AddFirst(relativeGamePath);
            _cubemapBytes += CubemapByteSize(decoded.Value);

            if (++_cubemapAddsSinceRepoll >= CubemapCacheRepollEveryAdds)
            {
                _cubemapAddsSinceRepoll = 0;
                _cubemapBudgetBytes = SystemMemoryBudget.Compute(
                    _cubemapBytes, CubemapCacheFreeRamFraction,
                    CubemapCacheMinBudgetBytes, CubemapCacheMaxFractionOfTotal);
            }

            while (_cubemapBytes > _cubemapBudgetBytes && _cubemapLru.Count > 1)
            {
                var oldestKey = _cubemapLru.Last!.Value;
                _cubemapLru.RemoveLast();
                if (_cubemapCache.TryGetValue(oldestKey, out var evicted) && evicted.HasValue)
                    _cubemapBytes -= CubemapByteSize(evicted.Value);
                _cubemapCache.Remove(oldestKey);
            }
        }

        return decoded;
    }

    /// <summary>
    /// Detects a cubemap DDS by reading the 124-byte header's Caps2 field, then
    /// feeds each of the six faces through Pfim individually. Pfim 0.11.4 only
    /// reads the first face of a multi-face DDS, so the workaround is to
    /// synthesize six "single-face" DDS streams in memory — same header with
    /// the cubemap bits cleared, prefixed to that face's slice of the original
    /// pixel payload — and decode each as a normal 2D image.
    ///
    /// DDS layout reference (Microsoft spec):
    ///   bytes  0–3  : "DDS " magic (0x20534444 little-endian)
    ///   bytes  4–127: 124-byte DDS_HEADER, with dwCaps2 at offset 112
    /// dwCaps2 bits:
    ///   0x0200 DDSCAPS2_CUBEMAP            — base cubemap flag
    ///   0x0400 DDSCAPS2_CUBEMAP_POSITIVEX  — face 0
    ///   0x0800 DDSCAPS2_CUBEMAP_NEGATIVEX  — face 1
    ///   0x1000 DDSCAPS2_CUBEMAP_POSITIVEY  — face 2
    ///   0x2000 DDSCAPS2_CUBEMAP_NEGATIVEY  — face 3
    ///   0x4000 DDSCAPS2_CUBEMAP_POSITIVEZ  — face 4
    ///   0x8000 DDSCAPS2_CUBEMAP_NEGATIVEZ  — face 5
    /// All seven bits combined: 0xFE00. Faces are stored sequentially after the
    /// header (each with its own mip chain inline), all the same size. We
    /// require a complete cubemap (mask 0xFE00) — partial cubemaps are rare in
    /// the wild and Skyrim envmaps are always complete.
    /// </summary>
    private DdsCubemapPixels? DecodeDdsCubemap(string relativeGamePath)
    {
        string? resolved = _assetResolver.ResolveAssetPath(relativeGamePath);
        if (resolved == null || !File.Exists(resolved)) return null;

        try
        {
            byte[] fileBytes = File.ReadAllBytes(resolved);
            if (fileBytes.Length < 128) return null;

            uint magic = BitConverter.ToUInt32(fileBytes, 0);
            if (magic != 0x20534444u) return null; // not a DDS

            const uint DDSCAPS2_CUBEMAP_COMPLETE = 0x0000FE00;
            uint caps2 = BitConverter.ToUInt32(fileBytes, 112);
            if ((caps2 & DDSCAPS2_CUBEMAP_COMPLETE) != DDSCAPS2_CUBEMAP_COMPLETE)
                return null; // not a cubemap (or partial cubemap)

            int payloadLen = fileBytes.Length - 128;
            if (payloadLen <= 0 || payloadLen % 6 != 0) return null;
            int perFaceLen = payloadLen / 6;

            // Build a header for the single-face streams: same as the original
            // but with all cubemap bits in Caps2 cleared so Pfim sees a normal
            // 2D DDS. Done once and copied into each face's buffer below.
            byte[] singleFaceHeader = new byte[128];
            Buffer.BlockCopy(fileBytes, 0, singleFaceHeader, 0, 128);
            uint clearedCaps2 = caps2 & ~DDSCAPS2_CUBEMAP_COMPLETE;
            BitConverter.GetBytes(clearedCaps2).CopyTo(singleFaceHeader, 112);

            var faces = new byte[6][];
            int faceWidth = 0, faceHeight = 0;

            for (int i = 0; i < 6; i++)
            {
                byte[] faceFile = new byte[128 + perFaceLen];
                Buffer.BlockCopy(singleFaceHeader, 0, faceFile, 0, 128);
                Buffer.BlockCopy(fileBytes, 128 + i * perFaceLen, faceFile, 128, perFaceLen);

                using var stream = new MemoryStream(faceFile, writable: false);
                using var image = Pfimage.FromStream(stream);
                byte[]? bgra = PfimageToBgra32(image, relativeGamePath);
                if (bgra == null) return null;

                faces[i] = bgra;
                if (i == 0)
                {
                    faceWidth = image.Width;
                    faceHeight = image.Height;
                }
                else if (image.Width != faceWidth || image.Height != faceHeight)
                {
                    if (_logGate != null && _logGate.Verbose)
                        _logger?.LogMessage("CharacterPreviewCache: Cubemap face " + i +
                            " has mismatched size for '" + relativeGamePath + "'");
                    return null;
                }
            }

            return new DdsCubemapPixels(faces, faceWidth, faceHeight);
        }
        catch (Exception ex)
        {
            if (_logGate != null && _logGate.Verbose)
                _logger?.LogMessage("CharacterPreviewCache: Failed to decode cubemap '" +
                    relativeGamePath + "': " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Converts a Pfim-decoded image to a tightly-packed BGRA32 buffer. Shared
    /// between <see cref="DecodeDds"/> and <see cref="DecodeDdsCubemap"/> so
    /// the two paths produce pixel-identical output for the same source bytes.
    /// </summary>
    private byte[]? PfimageToBgra32(Pfim.IImage image, string relativeGamePath)
    {
        int width = image.Width;
        int height = image.Height;
        int rowBytes = width * 4;
        int expectedSize = height * rowBytes;

        switch (image.Format)
        {
            case Pfim.ImageFormat.Rgba32:
            {
                byte[] data = new byte[expectedSize];
                if (image.Stride == rowBytes)
                    Buffer.BlockCopy(image.Data, 0, data, 0, expectedSize);
                else
                    for (int y = 0; y < height; y++)
                        Buffer.BlockCopy(image.Data, y * image.Stride, data, y * rowBytes, rowBytes);
                return data;
            }
            case Pfim.ImageFormat.Rgb24:
            {
                byte[] data = new byte[expectedSize];
                int srcStride = image.Stride;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int srcIdx = y * srcStride + x * 3;
                        int dstIdx = (y * width + x) * 4;
                        data[dstIdx] = image.Data[srcIdx];
                        data[dstIdx + 1] = image.Data[srcIdx + 1];
                        data[dstIdx + 2] = image.Data[srcIdx + 2];
                        data[dstIdx + 3] = 255;
                    }
                return data;
            }
            case Pfim.ImageFormat.Rgb8:
            {
                // 8-bit greyscale (e.g. Skyrim _S specular maps): one intensity
                // byte per pixel, broadcast to B/G/R with full alpha.
                byte[] data = new byte[expectedSize];
                int srcStride = image.Stride;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        byte v = image.Data[y * srcStride + x];
                        int dstIdx = (y * width + x) * 4;
                        data[dstIdx] = v;
                        data[dstIdx + 1] = v;
                        data[dstIdx + 2] = v;
                        data[dstIdx + 3] = 255;
                    }
                return data;
            }
            default:
                if (_logGate != null && _logGate.Verbose)
                    _logger?.LogMessage("CharacterPreviewCache: Unsupported DDS format " +
                        image.Format + " for '" + relativeGamePath + "'");
                return null;
        }
    }

    /// <summary>
    /// Warms the parsed-NIF and decoded-DDS caches for an NPC's base meshes with
    /// NO GL work, so a subsequent offscreen render of the same NPC hits both
    /// caches and pays only GL upload + draw + readback on the render thread.
    /// Intended to run on a worker thread — with the same resolver scopes pushed
    /// as the render (the caller does this) — while the render thread renders a
    /// different NPC: nifly parsing is thread-safe on separate <c>NifFile</c>
    /// instances, Pfim decode is per-call safe, and all three caches here are
    /// internally locked.
    ///
    /// <para>Mirrors <see cref="VM_CharacterViewer.LoadAllMeshParts"/>'s parse set
    /// (each body-part NIF plus its <c>_0</c> weight companion, built against the
    /// resolved skeleton so the cache key matches) and
    /// <c>VM_CharacterViewer.InstallOneShapeTextures</c>'s effective-texture set
    /// (NIF slots with ARMA TXST overrides applied to skin shapes, the env map via
    /// the cubemap path, plus the head FaceTint). It is strictly best-effort:
    /// anything it misses simply decodes on the render thread as before, so
    /// divergence from those methods degrades performance, never correctness.
    /// Mesh overrides (attire / headgear) ARE pre-warmed when supplied — their NIFs
    /// are parsed and their textures decoded so the render's ApplyMeshOverrides hits
    /// both caches. Outfits are diverse, so to keep that churn from displacing the
    /// shared body / skin assets reused on every NPC: the override parses cache under
    /// a null body part, which the parse cache's role-aware eviction reclaims before
    /// shared body parts; and the diverse-outfit vs shared-skin TEXTURE split is left
    /// to the resident GL texture cache's segmented LRU (which prewarm doesn't touch,
    /// and which graduates re-hit skin/eye/hair to its protected segment while
    /// evicting one-shot outfit textures first).</para>
    /// </summary>
    public void PrewarmNpc(ResolvedNpcMeshPaths paths,
        IEnumerable<MeshOverride>? meshOverrides = null,
        System.Threading.CancellationToken ct = default)
    {
        if (paths == null) return;

        // Load the skeleton once (the NifFile instance isn't part of the parse
        // cache key — only its disk path is — so the render's later parse still
        // hits the entries we warm here, even though it loads its own skeleton).
        nifly.NifFile? skeletonNif = null;
        string? skelDiskPath = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(paths.SkeletonPath))
            {
                skelDiskPath = _assetResolver.ResolveAssetPath(paths.SkeletonPath);
                if (skelDiskPath != null)
                {
                    skeletonNif = new nifly.NifFile();
                    if (skeletonNif.Load(skelDiskPath) != 0)
                    {
                        skeletonNif.Dispose();
                        skeletonNif = null;
                        // Keep skelDiskPath as the cache key even on load failure, so
                        // these entries key identically to the render's lazy path
                        // (LoadAllMeshParts keeps the resolved skeleton path as the
                        // key regardless of whether the NIF parses). Otherwise a
                        // skeleton that fails to load would make prewarm key on null
                        // and the render miss every part.
                    }
                }
            }

            PrewarmPart("Body", paths.BodyMeshPath, paths, skeletonNif, skelDiskPath, ct);
            PrewarmPart("Hands", paths.HandsMeshPath, paths, skeletonNif, skelDiskPath, ct);
            PrewarmPart("Feet", paths.FeetMeshPath, paths, skeletonNif, skelDiskPath, ct);
            PrewarmPart("Head", paths.HeadMeshPath, paths, skeletonNif, skelDiskPath, ct);
            PrewarmPart("Hair", paths.HairMeshPath, paths, skeletonNif, skelDiskPath, ct);
            PrewarmPart("Tail", paths.TailMeshPath, paths, skeletonNif, skelDiskPath, ct);

            // FaceTint is a per-NPC (often large) head texture pulled from the
            // resolved paths, not from any NIF, and decoded for the primary head
            // shape during install. Warm it directly.
            if (!string.IsNullOrWhiteSpace(paths.FaceTintPath))
            {
                ct.ThrowIfCancellationRequested();
                GetOrLoadDdsPixels(paths.FaceTintPath);
            }

            // Attire / headgear mesh overrides (Include Default Outfit / headgear).
            // Parse each override NIF + decode its textures so the render's
            // ApplyMeshOverrides hits the caches. Uses the same skeleton + null body
            // part as ApplyOneMeshOverride so the parse cache key lines up.
            if (meshOverrides != null)
            {
                foreach (var ov in meshOverrides)
                {
                    ct.ThrowIfCancellationRequested();
                    PrewarmMeshOverride(ov, skeletonNif, skelDiskPath, ct);
                }
            }
        }
        finally
        {
            skeletonNif?.Dispose();
        }
    }

    private void PrewarmMeshOverride(MeshOverride ov, nifly.NifFile? skeletonNif,
        string? skelDiskPath, System.Threading.CancellationToken ct)
    {
        if (ov == null || string.IsNullOrWhiteSpace(ov.MeshPath)) return;
        ct.ThrowIfCancellationRequested();

        string? diskPath = _assetResolver.ResolveAssetPath(ov.MeshPath);
        if (diskPath == null) return;

        // null bipedBodyPart matches ApplyOneMeshOverride (an override NIF is the
        // source for one slot; keep all its shapes — no dismember filter) so the
        // parse cache key matches the render's later BuildFromFile.
        var meshes = MeshBuilder.BuildFromFile(diskPath, skeletonNif, skelDiskPath, bipedBodyPart: null, ct: ct);

        string? weight0 = TryGetWeightZeroPath(ov.MeshPath);
        if (weight0 != null)
        {
            string? d0 = _assetResolver.ResolveAssetPath(weight0);
            if (d0 != null) MeshBuilder.BuildFromFile(d0, skeletonNif, skelDiskPath, bipedBodyPart: null, ct: ct);
        }

        // Effective textures mirror ApplyOneMeshOverride: ov.Textures override the
        // NIF's embedded set on ALL slots (not gated on shader type like base TXST).
        foreach (var built in meshes)
        {
            ct.ThrowIfCancellationRequested();
            Dictionary<int, string> effective;
            if (ov.Textures != null && ov.Textures.Count > 0)
            {
                effective = new Dictionary<int, string>(built.TexturePaths);
                foreach (var kv in ov.Textures) effective[kv.Key] = kv.Value;
            }
            else
            {
                effective = built.TexturePaths;
            }

            foreach (var (slot, path) in effective)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (slot == 4)
                {
                    if (GetOrLoadDdsCubemap(path) == null) GetOrLoadDdsPixels(path);
                }
                else
                {
                    GetOrLoadDdsPixels(path);
                }
            }
        }
    }

    private void PrewarmPart(string bodyPart, string? gamePath, ResolvedNpcMeshPaths paths,
        nifly.NifFile? skeletonNif, string? skelDiskPath, System.Threading.CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gamePath)) return;
        ct.ThrowIfCancellationRequested();

        string? diskPath = _assetResolver.ResolveAssetPath(gamePath);
        if (diskPath == null) return;

        // Parse the weight-1 NIF — warms the parsed-NIF LRU keyed on
        // (path, mtime, skeletonPath, mtime, bodyPart), the same key the render's
        // BuildFromFile uses, and returns the shapes whose textures we decode below.
        var meshes = MeshBuilder.BuildFromFile(diskPath, skeletonNif, skelDiskPath, bodyPart, ct);

        // Non-head parts ship a _0 weight companion that LoadAllMeshParts also
        // parses (for the weight morph); warm it too so that parse is a cache hit.
        if (bodyPart != "Head")
        {
            string? weight0 = TryGetWeightZeroPath(gamePath);
            if (weight0 != null)
            {
                string? d0 = _assetResolver.ResolveAssetPath(weight0);
                if (d0 != null) MeshBuilder.BuildFromFile(d0, skeletonNif, skelDiskPath, bodyPart, ct);
            }
        }

        // Head shapes apply no ARMA TXST overrides (those target body-part skin);
        // pass them only for the other parts, matching InstallOneShapeTextures.
        Dictionary<int, string>? txst = null;
        if (bodyPart != "Head") paths.TxstTextures.TryGetValue(bodyPart, out txst);

        foreach (var built in meshes)
        {
            ct.ThrowIfCancellationRequested();
            PrewarmShapeTextures(built, txst);
        }
    }

    private void PrewarmShapeTextures(NifMeshBuilder.BuiltMesh built, Dictionary<int, string>? txstOverrides)
    {
        // Build the effective texture set exactly as InstallOneShapeTextures does:
        // ARMA TXST overrides apply only to skin shapes (BSLSP shader type 5).
        Dictionary<int, string> effective;
        if (txstOverrides != null && built.ShaderType == 5)
        {
            effective = new Dictionary<int, string>(built.TexturePaths);
            foreach (var (slot, path) in txstOverrides) effective[slot] = path;
        }
        else
        {
            effective = built.TexturePaths;
        }

        foreach (var (slot, path) in effective)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (slot == 4)
            {
                // Env map slot: the render loads it via the cubemap cache first,
                // falling back to a 2D decode — warm the same cache it will read.
                if (GetOrLoadDdsCubemap(path) == null) GetOrLoadDdsPixels(path);
            }
            else
            {
                GetOrLoadDdsPixels(path);
            }
        }
    }

    /// <summary>Derives the weight-0 companion for a NIF path ending in
    /// <c>_1.nif</c>. Mirror of <c>VM_CharacterViewer.TryGetWeightZeroPath</c>.</summary>
    private static string? TryGetWeightZeroPath(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath)) return null;
        const string suffix = "_1.nif";
        if (gamePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return gamePath.Substring(0, gamePath.Length - suffix.Length) + "_0.nif";
        return null;
    }

    /// <summary>
    /// Drops every cached resolved path, parsed NIF, and decoded pixel buffer.
    /// Call when the patcher environment is rebuilt so subsequent viewer loads
    /// re-resolve from the new link cache and re-read NIFs / textures from disk.
    /// </summary>
    public void Clear()
    {
        lock (_meshPathsLock)
        {
            _meshPathsCache.Clear();
            _meshPathsLru.Clear();
            _meshPathsInvalidationToken = null;
        }
        lock (_pixelLock)
        {
            _pixelCache.Clear();
            _pixelLru.Clear();
            _pixelBytes = 0;
        }
        lock (_cubemapLock)
        {
            _cubemapCache.Clear();
            _cubemapLru.Clear();
            _cubemapBytes = 0;
        }
        MeshBuilder.ClearCache();
    }
}
