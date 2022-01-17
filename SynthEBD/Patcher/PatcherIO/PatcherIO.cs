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
        public static FileInfo CreateDirectoryIfNeeded(string path)
        {
            FileInfo file = new System.IO.FileInfo(path);
            file.Directory.Create(); // If the directory already exists, this method does nothing.
            return file;
        }

        public static async Task WriteTextFile(string path, string contents)
        {
            var file = CreateDirectoryIfNeeded(path);

            try
            {
                await File.WriteAllTextAsync(file.FullName, contents);
            }
            catch
            {
                Logger.LogError("Could not create file at " + path);
            }
        }
        public static async Task WriteTextFile(string path, List<string> contents)
        {
            await WriteTextFile(path, string.Join(Environment.NewLine, contents));
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
