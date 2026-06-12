using System.IO;
using Autofac;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Newtonsoft.Json;
using Noggog;

namespace SynthEBD.CLI;

/// <summary>
/// CLI verb that runs the Distribution Simulator headlessly via <see cref="AssetDistributionSimulator"/>:
/// for each requested NPC, repeatedly runs the production asset-selection pipeline against the chosen
/// Primary configs and reports per-pack and per-subgroup assignment counts. Subgroups that never get
/// assigned carry the verbose-log line explaining why (the GUI's "Why?" button); the full per-NPC XML
/// report can be written out for deep debugging of distribution failures.
/// </summary>
public static class SimulateVerb
{
    /// <summary>JSON/text report root for a simulate run.</summary>
    public class SimulateReport
    {
        public string Command { get; set; } = "simulate";
        public int Repetitions { get; set; }
        public List<string> SimulatedConfigs { get; set; } = new();
        /// <summary>True when every simulated NPC received at least one assignment.</summary>
        public bool AllNpcsAssigned { get; set; }
        public List<NpcSimulationReport> Npcs { get; set; } = new();
    }

    /// <summary>Simulation outcome for one NPC.</summary>
    public class NpcSimulationReport
    {
        public string Npc { get; set; } = "";
        public string FormKey { get; set; } = "";
        public string Gender { get; set; } = "";
        /// <summary>Total successful assignments across all repetitions (0 = this NPC gets nothing).</summary>
        public int TotalAssignments { get; set; }
        public List<PackCountReport> AssetPackCounts { get; set; } = new();
        public List<PackSubgroupReport> SubgroupCounts { get; set; } = new();
        public string? FullReportPath { get; set; }
        public string? FailureReason { get; set; }
    }

    public class PackCountReport
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    public class PackSubgroupReport
    {
        public string GroupName { get; set; } = "";
        public List<SubgroupCountReport> Subgroups { get; set; } = new();
    }

    public class SubgroupCountReport
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Count { get; set; }
        /// <summary>Verbose-log explanation; only populated for subgroups with zero assignments.</summary>
        public string? Explanation { get; set; }
    }

    public static Task<int> RunAsync(CliOptions options, TextWriter resultWriter)
    {
        if (!CliBootstrapper.TryCreate(options, out var bootstrapper, out var failureReason) || bootstrapper == null)
        {
            Console.Error.WriteLine("Could not initialize the SynthEBD environment: " + failureReason);
            return Task.FromResult(2);
        }

        using (bootstrapper)
        {
            var state = bootstrapper.PatcherState;

            // Resolve the Primary configs to simulate: --config names, or the currently selected set.
            HashSet<AssetPack> primaryPacks;
            if (options.ConfigNames.Any())
            {
                primaryPacks = new();
                foreach (var name in options.ConfigNames)
                {
                    var match = state.AssetPacks.FirstOrDefault(x =>
                        x.GroupName.Equals(name, StringComparison.OrdinalIgnoreCase)
                        || (x.FilePath != null && Path.GetFileNameWithoutExtension(x.FilePath).Equals(name, StringComparison.OrdinalIgnoreCase)));
                    if (match == null)
                    {
                        Console.Error.WriteLine("Config not found among installed asset packs: " + name);
                        Console.Error.WriteLine("Installed configs: " + string.Join(", ", state.AssetPacks.Select(x => x.GroupName)));
                        return Task.FromResult(2);
                    }
                    if (match.ConfigType != AssetPackType.Primary)
                    {
                        Console.Error.WriteLine("Config \"" + match.GroupName + "\" is a " + match.ConfigType +
                            " config; the simulator only supports Primary configs.");
                        return Task.FromResult(2);
                    }
                    primaryPacks.Add(match);
                }
            }
            else
            {
                primaryPacks = state.AssetPacks
                    .Where(x => x.ConfigType == AssetPackType.Primary && state.TexMeshSettings.SelectedAssetPacks.Contains(x.GroupName))
                    .ToHashSet();
            }

            if (!primaryPacks.Any())
            {
                Console.Error.WriteLine("No Primary configs to simulate. Pass --config <name>, or select configs in the GUI's Textures and Meshes menu.");
                return Task.FromResult(2);
            }
            Console.Error.WriteLine("Simulating configs: " + string.Join(", ", primaryPacks.Select(x => x.GroupName)));

            if (options.FullReportDir != null)
            {
                Directory.CreateDirectory(options.FullReportDir);
            }

            var simulator = bootstrapper.Container.Resolve<AssetDistributionSimulator>();
            var logger = bootstrapper.Container.Resolve<Logger>();

            var report = new SimulateReport
            {
                Repetitions = options.Repetitions,
                SimulatedConfigs = primaryPacks.Select(x => x.GroupName).ToList(),
            };

            foreach (var npcIdentifier in options.Npcs)
            {
                if (!TryResolveNpc(bootstrapper, npcIdentifier, out var npcGetter, out var npcError))
                {
                    Console.Error.WriteLine(npcError);
                    return Task.FromResult(2);
                }

                Console.Error.WriteLine("Simulating " + logger.GetNPCLogNameString(npcGetter) + " x" + options.Repetitions + "...");
                var npcReport = SimulateNpc(simulator, logger, npcGetter, primaryPacks, state, options);
                report.Npcs.Add(npcReport);
            }

            report.AllNpcsAssigned = report.Npcs.All(x => x.TotalAssignments > 0 && x.FailureReason == null);

            if (options.Json)
            {
                resultWriter.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
            }
            else
            {
                WriteTextReport(report, resultWriter);
            }

            return Task.FromResult(report.AllNpcsAssigned ? 0 : 1);
        }
    }

    /// <summary>Resolves an NPC by FormKey string or, failing that, by EditorID among winning overrides.</summary>
    private static bool TryResolveNpc(CliBootstrapper bootstrapper, string identifier, out INpcGetter npcGetter, out string error)
    {
        npcGetter = null!;
        error = string.Empty;

        if (FormKey.TryFactory(identifier, out var formKey))
        {
            if (bootstrapper.EnvironmentProvider.LinkCache.TryResolve<INpcGetter>(formKey, out var resolved))
            {
                npcGetter = resolved;
                return true;
            }
            error = "Could not resolve NPC " + identifier + " in the current load order.";
            return false;
        }

        var byEditorId = bootstrapper.EnvironmentProvider.LoadOrder.PriorityOrder
            .OnlyEnabledAndExisting()
            .WinningOverrides<INpcGetter>()
            .FirstOrDefault(x => identifier.Equals(x.EditorID, StringComparison.OrdinalIgnoreCase));
        if (byEditorId != null)
        {
            npcGetter = byEditorId;
            return true;
        }

        error = "\"" + identifier + "\" is neither a valid FormKey (\"123456:Plugin.esp\") nor a known NPC EditorID.";
        return false;
    }

    /// <summary>Runs the simulation for one NPC and converts the result to its report DTO.</summary>
    private static NpcSimulationReport SimulateNpc(AssetDistributionSimulator simulator, Logger logger,
        INpcGetter npcGetter, HashSet<AssetPack> primaryPacks, PatcherState state, CliOptions options)
    {
        var npcReport = new NpcSimulationReport
        {
            Npc = logger.GetNPCLogNameString(npcGetter),
            FormKey = npcGetter.FormKey.ToString(),
        };

        var result = simulator.SimulatePrimaryDistribution(npcGetter, primaryPacks, state.BodyGenConfigs,
            state.OBodySettings, state.BlockList, options.Repetitions, out var failureReason);
        if (result == null)
        {
            npcReport.FailureReason = failureReason;
            return npcReport;
        }

        npcReport.Gender = result.NPCInfo.Gender.ToString();

        var counts = simulator.Tally(result.Combinations, result.AvailableAssetPacks);
        npcReport.TotalAssignments = counts.AssetPackCounts.Sum(x => x.Count);
        npcReport.AssetPackCounts = counts.AssetPackCounts
            .Select(x => new PackCountReport { Name = x.Name, Count = x.Count }).ToList();

        foreach (var pack in counts.SubgroupCounts)
        {
            var packReport = new PackSubgroupReport { GroupName = pack.GroupName };
            foreach (var subgroup in pack.Subgroups)
            {
                packReport.Subgroups.Add(new SubgroupCountReport
                {
                    Id = subgroup.Id,
                    Name = subgroup.Name,
                    Count = subgroup.Count,
                    Explanation = subgroup.Count == 0
                        ? AssetDistributionSimulator.ExtractSubgroupExplanation(result.NPCInfo, pack.GroupName, subgroup.DetailedIdName)
                        : null,
                });
            }
            npcReport.SubgroupCounts.Add(packReport);
        }

        if (options.FullReportDir != null)
        {
            var fullReport = AssetDistributionSimulator.FormatFullReport(result.NPCInfo);
            if (!fullReport.IsNullOrWhitespace())
            {
                var fileName = MiscFunctions.MakeAlphanumeric(npcGetter.EditorID ?? "NPC") + "_"
                    + npcGetter.FormKey.ToString().Replace(':', '_') + ".xml";
                var fullReportPath = Path.Combine(options.FullReportDir, fileName);
                File.WriteAllText(fullReportPath, fullReport);
                npcReport.FullReportPath = fullReportPath;
                Console.Error.WriteLine("Full report written to " + fullReportPath);
            }
        }

        return npcReport;
    }

    private static void WriteTextReport(SimulateReport report, TextWriter writer)
    {
        writer.WriteLine("Simulated configs: " + string.Join(", ", report.SimulatedConfigs)
            + " (" + report.Repetitions + " repetitions per NPC)");
        foreach (var npc in report.Npcs)
        {
            writer.WriteLine();
            writer.WriteLine("=== " + npc.Npc + " (" + npc.Gender + ") ===");
            if (npc.FailureReason != null)
            {
                writer.WriteLine("  FAILED: " + npc.FailureReason);
                continue;
            }
            writer.WriteLine("  Total assignments: " + npc.TotalAssignments + " / " + report.Repetitions);
            foreach (var pack in npc.AssetPackCounts)
            {
                writer.WriteLine("  " + pack.Name + " (" + pack.Count + ")");
            }
            foreach (var pack in npc.SubgroupCounts)
            {
                writer.WriteLine("  Subgroups of " + pack.GroupName + ":");
                foreach (var subgroup in pack.Subgroups)
                {
                    writer.WriteLine("    " + subgroup.Id + " (" + subgroup.Name + "): " + subgroup.Count);
                    if (subgroup.Explanation != null)
                    {
                        writer.WriteLine("      Why: " + subgroup.Explanation.Trim());
                    }
                }
            }
            if (npc.FullReportPath != null)
            {
                writer.WriteLine("  Full report: " + npc.FullReportPath);
            }
        }
        writer.WriteLine();
        writer.WriteLine(report.AllNpcsAssigned
            ? "All NPCs received assignments."
            : "WARNING: at least one NPC received no assignments. Inspect the zero-count explanations / full report.");
    }
}
