using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SynthEBD;

/// <summary>
/// MO2-specific implementation of <see cref="ISourceResolver"/>.
///
/// Parses MO2's modlist.txt from the active profile to build a priority-ordered list
/// of enabled mod folders, excluding the SynthEBD Output folder. When invoked for a
/// specific FaceGen path, iterates from highest to lowest priority and returns the
/// first match via File.Exists.
///
/// Requires <see cref="Settings_ModManager.MO2.ExecutablePath"/> to be configured.
/// From the executable location, reads ModOrganizer.ini to find:
///   - The currently selected profile (selected_profile=@ByteArray(Profile_Name))
///   - The profiles directory (profiles_directory= or default "profiles")
///   - The mods directory (mod_directory= or default "mods")
/// Then parses {profilesDir}/{profileName}/modlist.txt.
/// </summary>
public class MO2SourceResolver : ISourceResolver
{
    // ─── Debug logging ───────────────────────────────────────────────────────
    // Toggle to enable verbose resolution logging. Keep false in release builds.
    private const bool LogSourceResolverDebug = false;

    private readonly PatcherState _patcherState;
    private readonly SynthEBDPaths _paths;
    private readonly Logger _logger;

    /// <summary>
    /// Priority-ordered list of absolute mod folder paths (highest priority first).
    /// Built during <see cref="Initialize"/> by reading modlist.txt top-to-bottom
    /// (MO2 stores highest priority first). The Overwrite folder is excluded.
    /// </summary>
    private List<string> _modFoldersByPriority;

    /// <summary>
    /// Pre-built lookup of relative Data paths to absolute file paths, populated
    /// by scanning the FaceGen facegeom subdirectory in each mod folder during
    /// <see cref="Initialize"/>. Highest-priority mod wins when multiple mods
    /// provide the same file. Eliminates per-NPC disk I/O in <see cref="TryResolve"/>.
    /// </summary>
    private Dictionary<string, string> _fileCache;

    private static readonly string FaceGenSubDir =
        Path.Combine("meshes", "actors", "character", "facegendata", "facegeom");

    public bool IsAvailable { get; private set; }

    public MO2SourceResolver(PatcherState patcherState, SynthEBDPaths paths, Logger logger)
    {
        _patcherState = patcherState;
        _paths = paths;
        _logger = logger;
    }

    private void ResolverLog(string message)
    {
        if (LogSourceResolverDebug)
            _logger.LogMessage("[DEBUG-SRCRESOLVE] " + message);
    }

    public void Initialize()
    {
        IsAvailable = false;
        _modFoldersByPriority = null;

        ResolverLog("Initialize() called");

        var mo2Settings = _patcherState.ModManagerSettings?.MO2Settings;
        if (mo2Settings == null || string.IsNullOrEmpty(mo2Settings.ExecutablePath))
        {
            _logger.LogMessage("MO2SourceResolver: MO2 path not configured. Resolver unavailable.");
            return;
        }

        ResolverLog("MO2 path (LinuxMode=" + mo2Settings.LinuxMode + "): " + mo2Settings.ExecutablePath);

        if (!File.Exists(mo2Settings.ExecutablePath))
        {
            _logger.LogMessage("MO2SourceResolver: MO2 path not found at: " + mo2Settings.ExecutablePath);
            return;
        }

        // In Linux mode the configured path points directly at ModOrganizer.ini (the Linux MO2
        // port has no executable); otherwise the ini sits next to ModOrganizer.exe.
        string iniPath = mo2Settings.LinuxMode
            ? mo2Settings.ExecutablePath
            : Path.Combine(Path.GetDirectoryName(mo2Settings.ExecutablePath), "ModOrganizer.ini");
        string mo2Dir = Path.GetDirectoryName(iniPath);
        ResolverLog("INI path: " + iniPath + " exists=" + File.Exists(iniPath));
        if (!File.Exists(iniPath))
        {
            _logger.LogMessage("MO2SourceResolver: ModOrganizer.ini not found at: " + iniPath);
            return;
        }

        // Read the INI once — we need multiple values from it.
        string[] iniLines;
        try
        {
            iniLines = File.ReadAllLines(iniPath);
        }
        catch (Exception ex)
        {
            _logger.LogMessage("MO2SourceResolver: Failed to read ModOrganizer.ini: " + ex.Message);
            return;
        }

        // Resolve the mods directory.
        string modsDir = GetIniFolder(iniLines, "mod_directory", "mods", mo2Dir);
        ResolverLog("Mods directory (from INI): " + (modsDir ?? "(null)") +
                     " exists=" + (!string.IsNullOrEmpty(modsDir) && Directory.Exists(modsDir)));
        if (string.IsNullOrEmpty(modsDir) || !Directory.Exists(modsDir))
        {
            // Fall back to what the user configured in SynthEBD settings.
            if (!string.IsNullOrEmpty(mo2Settings.ModFolderPath) && Directory.Exists(mo2Settings.ModFolderPath))
            {
                modsDir = mo2Settings.ModFolderPath;
                ResolverLog("Mods directory (SynthEBD fallback): " + modsDir);
            }
            else
            {
                _logger.LogMessage("MO2SourceResolver: MO2 mods directory not found. Resolver unavailable.");
                return;
            }
        }

        // Resolve the profiles directory and selected profile name.
        string profilesDir = GetIniFolder(iniLines, "profiles_directory", "profiles", mo2Dir);
        string profileName = GetSelectedProfile(iniLines);

        ResolverLog("Profiles directory: " + (profilesDir ?? "(null)"));
        ResolverLog("Selected profile: " + (profileName ?? "(null)"));

        if (string.IsNullOrEmpty(profileName))
        {
            _logger.LogMessage("MO2SourceResolver: Could not determine selected MO2 profile. Resolver unavailable.");
            return;
        }

        string modlistPath = Path.Combine(profilesDir, profileName, "modlist.txt");
        ResolverLog("modlist.txt path: " + modlistPath + " exists=" + File.Exists(modlistPath));
        if (!File.Exists(modlistPath))
        {
            _logger.LogMessage("MO2SourceResolver: modlist.txt not found at: " + modlistPath);
            return;
        }

        // Parse modlist.txt: each line is +ModName (enabled) or -ModName (disabled).
        // Lines starting with # are comments. Listed from highest to lowest priority.
        // The MO2 "Overwrite" folder is excluded — it either won't contain the
        // file we need, or it contains the conflict winner which is our own
        // previous output.
        var modFolders = new List<string>();

        try
        {
            var lines = File.ReadAllLines(modlistPath);

            // Determine the SynthEBD output folder name so we can exclude it.
            string outputFolderName = GetOutputModFolderName();
            ResolverLog("Output folder name to exclude: " + (outputFolderName ?? "(null)"));
            ResolverLog("modlist.txt has " + lines.Length + " lines");

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                // Only include enabled mods (prefixed with + or *).
                if (!line.StartsWith("+") && !line.StartsWith("*"))
                    continue;

                string modName = line.TrimStart('+', '-', '*');

                // Skip separators (MO2 visual separators end with _separator).
                if (modName.EndsWith("_separator", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Exclude the SynthEBD output folder.
                if (!string.IsNullOrEmpty(outputFolderName) &&
                    modName.Equals(outputFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    ResolverLog("Excluding SynthEBD output folder: " + modName);
                    continue;
                }

                string fullPath = Path.Combine(modsDir, modName);
                if (Directory.Exists(fullPath))
                {
                    modFolders.Add(fullPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogMessage("MO2SourceResolver: Failed to parse modlist.txt: " + ex.Message);
            return;
        }

        _modFoldersByPriority = modFolders;

        if (LogSourceResolverDebug)
        {
            ResolverLog("Mod folders by priority (highest first):");
            for (int i = 0; i < _modFoldersByPriority.Count; i++)
            {
                ResolverLog("  [" + i + "] " + _modFoldersByPriority[i]);
            }
        }

        // Pre-scan FaceGen facegeom directories to build a lookup cache.
        // Iterate highest priority first so the first entry wins ties.
        _fileCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int scannedFolders = 0;

        foreach (var modFolder in _modFoldersByPriority)
        {
            string faceGenDir = Path.Combine(modFolder, FaceGenSubDir);
            if (!Directory.Exists(faceGenDir))
                continue;

            scannedFolders++;
            foreach (var file in Directory.EnumerateFiles(faceGenDir, "*.nif", SearchOption.AllDirectories))
            {
                // Build the relative Data path (e.g. meshes\actors\...\Plugin.esm\ABCD1234.nif)
                string relativePath = file.Substring(modFolder.Length).TrimStart(Path.DirectorySeparatorChar);

                // Highest priority mod wins — don't overwrite existing entries.
                _fileCache.TryAdd(relativePath, file);
            }
        }

        IsAvailable = true;

        _logger.LogMessage("MO2SourceResolver: Initialized with " + _modFoldersByPriority.Count +
                           " mod folders (profile: " + profileName + "). " +
                           "FaceGen cache: " + _fileCache.Count + " files from " +
                           scannedFolders + " folders with facegeom data.");
    }

    public bool TryResolve(string relativeDataPath, out string absolutePath, bool verbose = false)
    {
        absolutePath = null;

        if (!IsAvailable || _fileCache == null)
            return false;

        bool shouldLog = LogSourceResolverDebug && verbose;

        // Normalize separators to match the cache keys built during Initialize().
        string normalized = relativeDataPath.Replace('/', Path.DirectorySeparatorChar);

        if (_fileCache.TryGetValue(normalized, out absolutePath!))
        {
            if (shouldLog)
                ResolverLog("TryResolve: \"" + normalized + "\" -> cache hit: " + absolutePath);
            return true;
        }

        if (shouldLog)
            ResolverLog("TryResolve: \"" + normalized + "\" -> not in cache");

        return false;
    }

    /// <summary>
    /// Extracts the folder name of the SynthEBD output mod from the output data folder path.
    /// For MO2, the output folder is typically "{modsDir}/SynthEBD Output" and the VFS
    /// maps it into Data. We need the folder name (e.g., "SynthEBD Output") to exclude
    /// it from the mod priority list.
    /// </summary>
    private string GetOutputModFolderName()
    {
        string outputDataFolder = _paths.OutputDataFolder;
        if (string.IsNullOrEmpty(outputDataFolder))
            return null;

        // The OutputDataFolder is the path where SynthEBD writes output files.
        // Under MO2, this is typically something like:
        //   {modsDir}/SynthEBD Output
        // The folder name in modlist.txt is just "SynthEBD Output".
        try
        {
            return new DirectoryInfo(outputDataFolder).Name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a folder path from the MO2 INI, handling relative paths.
    /// Falls back to the default subfolder under the MO2 directory.
    /// </summary>
    private static string GetIniFolder(string[] iniLines, string key, string defaultSubfolder, string mo2Dir)
    {
        string prefix = key + "=";
        foreach (var line in iniLines)
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string folderPath = line.Substring(prefix.Length).Trim();
                if (!string.IsNullOrEmpty(folderPath))
                {
                    if (!Path.IsPathRooted(folderPath))
                    {
                        folderPath = Path.Combine(mo2Dir, folderPath);
                    }
                    return Path.GetFullPath(folderPath);
                }
            }
        }

        return Path.Combine(mo2Dir, defaultSubfolder);
    }

    /// <summary>
    /// Extracts the selected profile name from ModOrganizer.ini.
    /// Format: selected_profile=@ByteArray(Profile_Name)
    /// </summary>
    private static string GetSelectedProfile(string[] iniLines)
    {
        foreach (var line in iniLines)
        {
            if (line.StartsWith("selected_profile=", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(line, @"@ByteArray\((.+?)\)");
                if (match.Success && match.Groups.Count > 1)
                {
                    return match.Groups[1].Value;
                }
            }
        }
        return null;
    }
}
