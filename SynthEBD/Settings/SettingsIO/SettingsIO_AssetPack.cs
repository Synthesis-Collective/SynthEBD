using System.IO;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class SettingsIO_AssetPack
{
    private readonly IEnvironmentStateProvider _stateProvider;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly Converters _converters;
    public SettingsIO_AssetPack(IEnvironmentStateProvider stateProvider, Logger logger, SynthEBDPaths paths, Converters converters)
    {
        _stateProvider = stateProvider;
        _logger = logger;
        _paths = paths;
        _converters = converters;
    }
    public Settings_TexMesh LoadTexMeshSettings(out bool loadSuccess)
    {
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

        return texMeshSettings;
    }

    public List<AssetPack> LoadAssetPacks(List<RaceGrouping> raceGroupings, List<SkyrimMod> recordTemplatePlugins, BodyGenConfigs availableBodyGenConfigs, out bool loadSuccess)
    {
        List<AssetPack> loadedPacks = new List<AssetPack>();

        loadSuccess = true;

        string[] filePaths;

        if (Directory.Exists(_paths.AssetPackDirPath))
        {
            filePaths = Directory.GetFiles(_paths.AssetPackDirPath, "*.json");
        }
        else
        {
            filePaths = Directory.GetFiles(_paths.GetFallBackPath(_paths.AssetPackDirPath), "*.json");
        }

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
            }
        }

        return loadedPacks;
    }

    public AssetPack LoadAssetPack(string path, List<RaceGrouping> fallBackRaceGroupings, List<SkyrimMod> recordTemplatePlugins, BodyGenConfigs availableBodyGenConfigs, out bool loadSuccess)
    {
        var synthEBDconfig = new AssetPack();

        synthEBDconfig = JSONhandler<AssetPack>.LoadJSONFile(path, out bool success, out string exceptionStr);
        if (!success)
        {
            var zEBDconfig = JSONhandler<ZEBDAssetPack>.LoadJSONFile(path, out bool zSuccess, out string zExceptionStr);
            if (zSuccess)
            {
                synthEBDconfig = zEBDconfig.ToSynthEBDAssetPack(fallBackRaceGroupings, recordTemplatePlugins, availableBodyGenConfigs, _stateProvider, _converters, _logger, _paths);
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

        foreach (var attributeGroup in PatcherSettings.General.AttributeGroups) // add any available attribute groups from the general patcher settings
        {
            if (!synthEBDconfig.AttributeGroups.Select(x => x.Label).Contains(attributeGroup.Label))
            {
                synthEBDconfig.AttributeGroups.Add(new AttributeGroup() { Label = attributeGroup.Label, Attributes = new HashSet<NPCAttribute>(attributeGroup.Attributes) });
            }
        }

        synthEBDconfig.FilePath = path;
        return synthEBDconfig;
    }

    public List<SkyrimMod> LoadRecordTemplates(out bool loadSuccess)
    {
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
        return loadedTemplatePlugins;
    }

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

    public void SaveAssetPack(AssetPack assetPack, out bool success)
    {
        success = true;
        if (assetPack.FilePath != "" && assetPack.FilePath.StartsWith(_paths.AssetPackDirPath, StringComparison.InvariantCultureIgnoreCase))
        {
            JSONhandler<AssetPack>.SaveJSONFile(assetPack, assetPack.FilePath, out success, out string exceptionStr);
            if (!success)
            {
                _logger.LogMessage("Error saving Asset Pack Config File: " + exceptionStr);
            }
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
            }

            else
            {
                // Configure save file dialog box
                var dialog = new Microsoft.Win32.SaveFileDialog();
                dialog.DefaultExt = ".json"; // Default file extension
                dialog.Filter = "JSON files (.json|*.json"; // Filter files by extension

                if (Directory.Exists(_paths.AssetPackDirPath))
                {
                    dialog.InitialDirectory = Path.GetFullPath(_paths.AssetPackDirPath);
                }
                else if (Directory.Exists(_paths.GetFallBackPath(_paths.AssetPackDirPath)))
                {
                    dialog.InitialDirectory = Path.GetFullPath(_paths.GetFallBackPath(_paths.AssetPackDirPath));
                }

                dialog.RestoreDirectory = true;

                // Show open file dialog box
                bool? result = dialog.ShowDialog();

                // Process open file dialog box results
                if (result == true)
                {
                    JSONhandler<AssetPack>.SaveJSONFile(assetPack, dialog.FileName, out success, out string exceptionStr);
                    if (!success)
                    {
                        _logger.LogMessage("Error saving Asset Pack Config File: " + exceptionStr);
                    }
                }
            }
        }
    }
}