using System.IO;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Loads and saves the asset-patching settings: the top-level <see cref="Settings_TexMesh"/> model, the
/// per-config <see cref="AssetPack"/> JSON files (with transparent zEBD-format fallback), and the record
/// template plugins. Handles primary/fallback directory resolution and, on save, filename validation plus
/// file-dialog prompts for configs whose group name is not a valid filename.
/// </summary>
public class SettingsIO_AssetPack
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly Converters _converters;
    /// <summary>Injects the environment provider, runtime <see cref="PatcherState"/>, logger, path resolver, and record converters.</summary>
    public SettingsIO_AssetPack(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths, Converters converters)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _paths = paths;
        _converters = converters;
    }
    /// <summary>
    /// Loads the top-level texture/mesh settings from the primary path, then the fallback path, returning a
    /// fresh default if neither exists. Reads from disk.
    /// </summary>
    /// <param name="loadSuccess">Set true if loaded cleanly (or defaulted), false on parse error.</param>
    /// <returns>The loaded or default <see cref="Settings_TexMesh"/>.</returns>
    public Settings_TexMesh LoadTexMeshSettings(out bool loadSuccess)
    {
        _logger.LogStartupEventStart("Loading TexMesh settings from disk");
        Settings_TexMesh texMeshSettings = new Settings_TexMesh();

        loadSuccess = true;

        if (File.Exists(_paths.TexMeshSettingsPath))
        {
            texMeshSettings = JSONhandler<Settings_TexMesh>.LoadJSONFile(_paths.TexMeshSettingsPath, out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load Texture/Mesh Settings. Error: " + exceptionStr);
            }
        }
        else if (File.Exists(_paths.GetFallBackPath(_paths.TexMeshSettingsPath)))
        {
            texMeshSettings = JSONhandler<Settings_TexMesh>.LoadJSONFile(_paths.GetFallBackPath(_paths.TexMeshSettingsPath), out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load Texture/Mesh Settings. Error: " + exceptionStr);
            }
        }
        _logger.LogStartupEventEnd("Loading TexMesh settings from disk");
        return texMeshSettings;
    }

    /// <summary>
    /// Enumerates every *.json file in the asset-pack directory (or its fallback) and loads each as an
    /// <see cref="AssetPack"/> via <see cref="LoadAssetPack"/>. Reads from disk. A single failed file logs an
    /// error and clears <paramref name="loadSuccess"/> but does not abort the rest.
    /// </summary>
    /// <param name="raceGroupings">Fallback race groupings passed to zEBD conversion.</param>
    /// <param name="recordTemplatePlugins">Record template plugins passed to zEBD conversion.</param>
    /// <param name="availableBodyGenConfigs">BodyGen configs passed to zEBD conversion.</param>
    /// <param name="loadSuccess">Set false if any config file failed to load.</param>
    /// <returns>The successfully loaded asset packs.</returns>
    public List<AssetPack> LoadAssetPacks(List<RaceGrouping> raceGroupings, List<SkyrimMod> recordTemplatePlugins, BodyGenConfigs availableBodyGenConfigs, out bool loadSuccess)
    {
        List<AssetPack> loadedPacks = new List<AssetPack>();

        loadSuccess = true;

        string[] filePaths;

        string logSearchPath = _paths.AssetPackDirPath;

        if (Directory.Exists(_paths.AssetPackDirPath))
        {
            filePaths = Directory.GetFiles(_paths.AssetPackDirPath, "*.json");
        }
        else
        {
            logSearchPath = _paths.GetFallBackPath(_paths.AssetPackDirPath);
            filePaths = Directory.GetFiles(logSearchPath , "*.json");
        }

        _logger.LogStartupEventStart("Loading " + filePaths.Count().ToString() + " config files from " + logSearchPath);

        foreach (string s in filePaths)
        {
            var synthEBDconfig = LoadAssetPack(s, raceGroupings, recordTemplatePlugins, availableBodyGenConfigs, out bool success);
            if (success)
            {
                loadedPacks.Add(synthEBDconfig);
            }
            else
            {
                loadSuccess = false;
                _logger.LogError("Failed to load config file from " + s);
            }
        }

        _logger.LogStartupEventEnd("Loading " + filePaths.Count().ToString() + " config files from " + logSearchPath);

        return loadedPacks;
    }

    /// <summary>
    /// Loads a single asset config from <paramref name="path"/>, first as a SynthEBD <see cref="AssetPack"/>
    /// and, if that fails, as a legacy <see cref="ZEBDAssetPack"/> which is converted in place. Reads from
    /// disk. Merges in any attribute groups from general settings that the config lacks, and stamps the
    /// config's <see cref="AssetPack.FilePath"/>.
    /// </summary>
    /// <param name="path">Absolute path to the config file.</param>
    /// <param name="fallBackRaceGroupings">Race groupings used when converting a zEBD config.</param>
    /// <param name="recordTemplatePlugins">Record templates used when converting a zEBD config.</param>
    /// <param name="availableBodyGenConfigs">BodyGen configs used when converting a zEBD config.</param>
    /// <param name="loadSuccess">Set true on success, false if neither format parsed.</param>
    /// <returns>The loaded asset pack; a partially initialized pack on failure.</returns>
    public AssetPack LoadAssetPack(string path, List<RaceGrouping> fallBackRaceGroupings, List<SkyrimMod> recordTemplatePlugins, BodyGenConfigs availableBodyGenConfigs, out bool loadSuccess)
    {
        var synthEBDconfig = new AssetPack();

        synthEBDconfig = JSONhandler<AssetPack>.LoadJSONFile(path, out bool success, out string exceptionStr);
        if (!success)
        {
            var zEBDconfig = JSONhandler<ZEBDAssetPack>.LoadJSONFile(path, out bool zSuccess, out string zExceptionStr);
            if (zSuccess)
            {
                synthEBDconfig = zEBDconfig.ToSynthEBDAssetPack(fallBackRaceGroupings, recordTemplatePlugins, availableBodyGenConfigs, _environmentProvider, _converters, _logger, _paths);
                loadSuccess = true;
            }
            else
            {
                _logger.LogError("Could not parse " + path + " as SynthEBD or zEBD Asset Config File. Error: " + exceptionStr);
                loadSuccess = false;
                return synthEBDconfig;
            }
        }
        else
        {
            loadSuccess = true;
        }

        foreach (var attributeGroup in _patcherState.GeneralSettings.AttributeGroups) // add any available attribute groups from the general patcher settings
        {
            if (!synthEBDconfig.AttributeGroups.Select(x => x.Label).Contains(attributeGroup.Label))
            {
                synthEBDconfig.AttributeGroups.Add(new AttributeGroup() { Label = attributeGroup.Label, Attributes = new HashSet<NPCAttribute>(attributeGroup.Attributes) });
            }
        }

        synthEBDconfig.FilePath = path;
        return synthEBDconfig;
    }

    /// <summary>
    /// Loads all *.esp record template plugins from the primary directory plus any additional ones present
    /// only in the fallback directory (so missing templates don't break configs). Reads/parses plugin files
    /// via Mutagen. A plugin that fails to parse logs an error and clears <paramref name="loadSuccess"/>.
    /// </summary>
    /// <param name="loadSuccess">Set false if any plugin failed to parse.</param>
    /// <returns>The loaded record template plugins.</returns>
    public List<SkyrimMod> LoadRecordTemplates(out bool loadSuccess)
    {
        _logger.LogStartupEventStart("Loading Record Template Plugins from disk");
        List<SkyrimMod> loadedTemplatePlugins = new List<SkyrimMod>();

        string[] filePaths;

        loadSuccess = true;

        if (Directory.Exists(_paths.RecordTemplatesDirPath))
        {
            filePaths = Directory.GetFiles(_paths.RecordTemplatesDirPath, "*.esp");

            // load any available record templates in the fallback folder since they may not have been copied over and missing them will screw up the config files
            var fileNames = filePaths.Select(x => Path.GetFileName(x));
            var fallBackFilePaths = Directory.GetFiles(_paths.GetFallBackPath(_paths.RecordTemplatesDirPath), "*.esp");
            var additionalFilePaths = fallBackFilePaths.Where(x => !fileNames.Contains(Path.GetFileName(x)));
            filePaths = filePaths.Concat(additionalFilePaths).ToArray();
        }
        else
        {
            filePaths = Directory.GetFiles(_paths.GetFallBackPath(_paths.RecordTemplatesDirPath), "*.esp");
        }

        foreach (string s in filePaths)
        {
            try
            {
                loadedTemplatePlugins.Add(SkyrimMod.CreateFromBinary(s, SkyrimRelease.SkyrimSE));
            }
            catch
            {
                _logger.LogError("Could not parse or load record template plugin " + s);
                loadSuccess = false;
            }
        }
        _logger.LogStartupEventEnd("Loading Record Template Plugins from disk");
        return loadedTemplatePlugins;
    }

    /// <summary>
    /// Loads record template plugins from an explicit set of file paths. Reads/parses plugin files via
    /// Mutagen. A plugin that fails to parse logs an error and clears <paramref name="loadSuccess"/>.
    /// </summary>
    /// <param name="filePaths">Absolute paths of the .esp plugins to load.</param>
    /// <param name="loadSuccess">Set false if any plugin failed to parse.</param>
    /// <returns>The loaded record template plugins.</returns>
    public List<SkyrimMod> LoadRecordTemplates(HashSet<string> filePaths, out bool loadSuccess)
    {
        List<SkyrimMod> loadedTemplatePlugins = new List<SkyrimMod>();

        loadSuccess = true;

        foreach (string s in filePaths)
        {
            try
            {
                loadedTemplatePlugins.Add(SkyrimMod.CreateFromBinary(s, SkyrimRelease.SkyrimSE));
            }
            catch
            {
                _logger.LogError("Could not parse or load record template plugin " + s);
                loadSuccess = false;
            }
        }
        return loadedTemplatePlugins;
    }

    /// <summary>
    /// Saves each asset pack in the list via <see cref="SaveAssetPack"/>. Writes to disk and may pop save
    /// dialogs. <paramref name="success"/> is cleared if any individual save fails.
    /// </summary>
    /// <param name="assetPacks">The asset packs to save.</param>
    /// <param name="success">Set false if any pack failed to save.</param>
    public void SaveAssetPacks(List<AssetPack> assetPacks, out bool success)
    {
        success = true;
        for (int i = 0; i < assetPacks.Count; i++)
        {
            SaveAssetPack(assetPacks[i], out bool apSuccess);
            if (!apSuccess)
            {
                success=false;
            }
        }
    }

    /// <summary>
    /// Saves a single asset pack. If it already has a path under the asset-pack directory, overwrites it;
    /// otherwise derives a path from the group name (when it is a valid filename) or prompts the user with a
    /// save-file dialog. Writes to disk and may pop a UI dialog.
    /// </summary>
    /// <param name="assetPack">The asset pack to save.</param>
    /// <param name="success">Set true on success, false on save failure.</param>
    /// <returns>The path the config was saved to (may be empty/unset if the user canceled the dialog).</returns>
    public string SaveAssetPack(AssetPack assetPack, out bool success) // returns the path to which config was saved
    {
        success = true;
        if (assetPack.FilePath != "" && assetPack.FilePath.StartsWith(_paths.AssetPackDirPath, StringComparison.InvariantCultureIgnoreCase))
        {
            JSONhandler<AssetPack>.SaveJSONFile(assetPack, assetPack.FilePath, out success, out string exceptionStr);
            if (!success)
            {
                _logger.LogMessage("Error saving Asset Pack Config File: " + exceptionStr);
            }
            return assetPack.FilePath;
        }
        else
        {
            string newPath = "";
            if (IO_Aux.IsValidFilename(assetPack.GroupName))
            {
                PatcherIO.CreateDirectoryIfNeeded(_paths.AssetPackDirPath, PatcherIO.PathType.Directory);
                if (Directory.Exists(_paths.AssetPackDirPath))
                {
                    newPath = Path.Combine(_paths.AssetPackDirPath, assetPack.GroupName + ".json");
                }
                else if (Directory.Exists(_paths.GetFallBackPath(_paths.AssetPackDirPath)))
                {
                    newPath = Path.Combine(_paths.GetFallBackPath(_paths.AssetPackDirPath), assetPack.GroupName + ".json");
                }

                JSONhandler<AssetPack>.SaveJSONFile(assetPack, newPath, out success, out string exceptionStr);
                if (!success)
                {
                    _logger.LogMessage("Error saving Asset Pack Config File: " + exceptionStr);
                }
                return newPath;
            }

            else
            {
                string initialDir = "";
                if (Directory.Exists(_paths.AssetPackDirPath))
                {
                    initialDir = Path.GetFullPath(_paths.AssetPackDirPath);
                }
                else if (Directory.Exists(_paths.GetFallBackPath(_paths.AssetPackDirPath)))
                {
                    initialDir = Path.GetFullPath(_paths.GetFallBackPath(_paths.AssetPackDirPath));
                }

                if (IO_Aux.SelectFileSave(initialDir, "JSON files (*.json)|*.json", ".json", "Save Asset Config File", out string savePath, IO_Aux.MakeValidFileName(assetPack.GroupName)))
                {
                    JSONhandler<AssetPack>.SaveJSONFile(assetPack, savePath, out success, out string exceptionStr);
                    if (!success)
                    {
                        _logger.LogMessage("Error saving Asset Pack Config File: " + exceptionStr);
                    }
                }
                return savePath;
            }
        }
    }
}