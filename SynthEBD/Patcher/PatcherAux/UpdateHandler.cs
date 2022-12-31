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
        private readonly Logger _logger;
        public UpdateHandler(SynthEBDPaths paths, PatcherIO patcherIO, Logger logger)
        {
            _paths = paths;
            _patcherIO = patcherIO; 
            _logger = logger;
        }
        public void CleanSPIDiniHeadParts()
        {
            _patcherIO.TryDeleteFile(Path.Combine(_paths.OutputDataFolder, "SynthEBDHeadPartDistributor_DISTR.ini"), _logger);
        }
        public void CleanSPIDiniOBody()
        {
            _patcherIO.TryDeleteFile(Path.Combine(_paths.OutputDataFolder, "SynthEBDBodySlideDistributor_DISTR.ini"), _logger);
        }
        public void CleanOldBodySlideDict()
        {
            _patcherIO.TryDeleteFile(Path.Combine(_paths.OutputDataFolder, "SynthEBD", "BodySlideDict.json"), _logger);
        }

        public Dictionary<string, string> V09PathReplacements { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Diffuse", "Diffuse.DataRelativePath" }
            //{ "Diffuse", "Diffuse.DataRelativePath" }
            //{ "Diffuse", "Diffuse.DataRelativePath" }
            //{ "Diffuse", "Diffuse.DataRelativePath" }
            //{ "Diffuse", "Diffuse.DataRelativePath" }
        };
    }
}
