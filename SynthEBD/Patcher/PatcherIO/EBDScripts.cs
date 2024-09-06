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
            if ((_environmentProvider.SkyrimVersion == Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimSE && !_patcherState.TexMeshSettings.bFixedScriptsOldSKSEversion) || _environmentProvider.SkyrimVersion == Mutagen.Bethesda.Skyrim.SkyrimRelease.EnderalSE)
            {
                _logger.LogMessage("Applying fixed EBD script (for SSE 1.5.97 or newer)");
                sourcePath = Path.Combine(_environmentProvider.InternalDataPath, "EBD Code", "SSE", "EBDGlobalFuncs.pex");
            }
            else if (_environmentProvider.SkyrimVersion == Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimVR && _patcherState.TexMeshSettings.bPO3ModeForVR)
            {
                _logger.LogMessage("Applying fixed EBD script (for VR via powerofthree's Papyrus Extender & Tweaks)");
                sourcePath = Path.Combine(_environmentProvider.InternalDataPath, "EBD Code", "VR", "PO3", "EBDGlobalFuncs.pex");
            }
            else
            {
                _logger.LogMessage("Applying fixed EBD script (for SSE < 1.5.97 or VR without powerofthree's Papyrus Extender & Tweaks)");
                sourcePath = Path.Combine(_environmentProvider.InternalDataPath, "EBD Code", "VR", "Non-PO3", "EBDGlobalFuncs.pex");
            }
            string destPath = Path.Combine(_paths.OutputDataFolder, "Scripts", "EBDGlobalFuncs.pex");
            _logger.LogMessage("destPath: " + destPath);
            _patcherIO.TryCopyResourceFile(sourcePath, destPath, _logger);
        }
    }
}
