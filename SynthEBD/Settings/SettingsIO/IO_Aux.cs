using System.IO;
using System.Text;
using System.Text.RegularExpressions;


namespace SynthEBD;

public class IO_Aux
{
    private readonly Logger _logger;
    public IO_Aux(Logger logger)
    {
        _logger = logger;
    }
    public static bool IsValidFilename(string testName)
    {
        return MakeValidFileName(testName) == testName;
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

    public static bool SelectFile(string initDir, string filter, string title, out string path, string startingFileName = "")
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
        if (startingFileName != "")
        {
            dialog.FileName = startingFileName;
        }

        dialog.Title = title;

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
    
    public static bool SelectFileSave(string initDir, string filter, string defaultExtension, string title, out string path, string startingFileName = "")
    {
        // Configure save file dialog box
        var dialog = new Microsoft.Win32.SaveFileDialog();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            dialog.Filter = filter;
        }

        if (initDir != "")
        {
            dialog.InitialDirectory = initDir;
        }
        if (startingFileName != "")
        {
            dialog.FileName = startingFileName;
        }

        dialog.DefaultExt = defaultExtension;

        dialog.Title = title;

        dialog.RestoreDirectory = true;

        // Show open file dialog box
        bool? result = dialog.ShowDialog();
        path = dialog.FileName;
        return result ?? false;
    }

    public static List<string> ReadFileToList(string path, out bool wasRead)
    {
        wasRead = false;
        List<string> lines = new List<string>();
        if (File.Exists(path))
        {
            foreach (string line in File.ReadLines(path))
            {
                lines.Add(line);
            }
            wasRead = true;
        }
        return lines;
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
    public void DeleteDirectory(string target_dir, bool isInner)
    {
        string[] files = Directory.GetFiles(target_dir);
        string[] dirs = Directory.GetDirectories(target_dir);

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
                _logger.LogError(ex.Message);
            }
            try
            {
                File.Delete(@"\\?\" + file);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
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
                _logger.LogError(ex.Message);
            }
        }
        else
        {
            exceedsPathLimit=true;
        }

        if (exceedsPathLimit && !isInner)
        {
            MessageWindow.DisplayNotificationOK("Deletion Warning", "Some file/folder paths in " + target_dir + " exceed 260 characters and cannot be deleted automatically. You may delete them manually after SynthEBD closes.");
        }
    }

    // https://stackoverflow.com/a/25223884
    static char[] _invalids;
    /// <summary>Replaces characters in <c>text</c> that are not allowed in 
    /// file names with the specified replacement character.</summary>
    /// <param name="text">Text to make into a valid filename. The same string is returned if it is valid already.</param>
    /// <param name="replacement">Replacement character, or null to simply remove bad characters.</param>
    /// <param name="fancy">Whether to replace quotes and slashes with the non-ASCII characters ” and ⁄.</param>
    /// <returns>A string that can be used as a filename. If the output string would otherwise be empty, returns "_".</returns>
    public static string MakeValidFileName(string text, char? replacement = '_', bool fancy = true)
    {
        StringBuilder sb = new StringBuilder(text.Length);
        var invalids = _invalids ?? (_invalids = Path.GetInvalidFileNameChars());
        bool changed = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (invalids.Contains(c))
            {
                changed = true;
                var repl = replacement ?? '\0';
                if (fancy)
                {
                    if (c == '"') repl = '”'; // U+201D right double quotation mark
                    else if (c == '\'') repl = '’'; // U+2019 right single quotation mark
                    else if (c == '/') repl = '⁄'; // U+2044 fraction slash
                }
                if (repl != '\0')
                    sb.Append(repl);
            }
            else
                sb.Append(c);
        }
        if (sb.Length == 0)
            return "_";
        return changed ? sb.ToString() : text;
    }

    public void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e)
        {
            _logger.LogError("Could not delete file " + path + Environment.NewLine + "Exception: " + ExceptionLogger.GetExceptionStack(e));
        }
    }


    public void TryDeleteDirectory(string path, bool recursive)
    {
        try
        {
            Directory.Delete(path, recursive);
        }
        catch (Exception e)
        {
            _logger.LogError("Could not delete directory " + path + Environment.NewLine + "Exception: " + ExceptionLogger.GetExceptionStack(e));
        }
    }

    public void DeleteDirectoryChainIfEmpty(string dirPath) // deletes directory if empty, and parent directory if empty, recursively
    {
        var parentDir = Directory.GetParent(dirPath);
        if (parentDir == null || !parentDir.Exists)
        {
            return;
        }

        if (Directory.EnumerateFileSystemEntries(parentDir.FullName).Any()) // if the directory has files, delete the subdirectory
        {
            TryDeleteDirectory(dirPath, true);
        }
        else // if the directory is empty, check the parent directory
        {
            DeleteDirectoryChainIfEmpty(parentDir.FullName);
        }
    }
}