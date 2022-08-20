using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class HeadPartPreprocessing
    {
        public static void CleanPreviousOutputs()
        {
            var outputDir = Path.Combine(PatcherSettings.General.OutputDataFolder, "SynthEBD");
            var oldFiles = Directory.GetFiles(outputDir).Where(x => Path.GetFileName(x).StartsWith("HeadPartDict"));
            foreach (var path in oldFiles)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                        Logger.LogErrorWithStatusUpdate("Could not delete file at " + path, ErrorType.Warning);
                    }
                }
            }
        }
    }
}
