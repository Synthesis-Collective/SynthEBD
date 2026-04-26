using System;
using System.Collections.Concurrent;
using System.IO;

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
    /// Key: lowercase game-relative path, Value: extracted disk path.
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

    private readonly CharacterViewerLogGate _logGate;

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

        // Fast path: previously-resolved loose file or definitive miss. Covers the
        // viewer's preset-switch re-request storm where the same ~20 texture paths
        // are asked for repeatedly. BSA hits have their own cache checked below.
        if (_looseSourceCache.TryGetValue(relativeGamePath, out var cachedSource))
        {
            return cachedSource;
        }

        // Step 0: Absolute-path passthrough. The Headparts preview flow supplies a
        // rooted path to a temp FaceGen NIF generated outside the game Data folder;
        // returning it as-is lets the viewer consume it without needing the file to
        // live under Data or to be prefixed with DataFolderPath.
        if (Path.IsPathRooted(relativeGamePath) && File.Exists(relativeGamePath))
        {
            var src = new AssetSource(AssetOriginKind.Loose, relativeGamePath, relativeGamePath, relativeGamePath, null, null);
            _looseSourceCache[relativeGamePath] = src;
            return src;
        }

        // Normalize separators
        string normalized = relativeGamePath.Replace('/', Path.DirectorySeparatorChar);

        // Step 1: Check loose file
        string loosePath = Path.Combine(_dataFolder.DataFolderPath, normalized);
        if (File.Exists(loosePath))
        {
            LogVerbose("CharacterViewer: Resolved '" + relativeGamePath + "' -> loose file at '" + loosePath + "'");
            var src = new AssetSource(AssetOriginKind.Loose, relativeGamePath, loosePath, loosePath, null, null);
            _looseSourceCache[relativeGamePath] = src;
            return src;
        }

        // Step 2: BSA fallback (uses extraction cache internally)
        var bsaResult = TryResolveFromBsa(relativeGamePath, normalized);
        if (bsaResult.Kind == AssetOriginKind.NotFound)
        {
            // Cache the miss so the BSA traversal doesn't repeat on every
            // subsequent request for the same unresolvable asset.
            _looseSourceCache[relativeGamePath] = bsaResult;
        }
        return bsaResult;
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
        // Check cached BSA source first (covers both re-resolves and re-extractions)
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

        // Build extraction destination preserving the relative directory structure
        string destPath = Path.Combine(_extractionDir, normalized);

        // Reuse a prior extraction if still on disk
        if (_extractionCache.TryGetValue(normalized, out string? priorExtract) && File.Exists(priorExtract))
        {
            var source = new AssetSource(AssetOriginKind.Bsa, relativeGamePath, priorExtract,
                null, containingBsaPath, bsaSubpath);
            _bsaSourceCache[normalized] = source;
            return source;
        }

        if (_bsaProvider.TryExtractToDisk(bsaSubpath, destPath))
        {
            _extractionCache[normalized] = destPath;
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
