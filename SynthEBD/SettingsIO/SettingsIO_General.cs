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
    public class SettingsIO_General
    {
        public static SynthEBD.Settings_General loadGeneralSettings()
        {
            Settings_General generalSettings = new Settings_General();

            Paths paths = new Paths(false); // argument doesn't matter here; paths.GeneralSettingsPath is static

            if (File.Exists(paths.GeneralSettingsPath))
            {
                string text = File.ReadAllText(paths.GeneralSettingsPath);
                generalSettings = JsonConvert.DeserializeObject<Settings_General>(text);
            }

            return generalSettings;
        }
    }
}
