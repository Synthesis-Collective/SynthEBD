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
    /// <summary>
    /// Thin wrapper over the bundled 7-Zip command-line tool (<c>7z.exe</c>) for extracting archives and
    /// listing their contents. Used by the installer/import flows.
    /// </summary>
    public class _7ZipInterface
    {
        private readonly IEnvironmentStateProvider _environmentStateProvider;
        /// <summary>Full path to the architecture-appropriate (x64/x86) bundled <c>7z.exe</c>.</summary>
        private string _sevenZipPath => Path.Combine(_environmentStateProvider.InternalDataPath, "7Zip",
                            Environment.Is64BitProcess ? "x64" : "x86", "7z.exe");

        /// <summary>Creates the interface, capturing the environment provider used to locate the bundled 7-Zip executable.</summary>
        /// <param name="environmentStateProvider">Provides the internal data path where 7-Zip is bundled.</param>
        public _7ZipInterface(IEnvironmentStateProvider environmentStateProvider)
        {
            _environmentStateProvider = environmentStateProvider;
        }

        /// <summary>Extracts an archive to a destination folder by invoking <c>7z x</c>, optionally streaming progress lines to a callback.</summary>
        /// <param name="archivePath">Path to the archive to extract.</param>
        /// <param name="destinationPath">Folder to extract into (overwriting via <c>-y</c>).</param>
        /// <param name="hideWindow">When <c>true</c>, runs 7-Zip without a visible console window.</param>
        /// <param name="mirrorUIstr">Callback receiving each line of 7-Zip's stdout (e.g. to mirror progress in the UI).</param>
        /// <returns><c>true</c> on success; <c>false</c> if extraction failed or 7-Zip reported "Can't open as archive". Failures show a notification dialog.</returns>
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

                    // Asynchronously read the standard output (only valid when stdout was redirected, i.e. a callback was supplied)
                    if (mirrorUIstr != null)
                    {
                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (e.Data != null)
                            {
                                mirrorUIstr(e.Data);
                                standardOutputCapture.AppendLine(e.Data); // Capture in buffer
                            }
                        };

                        process.BeginOutputReadLine();
                    }

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

        /// <summary>Lists the file entries in an archive, with no progress callback.</summary>
        /// <param name="archivePath">Path to the archive.</param>
        /// <param name="hideWindow">When <c>true</c>, runs 7-Zip without a visible console window.</param>
        /// <returns>The archive's file paths, or an empty list on failure.</returns>
        public async Task<List<string>> GetArchiveContents(string archivePath, bool hideWindow)
        {
            return await GetArchiveContents(archivePath, hideWindow, (_) => { });
        }

        /// <summary>Lists the file entries in an archive by invoking <c>7z l -slt</c> and parsing the "Path = " lines.</summary>
        /// <param name="archivePath">Path to the archive.</param>
        /// <param name="hideWindow">When <c>true</c>, runs 7-Zip without a visible console window.</param>
        /// <param name="mirrorUIstr">Callback receiving each line of 7-Zip's stdout.</param>
        /// <returns>The archive's file paths (entries judged to be files by <see cref="IsFilePathFragment"/>), or an empty list on failure.</returns>
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

                    // Asynchronously read the standard output (only valid when stdout was redirected, i.e. a callback was supplied)
                    if (mirrorUIstr != null)
                    {
                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (e.Data != null)
                            {
                                mirrorUIstr(e.Data);
                                outputLines.Add(e.Data);
                            }
                        };

                        process.BeginOutputReadLine();
                    }

                    // Wait for the process to exit
                    await process.WaitForExitAsync();

                    // Check the captured stdout (outputLines, where the handler actually wrote) for a corrupt-archive error
                    if (outputLines.Any(x => x.Contains("Can't open as archive")))
                    {
                        var outputStr = string.Join(Environment.NewLine, outputLines);
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

        /// <summary>Heuristically decides whether an archive entry path refers to a file rather than a directory.</summary>
        /// <param name="input">An archive entry path.</param>
        /// <returns><c>true</c> if the last path segment contains a dot (treated as an extension).</returns>
        /// <remarks>Splits on <see cref="Path.DirectorySeparatorChar"/> and assumes "has a dot ⇒ is a file", so dotted folder names or extensionless files are misclassified.</remarks>
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
