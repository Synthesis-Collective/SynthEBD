using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class PatcherIO
    {
        public static async Task WriteINIFile(string path, string contents)
        {
            try
            {
                await File.WriteAllTextAsync(path, contents);
            }
            catch
            {
                Logger.LogError("Could not create file at " + path);
            }
        }
        public static void WritePatch(string patchOutputPath, SkyrimMod outputMod)
        {
            try
            {
                if (System.IO.File.Exists(patchOutputPath))
                {
                    System.IO.File.Delete(patchOutputPath);
                }
                outputMod.WriteToBinary(patchOutputPath);
                Logger.LogMessage("Wrote output file at " + patchOutputPath + ".");
            }
            catch { Logger.LogErrorWithStatusUpdate("Could not write output file to " + patchOutputPath, ErrorType.Error); };
        }
    }
}
