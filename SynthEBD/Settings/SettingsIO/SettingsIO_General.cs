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
        public static void loadGeneralSettings()
        {
            if (File.Exists(Paths.GeneralSettingsPath))
            {
                PatcherSettings.General = JSONhandler<Settings_General>.LoadJSONFile(Paths.GeneralSettingsPath);
                if(PatcherSettings.General != null && string.IsNullOrWhiteSpace(PatcherSettings.General.OutputDataFolder))
                {
                    PatcherSettings.General.OutputDataFolder = PatcherEnvironmentProvider.Environment.DataFolderPath;
                }
            }
            else
            {
                Logger.TimedNotifyStatusUpdate("Could not find general settings file - creating new settings", ErrorType.Warning, 3);
                PatcherSettings.General = new Settings_General();
            }
        }
    }
}
