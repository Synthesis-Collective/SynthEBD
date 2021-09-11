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
        public static HashSet<SynthEBD.AssetPack> loadAssetPacks(List<RaceGrouping> raceGroupings)
        {
            HashSet<SynthEBD.AssetPack> loadedPacks = new HashSet<SynthEBD.AssetPack>();

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
            }

            return loadedPacks;
        }
    }
}
