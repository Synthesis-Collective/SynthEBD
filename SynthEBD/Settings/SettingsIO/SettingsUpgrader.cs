using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class SettingsUpgrader
    {
        public static string UpgradeDeprecatedSettings(string settingsStr)
        {
            var output = settingsStr;

            output = output.Replace("\"ForceIf\": false", "\"ForceMode\": \"Restrict\"");
            output = output.Replace("\"ForceIf\": true", "\"ForceMode\": \"ForceIfAndRestrict\"");

            return output;
        }
    }
}
