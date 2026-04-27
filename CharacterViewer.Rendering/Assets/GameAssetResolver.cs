using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

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
    /// render or load. Set via <see cref="SetAdditionalFolders"/> by the
    /// offscreen renderer (per-render scope) and <see cref="VM_CharacterViewer"/>
    /// (per-load scope). Cleared back to null at end-of-scope so renders
    /// don't leak mod folders between calls.
    ///
    /// Marked volatile because both writers (renderer's lock-held thread,
    /// VM's render-thread marshaller) and readers (resolver calls during
    /// off-thread <c>LoadAllMeshParts</c>) span thread boundaries —
    /// volatility provides the memory barrier without forcing every
    /// resolution to take a lock.
    /// </summary>
    private volatile IReadOnlyList<string>? _currentAdditionalFolders;

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
    /// <para>Marked volatile for the same memory-barrier reasons as
    /// <see cref="_currentAdditionalFolders"/> — written by the renderer's
    /// lock-held thread (offscreen path) or the VM's render-thread
    /// marshaller (interactive path), read during off-thread NIF parsing.</para>
    /// </summary>
    private volatile IReadOnlyList<RenderScope>? _currentAdditionalScopes;

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

        _extractionDir = Path.Combine(Path.GetTempPath(), "SynthEBD_ViewerCache");
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
    /// cache and clears the cache itself. Intended for one-and-done batch
    /// flows (see <see cref="Offscreen.OffscreenRenderRequest.ClearExtractionCacheAfterRender"/>)
    /// where the host generates each PNG once and the temp directory would
    /// otherwise grow without bound. Safe to call between renders; not
    /// safe to call concurrently with a render in progress (the renderer
    /// invokes this from its own per-render finally block on the dedicated
    /// render thread, which serializes against the next queued job).
    /// Returns the number of files actually deleted (best-effort —
    /// individual delete failures are swallowed and counted as misses).
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
    /// <para>Lifecycle is owned by the caller (offscreen renderer per-render,
    /// or <see cref="VM_CharacterViewer"/> per-load): set before resolution,
    /// clear in a finally / SceneCommitted block. The renderer's serialized
    /// lock and the VM's marshaller-anchored resolution flow ensure no two
    /// callers race on this state in practice.</para>
    /// </summary>
    public void SetAdditionalFolders(IReadOnlyList<string>? folders)
    {
        _currentAdditionalFolders = (folders == null || folders.Count == 0) ? null : folders;
    }

    /// <summary>
    /// Sets the strict two-phase resolution chain consulted for subsequent
    /// resolutions. Pass <c>null</c> (or an empty list) to clear back to
    /// legacy <see cref="SetAdditionalFolders"/> + vanilla-fallback behavior.
    /// While scopes are active the loose-file resolution cache is bypassed
    /// for both reads and writes (same reasoning as additional folders).
    /// Lifecycle is owned by the caller (offscreen renderer per-render or
    /// <see cref="VM_CharacterViewer"/> per-load).
    /// </summary>
    public void SetAdditionalScopes(IReadOnlyList<RenderScope>? scopes)
    {
        _currentAdditionalScopes = (scopes == null || scopes.Count == 0) ? null : scopes;
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

        // Snapshot the per-render scoping state once so concurrent
        // SetAdditional* calls don't change our view mid-resolution. Scopes
        // win over folders if both are set (scopes are strictly more
        // expressive). While either is active, the loose-file cache is
        // bypassed entirely — the same relative path may resolve to a
        // different file depending on which scope chain is current.
        var additionalScopes = _currentAdditionalScopes;
        var additionalFolders = additionalScopes == null ? _currentAdditionalFolders : null;
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
        // Phase 1: all loose checks first (last-to-first folder priority).
        for (int i = scopes.Count - 1; i >= 0; i--)
        {
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
                    if (_bsaProvider.TryExtractToDisk(containingBsaPath, bsaSubpath, destPath))
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
                        "' in scoped BSA '" + containingBsaPath + "' but extraction failed");
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
            if (_bsaProvider.TryExtractToDisk(containingBsaPath!, bsaSubpath, destPath))
            {
                _extractionCache[cacheKey] = destPath;
                LogVerbose("CharacterViewer: Resolved '" + relativeGamePath + "' -> BSA extraction at '" + destPath + "'");
                var source = new AssetSource(AssetOriginKind.Bsa, relativeGamePath, destPath,
                    null, containingBsaPath, bsaSubpath);
                _bsaSourceCache[normalized] = source;
                return source;
            }

            _logger.LogError("CharacterViewer: Found '" + relativeGamePath + "' in BSA but extraction failed");
            return AssetSource.NotFound(relativeGamePath);
        }
    }
}
