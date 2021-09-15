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
        private static string SynthEBDexePath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        public string GeneralSettingsPath = Path.Combine(SynthEBDexePath, "Settings\\GeneralSettings.json");

        public Paths(bool loadFromGameData)
        {
            // create relevant paths if necessary - only in the "home" directory. To avoid inadvertent clutter in the data folder, user must create these directories manually in their data folder
            string settingsDirPath = Path.Combine(SynthEBDexePath, "Settings");
            string assetsDirPath = Path.Combine(SynthEBDexePath, "Asset Packs");
            if (Directory.Exists(settingsDirPath) == false)
            {
                Directory.CreateDirectory(settingsDirPath);
            }
            if (Directory.Exists(assetsDirPath) == false)
            {
                Directory.CreateDirectory(assetsDirPath);
            }


            switch (loadFromGameData)
            {
                case false:
                    RelativePath = SynthEBDexePath;
                    break;
                case true:
                    var env = new GameEnvironmentProvider().MyEnvironment;
                    RelativePath = env.DataFolderPath;
                    break;
            }

            this.TexMeshSettingsPath = Path.Combine(RelativePath, "Settings\\TexMeshSettings.json");
            this.AssetPackPath = Path.Combine(RelativePath, "Asset Packs");
        }

        private string RelativePath { get; set; } 
        
        public string TexMeshSettingsPath { get; set; } // path of the Textures and Meshes settings file
        public string AssetPackPath { get; set; }
    }
}

    
