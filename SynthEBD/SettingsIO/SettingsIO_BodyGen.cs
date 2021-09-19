using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class SettingsIO_BodyGen
    {
        public static Settings_BodyGen LoadBodyGenSettings(Paths paths)
        {
            Settings_BodyGen bodygenSettings = new Settings_BodyGen();

            if (File.Exists(paths.HeightSettingsPath))
            {
                string text = File.ReadAllText(paths.HeightSettingsPath);
                bodygenSettings = JsonConvert.DeserializeObject<Settings_BodyGen>(text);
            }

            return bodygenSettings;
        }

        public static BodyGenConfigs loadBodyGenConfigs(List<RaceGrouping> raceGroupings, Paths paths)
        {
            BodyGenConfigs loadedPacks = new BodyGenConfigs();

            string[] filePaths = Directory.GetFiles(paths.BodyGenConfigDirPath, "*.json");

            foreach (string s in filePaths)
            {
                var synthEBDconfig = new BodyGenConfig();

                try // first try deserializing to SynthEBD asset pack
                {
                    synthEBDconfig = DeserializeFromJSON<BodyGenConfig>.loadJSONFile(s);
                    switch(synthEBDconfig.Gender)
                    {
                        case Gender.female:
                            loadedPacks.Female.Add(synthEBDconfig);
                            break;
                        case Gender.male:
                            loadedPacks.Male.Add(synthEBDconfig);
                            break;
                    }
                }
                catch
                {
                    try
                    {
                        // handle deserialization here because the zEBD BodyGenConfig uses key "params" which is reserved in C#
                        string text = File.ReadAllText(s);
                        text = text.Replace("params", "specs");
                        var zEBDconfig = JsonConvert.DeserializeObject<zEBDBodyGenConfig>(text);
                        var convertedPair = zEBDBodyGenConfig.ToSynthEBDConfig(zEBDconfig, raceGroupings, s);
                        if (convertedPair.bMaleInitialized)
                        {
                            loadedPacks.Male.Add(convertedPair.Male);
                        }
                        if (convertedPair.bFemaleInitialized)
                        {
                            loadedPacks.Female.Add(convertedPair.Female);
                        }
                    }
                    catch
                    {
                        throw new Exception("Could not parse the config file at " + s);
                    }
                }
            }

            return loadedPacks;
        }
    }
}
