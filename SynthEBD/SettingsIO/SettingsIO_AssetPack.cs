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
        public static Settings_TexMesh LoadTexMeshSettings()
        {
            Settings_TexMesh generalSettings = new Settings_TexMesh();

            string loadLoc = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "Settings\\TexMeshSettings.json");

            if (File.Exists(loadLoc))
            {
                string text = File.ReadAllText(loadLoc);
                generalSettings = JsonConvert.DeserializeObject<Settings_TexMesh>(text);
            }

            return generalSettings;
        }

        public static List<SynthEBD.AssetPack> loadAssetPacks(List<RaceGrouping> raceGroupings, List<string> paths)
        {
            List<SynthEBD.AssetPack> loadedPacks = new List<SynthEBD.AssetPack>();

            string loadLoc = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "Asset Packs");

            string[] filePaths = Directory.GetFiles(loadLoc, "*.json");

            foreach (string s in filePaths)
            {
                string text = File.ReadAllText(s);

                var synthEBDconfig = new AssetPack();

                try // first try deserializing to SynthEBD asset pack
                {
                    synthEBDconfig = JsonConvert.DeserializeObject<AssetPack>(text);
                }
                catch
                {
                    try
                    {
                        var zEBDconfig = JsonConvert.DeserializeObject<ZEBDAssetPack>(text);
                        synthEBDconfig = ZEBDAssetPack.ToSynthEBDAssetPack(zEBDconfig, raceGroupings);
                    }
                    catch
                    {
                        throw new Exception("Could not parse the config file at " + s);
                    }
                }

                loadedPacks.Add(synthEBDconfig);
                paths.Add(s);
            }

            return loadedPacks;
        }
    }
}
