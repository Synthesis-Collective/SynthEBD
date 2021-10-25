using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class SettingsIO_Height
    {
        public static Settings_Height LoadHeightSettings(Paths paths)
        {
            Settings_Height heightSettings = new Settings_Height();

            if (File.Exists(paths.HeightSettingsPath))
            {
                string text = File.ReadAllText(paths.HeightSettingsPath);
                heightSettings = JsonConvert.DeserializeObject<Settings_Height>(text);
            }

            return heightSettings;
        }

        public static List<HeightConfig> loadHeightConfigs(Paths paths, List<string> loadedHeightPaths)
        {
            List<HeightConfig> loaded = new List<HeightConfig>();

            string searchPath = "";
            if (Directory.Exists(paths.HeightConfigDirPath))
            {
                searchPath = paths.HeightConfigDirPath;
            }
            else if (Directory.Exists(paths.FallBackHeightConfigDirPath))
            {
                // Warn User
                searchPath = paths.FallBackHeightConfigDirPath;
            }
            else
            {
                // Warn User
                return loaded;
            }

            string[] filePaths = Directory.GetFiles(searchPath, "*.json");

            foreach (string s in filePaths)
            {
                string text = File.ReadAllText(s);

                if (text.Contains("\"EDID\":")) // zEBD formatted height config
                {
                    try
                    {
                        var zEBDformatted = DeserializeFromJSON<HashSet<HeightAssignment.zEBDHeightAssignment>>.loadJSONFile(s);
                        HeightConfig fromZformat = new HeightConfig();
                        fromZformat.Label = Path.GetFileNameWithoutExtension(s);

                        foreach (var zHC in zEBDformatted)
                        {
                            var ha = new HeightAssignment();
                            ha.Label = zHC.EDID;
                            ha.Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey> { Converters.RaceEDID2FormKey(zHC.EDID) };
                            ha.HeightMale = zHC.heightMale;
                            ha.HeightMaleRange = zHC.heightMaleRange;
                            ha.HeightFemale = zHC.heightFemale;
                            ha.HeightMaleRange = zHC.heightFemaleRange;
                            fromZformat.HeightAssignments.Add(ha);
                        }

                        loadedHeightPaths.Add(s);
                        loaded.Add(fromZformat);
                    }
                    catch
                    {
                    }
                }

                else
                {
                    try
                    {
                        var hc = DeserializeFromJSON<HeightConfig>.loadJSONFile(s);
                        loaded.Add(hc);
                        loadedHeightPaths.Add(s);
                    }
                    catch
                    {
                        //Warn User
                    }
                }
            }

            return loaded;
        }

        public static void SaveHeightConfigs(List<HeightConfig> heightConfigs, List<string> filePaths, Paths paths)
        {
            for (int i = 0; i < heightConfigs.Count; i++)
            {
                if (filePaths[i] != "")
                {
                    SerializeToJSON<HeightConfig>.SaveJSONFile(heightConfigs[i], filePaths[i]);
                }
                else
                {
                    string newPath = "";
                    if (IO_Aux.IsValidFilename(heightConfigs[i].Label))
                    {
                        if (Directory.Exists(paths.HeightConfigDirPath))
                        {
                            newPath = Path.Combine(paths.HeightConfigDirPath, heightConfigs[i].Label + ".json");
                        }
                        else if (Directory.Exists(paths.FallBackHeightConfigDirPath))
                        {
                            newPath = Path.Combine(paths.FallBackHeightConfigDirPath, heightConfigs[i].Label + ".json");
                        }

                        SerializeToJSON<HeightConfig>.SaveJSONFile(heightConfigs[i], newPath);
                    }

                    else
                    {
                        // Configure save file dialog box
                        var dialog = new Microsoft.Win32.SaveFileDialog();
                        dialog.DefaultExt = ".json"; // Default file extension
                        dialog.Filter = "JSON files (.json|*.json"; // Filter files by extension

                        if (Directory.Exists(paths.HeightConfigDirPath))
                        {
                            dialog.InitialDirectory = Path.GetFullPath(paths.HeightConfigDirPath);
                        }
                        else if (Directory.Exists(paths.FallBackHeightConfigDirPath))
                        {
                            dialog.InitialDirectory = Path.GetFullPath(paths.FallBackHeightConfigDirPath);
                        }

                        dialog.RestoreDirectory = true;

                        // Show open file dialog box
                        bool? result = dialog.ShowDialog();

                        // Process open file dialog box results
                        if (result == true)
                        {
                            SerializeToJSON<HeightConfig>.SaveJSONFile(heightConfigs[i], dialog.FileName);
                        }
                    }
                }
            }
        }
    }
}
