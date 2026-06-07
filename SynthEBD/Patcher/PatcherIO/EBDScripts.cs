using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Copies the correct variant of the EBD runtime global-functions Papyrus script
    /// (<c>EBDGlobalFuncs.pex</c>) into the output <c>Scripts</c> folder, selecting the build that matches
    /// the game version and PO3 Papyrus Extender configuration.
    /// </summary>
    public class EBDScripts
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly PatcherState _patcherState;
        private readonly Logger _logger;
        private readonly SynthEBDPaths _paths;
        private readonly PatcherIO _patcherIO;
        /// <summary>Initializes a new <see cref="EBDScripts"/> with the environment, patcher state, logger, paths, and IO helper.</summary>
        public EBDScripts(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths, PatcherIO patcherIO)
        {
            _environmentProvider = environmentProvider;
            _patcherState = patcherState;
            _logger = logger;
            _paths = paths;
            _patcherIO = patcherIO; 
        }
        /// <summary>
        /// Chooses the EBD global-functions script build by game release and settings — SSE/Enderal (1.5.97+),
        /// VR with PO3, or the fallback for older SSE / non-PO3 VR — and copies it to the output <c>Scripts</c> folder.
        /// </summary>
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
            _patcherIO.TryCopyResourceFile(sourcePath, destPath, _logger);
        }
    }
}
