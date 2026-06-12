using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Text.RegularExpressions;

namespace SynthEBD;

/// <summary>
/// Headless core of the asset-distribution simulator: repeatedly runs the production asset/body-shape
/// selection pipeline for one NPC (with consistency temporarily disabled) to estimate how often each
/// asset pack and subgroup would be assigned, capturing a verbose report on the final pass. Extracted
/// from <see cref="VM_AssetDistributionSimulator"/> so the simulator UI and the CLI share one
/// implementation; this class performs no UI interaction.
/// </summary>
public class AssetDistributionSimulator
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly DictionaryMapper _dictionaryMapper;
    private readonly AssetAndBodyShapeSelector _assetAndBodyShapeSelector;
    private readonly AssetSelector _assetSelector;
    private readonly OBodyPreprocessing _obodyPreProcessing;
    private readonly NPCInfo.Factory _npcInfoFactory;

    public AssetDistributionSimulator(IEnvironmentStateProvider environmentProvider, PatcherState patcherState,
        Logger logger, DictionaryMapper dictionaryMapper, AssetAndBodyShapeSelector assetAndBodyShapeSelector,
        AssetSelector assetSelector, OBodyPreprocessing oBodyPreprocessing, NPCInfo.Factory npcInfoFactory)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _dictionaryMapper = dictionaryMapper;
        _assetAndBodyShapeSelector = assetAndBodyShapeSelector;
        _assetSelector = assetSelector;
        _obodyPreProcessing = oBodyPreprocessing;
        _npcInfoFactory = npcInfoFactory;
    }

    /// <summary>Everything a simulation run produced: the simulated NPC (whose Report holds the verbose
    /// final-pass log), the assigned combinations, and the flattened packs that were eligible.</summary>
    public class SimulationResult
    {
        public NPCInfo NPCInfo { get; set; }
        public HashSet<SubgroupCombination> Combinations { get; set; } = new();
        public HashSet<FlattenedAssetPack> AvailableAssetPacks { get; set; } = new();
    }

    /// <summary>Assignment tallies across a run: per-pack counts (ascending, including zero-count
    /// candidates) and per-subgroup counts for every eligible pack.</summary>
    public class SimulationCounts
    {
        public List<PackCount> AssetPackCounts { get; set; } = new();
        public List<PackSubgroupCounts> SubgroupCounts { get; set; } = new();
    }

    /// <summary>How many simulated runs assigned the named asset pack.</summary>
    public class PackCount
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    /// <summary>Per-subgroup assignment counts for one asset pack.</summary>
    public class PackSubgroupCounts
    {
        public string GroupName { get; set; } = "";
        public List<SubgroupCount> Subgroups { get; set; } = new();
    }

    /// <summary>How many simulated runs assigned one subgroup (within its pack).</summary>
    public class SubgroupCount
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>The "Subgroup id (name)" form used to locate this subgroup in the verbose log.</summary>
        public string DetailedIdName { get; set; } = "";
        public int Count { get; set; }
    }

    /// <summary>
    /// Runs primary-asset selection <paramref name="repetitions"/> times for <paramref name="npcGetter"/>
    /// using the production selection pipeline, with consistency temporarily disabled so draws are
    /// independent. The final pass runs with detailed verbose logging into the returned NPCInfo's Report.
    /// </summary>
    /// <returns>The run's combinations and eligible packs, or <c>null</c> (with
    /// <paramref name="failureReason"/>) when no supplied pack matches the NPC's gender.</returns>
    public SimulationResult? SimulatePrimaryDistribution(INpcGetter npcGetter, HashSet<AssetPack> primaryAssetPacks,
        BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings, BlockList blockList, int repetitions,
        out string failureReason)
    {
        failureReason = string.Empty;
        var npcInfo = _npcInfoFactory(npcGetter, new(), new());

        var flattenedAssetPacks = primaryAssetPacks.Where(x => x.Gender == npcInfo.Gender)
            .Select(x => FlattenedAssetPack.FlattenAssetPack(x, _dictionaryMapper, _patcherState)).ToHashSet();

        if (!flattenedAssetPacks.Any())
        {
            failureReason = "No Primary Config Files with the same gender as " +
                _logger.GetNPCLogNameString(npcGetter) + " are selected in the Active Config Files list";
            return null;
        }

        var blockListNPCEntry = BlockListHandler.GetCurrentNPCBlockStatus(blockList, npcGetter.FormKey);
        var blockListPluginEntry = BlockListHandler.GetCurrentPluginBlockStatus(blockList, npcGetter.FormKey, _environmentProvider.LinkCache);
        var blockBodyShape = false;
        if (blockListNPCEntry.BodyShape || blockListPluginEntry.BodyShape || !_obodyPreProcessing.NPCIsEligibleForBodySlide(npcGetter)) { blockBodyShape = true; }

        HashSet<SubgroupCombination> combinations = new();

        var currentDetailedVerboseSetting = _patcherState.GeneralSettings.VerboseModeDetailedAttributes;

        bool backupConsistency = _patcherState.GeneralSettings.bEnableConsistency;
        _patcherState.GeneralSettings.bEnableConsistency = false;

        for (int i = 0; i < repetitions; i++)
        {
            if (i == repetitions - 1)
            {
                _patcherState.GeneralSettings.VerboseModeDetailedAttributes = true;
                npcInfo.Report.LogCurrentNPC = true;
                _logger.InitializeNewReport(npcInfo);
            }
            if (!blockBodyShape)
            {
                var assignments = _assetAndBodyShapeSelector.GenerateCombinationWithBodyShape(flattenedAssetPacks, bodyGenConfigs, oBodySettings, npcInfo, AssetSelector.AssetPackAssignmentMode.Primary, new());
                if (assignments.Assets != null)
                {
                    combinations.Add(assignments.Assets);
                }
            }
            else
            {
                var assignments = _assetSelector.AssignAssets(npcInfo, AssetSelector.AssetPackAssignmentMode.Primary, flattenedAssetPacks, null, null, out _);
                if (assignments != null)
                {
                    combinations.Add(assignments);
                }
            }
            if (i == repetitions - 1)
            {
                npcInfo.Report.LogCurrentNPC = false;
                _patcherState.GeneralSettings.VerboseModeDetailedAttributes = currentDetailedVerboseSetting;
            }
        }

        _patcherState.GeneralSettings.bEnableConsistency = backupConsistency;

        return new SimulationResult
        {
            NPCInfo = npcInfo,
            Combinations = combinations,
            AvailableAssetPacks = flattenedAssetPacks,
        };
    }

    /// <summary>
    /// Tallies how often each asset pack and subgroup appeared across the simulated combinations.
    /// Pack counts are ordered ascending and include zero-count entries for eligible packs that were
    /// never assigned; subgroup counts cover every subgroup of every eligible pack.
    /// </summary>
    public SimulationCounts Tally(HashSet<SubgroupCombination> combinations, HashSet<FlattenedAssetPack> available)
    {
        var counts = new SimulationCounts();

        var packCounts = new List<PackCount>();
        foreach (var combo in combinations.Where(x => x.AssetPack != null).ToArray())
        {
            var existing = packCounts.FirstOrDefault(x => x.Name == combo.AssignmentName);
            if (existing != null) { existing.Count++; }
            else
            {
                packCounts.Add(new() { Name = combo.AssignmentName, Count = 1 });
            }
        }
        foreach (var assetPack in available.Where(x => !packCounts.Select(y => y.Name).Contains(x.GroupName)).ToArray())
        {
            packCounts.Add(new() { Name = assetPack.GroupName, Count = 0 });
        }
        counts.AssetPackCounts = packCounts.OrderBy(x => x.Count).ToList();

        foreach (var ap in available)
        {
            var packSubgroups = new PackSubgroupCounts() { GroupName = ap.GroupName };
            for (int i = 0; i < ap.Subgroups.Count; i++)
            {
                var index = ap.Subgroups[i];
                foreach (var subgroup in index)
                {
                    packSubgroups.Subgroups.Add(new SubgroupCount
                    {
                        Id = subgroup.Id,
                        Name = subgroup.Name,
                        DetailedIdName = "Subgroup " + subgroup.GetDetailedID_NameString(false),
                        Count = combinations.Count(x => x.AssignmentName == ap.GroupName && x.ContainedSubgroups[i].Id == subgroup.Id),
                    });
                }
            }
            counts.SubgroupCounts.Add(packSubgroups);
        }

        return counts;
    }

    /// <summary>
    /// Extracts the verbose-log line explaining how the given subgroup was filtered for this NPC and
    /// asset pack (whitespace-insensitive match within the pack's "Filtering subgroups" log section).
    /// </summary>
    /// <param name="reportIDstring">The subgroup identifier as produced by <see cref="SubgroupCount.DetailedIdName"/>.</param>
    public static string ExtractSubgroupExplanation(NPCInfo npcInfo, string assetPackName, string reportIDstring)
    {
        var log = npcInfo.Report.RootElement.ToString();

        string startStr = "Filtering subgroups within asset pack: " + assetPackName;
        var split1 = log.Split(startStr).ToArray();
        if (split1.Length < 2) { return "Could not parse the log."; }

        string endStr = "</AssetPack>";
        var split2 = split1[1].Split(endStr);
        if (split2.Length < 2) { return "Could not parse the log."; }

        string subgroupStrs = split2[0];

        var subgroupStrArray = subgroupStrs.Split(Environment.NewLine).Where(x => !x.IsNullOrWhitespace()).ToArray();
        return subgroupStrArray.FirstOrDefault(x => ReplaceWhitespace(x, string.Empty).StartsWith(ReplaceWhitespace(reportIDstring, string.Empty))) ?? "No relevant information found";
    }

    /// <summary>Renders the NPC's full verbose report as indented XML text.</summary>
    public static string FormatFullReport(NPCInfo npcInfo)
    {
        if (npcInfo?.Report?.RootElement == null)
        {
            return string.Empty;
        }
        System.Xml.Linq.XDocument output = new();
        output.Add(npcInfo.Report.RootElement);
        return Logger.FormatLogStringIndents(output.ToString());
    }

    //https://stackoverflow.com/questions/6219454/efficient-way-to-remove-all-whitespace-from-string
    private static readonly Regex sWhitespace = new Regex(@"\s+");
    /// <summary>Replaces all whitespace runs in a string (used for tolerant log-line matching).</summary>
    public static string ReplaceWhitespace(string input, string replacement)
    {
        return sWhitespace.Replace(input, replacement);
    }
}
