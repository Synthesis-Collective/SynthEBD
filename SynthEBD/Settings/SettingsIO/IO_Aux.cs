using System.IO;
using System.Text;
using System.Text.RegularExpressions;


namespace SynthEBD;

/// <summary>
/// File-system helper utilities for the settings IO layer: filename validation/sanitization, folder/file
/// open-and-save dialogs, line-based file reading, and robust (long-path-aware) directory/file deletion.
/// Static members are pure helpers; instance members use the injected <see cref="Logger"/> to report and
/// swallow deletion errors. Several methods pop Windows UI dialogs.
/// </summary>
public class IO_Aux
{
    private readonly Logger _logger;
    /// <summary>Injects the logger used by the instance deletion helpers.</summary>
    public IO_Aux(Logger logger)
    {
        _logger = logger;
    }
    /// <summary>
    /// Returns true if <paramref name="testName"/> is already a valid filename (i.e. sanitizing it is a no-op).
    /// </summary>
    public static bool IsValidFilename(string testName)
    {
        return MakeValidFileName(testName) == testName;
    }

    /// <summary>
    /// Pops a folder-browser dialog. UI side effect.
    /// </summary>
    /// <param name="initDir">Initial directory, or empty to use the dialog default.</param>
    /// <param name="path">The selected folder path, or empty if canceled.</param>
    /// <returns>True if the user picked a folder; false if canceled.</returns>
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

    /// <summary>
    /// Pops an open-file dialog. UI side effect.
    /// </summary>
    /// <param name="initDir">Initial directory, or empty for the dialog default.</param>
    /// <param name="filter">File-type filter string, or empty/whitespace for none.</param>
    /// <param name="title">Dialog title.</param>
    /// <param name="path">The selected file path, or empty if canceled.</param>
    /// <param name="startingFileName">Optional pre-filled file name.</param>
    /// <returns>True if the user picked a file; false if canceled.</returns>
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
    
    /// <summary>
    /// Pops a save-file dialog. UI side effect.
    /// </summary>
    /// <param name="initDir">Initial directory, or empty for the dialog default.</param>
    /// <param name="filter">File-type filter string, or empty/whitespace for none.</param>
    /// <param name="defaultExtension">Default extension applied when the user omits one.</param>
    /// <param name="title">Dialog title.</param>
    /// <param name="path">The chosen save path (set from the dialog regardless of the result).</param>
    /// <param name="startingFileName">Optional pre-filled file name.</param>
    /// <returns>True if the user confirmed the save; false if canceled.</returns>
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
        path = (result ?? false) ? dialog.FileName : string.Empty; // don't hand back a path the user cancelled
        return result ?? false;
    }

    /// <summary>
    /// Reads a file into a list of its lines. Reads from disk.
    /// </summary>
    /// <param name="path">Absolute path to the file.</param>
    /// <param name="wasRead">Set true if the file existed and was read; false otherwise.</param>
    /// <returns>The file's lines, or an empty list if the file does not exist.</returns>
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
        
    /// <summary>
    /// Recursively deletes a directory and all its contents using AlphaFS long-path APIs (handles paths beyond
    /// the 260-character limit). Deletes from disk; may throw if a file/directory cannot be removed.
    /// </summary>
    /// <param name="dir">The directory to delete.</param>
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
    /// <summary>
    /// Recursively deletes a directory using the <c>\\?\</c> long-path prefix, clearing read-only attributes
    /// first. Deletes from disk; individual failures are logged rather than thrown. Paths exceeding 260
    /// characters are skipped, and at the top level (<paramref name="isInner"/> false) a warning dialog is
    /// shown listing that some paths must be removed manually.
    /// </summary>
    /// <param name="target_dir">The directory to delete.</param>
    /// <param name="isInner">True for recursive inner calls; false for the top-level call (enables the warning prompt).</param>
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

    /// <summary>
    /// Deletes a file, logging (and swallowing) any exception. Deletes from disk.
    /// </summary>
    /// <param name="path">The file to delete.</param>
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


    /// <summary>
    /// Deletes a directory, logging (and swallowing) any exception. Deletes from disk.
    /// </summary>
    /// <param name="path">The directory to delete.</param>
    /// <param name="recursive">Whether to delete contained files and subdirectories.</param>
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

    /// <summary>
    /// Walks up from <paramref name="dirPath"/>, deleting it and successive empty parents. Deletes from disk
    /// (errors are logged and swallowed via <see cref="TryDeleteDirectory"/>).
    /// </summary>
    /// <param name="dirPath">The starting directory.</param>
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