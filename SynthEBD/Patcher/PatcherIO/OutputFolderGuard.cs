using System.IO;

namespace SynthEBD;

/// <summary>
/// Detects an output folder that overlaps the active SynthEBD settings folder. Before each run the patcher deletes
/// the <see cref="ClearedSubfolders"/> of the output folder (see <see cref="PatcherIO.ClearPreviousScriptOutputs"/>),
/// and a portable settings folder must itself be named <c>SynthEBD</c>. So an output folder of <c>X</c> with
/// settings in <c>X\SynthEBD</c> would wipe every setting, config and rule file on the next patch run.
/// </summary>
public static class OutputFolderGuard
{
    /// <summary>Subfolders of the output folder that the patcher deletes before every run.</summary>
    public static readonly string[] ClearedSubfolders = { "Scripts", "Seq", "SKSE", "SynthEBD" };

    /// <summary>
    /// Returns a user-facing explanation when <paramref name="outputFolder"/> and <paramref name="settingsRoot"/>
    /// overlap (same folder, either inside the other, or a cleared subfolder that holds a settings folder), or
    /// <c>null</c> when they are independent. An output folder equal to <paramref name="dataFolder"/> is never
    /// cleared, so it is not treated as a conflict.
    /// </summary>
    public static string? GetSettingsConflict(string? outputFolder, string? settingsRoot, string? dataFolder)
    {
        if (string.IsNullOrWhiteSpace(outputFolder) || string.IsNullOrWhiteSpace(settingsRoot))
        {
            return null;
        }

        string output = Normalize(outputFolder);
        string settings = Normalize(settingsRoot);
        if (!string.IsNullOrWhiteSpace(dataFolder) && PathsEqual(output, Normalize(dataFolder)))
        {
            return null;
        }

        if (PathsEqual(output, settings))
        {
            return "The output folder is the SynthEBD settings folder (" + settings + "). Patching would delete settings files.";
        }
        if (IsInside(output, settings))
        {
            return "The output folder (" + output + ") is inside the SynthEBD settings folder (" + settings + ").";
        }
        if (IsInside(settings, output))
        {
            return "The output folder (" + output + ") contains the SynthEBD settings folder (" + settings + "). Before each run the patcher deletes the output folder's " +
                   string.Join(", ", ClearedSubfolders) + " subfolders, which would delete your settings, configs and rule files.";
        }

        foreach (var subfolder in ClearedSubfolders)
        {
            string cleared = Path.Combine(output, subfolder);
            if (LooksLikeSettingsFolder(cleared))
            {
                return "The output folder's " + subfolder + " subfolder (" + cleared + ") holds SynthEBD settings. Before each run the patcher deletes that subfolder, which would delete your settings, configs and rule files.";
            }
        }
        return null;
    }

    /// <summary>
    /// True when <paramref name="folder"/> holds a SynthEBD settings tree. Checked by content as well as by path so
    /// that mod-manager virtual folders, junctions and mapped drives can't hide the overlap.
    /// </summary>
    public static bool LooksLikeSettingsFolder(string folder)
    {
        return File.Exists(Path.Combine(folder, "Settings", "GeneralSettings.json"));
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool PathsEqual(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string child, string parent) =>
        child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
