using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class SettingsIO_ModManager
    {
        public static Settings_ModManager LoadModManagerSettings()
        {
            Settings_ModManager modManagerSettings = new Settings_ModManager();

            if (File.Exists(PatcherSettings.Paths.ModManagerSettingsPath))
            {
                string text = File.ReadAllText(PatcherSettings.Paths.ModManagerSettingsPath);
                modManagerSettings = JsonConvert.DeserializeObject<Settings_ModManager>(text);

                if (modManagerSettings != null && string.IsNullOrWhiteSpace(modManagerSettings.CurrentInstallationFolder))
                {
                    modManagerSettings.CurrentInstallationFolder = PatcherEnvironmentProvider.Environment.DataFolderPath;
                }
            }

            return modManagerSettings;
        }
    }
}
