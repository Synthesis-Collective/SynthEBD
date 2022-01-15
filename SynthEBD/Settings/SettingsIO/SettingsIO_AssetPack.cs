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
            else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.TexMeshSettingsPath)))
            {
                string text = File.ReadAllText(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.TexMeshSettingsPath));
                texMeshSettings = JsonConvert.DeserializeObject<Settings_TexMesh>(text);
            }

            texMeshSettings.TrimPaths = SettingsIO_Misc.LoadTrimPaths();

            return texMeshSettings;
        }

        public static List<SynthEBD.AssetPack> LoadAssetPacks(List<RaceGrouping> raceGroupings, List<SkyrimMod> recordTemplatePlugins, BodyGenConfigs availableBodyGenConfigs)
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
                filePaths = Directory.GetFiles(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.AssetPackDirPath), "*.json");
            }

            foreach (string s in filePaths)
            {
                var synthEBDconfig = LoadAssetPack(s, raceGroupings, recordTemplatePlugins, availableBodyGenConfigs);
                if (synthEBDconfig != null)
                {
                    loadedPacks.Add(synthEBDconfig);
                }
            }

            return loadedPacks;
        }

        public static AssetPack LoadAssetPack(string path, List<RaceGrouping> raceGroupings, List<SkyrimMod> recordTemplatePlugins, BodyGenConfigs availableBodyGenConfigs)
        {
            var synthEBDconfig = new AssetPack();

            try // first try deserializing to SynthEBD asset pack
            {
                synthEBDconfig = JSONhandler<AssetPack>.loadJSONFile(path);
            }
            catch
            {
                try
                {
                    var zEBDconfig = JSONhandler<ZEBDAssetPack>.loadJSONFile(path);
                    synthEBDconfig = ZEBDAssetPack.ToSynthEBDAssetPack(zEBDconfig, raceGroupings, recordTemplatePlugins, availableBodyGenConfigs);
                }
                catch
                {
                    throw new Exception("Could not parse the config file at " + path);
                }
            }

            synthEBDconfig.FilePath = path;
            return synthEBDconfig;
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
                filePaths = Directory.GetFiles(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.RecordTemplatesDirPath), "*.esp");
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
                if (assetPacks[i].FilePath != "" && assetPacks[i].FilePath.StartsWith(PatcherSettings.Paths.AssetPackDirPath, StringComparison.InvariantCultureIgnoreCase))
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
                        else if (Directory.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.AssetPackDirPath)))
                        {
                            newPath = Path.Combine(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.AssetPackDirPath), assetPacks[i].GroupName + ".json");
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
                        else if (Directory.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.AssetPackDirPath)))
                        {
                            dialog.InitialDirectory = Path.GetFullPath(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.AssetPackDirPath));
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
