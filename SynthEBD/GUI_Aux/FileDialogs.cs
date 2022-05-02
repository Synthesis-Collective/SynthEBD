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
        public static bool ConfirmFileDeletion(string path, string filetype)
        {
            if (CustomMessageBox.DisplayNotificationYesNo("Confirm Deletion", "Are you sure you want to permanently delete this " + filetype + "?"))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                    {
                        System.IO.File.Delete(path);
                    }
                    return true;
                }
                catch
                {
                    Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not delete file at " + path, ErrorType.Warning, 5);
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
    }
}
