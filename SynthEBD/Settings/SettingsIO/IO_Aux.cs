using System;
using System.Collections.Generic;
using System.IO;
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
        
        public static void DeleteDirectoryAF(string dir)
        {
            var directories = Alphaleonis.Win32.Filesystem.Directory.GetDirectories(dir);
            foreach (var d in directories)
            {
                DeleteDirectoryAF(d);
            }

            var files = Alphaleonis.Win32.Filesystem.Directory.GetFiles(dir);

            foreach (var file in files)
            {
                var longPath = @"\\?\" + file;
                Alphaleonis.Win32.Filesystem.File.Delete(longPath, false, Alphaleonis.Win32.Filesystem.PathFormat.LongFullPath);
            }

            var longDir = @"\\?\" + dir;
            Alphaleonis.Win32.Filesystem.Directory.Delete(longDir, Alphaleonis.Win32.Filesystem.PathFormat.LongFullPath);
        }
        public static void DeleteDirectory(string target_dir, bool isInner)
        {
            string[] files = Directory.GetFiles(target_dir);
            string[] dirs = Directory.GetDirectories(target_dir);

            bool error = false;

            bool exceedsPathLimit = false;

            foreach (string file in files)
            {
                if (file.Length > 260)
                {
                    exceedsPathLimit = true;
                    continue;
                }

                try
                {
                    File.SetAttributes(@"\\?\" + file, FileAttributes.Normal);
                }
                catch (Exception ex)
                {
                    error = true;
                    Logger.LogError(ex.Message);
                }
                try
                {
                    File.Delete(@"\\?\" + file);
                }
                catch (Exception ex)
                {
                    error = true;
                    Logger.LogError(ex.Message);
                }
            }

            foreach (string dir in dirs)
            {
                DeleteDirectory(dir, true);
            }

            if (target_dir.Length <= 260)
            {
                try
                {
                    Directory.Delete(@"\\?\" + target_dir, true); // handle long file paths https://stackoverflow.com/a/64568142
                }
                catch (Exception ex)
                {
                    error = true;
                    Logger.LogError(ex.Message);
                }
            }
            else
            {
                exceedsPathLimit=true;
            }

            if (error)
            {
                Logger.SwitchViewToLogDisplay();
            }

            if (exceedsPathLimit && !isInner)
            {
                System.Windows.MessageBox.Show("Some file/folder paths in " + target_dir + " exceed 260 characters and cannot be deleted automatically. You may delete them manually after SynthEBD closes.");
            }
        }
    }
}
