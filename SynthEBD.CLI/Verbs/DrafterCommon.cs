using System.IO;

namespace SynthEBD.CLI;

/// <summary>
/// Texture-folder plumbing shared by the scan and draft verbs: working-folder layout validation,
/// file collection/categorization, and duplicate (multiplet) detection with console progress —
/// mirroring the corresponding steps of <see cref="VM_ConfigDrafter"/>.
/// </summary>
public static class DrafterCommon
{
    /// <summary>One byte-identical duplicate group, with root-relative paths and the suggested keeper flagged.</summary>
    public class DuplicateGroupReport
    {
        public string FileName { get; set; } = "";
        public List<DuplicateFileReport> Files { get; set; } = new();
    }

    /// <summary>One file occurrence within a duplicate group.</summary>
    public class DuplicateFileReport
    {
        public string Path { get; set; } = "";
        public bool IsKeeper { get; set; }
    }

    /// <summary>
    /// Validates the working-folder layout the same way the drafter UI does: with the default
    /// (mod-style) layout each root must contain a non-empty <c>Textures\*</c> subtree; with
    /// --roots-have-prefix each root is used as-is.
    /// </summary>
    public static bool ValidateRootLayout(CliOptions options, out string error)
    {
        error = string.Empty;
        if (options.RootsHavePrefix)
        {
            return true;
        }
        foreach (var root in options.Roots)
        {
            var texturesDir = Path.Combine(root, "Textures");
            if (!Directory.Exists(texturesDir))
            {
                error = "The working folder must contain a Textures folder (layout: <root>\\textures\\<Prefix>\\...): " + root;
                return false;
            }
            if (Directory.GetDirectories(texturesDir).Length < 1)
            {
                error = "The working folder's Textures folder must contain at least one <Prefix> subfolder: " + root;
                return false;
            }
        }
        return true;
    }

    /// <summary>Collects and categorizes all .dds files under the roots, exactly as the drafter UI's scan step does.</summary>
    public static (List<string> All, List<string> Categorized, List<string> Uncategorized) CollectTextures(
        ConfigDrafter configDrafter, CliOptions options)
    {
        var all = configDrafter.GetDDSFiles(options.Roots);
        var (categorized, uncategorized) = configDrafter.CategorizeFiles(all);
        return (all, categorized, uncategorized);
    }

    /// <summary>Trims a full path to the root-relative form shown to users (and accepted back as input).</summary>
    public static string Trim(ConfigDrafter configDrafter, CliOptions options, string fullPath)
    {
        return configDrafter.RemoveRootFolder(fullPath, options.Roots, options.RootsHavePrefix);
    }

    /// <summary>
    /// Runs the drafter's duplicate detection (file-name groups, then MD5 within each group) off the
    /// dispatcher thread, reporting progress to stderr. Each returned container has its suggested
    /// keeper already deselected via <see cref="ConfigDrafter.ChooseLeastSpecificPath"/>.
    /// </summary>
    public static async Task<List<VM_FileDuplicateContainer>> ComputeDuplicatesAsync(
        ConfigDrafter configDrafter, CliOptions options, List<string> texturePaths)
    {
        var progress = new ConsoleHashingProgress();
        // The GUI passes isUsingModManager = !rootPathsHavePrefix; preserve that mapping so the
        // containers' DisplayedPaths are trimmed consistently with scan/draft output.
        var result = await Task.Run(() => VM_ConfigDrafter.ComputeFileDuplicates(
            texturePaths, progress, options.Roots, !options.RootsHavePrefix, configDrafter)).ConfigureAwait(true);
        progress.Finish();
        return result.ToList();
    }

    /// <summary>Converts duplicate containers to the report DTO (keeper = the deselected entry).</summary>
    public static List<DuplicateGroupReport> ToReport(IEnumerable<VM_FileDuplicateContainer> containers)
    {
        return containers.Select(container => new DuplicateGroupReport
        {
            FileName = container.FileName,
            Files = container.FilePaths.Select(file => new DuplicateFileReport
            {
                Path = file.DisplayedPath,
                IsKeeper = !file.IsSelected,
            }).ToList(),
        }).ToList();
    }

    /// <summary>Stderr progress reporter for the MD5 hashing pass; throttled to avoid log spam.</summary>
    private class ConsoleHashingProgress : IProgress<(int, int, string)>
    {
        private const int ReportInterval = 250;
        private int _lastReported = -ReportInterval;
        private int _max = 1;

        public void Report((int, int, string) value)
        {
            var (current, max, _) = value;
            _max = max;
            if (current - _lastReported >= ReportInterval)
            {
                _lastReported = current;
                Console.Error.WriteLine("Hashing for duplicates: file-name group " + current + " / " + max);
            }
        }

        public void Finish()
        {
            Console.Error.WriteLine("Hashing for duplicates: " + _max + " / " + _max + " file-name groups complete.");
        }
    }
}
