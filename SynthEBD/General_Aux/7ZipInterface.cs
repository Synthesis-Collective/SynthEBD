using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class _7ZipInterface
    {
        private readonly IEnvironmentStateProvider _environmentStateProvider;
        private string _sevenZipPath => Path.Combine(_environmentStateProvider.InternalDataPath, "7Zip",
                            Environment.Is64BitProcess ? "x64" : "x86", "7z.exe");
        public _7ZipInterface(IEnvironmentStateProvider environmentStateProvider)
        {
            _environmentStateProvider = environmentStateProvider;
        }

        public bool ExtractArchiveNew(string archivePath, string destinationPath, bool hideWindow)
        {
            try
            {
                //var sevenZipPath = Path.Combine(_environmentStateProvider.InternalDataPath, "7Zip",
                //            Environment.Is64BitProcess ? "x64" : "x86", "7z.exe");

                ProcessStartInfo pro = new ProcessStartInfo();
                pro.WindowStyle = ProcessWindowStyle.Hidden;
                pro.FileName = _sevenZipPath;
                pro.Arguments = string.Format("x \"{0}\" -y -o\"{1}\"", archivePath, destinationPath);
                pro.RedirectStandardOutput = true;
                pro.UseShellExecute = false;
                Process x = Process.Start(pro);
                x.WaitForExit();
                string output = x.StandardOutput.ReadToEnd();
                if (output.Contains("Can't open as archive"))
                {
                    CustomMessageBox.DisplayNotificationOK("File Extraction Error", "Extraction of " + archivePath + " appears to have failed with message: " + Environment.NewLine + output.Replace("\r\n", Environment.NewLine));
                    return false;
                }
            }

            catch (Exception e)
            {
                CustomMessageBox.DisplayNotificationOK("File Extraction Error", "Extraction of " + archivePath + " failed with message: " + Environment.NewLine + ExceptionLogger.GetExceptionStack(e));
                return false;
            }
            return true;
        }
    }
}
