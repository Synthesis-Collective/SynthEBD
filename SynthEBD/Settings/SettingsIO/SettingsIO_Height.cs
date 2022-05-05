using System.IO;

namespace SynthEBD
{
    class SettingsIO_Height
    {
        public static Settings_Height LoadHeightSettings(out bool loadSuccess)
        {
            Settings_Height heightSettings = new Settings_Height();

            loadSuccess = true;

            if (File.Exists(PatcherSettings.Paths.HeightSettingsPath))
            {
                heightSettings = JSONhandler<Settings_Height>.LoadJSONFile(PatcherSettings.Paths.HeightSettingsPath, out loadSuccess, out string exceptionStr);
                if (!loadSuccess)
                {
                    Logger.LogError("Could not load height settings. Error: " + exceptionStr);
                }
            }
            else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.HeightSettingsPath)))
            {
                heightSettings = JSONhandler<Settings_Height>.LoadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.HeightSettingsPath), out loadSuccess, out string exceptionStr);
                if (!loadSuccess)
                {
                    Logger.LogError("Could not load height settings. Error: " + exceptionStr);
                }
            }

            return heightSettings;
        }

        public static List<HeightConfig> LoadHeightConfigs(out bool loadSuccess)
        {
            List<HeightConfig> loaded = new List<HeightConfig>();

            loadSuccess = true;

            string searchPath = "";
            if (Directory.Exists(PatcherSettings.Paths.HeightConfigDirPath))
            {
                searchPath = PatcherSettings.Paths.HeightConfigDirPath;
            }
            else if (Directory.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.HeightConfigDirPath)))
            {
                searchPath = PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.HeightConfigDirPath);
            }
            else
            {
                Logger.LogError("Could not find the Height Config Directory expected at " + PatcherSettings.Paths.HeightConfigDirPath);
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
                        Logger.LogError("Could not load Height Config at " + s + ". Error: " + exceptionStr);
                        loadSuccess = false;
                        continue;
                    }
                    HeightConfig fromZformat = new HeightConfig();
                    fromZformat.Label = Path.GetFileNameWithoutExtension(s);

                    foreach (var zHC in zEBDformatted)
                    {
                        var ha = new HeightAssignment();
                        ha.Label = zHC.EDID;
                        ha.Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey> { Converters.RaceEDID2FormKey(zHC.EDID) };

                        if (float.TryParse(zHC.heightMale, out var maleHeight))
                        {
                            ha.HeightMale = maleHeight;
                        }
                        else
                        {
                            Logger.LogError("Cannot parse male height " + zHC.heightMale + " for Height Assignment: " + ha.Label);
                        }

                        if (float.TryParse(zHC.heightFemale, out var femaleHeight))
                        {
                            ha.HeightFemale = femaleHeight;
                        }
                        else
                        {
                            Logger.LogError("Cannot parse female height " + zHC.heightFemale + " for Height Assignment: " + ha.Label);
                        }

                        if (float.TryParse(zHC.heightMaleRange, out var maleHeightRange))
                        {
                            ha.HeightMaleRange = maleHeightRange;
                        }
                        else
                        {
                            Logger.LogError("Cannot parse male height range " + zHC.heightMaleRange + " for Height Assignment: " + ha.Label);
                        }

                        if (float.TryParse(zHC.heightFemaleRange, out var femaleHeightRange))
                        {
                            ha.HeightFemaleRange = femaleHeightRange;
                        }
                        else
                        {
                            Logger.LogError("Cannot parse female height range " + zHC.heightFemaleRange + " for Height Assignment: " + ha.Label);
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
                        Logger.LogError("Could not load Height Config at " + s + ". Error: " + exceptionStr);
                        loadSuccess = false;
                        continue;
                    }
                    hc.FilePath = s;
                    loaded.Add(hc);
                }
            }

            return loaded;
        }

        public static void SaveHeightConfigs(List<HeightConfig> heightConfigs, out bool saveSuccess)
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

        public static void SaveHeightConfig(HeightConfig heightConfig, out bool saveSuccess)
        {
            saveSuccess = true;
            if (!string.IsNullOrWhiteSpace(heightConfig.FilePath) && heightConfig.FilePath.StartsWith(PatcherSettings.Paths.HeightConfigDirPath, StringComparison.InvariantCultureIgnoreCase))
            {
                JSONhandler<HeightConfig>.SaveJSONFile(heightConfig, heightConfig.FilePath, out saveSuccess, out string exceptionStr);
                if (!saveSuccess)
                {
                    Logger.LogError("Could not save height config. Error: " + exceptionStr);
                }
            }
            else
            {
                string newPath = "";
                if (IO_Aux.IsValidFilename(heightConfig.Label))
                {
                    PatcherIO.CreateDirectoryIfNeeded(PatcherSettings.Paths.HeightConfigDirPath, PatcherIO.PathType.Directory);
                    if (Directory.Exists(PatcherSettings.Paths.HeightConfigDirPath))
                    {
                        newPath = Path.Combine(PatcherSettings.Paths.HeightConfigDirPath, heightConfig.Label + ".json");
                    }
                    else if (Directory.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.HeightConfigDirPath)))
                    {
                        newPath = Path.Combine(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.HeightConfigDirPath), heightConfig.Label + ".json");
                    }

                    JSONhandler<HeightConfig>.SaveJSONFile(heightConfig, newPath, out saveSuccess, out string exceptionStr);
                    if (!saveSuccess)
                    {
                        Logger.LogError("Could not save height config. Error: " + exceptionStr);
                    }
                }
                else
                {
                    // Configure save file dialog box
                    var dialog = new Microsoft.Win32.SaveFileDialog();
                    dialog.DefaultExt = ".json"; // Default file extension
                    dialog.Filter = "JSON files (.json|*.json"; // Filter files by extension

                    if (Directory.Exists(PatcherSettings.Paths.HeightConfigDirPath))
                    {
                        dialog.InitialDirectory = Path.GetFullPath(PatcherSettings.Paths.HeightConfigDirPath);
                    }
                    else if (Directory.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.HeightConfigDirPath)))
                    {
                        dialog.InitialDirectory = Path.GetFullPath(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.HeightConfigDirPath));
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
                            Logger.LogError("Could not save height config. Error: " + exceptionStr);
                        }
                    }
                }
            }
        }
    }
}
