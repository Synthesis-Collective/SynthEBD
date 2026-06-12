using System.IO;
using Autofac;
using Newtonsoft.Json;

namespace SynthEBD.CLI;

/// <summary>
/// CLI verb that performs the Config Drafter's pre-draft analysis on a working folder: collects and
/// categorizes .dds files, lists unmatched (uncategorized) textures the user/agent must decide on,
/// and detects byte-identical duplicate groups with suggested keepers. Its output feeds the draft
/// verb's --keep-unmatched / --keeper / --multiplet-mode arguments.
/// </summary>
public static class ScanVerb
{
    /// <summary>JSON/text report root for a scan run.</summary>
    public class ScanReport
    {
        public string Command { get; set; } = "scan";
        public List<string> Roots { get; set; } = new();
        public int TotalDdsFiles { get; set; }
        public int CategorizedCount { get; set; }
        /// <summary>Root-relative paths the drafter does not recognize (tint masks, complexions, meshes' textures...).
        /// Each must be kept (becomes an Unknown-type subgroup) or ignored when drafting.</summary>
        public List<string> UnmatchedTextures { get; set; } = new();
        public List<DrafterCommon.DuplicateGroupReport> DuplicateGroups { get; set; } = new();
    }

    public static async Task<int> RunAsync(CliOptions options, TextWriter resultWriter)
    {
        if (!DrafterCommon.ValidateRootLayout(options, out var layoutError))
        {
            Console.Error.WriteLine(layoutError);
            return 2;
        }

        if (!CliBootstrapper.TryCreate(options, out var bootstrapper, out var failureReason) || bootstrapper == null)
        {
            Console.Error.WriteLine("Could not initialize the SynthEBD environment: " + failureReason);
            return 2;
        }

        using (bootstrapper)
        {
            var configDrafter = bootstrapper.Container.Resolve<ConfigDrafter>();

            var (all, categorized, uncategorized) = DrafterCommon.CollectTextures(configDrafter, options);
            Console.Error.WriteLine("Found " + all.Count + " .dds file(s): " + categorized.Count + " categorized, "
                + uncategorized.Count + " unmatched.");

            var duplicates = await DrafterCommon.ComputeDuplicatesAsync(configDrafter, options, all);

            var report = new ScanReport
            {
                Roots = options.Roots.ToList(),
                TotalDdsFiles = all.Count,
                CategorizedCount = categorized.Count,
                UnmatchedTextures = uncategorized
                    .Select(path => DrafterCommon.Trim(configDrafter, options, path))
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                DuplicateGroups = DrafterCommon.ToReport(duplicates),
            };

            if (options.Json)
            {
                resultWriter.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
            }
            else
            {
                WriteTextReport(report, resultWriter);
            }

            return 0;
        }
    }

    private static void WriteTextReport(ScanReport report, TextWriter writer)
    {
        writer.WriteLine(report.TotalDdsFiles + " .dds file(s) found (" + report.CategorizedCount + " categorized).");

        writer.WriteLine();
        if (report.UnmatchedTextures.Any())
        {
            writer.WriteLine("Unmatched textures (" + report.UnmatchedTextures.Count + ") - keep or ignore each when drafting:");
            foreach (var path in report.UnmatchedTextures)
            {
                writer.WriteLine("  " + path);
            }
        }
        else
        {
            writer.WriteLine("No unmatched textures.");
        }

        writer.WriteLine();
        if (report.DuplicateGroups.Any())
        {
            writer.WriteLine("Byte-identical duplicate groups (" + report.DuplicateGroups.Count + "):");
            foreach (var group in report.DuplicateGroups)
            {
                writer.WriteLine("  " + group.FileName);
                foreach (var file in group.Files)
                {
                    writer.WriteLine("    " + (file.IsKeeper ? "[KEEP] " : "       ") + file.Path);
                }
            }
        }
        else
        {
            writer.WriteLine("No byte-identical duplicate textures.");
        }
    }
}
