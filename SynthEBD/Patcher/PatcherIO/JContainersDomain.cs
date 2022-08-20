using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class JContainersDomain
    {
        public static void CreateSynthEBDDomain()
        {
            string domainPath = Path.Combine(PatcherSettings.General.OutputDataFolder, "SKSE", "Plugins", "JCData", "Domains", "PSM_SynthEBD");
            Directory.CreateDirectory(domainPath);

            string domainScriptPath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "JContainers Domain", "PSM_SynthEBD.pex");
            string domainScriptDestPath = Path.Combine(PatcherSettings.General.OutputDataFolder, "Scripts", "PSM_SynthEBD.pex");
            PatcherIO.TryCopyResourceFile(domainScriptPath, domainScriptDestPath);
        }
    }
}
