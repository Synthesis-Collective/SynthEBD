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
    public class WinExplorerOpener
    {
        //https://www.codeproject.com/Questions/852563/How-to-open-file-explorer-at-given-location-in-csh
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
                    CustomMessageBox.DisplayNotificationOK("Explorer Launcher", "Could not launch Windows Explorer to directory: " + folderPath);
                }
            }
            else
            {
                CustomMessageBox.DisplayNotificationOK("Explorer Launcher", string.Format("{0} Directory does not exist!", folderPath));
            }
        }
    }
}
