using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class EBDScripts
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly PatcherState _patcherState;
        private readonly Logger _logger;
        private readonly SynthEBDPaths _paths;
        private readonly PatcherIO _patcherIO;
        public EBDScripts(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths, PatcherIO patcherIO)
        {
            _environmentProvider = environmentProvider;
            _patcherState = patcherState;
            _logger = logger;
            _paths = paths;
            _patcherIO = patcherIO; 
        }
        public void ApplyFixedScripts()
        {
            string sourcePath = String.Empty;
            if ((_environmentProvider.SkyrimVersion == Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimSE && !_patcherState.TexMeshSettings.bFixedScriptsOldSKSEversion) || _environmentProvider.SkyrimVersion == Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimVR && HasESLVR() || _environmentProvider.SkyrimVersion == Mutagen.Bethesda.Skyrim.SkyrimRelease.EnderalSE)
            {
                _logger.LogMessage("Applying fixed EBD script (for SSE 1.5.97 or newer)");
                sourcePath = Path.Combine(_environmentProvider.InternalDataPath, "EBD Code", "SSE", "EBDGlobalFuncs.pex");
            }
            else
            {
                _logger.LogMessage("Applying fixed EBD script (for VR or SSE < 1.5.97)");
                sourcePath = Path.Combine(_environmentProvider.InternalDataPath, "EBD Code", "VR", "EBDGlobalFuncs.pex");
            }
            string destPath = Path.Combine(_paths.OutputDataFolder, "Scripts", "EBDGlobalFuncs.pex");
            _patcherIO.TryCopyResourceFile(sourcePath, destPath, _logger);
        }

        private bool HasESLVR()
        {
            string expectedDLLpath = Path.Combine(_environmentProvider.DataFolderPath, "SKSE", "Plugins", "skyrimvresl.dll");
            bool exists = File.Exists(expectedDLLpath);
            if (exists)
            {
                _logger.LogMessage("Detected Skyrim VR ESL Support"); // ESL support comes with ported SKSE scripts that enabled the new EBDGlobalFuncs.pex to work. It does not work with the original SKSEVR scripts, so the old pre-1.5.97 version of EBDGlobalFuncs.pex must be used in that case.
            }
            else
            {
                _logger.LogMessage("Did not detect Skyrim VR ESL Support. Using EBD Script for SSE < 1.5.97");
            }
            return exists;
        }
    }
}
