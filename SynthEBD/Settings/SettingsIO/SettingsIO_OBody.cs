using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class SettingsIO_OBody
    {
        public static Settings_OBody LoadOBodySettings()
        {
            Settings_OBody oBodySettings = new Settings_OBody();

            if (File.Exists(PatcherSettings.Paths.OBodySettingsPath))
            {
                string text = File.ReadAllText(PatcherSettings.Paths.OBodySettingsPath);
                oBodySettings = JsonConvert.DeserializeObject<Settings_OBody>(text);
            }
            else if (File.Exists(PatcherSettings.Paths.FallBackOBodySettingsPath))
            {
                string text = File.ReadAllText(PatcherSettings.Paths.FallBackOBodySettingsPath);
                oBodySettings = JsonConvert.DeserializeObject<Settings_OBody>(text);
            }

            return oBodySettings;
        }
    }
}
