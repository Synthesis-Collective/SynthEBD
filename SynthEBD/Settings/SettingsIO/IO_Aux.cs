using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class IO_Aux
    {
        public static bool IsValidFilename(string testName)
        {
            Regex containsABadCharacter = new Regex("["
                  + Regex.Escape(new string(System.IO.Path.GetInvalidPathChars())) + "]");
            if (containsABadCharacter.IsMatch(testName)) { return false; };

            // other checks for UNC, drive-path format, etc

            return true;
        }

        public static bool SelectFolder(string initDir, out string path)
        {
            System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog();
            path = "";

            if (initDir != "")
            {
                dialog.InitialDirectory = initDir;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                path = dialog.SelectedPath;
                return true;
            }
            else
            {
                return false;
            }
        }

        public static bool SelectFile(string initDir, string filter, out string path)
        {
            path = "";

            System.Windows.Forms.OpenFileDialog dialog = new System.Windows.Forms.OpenFileDialog();
            if (!string.IsNullOrWhiteSpace(filter))
            {
                dialog.Filter = filter;
            }

            if (initDir != "")
            {
                dialog.InitialDirectory = initDir;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                path = dialog.FileName;
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
