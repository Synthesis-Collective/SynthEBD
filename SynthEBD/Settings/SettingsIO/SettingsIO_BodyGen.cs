using Mutagen.Bethesda.Plugins;
using Newtonsoft.Json;
using System.IO;

namespace SynthEBD;

/// <summary>
/// Loads and saves BodyGen data: the top-level <see cref="Settings_BodyGen"/> model and the per-gender
/// <see cref="BodyGenConfig"/> JSON files (with legacy zEBD-format conversion, including the "params"→"specs"
/// key rename and male/female file splitting). Also parses BodyGen template and morph .ini files. Handles
/// primary/fallback directory resolution and save-dialog prompts.
/// </summary>
public class SettingsIO_BodyGen
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly Converters _converters;
    /// <summary>Injects the environment provider, runtime <see cref="PatcherState"/>, logger, path resolver, and record converters.</summary>
    public SettingsIO_BodyGen(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths, Converters converters)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _paths = paths;
        _converters = converters;
    }
    /// <summary>
    /// Loads the top-level BodyGen settings from the primary path, then the fallback path, returning a fresh
    /// default if neither exists. Reads from disk.
    /// </summary>
    /// <param name="loadSuccess">Set true if loaded cleanly (or defaulted), false on parse error.</param>
    /// <returns>The loaded or default <see cref="Settings_BodyGen"/>.</returns>
    public Settings_BodyGen LoadBodyGenSettings(out bool loadSuccess)
    {
        _logger.LogStartupEventStart("Loading BodyGen settings from disk");
        Settings_BodyGen bodygenSettings = new Settings_BodyGen();

        loadSuccess = true;

        if (File.Exists(_paths.BodyGenSettingsPath))
        {
            bodygenSettings = JSONhandler<Settings_BodyGen>.LoadJSONFile(_paths.BodyGenSettingsPath, out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load BodyGen settings. Error: " + exceptionStr);
            }
        }
        else if (File.Exists(_paths.GetFallBackPath(_paths.BodyGenSettingsPath)))
        {
            bodygenSettings = JSONhandler<Settings_BodyGen>.LoadJSONFile(_paths.GetFallBackPath(_paths.BodyGenSettingsPath), out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load BodyGen settings. Error: " + exceptionStr);
            }
        }
        _logger.LogStartupEventEnd("Loading BodyGen settings from disk");
        return bodygenSettings;
    }

    /// <summary>
    /// Convenience overload that loads all BodyGen configs from the default directory using the general
    /// settings' race groupings. Reads from disk.
    /// </summary>
    /// <param name="raceGroupings">Accepted but ignored; the general settings' groupings are used instead.</param>
    /// <param name="loadSuccess">Set false if any config failed to load.</param>
    /// <returns>The loaded male/female config collection.</returns>
    public BodyGenConfigs LoadBodyGenConfigs(List<RaceGrouping> raceGroupings, out bool loadSuccess)
    {
        string[] empty = new string[0];
        return LoadBodyGenConfigs(empty, _patcherState.GeneralSettings.RaceGroupings, out loadSuccess);
    }
    /// <summary>
    /// Loads BodyGen configs from the given file paths (or, if none supplied, every *.json in the config
    /// directory or its fallback), sorting each into the male or female bucket by gender. Reads from and may
    /// write to disk: legacy zEBD configs are converted (with the "params"→"specs" rename) and, when a single
    /// file yields both male and female configs, the original is split into two and the source file is
    /// deleted. Finally merges in any missing attribute groups from general settings.
    /// </summary>
    /// <param name="filePaths">Explicit config files to load, or empty to scan the config directory.</param>
    /// <param name="raceGroupings">Race groupings used when converting legacy zEBD configs.</param>
    /// <param name="loadSuccess">Set false if any config failed to load or convert.</param>
    /// <returns>The loaded male/female config collection.</returns>
    public BodyGenConfigs LoadBodyGenConfigs(string[] filePaths, List<RaceGrouping> raceGroupings, out bool loadSuccess)
    {
        BodyGenConfigs loadedPacks = new BodyGenConfigs();

        loadSuccess = true;

        if (!filePaths.Any() && Directory.Exists(_paths.BodyGenConfigDirPath))
        {
            filePaths = Directory.GetFiles(_paths.BodyGenConfigDirPath, "*.json");
        }
        else if (!filePaths.Any() && Directory.Exists(_paths.GetFallBackPath(_paths.BodyGenConfigDirPath)))
        {
            filePaths = Directory.GetFiles(_paths.GetFallBackPath(_paths.BodyGenConfigDirPath), "*.json");
        }

        foreach (string s in filePaths)
        {
            _logger.LogStartupEventStart("Loading BodyGen Config from disk at " + s);
            var synthEBDconfig = new BodyGenConfig();

            synthEBDconfig = JSONhandler<BodyGenConfig>.LoadJSONFile(s, out bool success, out string exceptionStr);
            if (!success)
            {
                // handle zEBD deserialization here because the zEBD BodyGenConfig uses key "params" which is reserved in C#
                string text = File.ReadAllText(s);
                text = text.Replace("params", "specs");
                zEBDBodyGenConfig zEBDconfig = new zEBDBodyGenConfig(_environmentProvider, _logger, _converters);
                bool deserializationSuccess = false;
                try
                {
                    zEBDconfig = JsonConvert.DeserializeObject<zEBDBodyGenConfig>(text);
                    deserializationSuccess = true;
                }   
                catch
                {
                    deserializationSuccess = false;
                }
                if (!deserializationSuccess)
                {
                    _logger.LogMessage("Could not deserialize BodyGen config at " + s + " as SynthEBD or zEBD BodyGen config. Error: " + exceptionStr);
                    loadSuccess = false;
                    _logger.LogStartupEventEnd("Loading BodyGen Config from disk at " + s);
                    continue;
                }

                var convertedPair = zEBDconfig.ToSynthEBDConfig(raceGroupings, s);
                if (convertedPair.bMaleInitialized)
                {
                    loadedPacks.Male.Add(convertedPair.Male);
                }
                if (convertedPair.bFemaleInitialized)
                {
                    loadedPacks.Female.Add(convertedPair.Female);
                }

                // assign file paths depending on if the source file was split
                if (!convertedPair.bMaleInitialized && !convertedPair.bFemaleInitialized)
                {
                    _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not parse zEBD BodyGen Config file at " + s, ErrorType.Warning, 5);
                }
                else if (convertedPair.bMaleInitialized && !convertedPair.bFemaleInitialized)
                {
                    convertedPair.Male.Label = Path.GetFileNameWithoutExtension(s);
                    convertedPair.Male.FilePath = s;
                }
                else if (convertedPair.bFemaleInitialized && !convertedPair.bMaleInitialized)
                {
                    convertedPair.Female.Label = Path.GetFileNameWithoutExtension(s);
                    convertedPair.Female.FilePath = s;
                }
                else // if both male and female configs were initialized, split the file paths and delete original
                {
                    convertedPair.Male.Label = Path.GetFileNameWithoutExtension(s) + "_Male";
                    convertedPair.Female.Label = Path.GetFileNameWithoutExtension(s) + "_Female";

                    convertedPair.Male.FilePath = convertedPair.Male.Label + ".json";
                    convertedPair.Female.FilePath = convertedPair.Female.Label + ".json";
                    try
                    {
                        File.Delete(s);
                    }
                    catch
                    {
                        _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not delete zEBD bodygen config file after conversion to SynthEBD format: " + s, ErrorType.Warning, 5);
                    }
                }
            }

            if (synthEBDconfig is null)
            {
                _logger.LogError("Could not read BodyGen Config File at " + s);
                _logger.LogStartupEventEnd("Loading BodyGen Config from disk at " + s);
                continue;
            }

            synthEBDconfig.FilePath = s;
            switch (synthEBDconfig.Gender)
            {
                case Gender.Female:
                    loadedPacks.Female.Add(synthEBDconfig);
                    break;
                case Gender.Male:
                    loadedPacks.Male.Add(synthEBDconfig);
                    break;
            }

            _logger.LogStartupEventEnd("Loading BodyGen Config from disk at " + s);
        }

        foreach (var maleConfig in loadedPacks.Male)
        {
            foreach (var attributeGroup in _patcherState.GeneralSettings.AttributeGroups) // add any available attribute groups from the general patcher settings
            {
                if (!maleConfig.AttributeGroups.Select(x => x.Label).Contains(attributeGroup.Label))
                {
                    maleConfig.AttributeGroups.Add(new AttributeGroup() { Label = attributeGroup.Label, Attributes = new HashSet<NPCAttribute>(attributeGroup.Attributes) });
                }
            }
        }

        foreach (var femaleConfig in loadedPacks.Male)
        {
            foreach (var attributeGroup in _patcherState.GeneralSettings.AttributeGroups) // add any available attribute groups from the general patcher settings
            {
                if (!femaleConfig.AttributeGroups.Select(x => x.Label).Contains(attributeGroup.Label))
                {
                    femaleConfig.AttributeGroups.Add(new AttributeGroup() { Label = attributeGroup.Label, Attributes = new HashSet<NPCAttribute>(attributeGroup.Attributes) });
                }
            }
        }

        return loadedPacks;
    }

    /// <summary>
    /// Saves each BodyGen config via <see cref="SaveBodyGenConfig"/>. Writes to disk and may pop save
    /// dialogs. <paramref name="saveSuccess"/> is cleared if any individual save fails.
    /// </summary>
    /// <param name="bodyGenConfigs">The configs to save.</param>
    /// <param name="saveSuccess">Set false if any config failed to save.</param>
    public void SaveBodyGenConfigs(HashSet<BodyGenConfig> bodyGenConfigs, out bool saveSuccess)
    {
        saveSuccess = true;
        foreach (var bgConfig in bodyGenConfigs)
        {
            SaveBodyGenConfig(bgConfig, out bool success);
            if (!success)
            {
                saveSuccess = false;
            }
        }
    }

    /// <summary>
    /// Saves a single BodyGen config. If it already has a path under the config directory, overwrites it;
    /// otherwise derives a path from the label (when it is a valid filename) or prompts the user with a
    /// save-file dialog. Writes to disk and may pop a UI dialog.
    /// </summary>
    /// <param name="bgConfig">The config to save.</param>
    /// <param name="saveSuccess">Set true on success, false on save failure.</param>
    public void SaveBodyGenConfig(BodyGenConfig bgConfig, out bool saveSuccess)
    {
        saveSuccess = true; 
        if (!string.IsNullOrWhiteSpace(bgConfig.FilePath) && bgConfig.FilePath.StartsWith(_paths.BodyGenConfigDirPath, StringComparison.InvariantCultureIgnoreCase))
        {
            JSONhandler<BodyGenConfig>.SaveJSONFile(bgConfig, bgConfig.FilePath, out bool success, out string exceptionStr);
            if (!success)
            {
                saveSuccess = false;
                _logger.LogError("Could not save BodyGen Config. Error: " + exceptionStr);
            }
        }
        else
        {
            string newPath = "";
            if (IO_Aux.IsValidFilename(bgConfig.Label))
            {
                PatcherIO.CreateDirectoryIfNeeded(_paths.BodyGenConfigDirPath, PatcherIO.PathType.Directory);
                if (Directory.Exists(_paths.BodyGenConfigDirPath))
                {
                    newPath = Path.Combine(_paths.BodyGenConfigDirPath, bgConfig.Label + ".json");
                }
                else if (Directory.Exists(_paths.GetFallBackPath(_paths.BodyGenConfigDirPath)))
                {
                    newPath = Path.Combine(_paths.GetFallBackPath(_paths.BodyGenConfigDirPath), bgConfig.Label + ".json");
                }

                JSONhandler<BodyGenConfig>.SaveJSONFile(bgConfig, newPath, out bool success, out string exceptionStr);
                if (!success)
                {
                    saveSuccess = false;
                    _logger.LogError("Could not save BodyGen Config. Error: " + exceptionStr);
                }
            }

            else
            {
                // Configure save file dialog box
                var dialog = new Microsoft.Win32.SaveFileDialog();
                dialog.DefaultExt = ".json"; // Default file extension
                dialog.Filter = "JSON files (*.json)|*.json"; // Filter files by extension

                if (Directory.Exists(_paths.BodyGenConfigDirPath))
                {
                    dialog.InitialDirectory = Path.GetFullPath(_paths.BodyGenConfigDirPath);
                }
                else if (Directory.Exists(_paths.GetFallBackPath(_paths.BodyGenConfigDirPath)))
                {
                    dialog.InitialDirectory = Path.GetFullPath(_paths.GetFallBackPath(_paths.BodyGenConfigDirPath));
                }

                dialog.RestoreDirectory = true;

                // Show open file dialog box
                bool? result = dialog.ShowDialog();

                // Process open file dialog box results
                if (result == true)
                {
                    JSONhandler<BodyGenConfig>.SaveJSONFile(bgConfig, dialog.FileName, out bool success, out string exceptionStr);
                    if (!success)
                    {
                        saveSuccess = false;
                        _logger.LogError("Could not save BodyGen Config. Error: " + exceptionStr);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Parses a BodyGen templates .ini file (skipping '#' comment lines) into template objects, splitting
    /// each non-comment line on '=' into Label and Specs. Reads from disk; returns an empty set if the file
    /// is missing or unreadable.
    /// </summary>
    /// <param name="loadPath">Absolute path to the templates .ini.</param>
    /// <returns>The parsed templates.</returns>
    public HashSet<BodyGenConfig.BodyGenTemplate> LoadTemplatesINI(string loadPath)
    {
        var newTemplates = new HashSet<BodyGenConfig.BodyGenTemplate>();
        if (File.Exists(loadPath))
        {
            var fileLines = IO_Aux.ReadFileToList(loadPath, out bool wasRead);
            if (wasRead)
            {
                foreach (var line in fileLines.Where(x => !x.StartsWith('#')))
                {
                    var split = line.Split('=');
                    if (split.Length == 2)
                    {
                        BodyGenConfig.BodyGenTemplate template = new BodyGenConfig.BodyGenTemplate();
                        template.Label = split[0];
                        template.Specs = split[1];
                        newTemplates.Add(template);
                    }
                }
            }
        }
        return newTemplates;
    }

    /// <summary>
    /// Parses a BodyGen morphs .ini file (skipping '#' comment lines) into (FormKey, morphs) pairs. Each line
    /// is split on '=' into a "Plugin|FormID" NPC reference and the assigned morph string; the FormID is
    /// zero-padded to 6 digits. Reads from disk; lines whose FormKey cannot be parsed are skipped.
    /// </summary>
    /// <param name="loadPath">Absolute path to the morphs .ini.</param>
    /// <returns>The parsed NPC-to-morph assignments.</returns>
    public HashSet<Tuple<FormKey, string>> LoadMorphsINI(string loadPath)
    {
        var loadedAssignments = new HashSet<Tuple<FormKey, string>>();
        if (File.Exists(loadPath))
        {
            var fileLines = IO_Aux.ReadFileToList(loadPath, out bool wasRead);
            if (wasRead)
            {
                foreach (var line in fileLines.Where(x => !x.StartsWith('#')))
                {
                    var split = line.Split('=');
                    if (split.Length == 2)
                    {
                        var formSplit = split[0].Trim().Split('|');
                        var plugin = formSplit[0].Trim();
                        var id = formSplit[1].Trim().PadLeft(6, '0');
                        var npcFormKey = FormKey.TryFactory(id + ':' + plugin);
                        if (npcFormKey is not null)
                        {
                            loadedAssignments.Add(new Tuple<FormKey, string>(npcFormKey.Value, split[1].Trim()));
                        }
                    }
                }
            }
        }
        return loadedAssignments;
    }
}