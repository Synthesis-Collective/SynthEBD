using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class SettingsIO_BodyGen
    {
        public static Settings_BodyGen LoadBodyGenSettings(out bool loadSuccess)
        {
            Settings_BodyGen bodygenSettings = new Settings_BodyGen();

            loadSuccess = true;

            if (File.Exists(PatcherSettings.Paths.BodyGenSettingsPath))
            {
                bodygenSettings = JSONhandler<Settings_BodyGen>.LoadJSONFile(PatcherSettings.Paths.BodyGenSettingsPath, out loadSuccess, out string exceptionStr);
                if (!loadSuccess)
                {
                    Logger.LogError("Could not load BodyGen settings. Error: " + exceptionStr);
                }
            }
            else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.BodyGenSettingsPath)))
            {
                bodygenSettings = JSONhandler<Settings_BodyGen>.LoadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.BodyGenSettingsPath), out loadSuccess, out string exceptionStr);
                if (!loadSuccess)
                {
                    Logger.LogError("Could not load BodyGen settings. Error: " + exceptionStr);
                }
            }

            return bodygenSettings;
        }

        public static BodyGenConfigs LoadBodyGenConfigs(List<RaceGrouping> raceGroupings, out bool loadSuccess)
        {
            string[] empty = new string[0];
            return LoadBodyGenConfigs(empty, PatcherSettings.General.RaceGroupings, out loadSuccess);
        }
        public static BodyGenConfigs LoadBodyGenConfigs(string[] filePaths, List<RaceGrouping> raceGroupings, out bool loadSuccess)
        {
            BodyGenConfigs loadedPacks = new BodyGenConfigs();

            loadSuccess = true;

            if (!filePaths.Any() && Directory.Exists(PatcherSettings.Paths.BodyGenConfigDirPath))
            {
                filePaths = Directory.GetFiles(PatcherSettings.Paths.BodyGenConfigDirPath, "*.json");
            }
            else if (!filePaths.Any() && Directory.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.BodyGenConfigDirPath)))
            {
                filePaths = Directory.GetFiles(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.BodyGenConfigDirPath), "*.json");
            }

            foreach (string s in filePaths)
            {
                var synthEBDconfig = new BodyGenConfig();

                synthEBDconfig = JSONhandler<BodyGenConfig>.LoadJSONFile(s, out bool success, out string exceptionStr);
                if (!success)
                {
                    // handle zEBD deserialization here because the zEBD BodyGenConfig uses key "params" which is reserved in C#
                    string text = File.ReadAllText(s);
                    text = text.Replace("params", "specs");
                    zEBDBodyGenConfig zEBDconfig = new zEBDBodyGenConfig();
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
                        Logger.LogMessage("Could not deserialize BodyGen config at " + s + " as SynthEBD or zEBD BodyGen config. Error: " + exceptionStr);
                        loadSuccess = false;
                        continue;
                    }

                    var convertedPair = zEBDBodyGenConfig.ToSynthEBDConfig(zEBDconfig, raceGroupings, s);
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
                        Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not parse zEBD BodyGen Config file at " + s, ErrorType.Warning, 5);
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
                            Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not delete zEBD bodygen config file after conversion to SynthEBD format: " + s, ErrorType.Warning, 5);
                        }
                    }
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
            }

            foreach (var maleConfig in loadedPacks.Male)
            {
                foreach (var attributeGroup in PatcherSettings.General.AttributeGroups) // add any available attribute groups from the general patcher settings
                {
                    if (!maleConfig.AttributeGroups.Select(x => x.Label).Contains(attributeGroup.Label))
                    {
                        maleConfig.AttributeGroups.Add(new AttributeGroup() { Label = attributeGroup.Label, Attributes = new HashSet<NPCAttribute>(attributeGroup.Attributes) });
                    }
                }
            }

            foreach (var femaleConfig in loadedPacks.Male)
            {
                foreach (var attributeGroup in PatcherSettings.General.AttributeGroups) // add any available attribute groups from the general patcher settings
                {
                    if (!femaleConfig.AttributeGroups.Select(x => x.Label).Contains(attributeGroup.Label))
                    {
                        femaleConfig.AttributeGroups.Add(new AttributeGroup() { Label = attributeGroup.Label, Attributes = new HashSet<NPCAttribute>(attributeGroup.Attributes) });
                    }
                }
            }

            return loadedPacks;
        }

        public static void SaveBodyGenConfigs(HashSet<BodyGenConfig> bodyGenConfigs, out bool saveSuccess)
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

        public static void SaveBodyGenConfig(BodyGenConfig bgConfig, out bool saveSuccess)
        {
            saveSuccess = true; 
            if (!string.IsNullOrWhiteSpace(bgConfig.FilePath) && bgConfig.FilePath.StartsWith(PatcherSettings.Paths.BodyGenConfigDirPath, StringComparison.InvariantCultureIgnoreCase))
            {
                JSONhandler<BodyGenConfig>.SaveJSONFile(bgConfig, bgConfig.FilePath, out bool success, out string exceptionStr);
                if (!success)
                {
                    saveSuccess = false;
                    Logger.LogError("Could not save BodyGen Config. Error: " + exceptionStr);
                }
            }
            else
            {
                string newPath = "";
                if (IO_Aux.IsValidFilename(bgConfig.Label))
                {
                    PatcherIO.CreateDirectoryIfNeeded(PatcherSettings.Paths.BodyGenConfigDirPath, PatcherIO.PathType.Directory);
                    if (Directory.Exists(PatcherSettings.Paths.BodyGenConfigDirPath))
                    {
                        newPath = Path.Combine(PatcherSettings.Paths.BodyGenConfigDirPath, bgConfig.Label + ".json");
                    }
                    else if (Directory.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.BodyGenConfigDirPath)))
                    {
                        newPath = Path.Combine(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.BodyGenConfigDirPath), bgConfig.Label + ".json");
                    }

                    JSONhandler<BodyGenConfig>.SaveJSONFile(bgConfig, newPath, out bool success, out string exceptionStr);
                    if (!success)
                    {
                        saveSuccess = false;
                        Logger.LogError("Could not save BodyGen Config. Error: " + exceptionStr);
                    }
                }

                else
                {
                    // Configure save file dialog box
                    var dialog = new Microsoft.Win32.SaveFileDialog();
                    dialog.DefaultExt = ".json"; // Default file extension
                    dialog.Filter = "JSON files (.json|*.json"; // Filter files by extension

                    if (Directory.Exists(PatcherSettings.Paths.BodyGenConfigDirPath))
                    {
                        dialog.InitialDirectory = Path.GetFullPath(PatcherSettings.Paths.BodyGenConfigDirPath);
                    }
                    else if (Directory.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.BodyGenConfigDirPath)))
                    {
                        dialog.InitialDirectory = Path.GetFullPath(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.BodyGenConfigDirPath));
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
                            Logger.LogError("Could not save BodyGen Config. Error: " + exceptionStr);
                        }
                    }
                }
            }
        }
    }
}
