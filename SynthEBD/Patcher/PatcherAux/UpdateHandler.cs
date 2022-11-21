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
        public static void CleanSPIDiniHeadParts()
        {
            PatcherIO.TryDeleteFile(Path.Combine(PatcherSettings.Paths.OutputDataFolder, "SynthEBDHeadPartDistributor_DISTR.ini"));
        }
        public static void CleanSPIDiniOBody()
        {
            PatcherIO.TryDeleteFile(Path.Combine(PatcherSettings.Paths.OutputDataFolder, "SynthEBDBodySlideDistributor_DISTR.ini"));
        }
    }
}
