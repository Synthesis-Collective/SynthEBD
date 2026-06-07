using System.IO;
using System.Text;

namespace SynthEBD;

/// <summary>Helpers for working with file paths that may exceed the classic <c>MAX_PATH</c> limit,
/// where <see cref="File.Exists"/> and the standard open dialog misbehave.</summary>
public class LongPathHandler
{
    /// <summary>Creates an <see cref="System.Windows.Forms.OpenFileDialog"/> with <c>ValidateNames</c>
    /// disabled so it tolerates paths longer than 260 characters.</summary>
    public static System.Windows.Forms.OpenFileDialog CreateLongPathOpenFileDialog()
    {
        System.Windows.Forms.OpenFileDialog dialog = new System.Windows.Forms.OpenFileDialog
        {
            CheckFileExists = true,
            CheckPathExists = true,
            ValidateNames = false // this will allow paths over 260 characters
        };
        return dialog;
    }

    // https://stackoverflow.com/a/11212007
    private const int MAX_PATH = 200;
    /// <summary>Returns whether the file at <paramref name="path"/> exists, walking the directory tree
    /// segment-by-segment via <see cref="checkFile_LongPath"/> when the path is at/over <c>MAX_PATH</c>;
    /// otherwise defers to <see cref="File.Exists"/>.</summary>
    public static bool PathExists(string path)
    {
        if (path.Length >= MAX_PATH)
        {
            return checkFile_LongPath(path);
        }
        else if (!File.Exists(path))
        {
            return false;
        }
        else
        {
            return true;
        }
    }

    /// <summary>Existence check for over-long paths: builds the longest sub-path under <c>MAX_PATH</c>,
    /// then enumerates directories/files one level at a time (avoiding the path-length error you get from
    /// passing the full path to <c>GetDirectories</c>/<c>GetFiles</c>) until the file is found or absent.</summary>
    private static bool checkFile_LongPath(string path)
    {
        string[] subpaths = path.Split('\\');
        StringBuilder sbNewPath = new StringBuilder(subpaths[0]);
        // Build longest subpath that is less than MAX_PATH characters
        for (int i = 1; i < subpaths.Length; i++)
        {
            if (sbNewPath.Length + subpaths[i].Length >= MAX_PATH)
            {
                subpaths = subpaths.Skip(i).ToArray();
                break;
            }
            sbNewPath.Append("\\" + subpaths[i]);
        }
        DirectoryInfo dir = new DirectoryInfo(sbNewPath.ToString());
        bool foundMatch = dir.Exists;
        if (foundMatch)
        {
            // Make sure that all of the subdirectories in our path exist.
            // Skip the last entry in subpaths, since it is our filename.
            // If we try to specify the path in dir.GetDirectories(), 
            // We get a max path length error.
            int i = 0;
            while (i < subpaths.Length - 1 && foundMatch)
            {
                foundMatch = false;
                foreach (DirectoryInfo subDir in dir.GetDirectories())
                {
                    if (subDir.Name == subpaths[i])
                    {
                        // Move on to the next subDirectory
                        dir = subDir;
                        foundMatch = true;
                        break;
                    }
                }
                i++;
            }
            if (foundMatch)
            {
                foundMatch = false;
                // Now that we've gone through all of the subpaths, see if our file exists.
                // Once again, If we try to specify the path in dir.GetFiles(), 
                // we get a max path length error.
                foreach (FileInfo fi in dir.GetFiles())
                {
                    if (fi.Name == subpaths[subpaths.Length - 1])
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }
}