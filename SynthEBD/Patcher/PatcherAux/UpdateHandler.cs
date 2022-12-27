using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class UpdateHandler // handles backward compatibility for previous SynthEBD versions
    {
        private readonly SynthEBDPaths _paths;
        private readonly PatcherIO _patcherIO;
        public UpdateHandler(SynthEBDPaths paths, PatcherIO patcherIO)
        {
            _paths = paths;
            _patcherIO = patcherIO; 
        }
        public void CleanSPIDiniHeadParts()
        {
            _patcherIO.TryDeleteFile(Path.Combine(_paths.OutputDataFolder, "SynthEBDHeadPartDistributor_DISTR.ini"));
        }
        public void CleanSPIDiniOBody()
        {
            _patcherIO.TryDeleteFile(Path.Combine(_paths.OutputDataFolder, "SynthEBDBodySlideDistributor_DISTR.ini"));
        }
        public void CleanOldBodySlideDict()
        {
            _patcherIO.TryDeleteFile(Path.Combine(_paths.OutputDataFolder, "SynthEBD", "BodySlideDict.json"));
        }
    }
}
