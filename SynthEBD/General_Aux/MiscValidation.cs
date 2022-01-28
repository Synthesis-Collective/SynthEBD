using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace SynthEBD
{
    public class MiscValidation
    {
        public static bool VerifyEBDInstalled()
        {
            bool verified = true;
            
            string helperScriptPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "Scripts", "EBDHelperScript.pex");
            if (!File.Exists(helperScriptPath))
            {
                Logger.LogMessage("Could not find EBDHelperScript.pex from EveryBody's Different Redone SSE at " + helperScriptPath);
                verified = false;
            }

            string globalScriptPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "Scripts", "EBDGlobalFuncs.pex");
            if (!File.Exists(globalScriptPath))
            {
                Logger.LogMessage("Could not find EBDGlobalFuncs.pex from EveryBody's Different Redone SSE at " + globalScriptPath);
                verified = false;
            }

            if (!verified)
            {
                Logger.LogMessage("Please make sure that EveryBody's Different Redone SSE is installed.");
            }

            return verified;
        }

        public static bool VerifyRaceMenuInstalled()
        {
            bool verified = true;

            string dllPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "SKSE", "Plugins", "skee64.dll");
            if (!File.Exists(dllPath))
            {
                Logger.LogMessage("Could not find skee64.dll from RaceMenu at " + dllPath);
                verified = false;
            }

            string iniPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "SKSE", "Plugins", "skee64.ini");
            if (!File.Exists(iniPath))
            {
                Logger.LogMessage("Could not find skee64.dll from RaceMenu at " + iniPath);
                verified = false;
            }

            if (!verified)
            {
                Logger.LogMessage("Please make sure that RaceMenu SE is installed.");
            }

            return verified;
        }

        public static bool VerifyOBodyInstalled()
        {
            bool verified = true;

            string scriptPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "Scripts", "OBodyNative.pex");
            if (!File.Exists(scriptPath))
            {
                Logger.LogMessage("Could not find OBodyNative.pex from OBody at " + scriptPath);
                verified = false;
            }

            string dllPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "SKSE", "Plugins", "OBody.dll");
            if (!File.Exists(dllPath))
            {
                Logger.LogMessage("Could not find OBody.dll from OBody at " + dllPath);
                verified = false;
            }

            if (!verified)
            {
                Logger.LogMessage("Please make sure that OBody is installed.");
            }

            return verified;
        }

        public static bool VerifyAutoBodyInstalled()
        {
            bool verified = true;

            string scriptPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "Scripts", "autoBodyUtils.pex");
            if (!File.Exists(scriptPath))
            {
                Logger.LogMessage("Could not find autoBodyUtils.pex from AutoBody at " + scriptPath);
                verified = false;
            }

            string dllPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "SKSE", "Plugins", "autoBodyAE.dll");
            if (!File.Exists(dllPath))
            {
                Logger.LogMessage("Could not find autoBodyAE.dll from AutoBody at " + dllPath);
                verified = false;
            }

            if (!verified)
            {
                Logger.LogMessage("Please make sure that AutoBody is installed.");
            }

            return verified;
        }
    }
}
