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
        public static Settings_TexMesh LoadTexMeshSettings()
        {
            Settings_TexMesh texMeshSettings = new Settings_TexMesh();

            if (File.Exists(PatcherSettings.Paths.TexMeshSettingsPath))
            {
                string text = File.ReadAllText(PatcherSettings.Paths.TexMeshSettingsPath);
                texMeshSettings = JsonConvert.DeserializeObject<Settings_TexMesh>(text);
            }

            return texMeshSettings;
        }

        public static List<SynthEBD.AssetPack> loadAssetPacks(List<RaceGrouping> raceGroupings, List<SkyrimMod> recordTemplatePlugins, BodyGenConfigs availableBodyGenConfigs)
        {
            List<AssetPack> loadedPacks = new List<AssetPack>();

            string[] filePaths;

            if (Directory.Exists(PatcherSettings.Paths.AssetPackDirPath))
            {
                filePaths = Directory.GetFiles(PatcherSettings.Paths.AssetPackDirPath, "*.json");
            }
            else
            {
                // Warn User
                filePaths = Directory.GetFiles(PatcherSettings.Paths.FallBackAssetPackDirPath, "*.json");
            }

            foreach (string s in filePaths)
            {
                var synthEBDconfig = new AssetPack();
                
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

                synthEBDconfig.FilePath = s;
                loadedPacks.Add(synthEBDconfig);
            }

            return loadedPacks;
        }

        public static List<SkyrimMod> LoadRecordTemplates()
        {
            List<SkyrimMod> loadedTemplatePlugins = new List<SkyrimMod>();

            string[] filePaths;

            if (Directory.Exists(PatcherSettings.Paths.AssetPackDirPath))
            {
                filePaths = Directory.GetFiles(PatcherSettings.Paths.RecordTemplatesDirPath, "*.esp");
            }
            else
            {
                // Warn User
                filePaths = Directory.GetFiles(PatcherSettings.Paths.FallBackRecordTemplatesDirPath, "*.esp");
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

        public static void SaveAssetPacks(List<AssetPack> assetPacks)
        {
            for (int i = 0; i < assetPacks.Count; i++)
            {
                if (assetPacks[i].FilePath != "")
                {
                    JSONhandler<AssetPack>.SaveJSONFile(assetPacks[i], assetPacks[i].FilePath);
                }
                else
                {
                    string newPath = "";
                    if (IO_Aux.IsValidFilename(assetPacks[i].GroupName))
                    {
                        if (Directory.Exists(PatcherSettings.Paths.AssetPackDirPath))
                        {
                            newPath = Path.Combine(PatcherSettings.Paths.AssetPackDirPath, assetPacks[i].GroupName + ".json");
                        }
                        else if (Directory.Exists(PatcherSettings.Paths.FallBackAssetPackDirPath))
                        {
                            newPath = Path.Combine(PatcherSettings.Paths.FallBackAssetPackDirPath, assetPacks[i].GroupName + ".json");
                        }

                        JSONhandler<AssetPack>.SaveJSONFile(assetPacks[i], newPath);
                    }

                    else
                    {
                        // Configure save file dialog box
                        var dialog = new Microsoft.Win32.SaveFileDialog();
                        dialog.DefaultExt = ".json"; // Default file extension
                        dialog.Filter = "JSON files (.json|*.json"; // Filter files by extension

                        if (Directory.Exists(PatcherSettings.Paths.AssetPackDirPath))
                        {
                            dialog.InitialDirectory = Path.GetFullPath(PatcherSettings.Paths.AssetPackDirPath);
                        }
                        else if (Directory.Exists(PatcherSettings.Paths.FallBackAssetPackDirPath))
                        {
                            dialog.InitialDirectory = Path.GetFullPath(PatcherSettings.Paths.FallBackAssetPackDirPath);
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
