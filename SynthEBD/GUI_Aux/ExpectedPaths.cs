using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class Paths
    {
        private static string SynthEBDexeDirPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        public static string SettingsSourcePath = Path.Combine(SynthEBDexeDirPath, "Settings\\SettingsSource.json");

        private static string settingsDirRelPath = "Settings";
        private static string assetsDirRelPath = "Asset Packs";
        private static string heightsDirRelPath = "Height Configurations";
        private static string bodyGenDirRelPath = "BodyGen Configurations";
        private static string NPCConfigDirRelPath = "NPC Configuration";
        private static string recordTemplatesDirRelPath = "Record Templates";

        private string settingsDirPath = Path.Combine(SynthEBDexeDirPath, settingsDirRelPath);

        public Paths()
        {
            // create relevant paths if necessary - only in the "home" directory. To avoid inadvertent clutter in the data folder, user must create these directories manually in their data folder

            string settingsDirPath = Path.Combine(SynthEBDexeDirPath, settingsDirRelPath);
            string assetsDirPath = Path.Combine(SynthEBDexeDirPath, assetsDirRelPath);
            string heightsDirPath = Path.Combine(SynthEBDexeDirPath, heightsDirRelPath);
            string bodyGenDirPath = Path.Combine(SynthEBDexeDirPath, bodyGenDirRelPath);
            string NPCConfigDirPath = Path.Combine(SynthEBDexeDirPath, NPCConfigDirRelPath);
            string recordTemplatesDirPath = Path.Combine(SynthEBDexeDirPath, recordTemplatesDirRelPath);

            if (Directory.Exists(settingsDirPath) == false)
            {
                Directory.CreateDirectory(settingsDirPath);
            }
            if (Directory.Exists(assetsDirPath) == false)
            {
                Directory.CreateDirectory(assetsDirPath);
            }
            if (Directory.Exists(heightsDirPath) == false)
            {
                Directory.CreateDirectory(heightsDirPath);
            }
            if (Directory.Exists(bodyGenDirPath) == false)
            {
                Directory.CreateDirectory(bodyGenDirPath);
            }
            if (Directory.Exists(NPCConfigDirPath) == false)
            {
                Directory.CreateDirectory(NPCConfigDirPath);
            }
            if (Directory.Exists(recordTemplatesDirPath) == false)
            {
                Directory.CreateDirectory(recordTemplatesDirPath);
            }

            LogFolderPath = Path.Combine(SynthEBDexeDirPath, "Logs");
            ResourcesFolderPath = Path.Combine(SynthEBDexeDirPath, "Resources");
            RefreshRelativePath();
            RefreshPaths();
        }

        private string RelativePath { get; set; } 
        public string LogFolderPath { get; set; }
        public string ResourcesFolderPath { get; set; }
        public string GeneralSettingsPath { get; set; }
        public string TexMeshSettingsPath { get; set; } // path of the Textures and Meshes settings file
        public string AssetPackDirPath { get; set; }
        public string HeightSettingsPath { get; set; } // path of the Textures and Meshes settings file
        public string HeightConfigDirPath { get; set; }
        public string BodyGenSettingsPath { get; set; }
        public string BodyGenConfigDirPath { get; set; }
        public string OBodySettingsPath { get; set; }
        public string MaleTemplateGroupsPath { get; set; }
        public string FemaleTemplateGroupsPath { get; set; }
        public string ConsistencyPath { get; set; }
        public string SpecificNPCAssignmentsPath { get; set; }
        public string BlockListPath { get; set; }
        public string LinkedNPCNameExclusionsPath { get; set; }
        public string LinkedNPCsPath { get; set; }
        public string TrimPathsPath { get; set; }
        public string RecordReplacerSpecifiersPath { get; set; }
        public string RecordTemplatesDirPath { get; set; }
        public string ModManagerSettingsPath { get; set; }

        public string GetFallBackPath(string path)
        {
            var suffix = path.Remove(0, RelativePath.Length).Trim(Path.PathSeparator);
            return Path.Join(SynthEBDexeDirPath, suffix);
        }

        public void UpdatePaths()
        {
            RefreshRelativePath();
            RefreshPaths();
        }
        private void RefreshRelativePath()
        {
            switch (PatcherSettings.LoadFromDataFolder)
            {
                case false:
                    RelativePath = SynthEBDexeDirPath;
                    break;
                case true:
                    if (PatcherSettings.LoadFromDataFolder && PatcherSettings.General != null && !string.IsNullOrWhiteSpace(PatcherSettings.General.PortableSettingsFolder) && Directory.Exists(PatcherSettings.General.PortableSettingsFolder))
                    {
                        RelativePath = PatcherSettings.General.PortableSettingsFolder;
                    }
                    else
                    {
                        RelativePath = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "SynthEBD");
                    }
                    break;
            }
        }
        private void RefreshPaths()
        {
            this.GeneralSettingsPath = Path.Combine(RelativePath, settingsDirRelPath, "GeneralSettings.json");
            this.TexMeshSettingsPath = Path.Combine(RelativePath, settingsDirRelPath, "TexMeshSettings.json");
            this.AssetPackDirPath = Path.Combine(RelativePath, assetsDirRelPath);
            this.HeightSettingsPath = Path.Combine(RelativePath, settingsDirRelPath, "HeightSettings.json");
            this.HeightConfigDirPath = Path.Combine(RelativePath, heightsDirRelPath);
            this.BodyGenSettingsPath = Path.Combine(RelativePath, settingsDirRelPath, "BodyGenSettings.json");
            this.OBodySettingsPath = Path.Combine(RelativePath, settingsDirRelPath, "OBodySettings.json");
            this.MaleTemplateGroupsPath = Path.Combine(RelativePath, settingsDirPath, "SliderGroupGenders", "Male.json");
            this.FemaleTemplateGroupsPath = Path.Combine(RelativePath, settingsDirPath, "SliderGroupGenders", "Female.json");
            this.BodyGenConfigDirPath = Path.Combine(RelativePath, bodyGenDirRelPath);
            this.ConsistencyPath = Path.Combine(RelativePath, NPCConfigDirRelPath, "Consistency.json");
            this.SpecificNPCAssignmentsPath = Path.Combine(RelativePath, NPCConfigDirRelPath, "Specific NPC Assignments.json");
            this.BlockListPath = Path.Combine(RelativePath, NPCConfigDirRelPath, "BlockList.json");
            this.LinkedNPCNameExclusionsPath = Path.Combine(RelativePath, settingsDirRelPath, "LinkedNPCNameExclusions.json");
            this.LinkedNPCsPath = Path.Combine(RelativePath, settingsDirRelPath, "LinkedNPCs.json");
            this.TrimPathsPath = Path.Combine(RelativePath, settingsDirRelPath, "TrimPathsByExtension.json");
            this.RecordReplacerSpecifiersPath = Path.Combine(RelativePath, settingsDirRelPath, "RecordReplacerSpecifiers.json");
            this.RecordTemplatesDirPath = Path.Combine(RelativePath, recordTemplatesDirRelPath);
            this.ModManagerSettingsPath = Path.Combine(RelativePath, settingsDirRelPath, "ModManagerSettings.json");
        }
    }
}

    
