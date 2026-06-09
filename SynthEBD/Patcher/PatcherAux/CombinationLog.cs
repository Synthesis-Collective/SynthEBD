using Mutagen.Bethesda.Plugins;
using Noggog;
using Synthesis.Bethesda.Execution.Patchers.Solution;
using System.Diagnostics.CodeAnalysis;
using static SynthEBD.Patcher;

namespace SynthEBD;

/// <summary>
/// Accumulates which asset-pack subgroup combinations were assigned to which NPCs (primary, mix-in, and replacer
/// categories) during patching, then formats and writes the human-readable "Generated Combinations.txt" assignment
/// log. Records are logged per-NPC as assets are selected and flushed to file at the end of the run.
/// </summary>
public class CombinationLog
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly PatcherIO _patcherIO;
    private readonly SynthEBDPaths _paths;
    private readonly Converters _converters;

    /// <summary>Creates the log with the environment, patcher state, IO helper, paths, and converters, and initializes the three (empty) per-category combination dictionaries.</summary>
    public CombinationLog(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, PatcherIO patcherIO, SynthEBDPaths paths, Converters converters)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _patcherIO = patcherIO;
        _paths = paths;
        _converters = converters;

        AssignedPrimaryCombinations = new Dictionary<string, List<CombinationInfo>>();
        AssignedMixInCombinations = new Dictionary<string, List<CombinationInfo>>();
        AssignedReplacerCombinations = new Dictionary<string, List<CombinationInfo>>();
    }
    /// <summary>Assigned primary-asset-pack combinations, keyed by config-file/assignment name.</summary>
    public Dictionary<string, List<CombinationInfo>> AssignedPrimaryCombinations { get; set; }
    /// <summary>Assigned mix-in combinations, keyed by config-file/assignment name.</summary>
    public Dictionary<string, List<CombinationInfo>> AssignedMixInCombinations { get; set; }
    /// <summary>Assigned replacer combinations, keyed by config-file/assignment name.</summary>
    public Dictionary<string, List<CombinationInfo>> AssignedReplacerCombinations { get; set; }

    /// <summary>Resets all three combination dictionaries to fresh empty instances, discarding any prior run's data.</summary>
    public void Reinitialize() 
    {
        AssignedPrimaryCombinations = new Dictionary<string, List<CombinationInfo>>();
        AssignedMixInCombinations = new Dictionary<string, List<CombinationInfo>>();
        AssignedReplacerCombinations = new Dictionary<string, List<CombinationInfo>>();
    }

    /// <summary>
    /// Builds and writes the "Generated Combinations.txt" log (assignment statistics plus primary, mix-in, and replacer
    /// combination sections) into the run's timestamped log folder. No-ops when assignment logging is disabled.
    /// Side effect: writes the text file asynchronously via <see cref="Task.Run(System.Action)"/>.
    /// </summary>
    public void WriteToFile(CategorizedFlattenedAssetPacks assetPacks)
    {
        if (!_patcherState.TexMeshSettings.bGenerateAssignmentLog) { return; }
        string outputFile = System.IO.Path.Combine(_paths.LogFolderPath, _logger.PatcherExecutionStart.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture), "Generated Combinations.txt");

        _logger.LogMessage("Writing combination log to " + outputFile);

        List<string> output = new List<string>();

        output.Add("----------------Assignment Statistics:----------------" + Environment.NewLine);
        output.AddRange(FormatAssetPackStats(assetPacks));

        output.Add("----------------Primary Combinations:----------------" + Environment.NewLine);
        FormatCombinationInfoOutput(AssignedPrimaryCombinations, output);

        output.Add("----------------MixIn Combinations:----------------" + Environment.NewLine);
        FormatCombinationInfoOutput(AssignedMixInCombinations, output);

        output.Add("----------------Replacer Combinations:----------------" + Environment.NewLine);
        FormatCombinationInfoOutput(AssignedReplacerCombinations, output);

        Task.Run(() => PatcherIO.WriteTextFile(outputFile, output, _logger));
    }

    /// <summary>Formats per-subgroup assignment-count statistics across all categorized asset packs (primary and mix-in, male and female).</summary>
    public List<string> FormatAssetPackStats(CategorizedFlattenedAssetPacks assetPacks)
    {
        List<string> output = new();
        foreach (var ap in assetPacks.PrimaryMale.And(assetPacks.MixInFemale).And(assetPacks.PrimaryFemale).And(assetPacks.MixInMale))
        {
            output.AddRange(FormatAssetPackStats(ap));
        }
        return output;
    }

    /// <summary>Formats one asset pack's group name and the assignment count for each of its subgroups as indented text lines.</summary>
    public List<string> FormatAssetPackStats(FlattenedAssetPack ap)
    {
        List<string> output = new();
        output.Add("\t" + ap.GroupName);
        foreach (var subgroupPos in ap.Subgroups)
        {
            foreach (var subgroup in subgroupPos)
            {
                output.Add("\t\t" + subgroup.Id + " " + subgroup.DeepNamesString + ": " + subgroup.AssignmentCount.ToString());
            }
        }
        output.Add("");
        return output;
    }

    /// <summary>
    /// Appends a formatted block for each config file's combinations to <paramref name="fileContents"/>: subgroup IDs,
    /// deep names, the NPCs assigned, and the (recursively resolved) records belonging to each combination.
    /// </summary>
    public void FormatCombinationInfoOutput(Dictionary<string, List<CombinationInfo>> combinationInfo, List<string> fileContents)
    {
        foreach (var entry in combinationInfo)
        {
            fileContents.Add("Generated combinations for Config File: " + entry.Key);
            foreach (var combination in entry.Value.OrderBy(x => x.SubgroupIDs))
            {
                fileContents.Add(Environment.NewLine);
                fileContents.Add("\tCombination: " + combination.SubgroupIDs);
                fileContents.Add("\t\tSubgroup Names: ");
                foreach (var subgroupName in combination.SubgroupDeepNames)
                {
                    fileContents.Add("\t\t\t" + subgroupName);
                }
                fileContents.Add("\tAssigned to NPCs:");
                foreach (var npcString in combination.NPCsAssignedTo)
                {
                    fileContents.Add("\t\t\t" + npcString);
                }
                fileContents.Add("\t\tRecords Belonging to this combination");

                //resolve subrecords
                HashSet<GeneratedRecordInfo> resolvedSubRecords = new HashSet<GeneratedRecordInfo>(new GeneratedRecordInfo.CombinationRecordComparer());
                resolvedSubRecords.UnionWith(combination.AssignedRecords); // prevent duplicates

                foreach (var assignedRecord in combination.AssignedRecords)
                {
                    ResolveSubRecords(assignedRecord, resolvedSubRecords);
                }

                foreach (var record in resolvedSubRecords)
                {
                    if (_converters.TryFormKeyStringToFormIDString(record.FormKey, out string formID))
                    {
                        fileContents.Add("\t\t\t" + (record.EditorID) + " (" + formID + ")"); // not a Mutagen record; EditorID will never be null
                    }
                }
            }
            fileContents.Add(Environment.NewLine);
        }
    }

    /// <summary>
    /// Recursively walks the form links contained in <paramref name="recordInfo"/>, resolving each via the link cache and
    /// adding newly-seen sub-records to <paramref name="subRecords"/> (which doubles as the visited set to prevent cycles).
    /// </summary>
    public void ResolveSubRecords(GeneratedRecordInfo recordInfo, HashSet<GeneratedRecordInfo> subRecords)
    {
        foreach (var containedFormLink in recordInfo.SubRecords)
        {
            if (_environmentProvider.LinkCache.TryResolve(containedFormLink.FormKey, containedFormLink.Type, out var resolvedSubRecord))
            {
                var loggedSubRecord = new GeneratedRecordInfo() { EditorID =  EditorIDHandler.GetEditorIDSafely(resolvedSubRecord), FormKey = resolvedSubRecord.FormKey.ToString(), SubRecords = resolvedSubRecord.EnumerateFormLinks().Where(x => x.FormKey.ModKey == resolvedSubRecord.FormKey.ModKey).ToHashSet() };
                    
                if (subRecords.Add(loggedSubRecord))
                {
                    ResolveSubRecords(loggedSubRecord, subRecords);
                }
            }
        }
    }

    //public void LogAssignment(NPCInfo npcInfo, List<SubgroupCombination> combinations, List<FilePathReplacementParsed> assignedPaths)
    /// <summary>
    /// Records the subgroup combinations chosen for <paramref name="npcInfo"/> into the appropriate per-category dictionary,
    /// creating a <see cref="CombinationInfo"/> per distinct subgroup-ID signature, adding the NPC to its assigned list, and
    /// bumping assignment counts on the asset pack and its subgroups. No-ops when assignment logging is disabled.
    /// </summary>
    public void LogCombinationSelections(NPCInfo npcInfo, List<SubgroupCombination> combinations)
    {
        if (!_patcherState.TexMeshSettings.bGenerateAssignmentLog) { return; }

        Dictionary<string, List<CombinationInfo>> combinationDict = null;

        foreach (var combination in combinations)
        {
            switch (combination.AssetPack.Type)
            {
                case FlattenedAssetPack.AssetPackType.Primary: combinationDict = AssignedPrimaryCombinations; break;
                case FlattenedAssetPack.AssetPackType.MixIn: combinationDict = AssignedMixInCombinations; break;
                case FlattenedAssetPack.AssetPackType.ReplacerVirtual: combinationDict = AssignedReplacerCombinations; break;
                default: continue;
            }

            List<CombinationInfo> currentAssetPackCombinations = null;
            if (combinationDict.ContainsKey(combination.AssignmentName))
            {
                currentAssetPackCombinations = combinationDict[combination.AssignmentName];
            }
            else
            {
                currentAssetPackCombinations = new List<CombinationInfo>();
                combinationDict.Add(combination.AssignmentName, currentAssetPackCombinations);
            }

            if (!TryGetSubgroupIdsFromSignature(combination.Signature, out var currentSubgroupIDs)) { _logger.LogError("Couldn't record combination with signature: " + combination.Signature); continue; }
            var currentCombinationRecord = currentAssetPackCombinations.Where(x => x.SubgroupIDs == currentSubgroupIDs).FirstOrDefault();
            if (currentCombinationRecord == null)
            {
                currentCombinationRecord = new CombinationInfo() { SubgroupIDs = currentSubgroupIDs, SubgroupDeepNames = combination.ContainedSubgroups.Select(x => x.DeepNamesString).ToList() };
                currentAssetPackCombinations.Add(currentCombinationRecord);
            }

            currentCombinationRecord.NPCsAssignedTo.Add(npcInfo.LogIDstring);
            
            combination.AssetPack.AssignmentCount++;
            foreach (var subgroup in combination.ContainedSubgroups)
            {
                subgroup.AssignmentCount++;
            }
        }
    }

    /// <summary>Extracts the subgroup-ID portion (after the first ':') of a combination signature. Returns false when the
    /// signature has no ':', so callers skip a malformed signature instead of throwing on Split(':')[1].</summary>
    public static bool TryGetSubgroupIdsFromSignature(string signature, out string subgroupIDs)
    {
        var parts = signature.Split(':');
        if (parts.Length < 2)
        {
            subgroupIDs = string.Empty;
            return false;
        }
        subgroupIDs = parts[1];
        return true;
    }

    /// <summary>
    /// Attaches the generated records produced for <paramref name="npcInfo"/> to the matching previously-logged combination
    /// (by signature within its category), de-duplicating by FormKey. Returns early if the combination was not already logged.
    /// </summary>
    public void LogAssignedRecords(NPCInfo npcInfo, List<Patcher.SelectedAssetContainer> containers)
    {
        foreach (var container in containers)
        {
            foreach (var pathAssignment in container.Paths)
            {
                Dictionary<string, List<CombinationInfo>> combinationDict = null;
                switch (container.CombinationType)
                {
                    case FlattenedAssetPack.AssetPackType.Primary: combinationDict = AssignedPrimaryCombinations; break;
                    case FlattenedAssetPack.AssetPackType.MixIn: combinationDict = AssignedMixInCombinations; break;
                    case FlattenedAssetPack.AssetPackType.ReplacerVirtual:
                        combinationDict = AssignedReplacerCombinations; break;
                    default: return;
                }

                List<CombinationInfo> currentAssetPackCombinations = null;
                if (combinationDict.ContainsKey(container.LoggingLabel))
                {
                    currentAssetPackCombinations = combinationDict[container.LoggingLabel];
                }
                else
                {
                    currentAssetPackCombinations = new List<CombinationInfo>();
                    combinationDict.Add(container.LoggingLabel, currentAssetPackCombinations);
                }

                if (!TryGetSubgroupIdsFromSignature(container.Signature, out var currentSubgroupIDs)) { _logger.LogError("Couldn't record combination with signature: " + container.Signature); continue; }

                var currentCombinationRecord = currentAssetPackCombinations
                    .Where(x => x.SubgroupIDs == currentSubgroupIDs)
                    .FirstOrDefault();
                if (currentCombinationRecord == null)
                {
                    return;
                }

                foreach (var recordInfo in container.TraversedRecords.ToArray())
                {
                    if (currentCombinationRecord.AssignedFormKeys.Contains(recordInfo.FormKey))
                    {
                        continue;
                    }

                    currentCombinationRecord.AssignedRecords.Add(recordInfo);
                    currentCombinationRecord.AssignedFormKeys.Add(recordInfo.FormKey);
                }
            }
        }
    }
}

/// <summary>One assigned subgroup combination: its subgroup-ID signature, deep names, the NPCs it was assigned to, and the records it generated.</summary>
public class CombinationInfo
{
    /// <summary>The combination's subgroup-ID signature (the part after ':' in the combination signature).</summary>
    public string SubgroupIDs { get; set; } = "";
    public List<string> SubgroupDeepNames { get; set; } = new();
    public HashSet<GeneratedRecordInfo> AssignedRecords { get; set; } = new(new GeneratedRecordInfo.CombinationRecordComparer());
    public HashSet<string> NPCsAssignedTo { get; set; } = new();
    public HashSet<string> AssignedFormKeys { get; set; } = new(); // same data as AssignedRecords but easier to check against
}

/// <summary>Lightweight record descriptor used by the combination log: FormKey string, EditorID, and the form links it contains.</summary>
public class GeneratedRecordInfo
{
    public string FormKey { get; set; }
    public string EditorID { get; set; }
    public HashSet<IFormLinkGetter> SubRecords { get; set; }

    /// <summary>Equality comparer treating two records as equal when their FormKey strings match and their EditorIDs are both null or equal.</summary>
    public class CombinationRecordComparer : IEqualityComparer<GeneratedRecordInfo>
    {
        /// <summary>Returns true when both records share a FormKey and have matching (or both-null) EditorIDs.</summary>
        public bool Equals(GeneratedRecordInfo x, GeneratedRecordInfo y)
        {
            if (x.FormKey == y.FormKey)
            {
                if (x.EditorID != null && y.EditorID != null && x.EditorID == y.EditorID)
                {
                    return true;
                }
                else if (x.EditorID == null && y.EditorID == null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Hashes by FormKey string (EditorID intentionally excluded so it can vary within an equality bucket).</summary>
        public int GetHashCode([DisallowNull] GeneratedRecordInfo obj)
        {
            return obj.FormKey.GetHashCode();
        }
    }
}