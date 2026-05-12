using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
        bool vanillaLooseOverridesModLoose)
    {
        var snapshot = new ScopeSnapshot(
            _currentAdditionalScopes.Value,
            _currentAdditionalFolders.Value,
            _vanillaLooseOverridesBsa.Value,
            _vanillaLooseOverridesModLoose.Value);
        _currentAdditionalScopes.Value = (scopes == null || scopes.Count == 0) ? null : scopes;
        _currentAdditionalFolders.Value = (folders == null || folders.Count == 0) ? null : folders;
        _vanillaLooseOverridesBsa.Value = vanillaLooseOverridesBsa;
        _vanillaLooseOverridesModLoose.Value = vanillaLooseOverridesModLoose;
        return new ScopeToken(this, snapshot);
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

    /// <summary>Captured snapshot of the four scoping values for restoration
    /// by <see cref="ScopeToken"/>.</summary>
    private readonly record struct ScopeSnapshot(
        IReadOnlyList<RenderScope>? Scopes,
        IReadOnlyList<string>? Folders,
        bool? VanillaLooseOverridesBsa,
        bool VanillaLooseOverridesModLoose);

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

    /// <summary>
    /// Resolves a game-relative path and reports where the asset came from
    /// (loose file on disk, or a specific BSA archive). Always returns a
    /// non-null <see cref="AssetSource"/>; check <see cref="AssetSource.Kind"/>
    /// for <see cref="AssetOriginKind.NotFound"/>.
    /// </summary>
    public AssetSource ResolveAssetSource(string relativeGamePath)
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

    /// <summary>
    /// Strict two-phase scope iteration matching the contract on
    /// <see cref="Offscreen.OffscreenRenderRequest.AdditionalScopes"/>:
    /// (1) loose phase across all scopes last-to-first, (2) scoped-BSA
    /// phase across all scopes last-to-first via
    /// <see cref="IBsaArchiveProvider.TryLocateInScopedBsa"/>. No implicit
    /// vanilla fallback — the host includes vanilla as a scope if desired.
    /// </summary>
    private AssetSource ResolveViaScopes(string relativeGamePath, string normalized,
        IReadOnlyList<RenderScope> scopes)
    {
        // Snapshot once per call. AsyncLocal<bool?> defaults to null when
        // no caller pushed a value; treat null as the engine-default true.
        bool toggleVanillaOverridesBsa = _vanillaLooseOverridesBsa.Value ?? true;
        bool toggleVanillaOverridesModLoose = _vanillaLooseOverridesModLoose.Value;
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
        // BSA-fallback may need to extract; reuse the per-path lock + cache
        // pattern from TryResolveFromBsa to avoid double-extracting under
        // concurrency.
        string bsaSubpath = relativeGamePath.Replace('/', '\\');
        for (int i = scopes.Count - 1; i >= 0; i--)
        {
            var scope = scopes[i];
            if (string.IsNullOrEmpty(scope.FolderPath)) continue;
            if (scope.ModKeyFileNames == null || scope.ModKeyFileNames.Count == 0) continue;

            if (_bsaProvider.TryLocateInScopedBsa(bsaSubpath, scope.FolderPath,
                    scope.ModKeyFileNames, out string? containingBsaPath) &&
                containingBsaPath != null)
            {
                // Per-source-BSA cache + on-disk destination — see
                // _extractionCache field doc for why mixing BSAs under one
                // key/destination caused mod-scoped renders to render
                // vanilla content.
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
        }

        LogVerbose("CharacterViewer: Could not resolve '" + relativeGamePath +
            "' in any of " + scopes.Count + " scope(s)");
        return AssetSource.NotFound(relativeGamePath);
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
