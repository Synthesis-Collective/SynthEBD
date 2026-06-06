using System.IO;

namespace SynthEBD;

/// <summary>
/// Loads and saves height data: the top-level <see cref="Settings_Height"/> model and the per-config
/// <see cref="HeightConfig"/> JSON files (with conversion of legacy zEBD-format configs, detected by an
/// "EDID" key, into the modern race-FormKey model). Handles primary/fallback directory resolution and
/// save-dialog prompts.
/// </summary>
public class SettingsIO_Height
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    /// <summary>Injects the environment provider, logger, and path resolver.</summary>
    public SettingsIO_Height(IEnvironmentStateProvider environmentProvider, Logger logger, SynthEBDPaths paths)
    {
        _environmentProvider = environmentProvider;
        _logger = logger;
        _paths = paths;
    }
    /// <summary>
    /// Loads the top-level height settings from the primary path, then the fallback path, returning a fresh
    /// default if neither exists. Reads from disk.
    /// </summary>
    /// <param name="loadSuccess">Set true if loaded cleanly (or defaulted), false on parse error.</param>
    /// <returns>The loaded or default <see cref="Settings_Height"/>.</returns>
    public Settings_Height LoadHeightSettings(out bool loadSuccess)
    {
        _logger.LogStartupEventStart("Loading Height settings from disk");
        Settings_Height heightSettings = new Settings_Height();

        loadSuccess = true;

        if (File.Exists(_paths.HeightSettingsPath))
        {
            heightSettings = JSONhandler<Settings_Height>.LoadJSONFile(_paths.HeightSettingsPath, out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load height settings. Error: " + exceptionStr);
            }
        }
        else if (File.Exists(_paths.GetFallBackPath(_paths.HeightSettingsPath)))
        {
            heightSettings = JSONhandler<Settings_Height>.LoadJSONFile(_paths.GetFallBackPath(_paths.HeightSettingsPath), out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load height settings. Error: " + exceptionStr);
            }
        }
        _logger.LogStartupEventEnd("Loading Height settings from disk");
        return heightSettings;
    }

    /// <summary>
    /// Loads every *.json height config from the config directory (or its fallback). Reads from disk. Files
    /// containing an "EDID" key are treated as legacy zEBD configs and converted: race EDIDs are resolved to
    /// FormKeys and male/female heights and ranges are culture-dependently parsed from strings (unparseable
    /// values are logged and skipped). Returns the loaded configs, or empty with <paramref name="loadSuccess"/>
    /// false if the directory cannot be found.
    /// </summary>
    /// <param name="loadSuccess">Set false if the directory is missing or any config failed to load.</param>
    /// <returns>The loaded height configs.</returns>
    public List<HeightConfig> LoadHeightConfigs(out bool loadSuccess)
    {
        List<HeightConfig> loaded = new List<HeightConfig>();

        loadSuccess = true;

        string searchPath = "";
        if (Directory.Exists(_paths.HeightConfigDirPath))
        {
            searchPath = _paths.HeightConfigDirPath;
        }
        else if (Directory.Exists(_paths.GetFallBackPath(_paths.HeightConfigDirPath)))
        {
            searchPath = _paths.GetFallBackPath(_paths.HeightConfigDirPath);
        }
        else
        {
            _logger.LogError("Could not find the Height Config Directory expected at " + _paths.HeightConfigDirPath);
            loadSuccess = false;
            return loaded;
        }

        string[] filePaths = Directory.GetFiles(searchPath, "*.json");

        foreach (string s in filePaths)
        {
            _logger.LogStartupEventStart("Loading Height Config File from " + s);
            string text = File.ReadAllText(s);

            if (text.Contains("\"EDID\":")) // zEBD formatted height config
            {
                var zEBDformatted = JSONhandler<HashSet<HeightAssignment.zEBDHeightAssignment>>.LoadJSONFile(s, out bool success, out string exceptionStr);
                if (!success)
                {
                    _logger.LogError("Could not load Height Config at " + s + ". Error: " + exceptionStr);
                    loadSuccess = false;
                    _logger.LogStartupEventEnd("Loading Height Config File from " + s);
                    continue;
                }
                HeightConfig fromZformat = new HeightConfig();
                fromZformat.Label = Path.GetFileNameWithoutExtension(s);

                foreach (var zHC in zEBDformatted)
                {
                    var ha = new HeightAssignment();
                    ha.Label = zHC.EDID;
                    ha.Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey> { Converters.RaceEDID2FormKey(zHC.EDID, _environmentProvider) };

                    if (float.TryParse(zHC.heightMale, out var maleHeight))
                    {
                        ha.HeightMale = maleHeight;
                    }
                    else
                    {
                        _logger.LogError("Cannot parse male height " + zHC.heightMale + " for Height Assignment: " + ha.Label);
                    }

                    if (float.TryParse(zHC.heightFemale, out var femaleHeight))
                    {
                        ha.HeightFemale = femaleHeight;
                    }
                    else
                    {
                        _logger.LogError("Cannot parse female height " + zHC.heightFemale + " for Height Assignment: " + ha.Label);
                    }

                    if (float.TryParse(zHC.heightMaleRange, out var maleHeightRange))
                    {
                        ha.HeightMaleRange = maleHeightRange;
                    }
                    else
                    {
                        _logger.LogError("Cannot parse male height range " + zHC.heightMaleRange + " for Height Assignment: " + ha.Label);
                    }

                    if (float.TryParse(zHC.heightFemaleRange, out var femaleHeightRange))
                    {
                        ha.HeightFemaleRange = femaleHeightRange;
                    }
                    else
                    {
                        _logger.LogError("Cannot parse female height range " + zHC.heightFemaleRange + " for Height Assignment: " + ha.Label);
                    }

                    fromZformat.HeightAssignments.Add(ha);
                }

                fromZformat.FilePath = s;
                loaded.Add(fromZformat);
            }

            else
            {
                var hc = JSONhandler<HeightConfig>.LoadJSONFile(s, out bool success, out string exceptionStr);
                if (!success)
                {
                    _logger.LogError("Could not load Height Config at " + s + ". Error: " + exceptionStr);
                    loadSuccess = false;
                    _logger.LogStartupEventEnd("Loading Height Config File from " + s);
                    continue;
                }
                hc.FilePath = s;
                loaded.Add(hc);
                _logger.LogStartupEventEnd("Loading Height Config File from " + s);
            }
        }

        return loaded;
    }

    /// <summary>
    /// Saves each height config via <see cref="SaveHeightConfig"/>. Writes to disk and may pop save dialogs.
    /// <paramref name="saveSuccess"/> is cleared if any individual save fails.
    /// </summary>
    /// <param name="heightConfigs">The configs to save.</param>
    /// <param name="saveSuccess">Set false if any config failed to save.</param>
    public void SaveHeightConfigs(List<HeightConfig> heightConfigs, out bool saveSuccess)
    {
        saveSuccess = true;
        foreach (var heightConfig in heightConfigs)
        {
            SaveHeightConfig(heightConfig, out bool success);
            if (!success)
            {
                saveSuccess = false;
            }
        }
    }

    /// <summary>
    /// Saves a single height config. If it already has a path under the config directory, overwrites it;
    /// otherwise derives a path from the label (when it is a valid filename) or prompts the user with a
    /// save-file dialog. Writes to disk and may pop a UI dialog.
    /// </summary>
    /// <param name="heightConfig">The config to save.</param>
    /// <param name="saveSuccess">Set true on success, false on save failure.</param>
    public void SaveHeightConfig(HeightConfig heightConfig, out bool saveSuccess)
    {
        saveSuccess = true;
        if (!string.IsNullOrWhiteSpace(heightConfig.FilePath) && heightConfig.FilePath.StartsWith(_paths.HeightConfigDirPath, StringComparison.InvariantCultureIgnoreCase))
        {
            JSONhandler<HeightConfig>.SaveJSONFile(heightConfig, heightConfig.FilePath, out saveSuccess, out string exceptionStr);
            if (!saveSuccess)
            {
                _logger.LogError("Could not save height config. Error: " + exceptionStr);
            }
        }
        else
        {
            string newPath = "";
            if (IO_Aux.IsValidFilename(heightConfig.Label))
            {
                PatcherIO.CreateDirectoryIfNeeded(_paths.HeightConfigDirPath, PatcherIO.PathType.Directory);
                if (Directory.Exists(_paths.HeightConfigDirPath))
                {
                    newPath = Path.Combine(_paths.HeightConfigDirPath, heightConfig.Label + ".json");
                }
                else if (Directory.Exists(_paths.GetFallBackPath(_paths.HeightConfigDirPath)))
                {
                    newPath = Path.Combine(_paths.GetFallBackPath(_paths.HeightConfigDirPath), heightConfig.Label + ".json");
                }

                JSONhandler<HeightConfig>.SaveJSONFile(heightConfig, newPath, out saveSuccess, out string exceptionStr);
                if (!saveSuccess)
                {
                    _logger.LogError("Could not save height config. Error: " + exceptionStr);
                }
            }
            else
            {
                // Configure save file dialog box
                var dialog = new Microsoft.Win32.SaveFileDialog();
                dialog.DefaultExt = ".json"; // Default file extension
                dialog.Filter = "JSON files (.json|*.json"; // Filter files by extension

                if (Directory.Exists(_paths.HeightConfigDirPath))
                {
                    dialog.InitialDirectory = Path.GetFullPath(_paths.HeightConfigDirPath);
                }
                else if (Directory.Exists(_paths.GetFallBackPath(_paths.HeightConfigDirPath)))
                {
                    dialog.InitialDirectory = Path.GetFullPath(_paths.GetFallBackPath(_paths.HeightConfigDirPath));
                }

                dialog.RestoreDirectory = true;

                // Show open file dialog box
                bool? result = dialog.ShowDialog();

                // Process open file dialog box results
                if (result == true)
                {
                    JSONhandler<HeightConfig>.SaveJSONFile(heightConfig, dialog.FileName, out saveSuccess, out string exceptionStr);
                    if (!saveSuccess)
                    {
                        _logger.LogError("Could not save height config. Error: " + exceptionStr);
                    }
                }
            }
        }
    }
}