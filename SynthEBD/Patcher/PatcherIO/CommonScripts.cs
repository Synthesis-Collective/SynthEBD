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
        private readonly SynthEBDPaths _paths;
        private readonly PatcherIO _patcherIO;
        public CommonScripts(SynthEBDPaths paths, PatcherIO patcherIO)
        {
            _paths = paths;
            _patcherIO = patcherIO;
        }
        public void CopyAllToOutputFolder()
        {
            string sourcePath1 = Path.Combine(_paths.ResourcesFolderPath, "Common Scripts", "SynthEBDcLib.pex");
            string destPath1 = Path.Combine(_paths.OutputDataFolder, "Scripts", "SynthEBDcLib.pex");
            _patcherIO.TryCopyResourceFile(sourcePath1, destPath1);

            string sourcePath2 = Path.Combine(_paths.ResourcesFolderPath, "Common Scripts", "SynthEBDCommonFuncs.pex");
            string destPath2 = Path.Combine(_paths.OutputDataFolder, "Scripts", "SynthEBDCommonFuncs.pex");
            _patcherIO.TryCopyResourceFile(sourcePath2, destPath2);
        }
    }
}
