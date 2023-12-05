using Noggog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        public async Task<bool> ExtractArchive(string archivePath, string destinationPath, bool hideWindow, Action<string> mirrorUIstr)
        {
            try
            {
                ProcessStartInfo pro = new ProcessStartInfo();
                if (hideWindow)
                {
                    pro.UseShellExecute = false;
                    pro.CreateNoWindow = true;
                    pro.WindowStyle = ProcessWindowStyle.Hidden;
                }
                pro.FileName = _sevenZipPath;
                pro.Arguments = string.Format("x \"{0}\" -y -o\"{1}\"", archivePath, destinationPath);
                if (mirrorUIstr != null)
                {
                    pro.RedirectStandardOutput = true;
                    pro.RedirectStandardError = true;
                    pro.UseShellExecute = false;
                }
                using (Process process = new Process { StartInfo = pro, EnableRaisingEvents = true })
                {
                    process.Start();

                    // Capture the standard output
                    StringBuilder standardOutputCapture = new StringBuilder();

                    // Asynchronously read the standard output
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                        {
                            mirrorUIstr(e.Data);
                            standardOutputCapture.AppendLine(e.Data); // Capture in buffer
                        }
                    };

                    process.BeginOutputReadLine();

                    // Wait for the process to exit
                    await process.WaitForExitAsync();

                    // Do something with the captured standard output
                    var redirectedOutput = standardOutputCapture.ToString();
                    if (redirectedOutput.Contains("Can't open as archive"))
                    {
                        MessageWindow.DisplayNotificationOK("File Extraction Error", "Extraction of " + archivePath + " appears to have failed with message: " + Environment.NewLine + redirectedOutput.Replace("\r\n", Environment.NewLine));
                        return false;
                    }
                }
            }

            catch (Exception e)
            {
                MessageWindow.DisplayNotificationOK("File Extraction Error", "Extraction of " + archivePath + " failed with message: " + Environment.NewLine + ExceptionLogger.GetExceptionStack(e));
                return false;
            }
            return true;
        }

        public async Task<List<string>> GetArchiveContents(string archivePath, bool hideWindow)
        {
            return await GetArchiveContents(archivePath, hideWindow, (_) => { });
        }

        public async Task<List<string>> GetArchiveContents(string archivePath, bool hideWindow, Action<string> mirrorUIstr)
        {
            List<string> outputLines = new();
            try
            {
                ProcessStartInfo pro = new ProcessStartInfo();
                if (hideWindow)
                {
                    pro.UseShellExecute = false;
                    pro.CreateNoWindow = true;
                    pro.WindowStyle = ProcessWindowStyle.Hidden;
                }
                pro.FileName = _sevenZipPath;
                pro.Arguments = string.Format("l -slt \"{0}\"", archivePath);
                if (mirrorUIstr != null)
                {
                    pro.RedirectStandardOutput = true;
                    pro.RedirectStandardError = true;
                    pro.UseShellExecute = false;
                }
                using (Process process = new Process { StartInfo = pro, EnableRaisingEvents = true })
                {
                    process.Start();

                    // Capture the standard output
                    StringBuilder standardOutputCapture = new StringBuilder();

                    // Asynchronously read the standard output
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                        {
                            mirrorUIstr(e.Data);
                            outputLines.Add(e.Data);
                        }
                    };

                    process.BeginOutputReadLine();

                    // Wait for the process to exit
                    await process.WaitForExitAsync();

                    // Do something with the captured standard output
                    var  outputStr = standardOutputCapture.ToString();
                    if (outputStr.Contains("Can't open as archive"))
                    {
                        MessageWindow.DisplayNotificationOK("File Extraction Error", "Extraction of " + archivePath + " appears to have failed with message: " + Environment.NewLine + outputStr.Replace("\r\n", Environment.NewLine));
                        return new();
                    }
                }
            }

            catch (Exception e)
            {
                MessageWindow.DisplayNotificationOK("File Extraction Error", "Extraction of " + archivePath + " failed with message: " + Environment.NewLine + ExceptionLogger.GetExceptionStack(e));
                return new();
            }
 
            var processedOutput = new List<string>(outputLines.Where(x => x.StartsWith("Path = ")).Select(x => x.Replace("Path = ", "")).Where(x => IsFilePathFragment(x)));
            return processedOutput;
        }

        private bool IsFilePathFragment(string input)
        {
            var last = input.Split(Path.DirectorySeparatorChar).Last();
            if (!last.IsNullOrWhitespace())
            {
                var split = last.Split('.');
                if (split.Length > 1)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
