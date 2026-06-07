using Alphaleonis.Win32.Network;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>Helper for launching Windows Explorer at a given directory from the UI.</summary>
    public class WinExplorerOpener
    {
        //https://www.codeproject.com/Questions/852563/How-to-open-file-explorer-at-given-location-in-csh
        /// <summary>Opens Windows Explorer at <paramref name="folderPath"/>. Shows an OK dialog if the
        /// directory does not exist or if Explorer fails to launch.</summary>
        public static void OpenFolder(string folderPath)
        {
            if (Directory.Exists(folderPath))
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    Arguments = folderPath,
                    FileName = "explorer.exe"
                };
                try
                {
                    Process.Start(startInfo);
                }
                catch
                {
                    MessageWindow.DisplayNotificationOK("Explorer Launcher", "Could not launch Windows Explorer to directory: " + folderPath);
                }
            }
            else
            {
                MessageWindow.DisplayNotificationOK("Explorer Launcher", string.Format("{0} Directory does not exist!", folderPath));
            }
        }
    }
}
