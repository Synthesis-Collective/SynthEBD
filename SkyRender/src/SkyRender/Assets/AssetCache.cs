using System;
using System.Collections.Generic;
using System.IO;
using Pfim;

namespace SkyRender;

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
/// image. See <see cref="AssetCache.GetOrLoadDdsCubemap"/>.
/// </summary>
public readonly record struct DdsCubemapPixels(byte[][] Faces, int Width, int Height);

/// <summary>One CPU DDS decode (a pixel- or cubemap-cache miss), reported to
/// <see cref="AssetCache.DecodeObserver"/>. <see cref="Reason"/> is the innermost
/// <see cref="AssetCache.DecodeReason"/> scope on the decoding thread, or
/// "unattributed".</summary>
public readonly record struct DdsDecodeEvent(
    string RelativeGamePath, string DiskPath, string Reason, int Width, int Height,
    double Milliseconds, bool Cubemap);

/// <summary>
/// Process-wide cache of the expensive read-only asset inputs to rendering: the shared
/// <see cref="NifMeshBuilder"/> (parsed-NIF LRU) and the decoded-DDS pixel and cubemap caches,
/// plus the transparency verdicts and decode telemetry. Holds nothing about NPCs; the character
/// tier's <see cref="CharacterPreviewCache"/> composes one and adds NPC mesh-path resolution and
/// prewarming.
///
/// Two layers:
///   * <see cref="MeshBuilder"/> — owns the parsed-NIF LRU keyed on
///     (nifPath, mtime, skeletonPath, mtime). Shared across all viewers.
///   * <see cref="GetOrLoadDdsPixels"/> — LRU cache of BGRA32 pixel arrays keyed
///     on the RESOLVED DISK PATH (not the game-relative path — see the method
///     remarks for the cross-variant poisoning that relative keying caused under
///     strict per-mod scopes). GL texture handles are still created per
///     viewer (context-specific), but the Pfim decode + Rgb24→Rgba32 conversion
///     happens once.
///
/// Invalidation: the mesh LRU self-invalidates via file mtimes. The pixel cache doesn't
/// track mtimes — call <see cref="Clear"/> from an explicit env-refresh hook if textures
/// may have been edited on disk between sessions.
/// </summary>
public class AssetCache
{
    private int? _ddsMaximumFinalMipDimension;
    /// <summary>Frozen for this cache's lifetime. Defaults to <see cref="DdsMipPolicy.Parse"/> of the
    /// <c>CVR_DDS_MAX_FINAL_MIP_DIMENSION</c> environment variable — unset means
    /// <see cref="DdsMipPolicy.DefaultMaximumFinalMipDimension"/>; an invalid value logs and falls
    /// back to decoded. An object initializer overrides it. Null uses decoded pixels and generated mips.
    /// Otherwise, the last supplied DDS mip must have both dimensions at most this value.
    /// One requires a full chain; int.MaxValue permits single-level textures. Sampling clamps
    /// to the last supplied mip. This is an eligibility limit, not a visual error bound.</summary>
    public int? DdsMaximumFinalMipDimension
    {
        get => _ddsMaximumFinalMipDimension;
        init => SetDdsMaximumFinalMipDimension(value);
    }

    /// <summary>Applies the <see cref="DdsMaximumFinalMipDimension"/> init rule; lets a composing cache
    /// forward its own initializer here.</summary>
    internal void SetDdsMaximumFinalMipDimension(int? value) =>
        _ddsMaximumFinalMipDimension = value is <= 0
            ? throw new ArgumentOutOfRangeException(nameof(value), "Final mip dimension must be positive.") : value;

    /// <summary>Optional host profiling hook, invoked on the decoding thread after each
    /// successful decode. Must be cheap; exceptions are swallowed. Null (the default)
    /// costs one null check per decode.</summary>
    public Action<DdsDecodeEvent>? DecodeObserver { get; set; }

    [ThreadStatic] private static string? _decodeReason;

    /// <summary>Tags decodes made on this thread with <paramref name="reason"/> until the
    /// returned scope is disposed (restores the previous tag, so scopes nest).</summary>
    public static DecodeReasonScope DecodeReason(string reason)
    {
        var previous = _decodeReason;
        _decodeReason = reason;
        return new DecodeReasonScope(previous);
    }

    public readonly struct DecodeReasonScope : IDisposable
    {
        private readonly string? _previous;
        internal DecodeReasonScope(string? previous) { _previous = previous; }
        public void Dispose() => _decodeReason = _previous;
    }

    private void NotifyDecode(string relativeGamePath, string diskPath, int width, int height,
        long ticks, bool cubemap)
    {
        var observer = DecodeObserver;
        if (observer == null) return;
        try
        {
            observer(new DdsDecodeEvent(relativeGamePath, diskPath, _decodeReason ?? "unattributed",
                width, height, ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency, cubemap));
        }
        catch
        {
            // Profiling must never disrupt a load.
        }
    }

    private long _decodeCalls, _decodedBytes;
    public long TotalDecodeCalls => System.Threading.Interlocked.Read(ref _decodeCalls);
    public long TotalDecodedBytes => System.Threading.Interlocked.Read(ref _decodedBytes);
    public long CachedDecodedBytes { get { lock (_pixelLock) return _pixelBytes; } }

    private readonly IRenderLogger _logger;
    private readonly RenderLogGate _logGate;
    private readonly GameAssetResolver _assetResolver;

    public NifMeshBuilder MeshBuilder { get; }

    // Decoded BGRA32 pixel buffers, evicted by a dynamic byte budget rather than a
    // fixed entry count: a 4K texture is ~16x the bytes of a 1K one, so a count cap
    // could mean anywhere from a few hundred MB to several GB of resident pixels.
    // The budget tracks free system RAM (see SystemMemoryBudget) so the cache grows
    // to use spare memory on a big machine and shrinks on a constrained one. This is
    // by far the dominant in-RAM cache (a 50-NPC prewarm measured ~2 GB of resident
    // pixels vs tens of MB for meshes), so it gets the largest share (0.75 of the 0.85
    // total) of the collective free-RAM budget; the user's FreeRamCachePercent scales
    // that total (and the matching ceiling) while this cache keeps its 75:9:1 ratio.
    private const long PixelCacheMinBudgetBytes = 64L * 1024 * 1024;        // 64 MB floor
    private const double PixelCacheFreeRamFraction = 0.75;
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
    // pixel cache but with a tiny share of free RAM: a typical NPC pulls one envmap
    // and many share the default cubemap, so the working set is negligible (a 50-NPC
    // prewarm measured ~0.1 MB).
    private const long CubemapCacheMinBudgetBytes = 16L * 1024 * 1024;       // 16 MB floor
    private const double CubemapCacheFreeRamFraction = 0.01;
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

    public AssetCache(
        GameAssetResolver assetResolver,
        IRenderLogger logger,
        RenderLogGate logGate,
        IRenderSettings? settings = null)
    {
        _assetResolver = assetResolver;
        _logger = logger;
        try { DdsMaximumFinalMipDimension = DdsMipPolicy.Parse(Environment.GetEnvironmentVariable(DdsMipPolicy.EnvironmentVariable)); }
        catch (ArgumentException ex) { logger.LogError($"{DdsMipPolicy.EnvironmentVariable}: {ex.Message} Using decoded textures."); }
        _logGate = logGate;
        _settings = settings;
        MeshBuilder = new NifMeshBuilder(logger, logGate, assetResolver, settings);

        _pixelBudgetBytes = SystemMemoryBudget.Compute(
            CacheMode, FixedPoolBytes, FreeRamPercent,
            0, PixelCacheFreeRamFraction, PixelCacheMinBudgetBytes);
        _cubemapBudgetBytes = SystemMemoryBudget.Compute(
            CacheMode, FixedPoolBytes, FreeRamPercent,
            0, CubemapCacheFreeRamFraction, CubemapCacheMinBudgetBytes);
    }

    // Read live each repoll so a host-side cache-mode change takes effect within a few renders. Null
    // settings (host didn't supply them) => the historical % Free RAM behaviour.
    private readonly IRenderSettings? _settings;
    private RenderCacheMode CacheMode => _settings?.CacheMode ?? RenderCacheMode.PercentFreeRam;
    private long FixedPoolBytes => _settings?.FixedCacheBudgetBytes ?? 0;
    private double FreeRamPercent => _settings?.FreeRamCachePercent ?? SystemMemoryBudget.BaselineFreeRamPercent;

    /// <summary>Resolves a game-relative path to its on-disk file under the
    /// current scope chain (extracting from a BSA into the cache dir if needed),
    /// or null if unresolved. Exposed so the resident GL texture cache can key on
    /// the resolved disk path — correct across the strict per-mod scopes, where
    /// the same game-path can map to different files.</summary>
    public string? ResolveAssetPath(string relativeGamePath) =>
        _assetResolver.ResolveAssetPath(relativeGamePath);

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
    /// Entries are keyed on the RESOLVED DISK PATH, not the game-relative path.
    /// Under strict per-mod scopes the same relative path legitimately resolves
    /// to different files per scope chain (e.g. 17 variants of one mod family
    /// each shipping their own 'foxglovehair.dds' / FaceTint DDS under the same
    /// relative path); a relative-path key let whichever variant decoded first
    /// poison every sibling variant's renders — and the offscreen resident GL
    /// cache (correctly keyed on disk path) then persisted the wrong pixels
    /// under the right key until app restart. Disk-path keying keeps the
    /// cross-viewer sharing benefit (shared Core / vanilla assets resolve to
    /// one disk file) while making cross-variant collisions impossible. The
    /// resolve itself is cheap on the hot path — GameAssetResolver's
    /// scope-aware resolve cache serves repeat lookups.
    ///
    /// The returned <see cref="DdsPixels.Data"/> array is treated as immutable
    /// by the cache — callers that blend tints on the CPU must clone before
    /// mutating. <see cref="GlTextureManager"/>'s tint paths do exactly that.
    /// </summary>
    public DdsPixels? GetOrLoadDdsPixels(string relativeGamePath)
    {
        if (string.IsNullOrWhiteSpace(relativeGamePath)) return null;

        // Resolve BEFORE the cache lookup so the key is the physical file, not
        // the scope-dependent relative path (see method remarks). Unresolved =
        // miss, uncached (same contract as a failed decode).
        string? diskPath = _assetResolver.ResolveAssetPath(relativeGamePath);
        if (diskPath == null) return null;

        lock (_pixelLock)
        {
            if (_pixelCache.TryGetValue(diskPath, out var cached))
            {
                _pixelLru.Remove(diskPath);
                _pixelLru.AddFirst(diskPath);
                if (_logGate != null && _logGate.Verbose)
                    _logger?.LogMessage("SkyRender: DdsPixels cache hit for '" +
                        relativeGamePath + "' -> '" + diskPath + "'");
                return cached;
            }
        }

        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var decoded = DecodeDds(diskPath, relativeGamePath);
        long dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        System.Threading.Interlocked.Add(ref _decodeTicks, dt);
        _threadDecodeTicks += dt;

        // Don't cache misses — see method-level remark.
        if (decoded == null) return null;
        NotifyDecode(relativeGamePath, diskPath, decoded.Value.Width, decoded.Value.Height, dt, cubemap: false);

        lock (_pixelLock)
        {
            // Re-check in case a parallel caller already populated the entry.
            if (_pixelCache.TryGetValue(diskPath, out var racedCached))
            {
                _pixelLru.Remove(diskPath);
                _pixelLru.AddFirst(diskPath);
                return racedCached;
            }

            _pixelCache[diskPath] = decoded;
            _pixelLru.AddFirst(diskPath);
            _pixelBytes += decoded.Value.Data.Length;

            // Periodically re-evaluate the budget against current free RAM so the
            // cache expands into spare memory and contracts when it tightens.
            if (++_pixelAddsSinceRepoll >= PixelCacheRepollEveryAdds)
            {
                _pixelAddsSinceRepoll = 0;
                _pixelBudgetBytes = SystemMemoryBudget.Compute(
                    CacheMode, FixedPoolBytes, FreeRamPercent,
                    _pixelBytes, PixelCacheFreeRamFraction, PixelCacheMinBudgetBytes);
            }

            if (CacheBudgetDiag.Enabled)
                CacheBudgetDiag.ReportInsert(CacheBudgetDiag.Pixel,
                    decoded.Value.Data.Length, _pixelBytes, _pixelBudgetBytes);

            // Evict LRU until within budget, but always keep the entry just added
            // (a single texture larger than the whole budget must not loop forever).
            while (_pixelBytes > _pixelBudgetBytes && _pixelLru.Count > 1)
            {
                var oldestKey = _pixelLru.Last!.Value;
                _pixelLru.RemoveLast();
                if (_pixelCache.TryGetValue(oldestKey, out var evicted) && evicted.HasValue)
                {
                    _pixelBytes -= evicted.Value.Data.Length;
                    if (CacheBudgetDiag.Enabled)
                        CacheBudgetDiag.ReportEviction(CacheBudgetDiag.Pixel,
                            evicted.Value.Data.Length, _pixelBytes);
                }
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

        // Keyed on the resolved disk path for the same reason as the pixel
        // cache (see GetOrLoadDdsPixels): the verdict belongs to the physical
        // file, not the scope-dependent relative path — and this cache never
        // evicts, so a cross-variant wrong verdict would stick all session.
        string? diskPath = _assetResolver.ResolveAssetPath(relativeGamePath);
        if (diskPath == null) return false;

        lock (_transparencyLock)
            if (_fullyTransparentCache.TryGetValue(diskPath, out bool cached))
                return cached;

        // A block-level proof of one non-zero alpha texel settles "not fully
        // transparent" exactly, without decoding (an ordinary opaque diffuse is
        // proven by its first block). Anything unproven takes the full decode.
        bool verdict;
        if (DdsAlphaProbe.ProvesNonZeroAlpha(diskPath))
        {
            verdict = false;
        }
        else
        {
            DdsPixels? pixels;
            using (DecodeReason("alpha-check"))
                pixels = GetOrLoadDdsPixels(relativeGamePath);
            verdict = pixels != null && IsAllAlphaZero(pixels.Value.Data);
        }

        lock (_transparencyLock)
            _fullyTransparentCache[diskPath] = verdict;
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

    private DdsPixels? DecodeDds(string resolved, string relativeGamePath)
    {
        if (!File.Exists(resolved)) return null;

        try
        {
            System.Threading.Interlocked.Increment(ref _decodeCalls);
            using var image = Pfimage.FromFile(resolved);
            byte[]? bgra = PfimageToBgra32(image, relativeGamePath);
            if (bgra == null) return null;
            System.Threading.Interlocked.Add(ref _decodedBytes, bgra.LongLength);
            return new DdsPixels(bgra, image.Width, image.Height);
        }
        catch (Exception ex)
        {
            if (_logGate != null && _logGate.Verbose)
                _logger?.LogMessage("SkyRender: Failed to decode '" +
                    relativeGamePath + "': " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Returns six BGRA32 face buffers for the given game-relative cubemap DDS,
    /// or null if the file is missing, unreadable, or not a complete cubemap.
    /// Caching mirrors <see cref="GetOrLoadDdsPixels"/> — successful results
    /// are cached, failures are not, and entries are keyed on the RESOLVED DISK
    /// PATH (see that method's remarks for the cross-variant poisoning a
    /// relative-path key caused).
    /// </summary>
    public DdsCubemapPixels? GetOrLoadDdsCubemap(string relativeGamePath)
    {
        if (string.IsNullOrWhiteSpace(relativeGamePath)) return null;

        string? diskPath = _assetResolver.ResolveAssetPath(relativeGamePath);
        if (diskPath == null) return null;

        lock (_cubemapLock)
        {
            if (_cubemapCache.TryGetValue(diskPath, out var cached))
            {
                _cubemapLru.Remove(diskPath);
                _cubemapLru.AddFirst(diskPath);
                if (_logGate != null && _logGate.Verbose)
                    _logger?.LogMessage("SkyRender: DdsCubemap cache hit for '" +
                        relativeGamePath + "' -> '" + diskPath + "'");
                return cached;
            }
        }

        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var decoded = DecodeDdsCubemap(diskPath, relativeGamePath);
        long dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        System.Threading.Interlocked.Add(ref _decodeTicks, dt);
        _threadDecodeTicks += dt;
        if (decoded == null) return null;
        NotifyDecode(relativeGamePath, diskPath, decoded.Value.Width, decoded.Value.Height, dt, cubemap: true);

        lock (_cubemapLock)
        {
            if (_cubemapCache.TryGetValue(diskPath, out var racedCached))
            {
                _cubemapLru.Remove(diskPath);
                _cubemapLru.AddFirst(diskPath);
                return racedCached;
            }

            _cubemapCache[diskPath] = decoded;
            _cubemapLru.AddFirst(diskPath);
            _cubemapBytes += CubemapByteSize(decoded.Value);

            if (++_cubemapAddsSinceRepoll >= CubemapCacheRepollEveryAdds)
            {
                _cubemapAddsSinceRepoll = 0;
                _cubemapBudgetBytes = SystemMemoryBudget.Compute(
                    CacheMode, FixedPoolBytes, FreeRamPercent,
                    _cubemapBytes, CubemapCacheFreeRamFraction, CubemapCacheMinBudgetBytes);
            }

            if (CacheBudgetDiag.Enabled)
                CacheBudgetDiag.ReportInsert(CacheBudgetDiag.Cubemap,
                    CubemapByteSize(decoded.Value), _cubemapBytes, _cubemapBudgetBytes);

            while (_cubemapBytes > _cubemapBudgetBytes && _cubemapLru.Count > 1)
            {
                var oldestKey = _cubemapLru.Last!.Value;
                _cubemapLru.RemoveLast();
                if (_cubemapCache.TryGetValue(oldestKey, out var evicted) && evicted.HasValue)
                {
                    _cubemapBytes -= CubemapByteSize(evicted.Value);
                    if (CacheBudgetDiag.Enabled)
                        CacheBudgetDiag.ReportEviction(CacheBudgetDiag.Cubemap,
                            CubemapByteSize(evicted.Value), _cubemapBytes);
                }
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
    private DdsCubemapPixels? DecodeDdsCubemap(string resolved, string relativeGamePath)
    {
        if (!File.Exists(resolved)) return null;

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
                System.Threading.Interlocked.Increment(ref _decodeCalls);
                using var image = Pfimage.FromStream(stream);
                byte[]? bgra = PfimageToBgra32(image, relativeGamePath);
                if (bgra != null) System.Threading.Interlocked.Add(ref _decodedBytes, bgra.LongLength);
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
                        _logger?.LogMessage("SkyRender: Cubemap face " + i +
                            " has mismatched size for '" + relativeGamePath + "'");
                    return null;
                }
            }

            return new DdsCubemapPixels(faces, faceWidth, faceHeight);
        }
        catch (Exception ex)
        {
            if (_logGate != null && _logGate.Verbose)
                _logger?.LogMessage("SkyRender: Failed to decode cubemap '" +
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
                    _logger?.LogMessage("SkyRender: Unsupported DDS format " +
                        image.Format + " for '" + relativeGamePath + "'");
                return null;
        }
    }

    /// <summary>
    /// Drops every parsed NIF and decoded pixel buffer. Call when the host's
    /// environment is rebuilt so subsequent loads re-read NIFs / textures from disk.
    /// </summary>
    public void Clear()
    {
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
        if (CacheBudgetDiag.Enabled)
        {
            CacheBudgetDiag.ReportClear(CacheBudgetDiag.Pixel);
            CacheBudgetDiag.ReportClear(CacheBudgetDiag.Cubemap);
        }
        MeshBuilder.ClearCache();
    }
}
