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

        public static HashSet<HeightConfig> loadHeightConfig(string path)
        {
            HashSet<HeightConfig> loaded = new HashSet<HeightConfig>();
            if (File.Exists(path))
            {
                string text = File.ReadAllText(path);

                if (text.Contains("\"EDID\":")) // zEBD formatted height config
                {
                    try
                    {
                        var zEBDformatted = DeserializeFromJSON<HashSet<HeightConfig.zEBDHeightConfig>>.loadJSONFile(path);

                        foreach (HeightConfig.zEBDHeightConfig zHC in zEBDformatted)
                        {
                            var hc = new HeightConfig();
                            hc.Label = zHC.EDID;
                            hc.Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey> { Converters.RaceEDID2FormKey(zHC.EDID) };
                            hc.HeightMale = zHC.heightMale;
                            hc.HeightMaleRange = zHC.heightMaleRange;
                            hc.HeightFemale = zHC.heightFemale;
                            hc.HeightMaleRange = zHC.heightFemaleRange;
                            loaded.Add(hc);
                        }
                    }
                    catch
                    {
                    }
                }

                else
                {
                    try
                    {
                        loaded = DeserializeFromJSON<HashSet<HeightConfig>>.loadJSONFile(path);
                    }
                    catch
                    {

                    }
                }
            }

            return loaded;
        }
    }
}
