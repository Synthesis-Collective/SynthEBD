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
        public static void LoadGeneralSettings(out bool loadSuccess)
        {
            if (File.Exists(PatcherSettings.Paths.GeneralSettingsPath))
            {
                PatcherSettings.General = JSONhandler<Settings_General>.LoadJSONFile(PatcherSettings.Paths.GeneralSettingsPath, out loadSuccess, out string exceptionStr);
                if(loadSuccess && string.IsNullOrWhiteSpace(PatcherSettings.General.OutputDataFolder))
                {
                    PatcherSettings.General.OutputDataFolder = PatcherEnvironmentProvider.Environment.DataFolderPath;
                }
                else if (!loadSuccess)
                {
                    Logger.LogError("Could not parse General Settings. Error: " + exceptionStr);
                }
            }
            else
            {
                PatcherSettings.General = new Settings_General();
                PatcherSettings.General.OutputDataFolder = PatcherEnvironmentProvider.Environment.DataFolderPath;
                loadSuccess = true;
            }
        }
    }
}
