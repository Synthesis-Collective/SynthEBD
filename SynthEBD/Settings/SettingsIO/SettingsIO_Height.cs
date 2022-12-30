using System.IO;

namespace SynthEBD;

public class SettingsIO_Height
{
    private readonly IStateProvider _stateProvider;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    public SettingsIO_Height(IStateProvider stateProvider, Logger logger, SynthEBDPaths paths)
    {
        _stateProvider = stateProvider;
        _logger = logger;
        _paths = paths;
    }
    public Settings_Height LoadHeightSettings(out bool loadSuccess)
    {
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

        return heightSettings;
    }

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
            string text = File.ReadAllText(s);

            if (text.Contains("\"EDID\":")) // zEBD formatted height config
            {
                var zEBDformatted = JSONhandler<HashSet<HeightAssignment.zEBDHeightAssignment>>.LoadJSONFile(s, out bool success, out string exceptionStr);
                if (!success)
                {
                    _logger.LogError("Could not load Height Config at " + s + ". Error: " + exceptionStr);
                    loadSuccess = false;
                    continue;
                }
                HeightConfig fromZformat = new HeightConfig();
                fromZformat.Label = Path.GetFileNameWithoutExtension(s);

                foreach (var zHC in zEBDformatted)
                {
                    var ha = new HeightAssignment();
                    ha.Label = zHC.EDID;
                    ha.Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey> { Converters.RaceEDID2FormKey(zHC.EDID, _stateProvider) };

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
                    continue;
                }
                hc.FilePath = s;
                loaded.Add(hc);
            }
        }

        return loaded;
    }

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