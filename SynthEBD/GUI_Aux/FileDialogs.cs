using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SynthEBD
{
    class FileDialogs
    {
        public static void ConfirmFileDeletion(string path, string filetype)
        {
            MessageBoxResult result = MessageBox.Show("Are you sure you want to permanently delete this " + filetype + "?", "", MessageBoxButton.YesNo);

            if (path != "")
            {
                try
                {
                    System.IO.File.Delete(path);
                }
                catch
                {
                    Logger.Instance.CallTimedNotifyStatusUpdateAsync("Could not delete file at " + path, ErrorType.Warning, 5);
                }
            }
        }
    }
}
