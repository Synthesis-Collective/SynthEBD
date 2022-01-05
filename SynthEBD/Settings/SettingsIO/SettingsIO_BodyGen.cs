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
        public static Settings_BodyGen LoadBodyGenSettings()
        {
            Settings_BodyGen bodygenSettings = new Settings_BodyGen();

            if (File.Exists(PatcherSettings.Paths.BodyGenSettingsPath))
            {
                string text = File.ReadAllText(PatcherSettings.Paths.BodyGenSettingsPath);
                bodygenSettings = JsonConvert.DeserializeObject<Settings_BodyGen>(text);
            }
            else if (File.Exists(PatcherSettings.Paths.FallBackBodyGenSettingsPath))
            {
                string text = File.ReadAllText(PatcherSettings.Paths.FallBackBodyGenSettingsPath);
                bodygenSettings = JsonConvert.DeserializeObject<Settings_BodyGen>(text);
            }

            return bodygenSettings;
        }

        public static BodyGenConfigs loadBodyGenConfigs(List<RaceGrouping> raceGroupings)
        {
            BodyGenConfigs loadedPacks = new BodyGenConfigs();

            string[] filePaths = Directory.GetFiles(PatcherSettings.Paths.BodyGenConfigDirPath, "*.json");

            foreach (string s in filePaths)
            {
                var synthEBDconfig = new BodyGenConfig();

                try // first try deserializing to SynthEBD asset pack
                {
                    synthEBDconfig = JSONhandler<BodyGenConfig>.loadJSONFile(s);
                    synthEBDconfig.FilePath = s;
                    switch(synthEBDconfig.Gender)
                    {
                        case Gender.female:
                            loadedPacks.Female.Add(synthEBDconfig);
                            break;
                        case Gender.male:
                            loadedPacks.Male.Add(synthEBDconfig);
                            break;
                    }
                }
                catch
                {
                    try
                    {
                        // handle deserialization here because the zEBD BodyGenConfig uses key "params" which is reserved in C#
                        string text = File.ReadAllText(s);
                        text = text.Replace("params", "specs");
                        var zEBDconfig = JsonConvert.DeserializeObject<zEBDBodyGenConfig>(text);
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
                            Logger.CallTimedNotifyStatusUpdateAsync("Could not parse zEBD BodyGen Config file at " + s, ErrorType.Warning, 5);
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
                                Logger.CallTimedNotifyStatusUpdateAsync("Could not delete zEBD bodygen config file after conversion to SynthEBD format: " + s, ErrorType.Warning, 5);
                            }
                        }
                        
                    }
                    catch
                    {
                        throw new Exception("Could not parse the config file at " + s);
                    }
                }
            }

            return loadedPacks;
        }

        public static void SaveBodyGenConfigs(HashSet<BodyGenConfig> bodyGenConfigs)
        {
            foreach (var bgConfig in bodyGenConfigs)
            {
                if (bgConfig.FilePath != null && bgConfig.FilePath != "")
                {
                    JSONhandler<BodyGenConfig>.SaveJSONFile(bgConfig, bgConfig.FilePath);
                }
                else
                {
                    string newPath = "";
                    if (IO_Aux.IsValidFilename(bgConfig.Label))
                    {
                        if (Directory.Exists(PatcherSettings.Paths.BodyGenConfigDirPath))
                        {
                            newPath = Path.Combine(PatcherSettings.Paths.BodyGenConfigDirPath, bgConfig.Label + ".json");
                        }
                        else if (Directory.Exists(PatcherSettings.Paths.FallBackBodyGenConfigDirPath))
                        {
                            newPath = Path.Combine(PatcherSettings.Paths.FallBackBodyGenConfigDirPath, bgConfig.Label + ".json");
                        }

                        JSONhandler<BodyGenConfig>.SaveJSONFile(bgConfig, newPath);
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
                        else if (Directory.Exists(PatcherSettings.Paths.FallBackBodyGenConfigDirPath))
                        {
                            dialog.InitialDirectory = Path.GetFullPath(PatcherSettings.Paths.FallBackBodyGenConfigDirPath);
                        }

                        dialog.RestoreDirectory = true;

                        // Show open file dialog box
                        bool? result = dialog.ShowDialog();

                        // Process open file dialog box results
                        if (result == true)
                        {
                            JSONhandler<BodyGenConfig>.SaveJSONFile(bgConfig, dialog.FileName);
                        }
                    }
                }
            }
        }
    }
}
