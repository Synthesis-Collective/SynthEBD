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
        public static Settings_TexMesh LoadTexMeshSettings(out bool loadSuccess)
        {
            Settings_TexMesh texMeshSettings = new Settings_TexMesh();

            loadSuccess = true;

            if (File.Exists(PatcherSettings.Paths.TexMeshSettingsPath))
            {
                texMeshSettings = JSONhandler<Settings_TexMesh>.LoadJSONFile(PatcherSettings.Paths.TexMeshSettingsPath, out loadSuccess, out string exceptionStr);
                if (!loadSuccess)
                {
                    Logger.LogError("Could not load Texture/Mesh Settings. Error: " + exceptionStr);
                }
            }
            else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.TexMeshSettingsPath)))
            {
                texMeshSettings = JSONhandler<Settings_TexMesh>.LoadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.TexMeshSettingsPath), out loadSuccess, out string exceptionStr);
                if (!loadSuccess)
                {
                    Logger.LogError("Could not load Texture/Mesh Settings. Error: " + exceptionStr);
                }
            }

            texMeshSettings.TrimPaths = SettingsIO_Misc.LoadTrimPaths(out bool trimPathLoadSuccess);
            if (!trimPathLoadSuccess) { loadSuccess = false; }

            return texMeshSettings;
        }

        public static List<SynthEBD.AssetPack> LoadAssetPacks(List<RaceGrouping> raceGroupings, List<SkyrimMod> recordTemplatePlugins, BodyGenConfigs availableBodyGenConfigs, out bool loadSuccess)
        {
            List<AssetPack> loadedPacks = new List<AssetPack>();

            loadSuccess = true;

            string[] filePaths;

            if (Directory.Exists(PatcherSettings.Paths.AssetPackDirPath))
            {
                filePaths = Directory.GetFiles(PatcherSettings.Paths.AssetPackDirPath, "*.json");
            }
            else
            {
                filePaths = Directory.GetFiles(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.AssetPackDirPath), "*.json");
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

        public static AssetPack LoadAssetPack(string path, List<RaceGrouping> raceGroupings, List<SkyrimMod> recordTemplatePlugins, BodyGenConfigs availableBodyGenConfigs, out bool loadSuccess)
        {
            var synthEBDconfig = new AssetPack();

            synthEBDconfig = JSONhandler<AssetPack>.LoadJSONFile(path, out bool success, out string exceptionStr);
            if (!success)
            {
                var zEBDconfig = JSONhandler<ZEBDAssetPack>.LoadJSONFile(path, out bool zSuccess, out string zExceptionStr);
                if (zSuccess)
                {
                    synthEBDconfig = ZEBDAssetPack.ToSynthEBDAssetPack(zEBDconfig, raceGroupings, recordTemplatePlugins, availableBodyGenConfigs);
                    loadSuccess = true;
                }
                else
                {
                    Logger.LogError("Could not parse " + path + " as SynthEBD or zEBD Asset Config File. Error: " + exceptionStr);
                    Logger.SwitchViewToLogDisplay();
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

        public static List<SkyrimMod> LoadRecordTemplates(out bool loadSuccess)
        {
            List<SkyrimMod> loadedTemplatePlugins = new List<SkyrimMod>();

            string[] filePaths;

            loadSuccess = true;

            if (Directory.Exists(PatcherSettings.Paths.RecordTemplatesDirPath))
            {
                filePaths = Directory.GetFiles(PatcherSettings.Paths.RecordTemplatesDirPath, "*.esp");
            }
            else
            {
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
                    Logger.LogError("Could not parse or load record template plugin " + s);
                    loadSuccess = false;
                }
            }
            return loadedTemplatePlugins;
        }

        public static List<SkyrimMod> LoadRecordTemplates(HashSet<string> filePaths, out bool loadSuccess)
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
                    Logger.LogError("Could not parse or load record template plugin " + s);
                    loadSuccess = false;
                }
            }
            return loadedTemplatePlugins;
        }

        public static void SaveAssetPacks(List<AssetPack> assetPacks, out bool success)
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

        public static void SaveAssetPack(AssetPack assetPack, out bool success)
        {
            success = true;
            if (assetPack.FilePath != "" && assetPack.FilePath.StartsWith(PatcherSettings.Paths.AssetPackDirPath, StringComparison.InvariantCultureIgnoreCase))
            {
                JSONhandler<AssetPack>.SaveJSONFile(assetPack, assetPack.FilePath, out success, out string exceptionStr);
                if (!success)
                {
                    Logger.LogMessage("Error saving Asset Pack Config File: " + exceptionStr);
                }
            }
            else
            {
                string newPath = "";
                if (IO_Aux.IsValidFilename(assetPack.GroupName))
                {
                    PatcherIO.CreateDirectoryIfNeeded(PatcherSettings.Paths.AssetPackDirPath, PatcherIO.PathType.Directory);
                    if (Directory.Exists(PatcherSettings.Paths.AssetPackDirPath))
                    {
                        newPath = Path.Combine(PatcherSettings.Paths.AssetPackDirPath, assetPack.GroupName + ".json");
                    }
                    else if (Directory.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.AssetPackDirPath)))
                    {
                        newPath = Path.Combine(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.AssetPackDirPath), assetPack.GroupName + ".json");
                    }

                    JSONhandler<AssetPack>.SaveJSONFile(assetPack, newPath, out success, out string exceptionStr);
                    if (!success)
                    {
                        Logger.LogMessage("Error saving Asset Pack Config File: " + exceptionStr);
                    }
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
                        JSONhandler<AssetPack>.SaveJSONFile(assetPack, dialog.FileName, out success, out string exceptionStr);
                        if (!success)
                        {
                            Logger.LogMessage("Error saving Asset Pack Config File: " + exceptionStr);
                        }
                    }
                }
            }
        }
    }
}
