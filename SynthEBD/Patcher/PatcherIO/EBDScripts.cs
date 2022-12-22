using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class EBDScripts
    {
        public static void ApplyFixedScripts()
        {
            string sourcePath = String.Empty;
            if ((PatcherEnvironmentProvider.Instance.Environment.GameRelease == Mutagen.Bethesda.GameRelease.SkyrimSE && !PatcherSettings.TexMesh.bFixedScriptsOldSKSEversion) || PatcherEnvironmentProvider.Instance.Environment.GameRelease == Mutagen.Bethesda.GameRelease.EnderalSE)
            {
                Logger.LogMessage("Applying fixed EBD script (for SSE 1.5.97 or newer)");
                sourcePath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "EBD Code", "SSE", "EBDGlobalFuncs.pex");
            }
            else
            {
                Logger.LogMessage("Applying fixed EBD script (for VR or SSE < 1.5.97)");
                sourcePath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "EBD Code", "VR", "EBDGlobalFuncs.pex");
            }
            string destPath = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "Scripts", "EBDGlobalFuncs.pex");
            PatcherIO.TryCopyResourceFile(sourcePath, destPath);
        }
    }
}
