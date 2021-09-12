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

            string loadLoc = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "Settings\\GeneralSettings.json");

            if (File.Exists(loadLoc))
            {
                string text = File.ReadAllText(loadLoc);
                generalSettings = JsonConvert.DeserializeObject<Settings_General>(text);
            }

            return generalSettings;
        }
    }
}
