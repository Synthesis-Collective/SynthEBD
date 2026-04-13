using System;
using System.Collections.Concurrent;
using System.IO;
using Mutagen.Bethesda.Archives;

namespace SynthEBD;

/// <summary>
/// Resolves game-relative asset paths (e.g. "meshes/actors/character/...")
/// to actual file paths on disk. Checks loose files first, then falls back
/// to BSA archive extraction with a persistent temp cache.
/// </summary>
public class GameAssetResolver
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly BSAHandler _bsaHandler;
    private readonly Logger _logger;

    /// <summary>
    /// Cache of BSA-extracted files so repeated lookups don't re-extract.
    /// Key: lowercase game-relative path, Value: extracted disk path.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _extractionCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _extractionDir;

    public GameAssetResolver(
        IEnvironmentStateProvider environmentProvider,
        BSAHandler bsaHandler,
        Logger logger)
    {
        _environmentProvider = environmentProvider;
        _bsaHandler = bsaHandler;
        _logger = logger;

        _extractionDir = Path.Combine(Path.GetTempPath(), "SynthEBD_ViewerCache");
    }

    /// <summary>
    /// Resolves a game-relative path to a full disk path.
    /// Returns null if the asset cannot be found in loose files or any BSA.
    /// </summary>
    public string? ResolveAssetPath(string relativeGamePath)
    {
        if (string.IsNullOrWhiteSpace(relativeGamePath))
        {
            return null;
        }

        // Normalize separators
        string normalized = relativeGamePath.Replace('/', Path.DirectorySeparatorChar);

        // Step 1: Check loose file
        string loosePath = Path.Combine(_environmentProvider.DataFolderPath, normalized);
        if (File.Exists(loosePath))
        {
            _logger.LogMessage("CharacterViewer: Resolved '" + relativeGamePath + "' -> loose file at '" + loosePath + "'");
            return loosePath;
        }

        // Step 2: Check extraction cache
        if (_extractionCache.TryGetValue(normalized, out string? cached) && File.Exists(cached))
        {
            return cached;
        }

        // Step 3: BSA fallback
        return TryExtractFromBsa(relativeGamePath, normalized);
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

    private string? TryExtractFromBsa(string relativeGamePath, string normalized)
    {
        // Use forward-slash subpath for BSA lookup (Mutagen convention)
        string bsaSubpath = relativeGamePath.Replace('\\', '/');

        _bsaHandler.EnsureAllArchivesOpened();

        if (!_bsaHandler.TryFindFileInAnyArchive(bsaSubpath, out IArchiveFile archiveFile))
        {
            _logger.LogMessage("CharacterViewer: Could not resolve '" + relativeGamePath + "' in loose files or any BSA");
            return null;
        }

        // Build extraction destination preserving the relative directory structure
        string destPath = Path.Combine(_extractionDir, normalized);

        if (_bsaHandler.TryExtractFileFromBSA(archiveFile, destPath))
        {
            _extractionCache[normalized] = destPath;
            _logger.LogMessage("CharacterViewer: Resolved '" + relativeGamePath + "' -> BSA extraction at '" + destPath + "'");
            return destPath;
        }

        _logger.LogError("CharacterViewer: Found '" + relativeGamePath + "' in BSA but extraction failed");
        return null;
    }
}
