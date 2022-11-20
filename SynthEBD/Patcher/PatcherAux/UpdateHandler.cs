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
            var path = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "SynthEBDHeadPartDistributor_DISTR.ini");
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception e)
                {
                    Logger.LogErrorWithStatusUpdate("Could not delete outdated SPID ini", ErrorType.Warning);
                    string error = ExceptionLogger.GetExceptionStack(e, "");
                    Logger.LogMessage("Could not delete outdated SPID ini" + Environment.NewLine + error);
                }
            }
        }
    }
}
