using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class CommonScripts
    {
        public static void CopyAllToOutputFolder()
        {
            string sourcePath1 = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "Common Scripts", "SynthEBDcLib.pex");
            string destPath1 = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "Scripts", "SynthEBDcLib.pex");
            PatcherIO.TryCopyResourceFile(sourcePath1, destPath1);

            string sourcePath2 = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "Common Scripts", "SynthEBDCommonFuncs.pex");
            string destPath2 = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "Scripts", "SynthEBDCommonFuncs.pex");
            PatcherIO.TryCopyResourceFile(sourcePath2, destPath2);
        }
    }
}
