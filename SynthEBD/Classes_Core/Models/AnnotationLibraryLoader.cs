using System.Collections.Generic;
using System.IO;

namespace SynthEBD;

/// <summary>
/// Loads per-body-type annotation libraries from
/// <c>InternalData/BodySlideAnnotationLibraries/*.json</c>.
/// Results are cached after the first call; call <see cref="InvalidateCache"/> to force a reload.
/// </summary>
public class AnnotationLibraryLoader
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;

    private Dictionary<string, AnnotationLibrary>? _cache;

    public AnnotationLibraryLoader(IEnvironmentStateProvider environmentProvider, Logger logger)
    {
        _environmentProvider = environmentProvider;
        _logger = logger;
    }

    /// <summary>
    /// Returns all loaded annotation libraries keyed by body type (case-insensitive).
    /// Returns an empty dictionary if the directory does not exist or contains no valid files.
    /// </summary>
    public Dictionary<string, AnnotationLibrary> LoadLibraries()
    {
        if (_cache != null) return _cache;

        _cache = new Dictionary<string, AnnotationLibrary>(System.StringComparer.OrdinalIgnoreCase);

        string dir = Path.Combine(_environmentProvider.InternalDataPath, "BodySlideAnnotationLibraries");
        if (!Directory.Exists(dir))
        {
            return _cache;
        }

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var lib = JSONhandler<AnnotationLibrary>.LoadJSONFile(file, out bool ok, out string err);
            if (!ok || lib == null)
            {
                _logger.LogMessage($"AnnotationLibraryLoader: failed to load '{file}': {err}");
                continue;
            }

            string bodyType = !string.IsNullOrWhiteSpace(lib.BodyType)
                ? lib.BodyType
                : Path.GetFileNameWithoutExtension(file);

            if (_cache.ContainsKey(bodyType))
            {
                _logger.LogMessage($"AnnotationLibraryLoader: duplicate body type '{bodyType}' in '{file}', skipping.");
                continue;
            }

            _cache[bodyType] = lib;
        }

        _logger.LogMessage($"AnnotationLibraryLoader: loaded {_cache.Count} annotation library file(s).");
        return _cache;
    }

    /// <summary>Forces the next call to <see cref="LoadLibraries"/> to re-read from disk.</summary>
    public void InvalidateCache() => _cache = null;
}
