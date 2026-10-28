using System.IO;
using System.Text.Json;

namespace SynthEBD;

/// <summary>
/// Vortex implementation of <see cref="ISourceResolver"/>.
///
/// <para><b>FaceGen re-run resolution</b> (<see cref="Initialize"/> / <see cref="TryResolve"/>) is still a
/// placeholder: it always reports unavailable, so FaceGenPatcher falls back to the abort-and-notify path
/// for Vortex users on re-runs.</para>
///
/// <para><b>Providing mod lookup</b> (<see cref="FindProvidingModFolder"/>) reads Vortex's deployment
/// manifest, the record Vortex itself keeps of what it deployed. For the default mod type it is
/// <c>vortex.deployment.json</c> in the deploy path -- the game's Data folder -- with a backup copy of the
/// same name in the staging folder. Its shape (Vortex <c>IDeploymentManifest</c> / <c>IDeployedFile</c>):
/// <c>{ version, instance, deploymentMethod?, deploymentTime?, stagingPath?, gameId?, targetPath?,
/// files: [ { relPath, source, merged?, target?, time } ] }</c>, where <c>relPath</c> is relative to the
/// deploy path and <c>source</c> is the mod's folder name under the staging path (Vortex rebuilds a
/// deployed file's origin as <c>stagingPath\source\relPath</c>). The manifest lists only deployed files --
/// enabled mods, conflict winners -- so it answers the same question MO2's modlist.txt order does.
/// When no manifest can be read, the staging folder's mod folders are searched instead (enabled state
/// and priority unknown there; first match by folder name).</para>
///
/// <para><b>Inactive mods</b> (<see cref="GetInactiveModFolders"/>) are the staging folder's mod folders
/// that no deployed file names as its <c>source</c> -- installed but disabled or undeployed. With no
/// readable manifest every staging mod folder counts (nothing is known to be deployed).</para>
/// </summary>
public class VortexSourceResolver : ISourceResolver
{
    private const string ManifestFileName = "vortex.deployment.json";

    private readonly Logger _logger;
    private readonly PatcherState _patcherState;
    private readonly IEnvironmentStateProvider _environmentProvider;

    private readonly object _manifestLock = new();
    private string _manifestPathLoaded;
    private DateTime _manifestTimeLoaded;
    private Dictionary<string, string> _sourceByRelPath;
    private HashSet<string> _deployedSources;
    private string _manifestStagingPath;

    public bool IsAvailable => false;

    public VortexSourceResolver(Logger logger, PatcherState patcherState, IEnvironmentStateProvider environmentProvider)
    {
        _logger = logger;
        _patcherState = patcherState;
        _environmentProvider = environmentProvider;
    }

    public void Initialize()
    {
        _logger.LogMessage("VortexSourceResolver: Vortex source resolution is not yet implemented. " +
                           "Vortex users must manually delete the SynthEBD output FaceGen directory before re-running.");
    }

    public bool TryResolve(string relativeDataPath, out string absolutePath, bool verbose = false)
    {
        absolutePath = null;
        return false;
    }

    /// <summary>The staging-folder mod that Vortex deployed <paramref name="relativeDataPath"/> from, per
    /// its deployment manifest; with no readable manifest, the first staging mod folder containing it.</summary>
    public string FindProvidingModFolder(string relativeDataPath)
    {
        if (string.IsNullOrWhiteSpace(relativeDataPath)) return null;
        string normalized = Normalize(relativeDataPath);

        string? settingsStaging = _patcherState?.ModManagerSettings?.VortexSettings?.StagingFolderPath;
        if (TryLoadManifest(settingsStaging))
        {
            if (!_sourceByRelPath.TryGetValue(normalized, out var source)) return null; // not Vortex-deployed
            string staging = !string.IsNullOrWhiteSpace(_manifestStagingPath) ? _manifestStagingPath : settingsStaging;
            if (string.IsNullOrWhiteSpace(staging)) return null;
            string folder = Path.Combine(staging, source);
            return File.Exists(Path.Combine(folder, normalized)) ? folder : null;
        }

        // No manifest: search the staging folder directly.
        if (string.IsNullOrWhiteSpace(settingsStaging) || !Directory.Exists(settingsStaging)) return null;
        return Directory.EnumerateDirectories(settingsStaging)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(d => File.Exists(Path.Combine(d, normalized)));
    }

    /// <summary>Staging mod folders with nothing deployed, by folder name (Vortex keeps no priority order
    /// for disabled mods).</summary>
    public IReadOnlyList<string> GetInactiveModFolders()
    {
        string? settingsStaging = _patcherState?.ModManagerSettings?.VortexSettings?.StagingFolderPath;
        bool haveManifest = TryLoadManifest(settingsStaging);
        string? staging = haveManifest && !string.IsNullOrWhiteSpace(_manifestStagingPath) ? _manifestStagingPath : settingsStaging;
        if (string.IsNullOrWhiteSpace(staging) || !Directory.Exists(staging)) return Array.Empty<string>();

        return Directory.EnumerateDirectories(staging)
            .Where(d => !haveManifest || !_deployedSources.Contains(Path.GetFileName(d)))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Every staging mod folder: those with deployed files first, then the rest, each by name.</summary>
    public IReadOnlyList<string> GetAllModFolders()
    {
        string? settingsStaging = _patcherState?.ModManagerSettings?.VortexSettings?.StagingFolderPath;
        bool haveManifest = TryLoadManifest(settingsStaging);
        string? staging = haveManifest && !string.IsNullOrWhiteSpace(_manifestStagingPath) ? _manifestStagingPath : settingsStaging;
        if (string.IsNullOrWhiteSpace(staging) || !Directory.Exists(staging)) return Array.Empty<string>();

        return Directory.EnumerateDirectories(staging)
            .OrderBy(d => haveManifest && _deployedSources.Contains(Path.GetFileName(d)) ? 0 : 1)
            .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Loads (or reuses, while the file is unchanged) the default-mod-type deployment manifest
    /// from the Data folder, falling back to the staging folder's backup copy. False when neither reads.</summary>
    private bool TryLoadManifest(string? settingsStaging)
    {
        var candidates = new List<string>();
        string dataFolder = _environmentProvider?.DataFolderPath;
        if (!string.IsNullOrWhiteSpace(dataFolder)) candidates.Add(Path.Combine(dataFolder, ManifestFileName));
        if (!string.IsNullOrWhiteSpace(settingsStaging)) candidates.Add(Path.Combine(settingsStaging, ManifestFileName));

        lock (_manifestLock)
        {
            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                var time = File.GetLastWriteTimeUtc(path);
                if (_sourceByRelPath != null && string.Equals(path, _manifestPathLoaded, StringComparison.OrdinalIgnoreCase)
                    && time == _manifestTimeLoaded) return true;

                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    var root = doc.RootElement;
                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var file in files.EnumerateArray())
                        {
                            if (!file.TryGetProperty("relPath", out var rel) || rel.ValueKind != JsonValueKind.String) continue;
                            if (!file.TryGetProperty("source", out var src) || src.ValueKind != JsonValueKind.String) continue;
                            // Mods deployed to a different output directory (target) aren't Data-relative.
                            if (file.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String
                                && !string.IsNullOrEmpty(target.GetString())) continue;
                            map[Normalize(rel.GetString()!)] = src.GetString()!;
                        }
                    }

                    _manifestStagingPath = root.TryGetProperty("stagingPath", out var sp) && sp.ValueKind == JsonValueKind.String
                        ? sp.GetString() : null;
                    _sourceByRelPath = map;
                    _deployedSources = new HashSet<string>(map.Values, StringComparer.OrdinalIgnoreCase);
                    _manifestPathLoaded = path;
                    _manifestTimeLoaded = time;
                    _logger.LogMessage($"VortexSourceResolver: read deployment manifest '{path}' ({map.Count} file(s), "
                        + $"staging '{_manifestStagingPath ?? settingsStaging ?? "?"}').");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogMessage($"VortexSourceResolver: could not read deployment manifest '{path}': {ex.Message}");
                }
            }

            _sourceByRelPath = null;
            _deployedSources = null;
            _manifestPathLoaded = null;
            return false;
        }
    }

    private static string Normalize(string path)
        => path.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
}
