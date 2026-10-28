using System;
using System.Collections.Generic;

namespace SkyRender.Character;

/// <summary>
/// Process-wide cache of expensive read-only inputs to the 3D preview pipeline.
///
/// Each VM_CharacterViewer is short-lived (e.g. a host may dispose the
/// previous viewer on every selection change to release its GL context), which
/// throws away the per-instance NIF parse cache, resolved-mesh-path data, and
/// decoded DDS pixel buffers. Hoisting all three into a singleton lets a
/// freshly-constructed viewer skip the link-cache traversal, NIF re-parse, and
/// Pfim DDS re-decode when it's loading the same preview NPC the previous
/// viewer just released.
///
/// Composes the core <see cref="AssetCache"/> (<see cref="Assets"/>), which owns the parsed-NIF
/// and decoded-DDS layers below, and adds NPC mesh-path resolution and prewarming. Its asset members
/// forward to <see cref="Assets"/>.
///
/// Three layers:
///   * <see cref="MeshBuilder"/> — owns the parsed-NIF LRU keyed on
///     (nifPath, mtime, skeletonPath, mtime). Shared across all viewers.
///   * <see cref="GetOrResolveMeshPaths"/> — caches the data source's resolved
///     paths keyed on (invalidation-token identity, <see cref="NpcIdentity"/>).
///     The head-override path is applied by the caller after retrieval since
///     it's a per-load decoration, not part of the resolved record chain.
///   * <see cref="GetOrLoadDdsPixels"/> — LRU cache of BGRA32 pixel arrays keyed
///     on the RESOLVED DISK PATH (not the game-relative path — see the method
///     remarks for the cross-variant poisoning that relative keying caused under
///     strict per-mod scopes). GL texture handles are still created per
///     viewer (context-specific), but the Pfim decode + Rgb24→Rgba32 conversion
///     happens once. Switching between previews is the hot path — a typical NPC pulls
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
    private readonly IRenderLogger _logger;
    private readonly RenderLogGate _logGate;
    private readonly INpcMeshDataSource _dataSource;
    private readonly GameAssetResolver _assetResolver;

    /// <summary>The core asset cache (parsed NIFs, decoded DDS pixels) this cache composes. Core
    /// services (<see cref="GlTextureManager"/>, the offscreen renderer) take this directly.</summary>
    public AssetCache Assets { get; }

    private const int MeshPathsCacheMaxEntries = 32;
    private readonly Dictionary<NpcIdentity, ResolvedNpcMeshPaths?> _meshPathsCache = new();
    private readonly LinkedList<NpcIdentity> _meshPathsLru = new();
    private readonly object _meshPathsLock = new();
    private object? _meshPathsInvalidationToken;

    public CharacterPreviewCache(
        INpcMeshDataSource dataSource,
        GameAssetResolver assetResolver,
        IRenderLogger logger,
        RenderLogGate logGate,
        ICharacterViewerSettings? settings = null)
    {
        _dataSource = dataSource;
        _assetResolver = assetResolver;
        _logger = logger;
        _logGate = logGate;
        Assets = new AssetCache(assetResolver, logger, logGate, settings);
    }

    // ── Asset members, forwarded to Assets ──────────────────────────────────

    /// <summary>See <see cref="AssetCache.DdsMaximumFinalMipDimension"/>.</summary>
    public int? DdsMaximumFinalMipDimension
    {
        get => Assets.DdsMaximumFinalMipDimension;
        init => Assets.SetDdsMaximumFinalMipDimension(value);
    }

    /// <summary>See <see cref="AssetCache.DecodeObserver"/>.</summary>
    public Action<DdsDecodeEvent>? DecodeObserver
    {
        get => Assets.DecodeObserver;
        set => Assets.DecodeObserver = value;
    }

    /// <summary>See <see cref="AssetCache.DecodeReason"/>.</summary>
    public static AssetCache.DecodeReasonScope DecodeReason(string reason) => AssetCache.DecodeReason(reason);

    public long TotalDecodeCalls => Assets.TotalDecodeCalls;
    public long TotalDecodedBytes => Assets.TotalDecodedBytes;
    public long CachedDecodedBytes => Assets.CachedDecodedBytes;
    public NifMeshBuilder MeshBuilder => Assets.MeshBuilder;
    public double TotalDecodeMs => Assets.TotalDecodeMs;
    public double ThreadDecodeMs => Assets.ThreadDecodeMs;
    public string? ResolveAssetPath(string relativeGamePath) => Assets.ResolveAssetPath(relativeGamePath);
    public DdsPixels? GetOrLoadDdsPixels(string relativeGamePath) => Assets.GetOrLoadDdsPixels(relativeGamePath);
    public DdsCubemapPixels? GetOrLoadDdsCubemap(string relativeGamePath) => Assets.GetOrLoadDdsCubemap(relativeGamePath);
    public bool IsFullyTransparent(string? relativeGamePath) => Assets.IsFullyTransparent(relativeGamePath);

    // ── NPC mesh paths ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns cached <see cref="ResolvedNpcMeshPaths"/> for this NPC under the
    /// data source's current invalidation token, or resolves and caches if absent.
    /// Null results are NOT cached — under the strict-scopes contract a miss
    /// can legitimately differ across scopes (one render's vanilla-only scope
    /// failing to resolve doesn't tell us what a later mod-scoped render would
    /// produce). Treating misses as fresh-each-time avoids cross-scope cache
    /// poisoning.
    /// </summary>
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

    // ── Prewarm ─────────────────────────────────────────────────────────────

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
                // Uploaded through the ordinary LoadTexture route (tinting is on
                // the GPU), so an eligible compressed FaceTint needs no decode.
                using (AssetCache.DecodeReason("prewarm-facetint"))
                    PrewarmTexture(paths.FaceTintPath);
            }

            // Attire / headgear mesh overrides.
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

        var meshSource = _assetResolver.ResolveAssetSource(ov.MeshPath);
        string? diskPath = meshSource.ResolvedDiskPath;
        if (diskPath == null) return;

        // Referencer scoping mirror of ApplyOneMeshOverride — see PrewarmPart's
        // comment. The override NIF reported itself above; its weight-0
        // companion and texture warms below are its internals.
        using var __fallbackReportMute = meshSource.ViaDataFolderFallback
            ? _assetResolver.PushDataFolderFallbackReportSuppression()
            : null;

        // null bipedBodyPart matches ApplyOneMeshOverride (an override NIF is the
        // source for one slot; keep all its shapes — no dismember filter) so the
        // parse cache key matches the render's later BuildFromFile.
        var meshes = Assets.MeshBuilder.BuildFromFile(diskPath, skeletonNif, skelDiskPath, bipedBodyPart: null, ct: ct);

        string? weight0 = TryGetWeightZeroPath(ov.MeshPath);
        if (weight0 != null)
        {
            string? d0 = _assetResolver.ResolveAssetPath(weight0);
            if (d0 != null) Assets.MeshBuilder.BuildFromFile(d0, skeletonNif, skelDiskPath, bipedBodyPart: null, ct: ct);
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
                    if (Assets.GetOrLoadDdsCubemap(path) == null) PrewarmTexture(path);
                }
                else
                {
                    PrewarmTexture(path, slot == 0);
                }
            }
        }

        // Per-shape AlternateTextures (either channel). Decoding doesn't need the
        // render's shape matching — warm every referenced path once and let the
        // DDS cache dedupe; whichever shapes they land on at render time hit warm.
        void WarmSlotPaths(IReadOnlyDictionary<int, string> slotPaths)
        {
            foreach (var (slot, path) in slotPaths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (slot == 4)
                {
                    if (Assets.GetOrLoadDdsCubemap(path) == null) PrewarmTexture(path);
                }
                else
                {
                    PrewarmTexture(path, slot == 0);
                }
            }
        }
        if (ov.AlternateTextures != null)
        {
            foreach (var spec in ov.AlternateTextures)
            {
                ct.ThrowIfCancellationRequested();
                WarmSlotPaths(spec.Textures);
            }
        }
        else if (ov.ShapeTextures != null)
        {
            foreach (var slotPaths in ov.ShapeTextures.Values)
            {
                ct.ThrowIfCancellationRequested();
                WarmSlotPaths(slotPaths);
            }
        }
    }

    private void PrewarmPart(string bodyPart, string? gamePath, ResolvedNpcMeshPaths paths,
        nifly.NifFile? skeletonNif, string? skelDiskPath, System.Threading.CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gamePath)) return;
        ct.ThrowIfCancellationRequested();

        var meshSource = _assetResolver.ResolveAssetSource(gamePath);
        string? diskPath = meshSource.ResolvedDiskPath;
        if (diskPath == null) return;

        // Referencer scoping for the data-folder-fallback report, mirroring
        // VM_CharacterViewer.InstallOneShape: a fallback-resolved part NIF (the
        // user's body/skin baseline) reported itself above; the textures it
        // references are its own internals, so mute their reports here too.
        // Prewarm and render feed ONE shared per-request set, so without this
        // mirror the prewarm flow would re-report exactly what the render
        // suppresses. Null token (no-op) for in-scope NIFs.
        using var __fallbackReportMute = meshSource.ViaDataFolderFallback
            ? _assetResolver.PushDataFolderFallbackReportSuppression()
            : null;

        // Parse the weight-1 NIF — warms the parsed-NIF LRU keyed on
        // (path, mtime, skeletonPath, mtime, bodyPart), the same key the render's
        // BuildFromFile uses, and returns the shapes whose textures we decode below.
        var meshes = Assets.MeshBuilder.BuildFromFile(diskPath, skeletonNif, skelDiskPath, bodyPart, ct);

        // Non-head parts ship a _0 weight companion that LoadAllMeshParts also
        // parses (for the weight morph); warm it too so that parse is a cache hit.
        if (bodyPart != "Head")
        {
            string? weight0 = TryGetWeightZeroPath(gamePath);
            if (weight0 != null)
            {
                string? d0 = _assetResolver.ResolveAssetPath(weight0);
                if (d0 != null) Assets.MeshBuilder.BuildFromFile(d0, skeletonNif, skelDiskPath, bodyPart, ct);
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
                if (Assets.GetOrLoadDdsCubemap(path) == null) PrewarmTexture(path);
            }
            else
            {
                PrewarmTexture(path, slot == 0);
            }
        }
    }

    // A diffuse also warms its collision-proxy transparency verdict, which
    // DdsAlphaProbe usually settles from one block (an unproven texture is decoded
    // here, off the render thread, as before). Eligible 2D slots then stay lazy:
    // inspection reads only the header, checks the payload length, and retains no
    // second CPU block cache. If the current GL context lacks support, LoadTexture
    // performs the ordinary lazy decode. The decoded route still decodes here for
    // the upload, exactly as before.
    private void PrewarmTexture(string path, bool isDiffuse = false)
    {
        if (isDiffuse)
        {
            using (AssetCache.DecodeReason("prewarm-diffuse"))
                Assets.IsFullyTransparent(path);
        }
        if (Assets.DdsMaximumFinalMipDimension is int maximum)
        {
            string? disk = Assets.ResolveAssetPath(path);
            if (disk != null && CompressedDds.Inspect(disk, maximum, out _) == DdsReadStatus.Eligible) return;
        }
        using (AssetCache.DecodeReason(isDiffuse ? "prewarm-diffuse" : "prewarm"))
            Assets.GetOrLoadDdsPixels(path);
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
    /// Call when the host's game environment is rebuilt so subsequent viewer loads
    /// re-resolve from the new link cache and re-read NIFs / textures from disk.
    /// </summary>
    public void Clear()
    {
        ClearMeshPaths();
        Assets.Clear();
    }

    /// <summary>Drops only the resolved NPC mesh paths (the composed <see cref="Assets"/> are left
    /// alone), for a caller that clears the asset cache itself.</summary>
    internal void ClearMeshPaths()
    {
        lock (_meshPathsLock)
        {
            _meshPathsCache.Clear();
            _meshPathsLru.Clear();
            _meshPathsInvalidationToken = null;
        }
    }
}
