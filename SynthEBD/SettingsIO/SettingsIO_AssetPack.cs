using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

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

        public static List<SynthEBD.AssetPack> loadAssetPacks(List<RaceGrouping> raceGroupings, Paths paths, List<string> loadedAssetPackPaths)
        {
            List<AssetPack> loadedPacks = new List<AssetPack>();

            string[] filePaths = Directory.GetFiles(paths.AssetPackDirPath, "*.json");

            foreach (string s in filePaths)
            {
                var synthEBDconfig = new AssetPack();

                try // first try deserializing to SynthEBD asset pack
                {
                    synthEBDconfig = DeserializeFromJSON<AssetPack>.loadJSONFile(s);
                }
                catch
                {
                    try
                    {
                        var zEBDconfig = DeserializeFromJSON<ZEBDAssetPack>.loadJSONFile(s);
                        synthEBDconfig = ZEBDAssetPack.ToSynthEBDAssetPack(zEBDconfig, raceGroupings);
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
    }
}
