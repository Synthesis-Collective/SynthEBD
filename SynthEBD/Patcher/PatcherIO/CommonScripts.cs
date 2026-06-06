using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Copies SynthEBD's shared compiled Papyrus libraries (<c>SynthEBDcLib.pex</c>,
    /// <c>SynthEBDCommonFuncs.pex</c>) from internal data into the output <c>Scripts</c> folder.
    /// </summary>
    public class CommonScripts
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly SynthEBDPaths _paths;
        private readonly PatcherIO _patcherIO;
        private readonly Logger _logger;
        /// <summary>Initializes a new <see cref="CommonScripts"/> with the environment, paths, IO helper, and logger.</summary>
        public CommonScripts(IEnvironmentStateProvider environmentProvider, SynthEBDPaths paths, PatcherIO patcherIO, Logger logger)
        {
            _environmentProvider = environmentProvider;
            _paths = paths;
            _patcherIO = patcherIO;
            _logger = logger;
        }
        /// <summary>Copies both common Papyrus library <c>.pex</c> files into the output <c>Scripts</c> folder.</summary>
        public void CopyAllToOutputFolder()
        {
            string sourcePath1 = Path.Combine(_environmentProvider.InternalDataPath, "Common Scripts", "SynthEBDcLib.pex");
            string destPath1 = Path.Combine(_paths.OutputDataFolder, "Scripts", "SynthEBDcLib.pex");
            _patcherIO.TryCopyResourceFile(sourcePath1, destPath1, _logger);

            string sourcePath2 = Path.Combine(_environmentProvider.InternalDataPath, "Common Scripts", "SynthEBDCommonFuncs.pex");
            string destPath2 = Path.Combine(_paths.OutputDataFolder, "Scripts", "SynthEBDCommonFuncs.pex");
            _patcherIO.TryCopyResourceFile(sourcePath2, destPath2, _logger);
        }
    }
}
