using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD
{
    class SettingsIO_AssetPack
    {
        public static Settings_TexMesh LoadTexMeshSettings(Paths paths)
        {
            Settings_TexMesh texMeshSettings = new Settings_TexMesh();

            if (File.Exists(paths.TexMeshSettingsPath))
            {
                string text = File.ReadAllText(paths.TexMeshSettingsPath);
                texMeshSettings = JsonConvert.DeserializeObject<Settings_TexMesh>(text);
            }

            return texMeshSettings;
        }

        public static List<SynthEBD.AssetPack> loadAssetPacks(List<RaceGrouping> raceGroupings, Paths paths, List<string> loadedAssetPackPaths, List<SkyrimMod> recordTemplatePlugins, BodyGenConfigs availableBodyGenConfigs)
        {
            List<AssetPack> loadedPacks = new List<AssetPack>();

            string[] filePaths;

            if (Directory.Exists(paths.AssetPackDirPath))
            {
                filePaths = Directory.GetFiles(paths.AssetPackDirPath, "*.json");
            }
            else
            {
                // Warn User
                filePaths = Directory.GetFiles(paths.FallBackAssetPackDirPath, "*.json");
            }

            foreach (string s in filePaths)
            {
                var synthEBDconfig = new AssetPack();


                //DEBUG
                //synthEBDconfig = JSONhandler<AssetPack>.loadJSONFile(s);
                
                try // first try deserializing to SynthEBD asset pack
                {
                    synthEBDconfig = JSONhandler<AssetPack>.loadJSONFile(s);
                }
                catch
                {
                    try
                    {
                        var zEBDconfig = JSONhandler<ZEBDAssetPack>.loadJSONFile(s);
                        synthEBDconfig = ZEBDAssetPack.ToSynthEBDAssetPack(zEBDconfig, raceGroupings, recordTemplatePlugins, availableBodyGenConfigs);
                    }
                    catch
                    {
                        throw new Exception("Could not parse the config file at " + s);
                    }
                }
                

                loadedPacks.Add(synthEBDconfig);
                loadedAssetPackPaths.Add(s);
            }

            return loadedPacks;
        }

        public static List<SkyrimMod> LoadRecordTemplates(Paths paths)
        {
            List<SkyrimMod> loadedTemplatePlugins = new List<SkyrimMod>();

            string[] filePaths;

            if (Directory.Exists(paths.AssetPackDirPath))
            {
                filePaths = Directory.GetFiles(paths.RecordTemplatesDirPath, "*.esp");
            }
            else
            {
                // Warn User
                filePaths = Directory.GetFiles(paths.FallBackRecordTemplatesDirPath, "*.esp");
            }

            foreach (string s in filePaths)
            {
                try
                {
                    loadedTemplatePlugins.Add(SkyrimMod.CreateFromBinary(s, SkyrimRelease.SkyrimSE));
                }
                catch
                {
                    // Warn User
                }
            }
            return loadedTemplatePlugins;
        }

        public static void SaveAssetPacks(List<AssetPack> assetPacks, List<string> filePaths, Paths paths)
        {
            for (int i = 0; i < assetPacks.Count; i++)
            {
                if (filePaths[i] != "")
                {
                    JSONhandler<AssetPack>.SaveJSONFile(assetPacks[i], filePaths[i]);
                }
                else
                {
                    string newPath = "";
                    if (IO_Aux.IsValidFilename(assetPacks[i].GroupName))
                    {
                        if (Directory.Exists(paths.AssetPackDirPath))
                        {
                            newPath = Path.Combine(paths.AssetPackDirPath, assetPacks[i].GroupName + ".json");
                        }
                        else if (Directory.Exists(paths.FallBackAssetPackDirPath))
                        {
                            newPath = Path.Combine(paths.FallBackAssetPackDirPath, assetPacks[i].GroupName + ".json");
                        }

                        JSONhandler<AssetPack>.SaveJSONFile(assetPacks[i], newPath);
                    }

                    else
                    {
                        // Configure save file dialog box
                        var dialog = new Microsoft.Win32.SaveFileDialog();
                        dialog.DefaultExt = ".json"; // Default file extension
                        dialog.Filter = "JSON files (.json|*.json"; // Filter files by extension

                        if (Directory.Exists(paths.AssetPackDirPath))
                        {
                            dialog.InitialDirectory = Path.GetFullPath(paths.AssetPackDirPath);
                        }
                        else if (Directory.Exists(paths.FallBackAssetPackDirPath))
                        {
                            dialog.InitialDirectory = Path.GetFullPath(paths.FallBackAssetPackDirPath);
                        }

                        dialog.RestoreDirectory = true;

                        // Show open file dialog box
                        bool? result = dialog.ShowDialog();

                        // Process open file dialog box results
                        if (result == true)
                        {
                            JSONhandler<AssetPack>.SaveJSONFile(assetPacks[i], dialog.FileName);
                        }
                    }
                }
            }
        }
    }
}
