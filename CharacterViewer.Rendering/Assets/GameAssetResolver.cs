using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CharacterViewer.Rendering;

/// <summary>
/// Describes where a resolved asset originated.
/// </summary>
public enum AssetOriginKind
{
    NotFound,
    Loose,
    Bsa
}

/// <summary>
/// Records the origin of a resolved asset so callers can present it
/// (e.g. in a hover tooltip) without re-resolving or probing the file system.
/// </summary>
public sealed record AssetSource(
    AssetOriginKind Kind,
    string GamePath,
    string? ResolvedDiskPath,
    string? LoosePath,
    string? BsaPath,
    string? InternalBsaPath)
{
    public static AssetSource NotFound(string gamePath) =>
        new(AssetOriginKind.NotFound, gamePath, null, null, null, null);
}

/// <summary>
/// Resolves game-relative asset paths (e.g. "meshes/actors/character/...")
/// to actual file paths on disk. Checks loose files first, then falls back
/// to BSA archive extraction with a persistent temp cache.
/// </summary>
public class GameAssetResolver
{
    private readonly IDataFolderProvider _dataFolder;
    private readonly IBsaArchiveProvider _bsaProvider;
    private readonly ICharacterViewerLogger _logger;

    /// <summary>
    /// Cache of BSA-extracted files so repeated lookups don't re-extract.
    /// Key: composite of source-BSA path + normalized game-relative path
    /// (see <see cref="MakeExtractionCacheKey"/>); Value: extracted disk path.
    /// The BSA path is part of the key because the SAME relative path can
    /// resolve to different physical files depending on which BSA the strict
    /// scope chain selects — caching by relative path alone caused vanilla
    /// FaceGen to leak into mod-scoped renders once any earlier render
    /// extracted the vanilla copy first.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _extractionCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parallel cache of BSA-origin metadata so repeat resolves of the same
    /// asset return the full <see cref="AssetSource"/> (including BSA path
    /// and internal sub-path) without re-querying the BSA handler.
    /// </summary>
    private readonly ConcurrentDictionary<string, AssetSource> _bsaSourceCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cache of loose-file resolutions and definitive misses so repeat texture
    /// lookups on the viewer hot path skip the <see cref="File.Exists"/> syscall
    /// (and the BSA traversal for not-found) on every call. A single NPC load
    /// requests ~20 unique paths across ~8 texture slots per mesh; preset
    /// switches re-request the exact same paths, so the hit rate is near-total
    /// on the second and subsequent loads.
    /// </summary>
    private readonly ConcurrentDictionary<string, AssetSource> _looseSourceCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Scope-aware resolve cache for the strict two-phase scope chain
    /// (<see cref="ResolveViaScopes"/>). Key = signature(ordered scopes) + both
    /// vanilla-loose toggles + normalized path, so a hit is only ever returned for
    /// an identical resolution context — different scope chains / toggles produce
    /// different keys. That's what makes caching safe here where the path-only
    /// <see cref="_looseSourceCache"/> is not: under strict scopes the same path can
    /// resolve to different files, so path-only keying would poison across scopes.
    /// Both hits and definitive misses are cached; a hit re-validates the on-disk
    /// file still exists before returning (a cleared BSA extraction or removed loose
    /// file falls through to a full re-resolve). Cleared on env change and on
    /// <see cref="ClearExtractedFiles"/>.
    /// </summary>
    private readonly ConcurrentDictionary<string, AssetSource> _scopedResolveCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Memoizes a scope set's signature per list reference, so it's hashed
    /// once per distinct scope list (built once per render and immutable for its
    /// lifetime) rather than on every resolve. Two different list instances with
    /// identical content still produce the same signature, so the resolve cache is
    /// shared across renders that use the same scope set, not just within one.</summary>
    private readonly ConditionalWeakTable<IReadOnlyList<RenderScope>, string> _scopeSetSigCache = new();

    private readonly string _extractionDir;

    /// <summary>
    /// Per-(BSA, normalized-path) lock objects so concurrent resolutions for
    /// the same asset don't both try to extract to the same destination file.
    /// Keyed the same way as <see cref="_extractionCache"/> — a vanilla
    /// extraction and a mod-BSA extraction of the same relative path land in
    /// different on-disk paths and therefore shouldn't share a lock.
    /// </summary>
    private readonly ConcurrentDictionary<string, object> _extractionLocks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stable per-process map from a containing-BSA absolute path to a short
    /// directory token used to scope its extracted files under
    /// <see cref="_extractionDir"/>. Lazily populated; the token is a SHA256
    /// prefix so it's stable across sessions (allowing extracted files to
    /// remain reusable on disk between runs) and never collides in practice.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _bsaPathTokens =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly CharacterViewerLogGate _logGate;

    /// <summary>
    /// Optional priority-ordered loose-file search paths consulted BEFORE
    /// <see cref="IDataFolderProvider.DataFolderPath"/> for the active
    /// render or load. Pushed via <see cref="PushScopes"/> by the offscreen
    /// renderer (per-render) and <see cref="VM_CharacterViewer"/> (per-load,
    /// re-pushed inside per-tick installs). Restored at end-of-scope so
    /// renders don't leak between calls.
    ///
    /// <para>Backed by <see cref="AsyncLocal{T}"/> so the value flows through
    /// <c>await</c> continuations and <c>Task.Run</c> for off-thread NIF parsing,
    /// while staying isolated between concurrent renders on different async
    /// flows. The earlier volatile-field design shared a single value across
    /// every flow — a 3D preview opening mid-mugshot-batch could overwrite the
    /// mugshot's scope chain mid-resolve, producing wrong textures or wireframes
    /// that the path-keyed pixel cache then poisoned. AsyncLocal storage plus
    /// a per-render <see cref="PushScopes"/> bracket eliminates the race.</para>
    /// </summary>
    private readonly AsyncLocal<IReadOnlyList<string>?> _currentAdditionalFolders = new();

    /// <summary>
    /// Strict two-phase resolution chain. When non-null, OVERRIDES
    /// <see cref="_currentAdditionalFolders"/> + the host's
    /// <see cref="_dataFolder"/> + the broadcast
    /// <see cref="IBsaArchiveProvider.TryLocateInBsa"/>. The resolver runs
    /// loose-phase across all scopes last-to-first, then scoped-BSA-phase
    /// across all scopes last-to-first. No implicit vanilla fallback. See
    /// the contract on <see cref="Offscreen.OffscreenRenderRequest.AdditionalScopes"/>
    /// for the rationale.
    ///
    /// <para>Backed by <see cref="AsyncLocal{T}"/> for the same flow-isolation
    /// reasons as <see cref="_currentAdditionalFolders"/>.</para>
    /// </summary>
    private readonly AsyncLocal<IReadOnlyList<RenderScope>?> _currentAdditionalScopes = new();

    /// <summary>Companion to
    /// <see cref="Offscreen.OffscreenRenderRequest.VanillaLooseOverridesBsa"/>.
    /// Effective default is <c>true</c> (engine behavior); a <c>null</c>
    /// AsyncLocal value reads back as the default. When set false, scope-chain
    /// Phase 1 skips the vanilla scope's loose check so vanilla loose files
    /// don't preempt mod-scoped BSA hits.</summary>
    private readonly AsyncLocal<bool?> _vanillaLooseOverridesBsa = new();

    /// <summary>Companion to
    /// <see cref="Offscreen.OffscreenRenderRequest.VanillaLooseOverridesModLoose"/>.
    /// Effective default is <c>false</c> (matches <see cref="AsyncLocal{T}"/>
    /// of <c>bool</c>). When true, the resolver checks the vanilla scope's
    /// loose folder for non-FaceGen paths before walking mod-folder loose
    /// files.</summary>
    private readonly AsyncLocal<bool> _vanillaLooseOverridesModLoose = new();

    /// <summary>When true, scope-chain resolution runs in ENGINE-ORDER mode:
    /// per-scope blocks (loose then that scope's BSAs) for the non-vanilla
    /// scopes, then vanilla loose, then the broadcast archive tier
    /// (<see cref="IBsaArchiveProvider.TryLocateInBsa"/>), then any
    /// <see cref="RenderScope.DeprioritizeBelowDataFolder"/> scopes — instead
    /// of the strict two-phase walk ending in NotFound. Default false.
    /// Set scene-wide by <see cref="PushScopes"/> (from
    /// <see cref="Offscreen.OffscreenRenderRequest.AllowLoadOrderFallback"/>)
    /// or per-asset by <see cref="PushLoadOrderFallback"/> (see
    /// <see cref="MeshOverride.AllowLoadOrderFallback"/>); backed by
    /// <see cref="AsyncLocal{T}"/> for the same flow-isolation reasons as
    /// the fields above.</summary>
    private readonly AsyncLocal<bool> _allowLoadOrderFallback = new();

    public GameAssetResolver(
        IDataFolderProvider dataFolder,
        IBsaArchiveProvider bsaProvider,
        CharacterViewerLogGate logGate,
        ICharacterViewerLogger logger)
    {
        _dataFolder = dataFolder;
        _bsaProvider = bsaProvider;
        _logGate = logGate;
        _logger = logger;

        // Exe-relative so the cache is visible next to the running EXE
        // rather than buried under %TEMP%. Per-BSA SHA token under here keeps
        // files reusable across sessions (see GetBsaPathToken).
        _extractionDir = Path.Combine(AppContext.BaseDirectory, "CharacterViewerCache");
    }

    private void LogVerbose(string message)
    {
        if (_logGate != null && _logGate.Verbose) _logger?.LogMessage(message);
    }

    /// <summary>Composite cache key: same relative path can resolve to
    /// different physical files depending on which BSA the strict scope
    /// chain selects, so the cache must distinguish source BSAs.</summary>
    private static string MakeExtractionCacheKey(string bsaPath, string normalized)
        => bsaPath + "|" + normalized;

    /// <summary>Builds an on-disk extraction path under
    /// <see cref="_extractionDir"/> scoped by source BSA so a vanilla
    /// extraction and a mod-BSA extraction of the same relative path don't
    /// collide on disk (last-writer-wins would otherwise serve whichever
    /// extracted last to subsequent renders of either source).</summary>
    private string MakeExtractionDestPath(string bsaPath, string normalized)
        => Path.Combine(_extractionDir, GetBsaPathToken(bsaPath), normalized);

    /// <summary>Returns a stable short directory token for a BSA absolute
    /// path. SHA256-prefix derived so it persists across sessions (extracted
    /// files remain reusable on disk between runs) and effectively never
    /// collides — 32 bits ≈ 1 in 4 billion across BSA paths.</summary>
    private string GetBsaPathToken(string bsaPath)
    {
        return _bsaPathTokens.GetOrAdd(bsaPath, p =>
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(p.ToLowerInvariant()));
            return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
        });
    }

    private static void Trace(string message)
    {
        System.Diagnostics.Debug.WriteLine("[GameAssetResolver] " + message);
        System.Diagnostics.Trace.WriteLine("[GameAssetResolver] " + message);
    }

    /// <summary>
    /// Drops every BSA-extracted file currently tracked in the resolver's
    /// cache and clears the cache itself. Hosts can call this on demand
    /// (e.g. a manual "Clear cache" UI action or app shutdown) when the
    /// on-disk extraction directory has grown larger than they want to
    /// keep around. Not safe to call concurrently with a render in
    /// progress — coordinate with the host's render queue / VM lifecycle
    /// to ensure quiescence before invoking. Returns the number of files
    /// actually deleted (best-effort — individual delete failures are
    /// swallowed and counted as misses).
    /// </summary>
    public int ClearExtractedFiles()
    {
        // Snapshot before mutating: ToArray takes a stable view of the
        // ConcurrentDictionary's entries so we don't race a concurrent
        // writer (defensive — the documented contract is single-threaded
        // between renders).
        var snapshot = _extractionCache.ToArray();
        _extractionCache.Clear();
        _bsaSourceCache.Clear();
        _extractionLocks.Clear();
        // Scoped resolve entries point at the extractions we're about to delete; drop
        // them so a later hit doesn't re-validate a dangling path then re-resolve.
        _scopedResolveCache.Clear();

        int deleted = 0;
        foreach (var kv in snapshot)
        {
            try
            {
                if (File.Exists(kv.Value))
                {
                    File.Delete(kv.Value);
                    deleted++;
                }
            }
            catch
            {
                // Best-effort. A still-open file handle (shouldn't happen
                // post-render) is left for OS temp-dir cleanup.
            }
        }

        // Stale parse / pixel cache entries that referenced the now-deleted
        // files are detected on the next access (NifMeshBuilder via mtime
        // mismatch on re-extracted file; the pixel cache by path-hit which
        // remains valid since the same BSA produces identical decoded
        // pixels). Leave those caches alone — re-parsing on the next render
        // is the expected cost of opting into per-render clearing.
        Trace($"ClearExtractedFiles: deleted={deleted}/{snapshot.Length} cached extractions");
        return deleted;
    }

    /// <summary>
    /// Pushes a complete per-flow scoping snapshot onto the resolver and
    /// returns a token whose <see cref="IDisposable.Dispose"/> restores the
    /// prior values. Replaces the older <see cref="SetAdditionalScopes"/>
    /// + <see cref="SetAdditionalFolders"/> + <see cref="SetVanillaLooseOverridesBsa"/>
    /// + <see cref="SetVanillaLooseOverridesModLoose"/> bracket pattern with
    /// a single <c>using</c> scope.
    ///
    /// <para>Two reasons to prefer this over the four <c>Set*</c> calls:
    /// (1) all four values move together, so one push is harder to half-clear
    /// than four; (2) the token snapshots whatever was previously in scope,
    /// so nested pushes (e.g. an offscreen render's outer push + the VM's
    /// inner push during <c>LoadAsync</c>) restore correctly instead of
    /// always clearing to null.</para>
    ///
    /// <para>Backed by <see cref="AsyncLocal{T}"/>, so the pushed values flow
    /// through <c>await</c> continuations and <c>Task.Run</c> for off-thread
    /// NIF parsing. A peer render on a different async flow sees its own
    /// values, not this one's — concurrent renders cannot stomp each other.
    /// The prior volatile-singleton design did not have that property.</para>
    /// </summary>
    public IDisposable PushScopes(
        IReadOnlyList<RenderScope>? scopes,
        IReadOnlyList<string>? folders,
        bool vanillaLooseOverridesBsa,
        bool vanillaLooseOverridesModLoose,
        bool allowLoadOrderFallback = false)
    {
        var snapshot = new ScopeSnapshot(
            _currentAdditionalScopes.Value,
            _currentAdditionalFolders.Value,
            _vanillaLooseOverridesBsa.Value,
            _vanillaLooseOverridesModLoose.Value,
            _allowLoadOrderFallback.Value);
        _currentAdditionalScopes.Value = (scopes == null || scopes.Count == 0) ? null : scopes;
        _currentAdditionalFolders.Value = (folders == null || folders.Count == 0) ? null : folders;
        _vanillaLooseOverridesBsa.Value = vanillaLooseOverridesBsa;
        _vanillaLooseOverridesModLoose.Value = vanillaLooseOverridesModLoose;
        _allowLoadOrderFallback.Value = allowLoadOrderFallback;
        return new ScopeToken(this, snapshot);
    }

    /// <summary>
    /// Pushes <see cref="_allowLoadOrderFallback"/> for the current flow and
    /// returns a token restoring the prior value on dispose.
    ///
    /// <para>Deliberately separate from <see cref="PushScopes"/>: this widens ONE
    /// asset's resolution without restating the scope chain. Re-pushing the chain
    /// just to flip this bit would mean reconstructing it at the call site, and a
    /// caller that reconstructed it as null — easy to do from inside a nested
    /// bracket, where the scene snapshot fields may legitimately be null because
    /// an OUTER push already established the chain — would silently clear the
    /// ambient scopes and resolve the asset against nothing.</para>
    /// </summary>
    public IDisposable PushLoadOrderFallback(bool value)
    {
        bool prev = _allowLoadOrderFallback.Value;
        _allowLoadOrderFallback.Value = value;
        return new LoadOrderFallbackToken(this, prev);
    }

    /// <summary>Restores the captured <see cref="_allowLoadOrderFallback"/> value
    /// on <see cref="Dispose"/>. Idempotent, like <see cref="ScopeToken"/>.</summary>
    private sealed class LoadOrderFallbackToken : IDisposable
    {
        private readonly GameAssetResolver _owner;
        private readonly bool _prev;
        private bool _disposed;

        public LoadOrderFallbackToken(GameAssetResolver owner, bool prev)
        {
            _owner = owner;
            _prev = prev;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner._allowLoadOrderFallback.Value = _prev;
        }
    }

    /// <summary>
    /// Sets the priority-ordered loose-file search paths consulted before the
    /// vanilla Data folder for subsequent resolutions. Pass <c>null</c> (or
    /// an empty list) to clear back to vanilla-only behavior.
    ///
    /// <para>Last entry wins (matches the "later mod folder beats earlier in
    /// the same conceptual mod" convention used by mod managers like MO2).
    /// While additional folders are active, the loose-file resolution cache
    /// is bypassed for both reads and writes — the same relative path may
    /// resolve to different files between renders depending on which mod's
    /// folders are currently scoped.</para>
    ///
    /// <para>Mutates the AsyncLocal value on the calling flow only — sibling
    /// flows are unaffected. Prefer <see cref="PushScopes"/> for new code
    /// because its token-based bracket avoids the leak-on-exception risk of
    /// a manual set/clear pair.</para>
    /// </summary>
    public void SetAdditionalFolders(IReadOnlyList<string>? folders)
    {
        _currentAdditionalFolders.Value = (folders == null || folders.Count == 0) ? null : folders;
    }

    /// <summary>
    /// Sets the strict two-phase resolution chain consulted for subsequent
    /// resolutions. Pass <c>null</c> (or an empty list) to clear back to
    /// legacy <see cref="SetAdditionalFolders"/> + vanilla-fallback behavior.
    /// While scopes are active the loose-file resolution cache is bypassed
    /// for both reads and writes (same reasoning as additional folders).
    /// Mutates the AsyncLocal value on the calling flow only. Prefer
    /// <see cref="PushScopes"/> for new code.
    /// </summary>
    public void SetAdditionalScopes(IReadOnlyList<RenderScope>? scopes)
    {
        _currentAdditionalScopes.Value = (scopes == null || scopes.Count == 0) ? null : scopes;
    }

    /// <summary>
    /// Sets whether vanilla loose files override BSA-packed files (engine-default
    /// behavior; default <c>true</c>). When false, Phase 1 of the strict scope
    /// walk skips the vanilla scope (i=0) so its loose files don't preempt a
    /// mod-scoped BSA hit. Mod-folder loose files in higher scopes are
    /// unaffected. Mutates the AsyncLocal value on the calling flow only.
    /// See <see cref="Offscreen.OffscreenRenderRequest.VanillaLooseOverridesBsa"/>.
    /// </summary>
    public void SetVanillaLooseOverridesBsa(bool value)
    {
        _vanillaLooseOverridesBsa.Value = value;
    }

    /// <summary>
    /// Sets whether vanilla loose files (and only loose, never vanilla BSA)
    /// take priority over mod-folder loose files for non-FaceGen paths.
    /// When true, the user's installed body / skin / texture replacers in
    /// the data folder leak into mod-specific previews. The
    /// <c>FaceGenData</c> tree is excluded regardless. Mutates the AsyncLocal
    /// value on the calling flow only. See
    /// <see cref="Offscreen.OffscreenRenderRequest.VanillaLooseOverridesModLoose"/>.
    /// </summary>
    public void SetVanillaLooseOverridesModLoose(bool value)
    {
        _vanillaLooseOverridesModLoose.Value = value;
    }

    /// <summary>Captured snapshot of the five scoping values for restoration
    /// by <see cref="ScopeToken"/>.</summary>
    private readonly record struct ScopeSnapshot(
        IReadOnlyList<RenderScope>? Scopes,
        IReadOnlyList<string>? Folders,
        bool? VanillaLooseOverridesBsa,
        bool VanillaLooseOverridesModLoose,
        bool AllowLoadOrderFallback);

    /// <summary>Restores the captured <see cref="ScopeSnapshot"/> on
    /// <see cref="Dispose"/>. Idempotent — multiple disposes are no-ops so
    /// the token is safe inside <c>using</c> + manual dispose pairs.</summary>
    private sealed class ScopeToken : IDisposable
    {
        private readonly GameAssetResolver _owner;
        private readonly ScopeSnapshot _prev;
        private bool _disposed;

        public ScopeToken(GameAssetResolver owner, ScopeSnapshot prev)
        {
            _owner = owner;
            _prev = prev;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner._currentAdditionalScopes.Value = _prev.Scopes;
            _owner._currentAdditionalFolders.Value = _prev.Folders;
            _owner._vanillaLooseOverridesBsa.Value = _prev.VanillaLooseOverridesBsa;
            _owner._vanillaLooseOverridesModLoose.Value = _prev.VanillaLooseOverridesModLoose;
            _owner._allowLoadOrderFallback.Value = _prev.AllowLoadOrderFallback;
        }
    }

    /// <summary>True for paths under the FaceGen tree (FaceGeom NIFs and
    /// FaceTint DDS). These are NPC-keyed (FormID-named) and a vanilla
    /// loose copy must NEVER preempt a mod's actual face override.</summary>
    private static bool IsFaceGenPath(string normalizedPath)
    {
        return normalizedPath.IndexOf("FaceGenData", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Resolves a game-relative path to a full disk path.
    /// Returns null if the asset cannot be found in loose files or any BSA.
    /// </summary>
    public string? ResolveAssetPath(string relativeGamePath)
    {
        return ResolveAssetSource(relativeGamePath).ResolvedDiskPath;
    }

    // Per-thread time spent resolving asset paths (scope walk + File.Exists probes
    // + BSA locate). ThreadStatic so the inline offscreen build phase can attribute
    // its OWN resolve cost. Under strict scopes the loose cache is bypassed, so the
    // scope walk runs every render and prewarm can't warm it — this column shows how
    // much of `build` is that uncacheable resolve floor.
    [ThreadStatic] private static long _threadResolveTicks;
    public double ThreadResolveMs => _threadResolveTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>
    /// Resolves a game-relative path and reports where the asset came from
    /// (loose file on disk, or a specific BSA archive). Always returns a
    /// non-null <see cref="AssetSource"/>; check <see cref="AssetSource.Kind"/>
    /// for <see cref="AssetOriginKind.NotFound"/>.
    /// </summary>
    public AssetSource ResolveAssetSource(string relativeGamePath)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try { return ResolveAssetSourceCore(relativeGamePath); }
        finally { _threadResolveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0; }
    }

    private AssetSource ResolveAssetSourceCore(string relativeGamePath)
    {
        if (string.IsNullOrWhiteSpace(relativeGamePath))
        {
            return AssetSource.NotFound(relativeGamePath ?? string.Empty);
        }

        // Snapshot the per-flow scoping state once so a sibling flow's
        // PushScopes can't change our view mid-resolution. Scopes win over
        // folders if both are set (scopes are strictly more expressive).
        // While either is active, the loose-file cache is bypassed entirely
        // — the same relative path may resolve to a different file depending
        // on which scope chain is current.
        var additionalScopes = _currentAdditionalScopes.Value;
        var additionalFolders = additionalScopes == null ? _currentAdditionalFolders.Value : null;
        bool useLooseCache = additionalScopes == null && additionalFolders == null;

        // Fast path: previously-resolved loose file or definitive miss. Covers the
        // viewer's preset-switch re-request storm where the same ~20 texture paths
        // are asked for repeatedly. BSA hits have their own cache checked below.
        if (useLooseCache && _looseSourceCache.TryGetValue(relativeGamePath, out var cachedSource))
        {
            return cachedSource;
        }

        // Absolute-path passthrough. The Headparts preview flow supplies a
        // rooted path to a temp FaceGen NIF generated outside the game Data
        // folder; returning it as-is lets the viewer consume it without
        // needing the file to live under Data or to be prefixed with
        // DataFolderPath. Applies regardless of scoping.
        if (Path.IsPathRooted(relativeGamePath) && File.Exists(relativeGamePath))
        {
            var src = new AssetSource(AssetOriginKind.Loose, relativeGamePath, relativeGamePath, relativeGamePath, null, null);
            if (useLooseCache) _looseSourceCache[relativeGamePath] = src;
            return src;
        }

        // Normalize separators
        string normalized = relativeGamePath.Replace('/', Path.DirectorySeparatorChar);

        // ─── Strict two-phase scope chain ──────────────────────────────────
        // When AdditionalScopes is provided, follow ONLY the scope chain.
        // No fallback to vanilla data folder loose / BSA broadcast. Hosts
        // that want vanilla as a fallback include it as scopes[0] (which is
        // checked LAST due to the last-to-first iteration).
        if (additionalScopes != null)
        {
            return ResolveViaScopes(relativeGamePath, normalized, additionalScopes);
        }

        // ─── Legacy chain (when AdditionalScopes is null) ──────────────────

        // Mod-scoped loose-file lookup. Last entry wins (MO2-style) — iterate
        // in reverse so the highest-priority mod folder takes precedence when
        // multiple ship the same relative path.
        if (additionalFolders != null)
        {
            for (int i = additionalFolders.Count - 1; i >= 0; i--)
            {
                var folder = additionalFolders[i];
                if (string.IsNullOrEmpty(folder)) continue;
                string candidate = Path.Combine(folder, normalized);
                if (File.Exists(candidate))
                {
                    LogVerbose("CharacterViewer: Resolved '" + relativeGamePath +
                        "' -> mod-folder loose file at '" + candidate + "'");
                    return new AssetSource(AssetOriginKind.Loose, relativeGamePath,
                        candidate, candidate, null, null);
                }
            }
        }

        // Vanilla loose file
        string loosePath = Path.Combine(_dataFolder.DataFolderPath, normalized);
        if (File.Exists(loosePath))
        {
            LogVerbose("CharacterViewer: Resolved '" + relativeGamePath + "' -> loose file at '" + loosePath + "'");
            var src = new AssetSource(AssetOriginKind.Loose, relativeGamePath, loosePath, loosePath, null, null);
            if (useLooseCache) _looseSourceCache[relativeGamePath] = src;
            return src;
        }

        // BSA fallback (broadcast, uses extraction cache internally)
        var bsaResult = TryResolveFromBsa(relativeGamePath, normalized);
        if (bsaResult.Kind == AssetOriginKind.NotFound && useLooseCache)
        {
            // Cache the miss so the BSA traversal doesn't repeat on every
            // subsequent request for the same unresolvable asset.
            _looseSourceCache[relativeGamePath] = bsaResult;
        }
        return bsaResult;
    }

    /// <summary>Drops the scope-aware resolve cache AND the legacy (non-scoped)
    /// loose/BSA resolution caches. Call on env change (asset set may differ)
    /// and when extracted BSA files are cleared (cached BSA dest paths would
    /// dangle — hits already re-validate existence, but clearing avoids the
    /// wasted hit + re-resolve). The legacy caches are relative-path-keyed and
    /// encode load-order-dependent verdicts (a cached NotFound means "not loose
    /// AND not in any then-open BSA"; a BSA hit records which archive won), so
    /// they go stale across an in-process load-order change — previously only
    /// the scoped cache was dropped here and _looseSourceCache was never
    /// cleared anywhere. Thread-safe; doesn't touch the filesystem.</summary>
    public void ClearResolveCache()
    {
        _scopedResolveCache.Clear();
        _looseSourceCache.Clear();
        _bsaSourceCache.Clear();
    }

    /// <summary>Stable content signature for a scope set (ordered folder paths +
    /// modkey filenames), memoized per list reference. Order-sensitive (scopes are
    /// last-to-first priority). The vanilla-loose toggles are NOT included here —
    /// the caller appends them to the cache key — because they vary per render
    /// independently of the scope list.</summary>
    private string GetScopeSetSignature(IReadOnlyList<RenderScope> scopes) =>
        _scopeSetSigCache.GetValue(scopes, ComputeScopeSetSignature);

    private static string ComputeScopeSetSignature(IReadOnlyList<RenderScope> scopes)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < scopes.Count; i++)
        {
            var s = scopes[i];
            sb.Append(s.FolderPath ?? string.Empty).Append((char)1);
            if (s.ModKeyFileNames != null)
                for (int k = 0; k < s.ModKeyFileNames.Count; k++)
                    sb.Append(s.ModKeyFileNames[k]).Append((char)2);
            // Demotion changes where the scope ranks, so two chains differing
            // only in this bit must not share resolve-cache entries.
            sb.Append(s.DeprioritizeBelowDataFolder ? 'd' : 'n').Append((char)3);
        }
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash, 0, 12);
    }

    /// <summary>
    /// Strict two-phase scope iteration matching the contract on
    /// <see cref="Offscreen.OffscreenRenderRequest.AdditionalScopes"/>:
    /// (1) loose phase across all scopes last-to-first, (2) scoped-BSA
    /// phase across all scopes last-to-first via
    /// <see cref="IBsaArchiveProvider.TryLocateInScopedBsa"/>. No implicit
    /// vanilla fallback — the host includes vanilla as a scope if desired.
    ///
    /// <para>This wrapper adds the scope-aware resolve cache (<see cref="_scopedResolveCache"/>):
    /// it's keyed on the scope-set signature + both vanilla-loose toggles + path, so
    /// a hit is only returned for an identical resolution context. A hit re-validates
    /// the on-disk file still exists; a vanished file (cleared extraction / removed
    /// loose file) falls through to a full re-resolve via
    /// <see cref="ResolveViaScopesUncached"/>.</para>
    /// </summary>
    private AssetSource ResolveViaScopes(string relativeGamePath, string normalized,
        IReadOnlyList<RenderScope> scopes)
    {
        // Snapshot the per-flow toggles once. AsyncLocal<bool?> defaults to null
        // when no caller pushed a value; treat null as the engine-default true.
        // These are part of the cache key AND passed to the uncached core so the
        // key and the resolution it caches can't disagree.
        bool toggleVanillaOverridesBsa = _vanillaLooseOverridesBsa.Value ?? true;
        bool toggleVanillaOverridesModLoose = _vanillaLooseOverridesModLoose.Value;
        // Part of the key too: the same path under the same scopes resolves
        // differently with the fallback on (broadcast hit) vs off (NotFound),
        // so sharing one entry between them would serve a widened answer to a
        // strictly-scoped caller — the exact leak the scope chain exists to
        // prevent.
        bool toggleLoadOrderFallback = _allowLoadOrderFallback.Value;

        string cacheKey = GetScopeSetSignature(scopes)
            + (toggleVanillaOverridesBsa ? '1' : '0')
            + (toggleVanillaOverridesModLoose ? '1' : '0')
            + (toggleLoadOrderFallback ? '1' : '0')
            + '|' + normalized;

        if (_scopedResolveCache.TryGetValue(cacheKey, out var cachedScoped))
        {
            // NotFound has no file to validate and stays valid for this scope set
            // until env-change invalidation. For a hit with a disk path, re-validate
            // it still exists — a loose file may have been removed or a BSA
            // extraction cleared since caching — else fall through to re-resolve.
            if (cachedScoped.Kind == AssetOriginKind.NotFound) return cachedScoped;
            if (cachedScoped.ResolvedDiskPath != null && File.Exists(cachedScoped.ResolvedDiskPath))
                return cachedScoped;
        }

        var resolvedScoped = ResolveViaScopesUncached(
            relativeGamePath, normalized, scopes,
            toggleVanillaOverridesBsa, toggleVanillaOverridesModLoose,
            toggleLoadOrderFallback);
        _scopedResolveCache[cacheKey] = resolvedScoped;
        return resolvedScoped;
    }

    private AssetSource ResolveViaScopesUncached(string relativeGamePath, string normalized,
        IReadOnlyList<RenderScope> scopes,
        bool toggleVanillaOverridesBsa, bool toggleVanillaOverridesModLoose,
        bool toggleLoadOrderFallback)
    {
        bool isFaceGen = IsFaceGenPath(normalized);

        // Toggle 2 fast-path: vanilla loose preempts mod-folder loose for
        // non-FaceGen assets. Lets the user's installed body / skin /
        // texture replacers leak into mod-scoped previews. FaceGen is
        // excluded because its files are NPC-keyed (FormID-named) and a
        // vanilla copy would defeat the mod's actual face override. Only
        // checks the vanilla scope's LOOSE folder — vanilla BSA is left
        // for the normal Phase 2 walk.
        bool vanillaLooseAlreadyChecked = false;
        if (toggleVanillaOverridesModLoose && !isFaceGen && scopes.Count > 0)
        {
            var vanilla = scopes[0];
            if (!string.IsNullOrEmpty(vanilla.FolderPath))
            {
                string candidate = Path.Combine(vanilla.FolderPath, normalized);
                if (File.Exists(candidate))
                {
                    LogVerbose("CharacterViewer: Resolved '" + relativeGamePath +
                        "' -> vanilla loose (override) at '" + candidate + "'");
                    return new AssetSource(AssetOriginKind.Loose, relativeGamePath,
                        candidate, candidate, null, null);
                }
                vanillaLooseAlreadyChecked = true;
            }
        }

        // Engine-order mode: rank sources the way the game will actually see
        // them after the host's output is generated, instead of the strict
        // two-phase depiction walk below. Branches here so each mode stays
        // independently readable; the toggle is part of the resolve-cache
        // key, so entries never leak across modes.
        if (toggleLoadOrderFallback)
        {
            return ResolveViaScopesEngineOrder(relativeGamePath, normalized, scopes,
                toggleVanillaOverridesBsa, isFaceGen, vanillaLooseAlreadyChecked);
        }

        // Phase 1: all loose checks (last-to-first folder priority). The
        // vanilla scope (i=0) gets skipped when:
        //  * toggle 1 is off — strict-BSA mode, vanilla loose can't preempt
        //    a mod-scoped BSA hit.
        //  * the path is FaceGen — FaceGen NIFs and FaceTint DDS are
        //    NPC-keyed (FormID-named) and a vanilla loose copy must never
        //    preempt the mod's actual override or the original BSA content,
        //    regardless of toggle state. Mod-folder loose FaceGen still
        //    applies (that's the mod's intentional override).
        //  * the toggle 2 fast-path already checked vanilla loose above —
        //    a no-op since a hit would have returned, but avoids the
        //    redundant File.Exists syscall.
        for (int i = scopes.Count - 1; i >= 0; i--)
        {
            if (i == 0)
            {
                if (!toggleVanillaOverridesBsa) continue;
                if (isFaceGen) continue;
                if (vanillaLooseAlreadyChecked) continue;
            }
            var folder = scopes[i].FolderPath;
            if (string.IsNullOrEmpty(folder)) continue;
            string candidate = Path.Combine(folder, normalized);
            if (File.Exists(candidate))
            {
                LogVerbose("CharacterViewer: Resolved '" + relativeGamePath +
                    "' -> scoped loose file at '" + candidate + "'");
                return new AssetSource(AssetOriginKind.Loose, relativeGamePath,
                    candidate, candidate, null, null);
            }
        }

        // Phase 2: all scoped-BSA checks (last-to-first folder priority).
        string bsaSubpath = relativeGamePath.Replace('/', '\\');
        for (int i = scopes.Count - 1; i >= 0; i--)
        {
            var scopeHit = TryResolveScopeBsa(scopes[i], relativeGamePath, normalized, bsaSubpath);
            if (scopeHit != null) return scopeHit;
        }

        LogVerbose("CharacterViewer: Could not resolve '" + relativeGamePath +
            "' in any of " + scopes.Count + " scope(s)");
        return AssetSource.NotFound(relativeGamePath);
    }

    /// <summary>
    /// Engine-order resolution (<c>toggleLoadOrderFallback</c> on): rank
    /// sources the way the game will see them once the host's generated
    /// output is in place, per the contract on
    /// <see cref="Offscreen.OffscreenRenderRequest.AllowLoadOrderFallback"/>:
    /// <list type="number">
    /// <item><b>Mod scopes</b> — non-vanilla scopes last-to-first, each as a
    /// block of loose-then-own-BSAs. Assets here are destined to be copied
    /// into the output (deployed loose, beating everything at runtime), so
    /// a mod's BSA-packed asset outranks another mod's loose file. Scopes
    /// flagged <see cref="RenderScope.DeprioritizeBelowDataFolder"/> drop to
    /// tier 4 — except for FaceGen paths, which are always copied.</item>
    /// <item><b>Data-folder loose</b> — the vanilla scope's folder (under a
    /// mod manager's VFS this is every enabled mod's loose files). Engine
    /// rule: loose beats BSA. Skipped for FaceGen (NPC-keyed; a stray loose
    /// copy must never preempt the mod's face override) and when
    /// <c>VanillaLooseOverridesBsa</c> is off.</item>
    /// <item><b>Broadcast archives</b> — every archive the provider has
    /// indexed, provider-ranked (NPC2 restricts this tier to data-folder
    /// archives of enabled plugins and ranks by load order, vanilla
    /// naturally lowest). The vanilla scope's BSAs are deliberately NOT
    /// consulted as a scoped phase in this mode — they surface here at
    /// their true load-order rank instead of above later-loading mods.</item>
    /// <item><b>Demoted scopes</b> — last resort, so previewing a mod whose
    /// assets won't be copied still shows content when the live setup
    /// doesn't provide it (direct launches without the VFS, disabled
    /// mods being browsed).</item>
    /// </list>
    /// </summary>
    private AssetSource ResolveViaScopesEngineOrder(string relativeGamePath, string normalized,
        IReadOnlyList<RenderScope> scopes, bool toggleVanillaOverridesBsa,
        bool isFaceGen, bool vanillaLooseAlreadyChecked)
    {
        string bsaSubpath = relativeGamePath.Replace('/', '\\');

        // Tier 1: mod scopes, per-scope loose-then-BSA blocks.
        for (int i = scopes.Count - 1; i >= 1; i--)
        {
            var scope = scopes[i];
            if (scope.DeprioritizeBelowDataFolder && !isFaceGen) continue;
            var hit = TryResolveScopeLoose(scope, relativeGamePath, normalized, "scoped")
                      ?? TryResolveScopeBsa(scope, relativeGamePath, normalized, bsaSubpath);
            if (hit != null) return hit;
        }

        // Tier 2: data-folder loose. Same skip conditions as the strict
        // walk's vanilla-scope rules (FaceGen / toggle 1 / toggle-2 fast
        // path already probed it).
        if (scopes.Count > 0 && toggleVanillaOverridesBsa && !isFaceGen && !vanillaLooseAlreadyChecked)
        {
            var vanillaHit = TryResolveScopeLoose(scopes[0], relativeGamePath, normalized, "data-folder");
            if (vanillaHit != null) return vanillaHit;
        }

        // Tier 3: broadcast archive lookup (provider-ranked; includes the
        // vanilla archives at their natural rank).
        var broadcast = TryResolveFromBsa(relativeGamePath, normalized);
        if (broadcast.Kind != AssetOriginKind.NotFound)
        {
            LogVerbose("CharacterViewer: Resolved '" + relativeGamePath +
                "' -> broadcast archive tier at '" + broadcast.ResolvedDiskPath +
                "' (from '" + broadcast.BsaPath + "')");
            return broadcast;
        }

        // Tier 4: demoted scopes as a last resort (FaceGen already ran in tier 1).
        for (int i = scopes.Count - 1; i >= 1; i--)
        {
            var scope = scopes[i];
            if (!scope.DeprioritizeBelowDataFolder || isFaceGen) continue;
            var hit = TryResolveScopeLoose(scope, relativeGamePath, normalized, "demoted-scope")
                      ?? TryResolveScopeBsa(scope, relativeGamePath, normalized, bsaSubpath);
            if (hit != null) return hit;
        }

        LogVerbose("CharacterViewer: Could not resolve '" + relativeGamePath +
            "' in any of " + scopes.Count + " scope(s), the data folder, or any indexed archive (engine-order mode)");
        return AssetSource.NotFound(relativeGamePath);
    }

    /// <summary>Loose-file probe for one scope's folder. Null = not present
    /// (keep searching); non-null = resolved.</summary>
    private AssetSource? TryResolveScopeLoose(RenderScope scope, string relativeGamePath,
        string normalized, string tierTag)
    {
        var folder = scope.FolderPath;
        if (string.IsNullOrEmpty(folder)) return null;
        string candidate = Path.Combine(folder, normalized);
        if (!File.Exists(candidate)) return null;
        LogVerbose("CharacterViewer: Resolved '" + relativeGamePath +
            "' -> " + tierTag + " loose file at '" + candidate + "'");
        return new AssetSource(AssetOriginKind.Loose, relativeGamePath,
            candidate, candidate, null, null);
    }

    /// <summary>Scoped-BSA probe for one scope (locate + extract). Null = not
    /// present in this scope's archives (keep searching). Non-null is TERMINAL:
    /// either the resolved extraction, or NotFound when the file was located
    /// but extraction failed — searching further would silently substitute a
    /// different mod's copy for a file we know exists in this scope.
    /// BSA-fallback may need to extract; reuses the per-path lock + cache
    /// pattern from <see cref="TryResolveFromBsa"/> to avoid double-extracting
    /// under concurrency.</summary>
    private AssetSource? TryResolveScopeBsa(RenderScope scope, string relativeGamePath,
        string normalized, string bsaSubpath)
    {
        if (string.IsNullOrEmpty(scope.FolderPath)) return null;
        if (scope.ModKeyFileNames == null || scope.ModKeyFileNames.Count == 0) return null;

        if (!_bsaProvider.TryLocateInScopedBsa(bsaSubpath, scope.FolderPath,
                scope.ModKeyFileNames, out string? containingBsaPath) ||
            containingBsaPath == null)
        {
            return null;
        }

        // Per-source-BSA cache + on-disk destination — see _extractionCache
        // field doc for why mixing BSAs under one key/destination caused
        // mod-scoped renders to render vanilla content.
        string cacheKey = MakeExtractionCacheKey(containingBsaPath, normalized);
        string destPath = MakeExtractionDestPath(containingBsaPath, normalized);
        var lockObj = _extractionLocks.GetOrAdd(cacheKey, _ => new object());
        lock (lockObj)
        {
            if (_extractionCache.TryGetValue(cacheKey, out string? priorExtract) &&
                File.Exists(priorExtract))
            {
                Trace($"scoped CACHE-HIT file=[{normalized}] bsa=[{containingBsaPath}] dest=[{priorExtract}]");
                return new AssetSource(AssetOriginKind.Bsa, relativeGamePath,
                    priorExtract, null, containingBsaPath, bsaSubpath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            if (_bsaProvider.TryExtractToDisk(containingBsaPath, bsaSubpath, destPath, out string? scopedExtractError))
            {
                _extractionCache[cacheKey] = destPath;
                Trace($"scoped EXTRACT file=[{normalized}] bsa=[{containingBsaPath}] dest=[{destPath}]");
                LogVerbose("CharacterViewer: Resolved '" + relativeGamePath +
                    "' -> scoped-BSA extraction at '" + destPath +
                    "' (from '" + containingBsaPath + "')");
                return new AssetSource(AssetOriginKind.Bsa, relativeGamePath,
                    destPath, null, containingBsaPath, bsaSubpath);
            }

            _logger.LogError("CharacterViewer: Found '" + relativeGamePath +
                "' in scoped BSA '" + containingBsaPath + "' but extraction failed: " +
                (scopedExtractError ?? "(no detail)"));
            return AssetSource.NotFound(relativeGamePath);
        }
    }

    /// <summary>
    /// Opens a readable stream for a game-relative asset path.
    /// Returns null if the asset cannot be found.
    /// For BSA assets, this extracts to temp and opens the extracted file.
    /// </summary>
    public Stream? OpenAssetStream(string relativeGamePath)
    {
        string? resolved = ResolveAssetPath(relativeGamePath);
        if (resolved == null)
        {
            return null;
        }

        try
        {
            return File.OpenRead(resolved);
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer: Failed to open stream for '" + resolved + "': " + ex.Message);
            return null;
        }
    }

    private AssetSource TryResolveFromBsa(string relativeGamePath, string normalized)
    {
        // Fast path before locking: if another thread already extracted and
        // cached, return that immediately.
        if (_bsaSourceCache.TryGetValue(normalized, out var cachedSource) &&
            cachedSource.ResolvedDiskPath != null && File.Exists(cachedSource.ResolvedDiskPath))
        {
            return cachedSource;
        }

        // Normalize the path for BSA lookup (Skyrim BSAs use backslash-separated paths).
        string bsaSubpath = relativeGamePath.Replace('/', '\\');

        _bsaProvider.EnsureAllArchivesOpened();

        if (!_bsaProvider.TryLocateInBsa(bsaSubpath, out string? containingBsaPath))
        {
            LogVerbose("CharacterViewer: Could not resolve '" + relativeGamePath + "' in loose files or any BSA");
            return AssetSource.NotFound(relativeGamePath);
        }

        // Per-source-BSA cache + on-disk destination — see _extractionCache
        // field doc for the mod-scoping bug that motivates BSA-aware keying.
        string cacheKey = MakeExtractionCacheKey(containingBsaPath!, normalized);
        string destPath = MakeExtractionDestPath(containingBsaPath!, normalized);

        // Per-(BSA, path) lock so the cache-check + extract sequence is atomic
        // for concurrent callers asking for the same asset from the same BSA.
        var lockObj = _extractionLocks.GetOrAdd(cacheKey, _ => new object());
        lock (lockObj)
        {
            // Re-check cache inside the lock — another thread may have completed
            // the extraction while we were waiting.
            if (_bsaSourceCache.TryGetValue(normalized, out var racedSource) &&
                racedSource.BsaPath == containingBsaPath &&
                racedSource.ResolvedDiskPath != null &&
                File.Exists(racedSource.ResolvedDiskPath))
            {
                return racedSource;
            }

            // Reuse a prior extraction if still on disk
            if (_extractionCache.TryGetValue(cacheKey, out string? priorExtract) && File.Exists(priorExtract))
            {
                var source = new AssetSource(AssetOriginKind.Bsa, relativeGamePath, priorExtract,
                    null, containingBsaPath, bsaSubpath);
                _bsaSourceCache[normalized] = source;
                return source;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            if (_bsaProvider.TryExtractToDisk(containingBsaPath!, bsaSubpath, destPath, out string? extractError))
            {
                _extractionCache[cacheKey] = destPath;
                LogVerbose("CharacterViewer: Resolved '" + relativeGamePath + "' -> BSA extraction at '" + destPath + "'");
                var source = new AssetSource(AssetOriginKind.Bsa, relativeGamePath, destPath,
                    null, containingBsaPath, bsaSubpath);
                _bsaSourceCache[normalized] = source;
                return source;
            }

            _logger.LogError("CharacterViewer: Found '" + relativeGamePath + "' in BSA but extraction failed: " +
                (extractError ?? "(no detail)"));
            return AssetSource.NotFound(relativeGamePath);
        }
    }
}
