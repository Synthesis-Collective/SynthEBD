using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Migrates deprecated settings JSON to the current schema by string-rewriting renamed/restructured keys
    /// and values before deserialization. Invoked by <see cref="JSONhandler{T}.Deserialize"/> on every load.
    /// </summary>
    public class SettingsUpgrader
    {
        /// <summary>
        /// Rewrites legacy keys/values in raw settings JSON: the old "ForceIf" boolean becomes a "ForceMode"
        /// string ("Restrict"/"ForceIfAndRestrict"), and "bPureScriptMode" becomes "bSkyPatcherModeAssets".
        /// Operates purely on the text; does not parse JSON.
        /// </summary>
        /// <param name="settingsStr">The raw settings JSON text.</param>
        /// <returns>The upgraded JSON text.</returns>
        public static string UpgradeDeprecatedSettings(string settingsStr)
        {
            var output = settingsStr;

            output = output.Replace("\"ForceIf\": false", "\"ForceMode\": \"Restrict\"");
            output = output.Replace("\"ForceIf\": true", "\"ForceMode\": \"ForceIfAndRestrict\"");
            
            output = output.Replace("\"bPureScriptMode\"", "\"bSkyPatcherModeAssets\"");

            return output;
        }
    }
}
