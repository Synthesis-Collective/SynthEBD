using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class SettingsIO_HeadParts
    {
        public static Settings_Headparts LoadHeadPartSettings(out bool loadSuccess)
        {
            Settings_Headparts headPartSettings = new Settings_Headparts();

            loadSuccess = true;

            if (File.Exists(PatcherSettings.Paths.HeadPartsSettingsPath))
            {
                headPartSettings = JSONhandler<Settings_Headparts>.LoadJSONFile(PatcherSettings.Paths.HeadPartsSettingsPath, out loadSuccess, out string exceptionStr);
                if (!loadSuccess)
                {
                    Logger.LogError("Could not load head part settings. Error: " + exceptionStr);
                }
            }
            else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.HeadPartsSettingsPath)))
            {
                headPartSettings = JSONhandler<Settings_Headparts>.LoadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.HeadPartsSettingsPath), out loadSuccess, out string exceptionStr);
                if (!loadSuccess)
                {
                    Logger.LogError("Could not load head part settings. Error: " + exceptionStr);
                }
            }

            return headPartSettings;
        }
    }
}
