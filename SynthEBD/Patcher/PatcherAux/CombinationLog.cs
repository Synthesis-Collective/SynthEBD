using Mutagen.Bethesda.Plugins;
using Noggog;
using Synthesis.Bethesda.Execution.Patchers.Solution;
using System.Diagnostics.CodeAnalysis;
using static SynthEBD.Patcher;

namespace SynthEBD;

public class CombinationLog
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly PatcherIO _patcherIO;
    private readonly SynthEBDPaths _paths;
    private readonly Converters _converters;

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
    public Dictionary<string, List<CombinationInfo>> AssignedPrimaryCombinations { get; set; }
    public Dictionary<string, List<CombinationInfo>> AssignedMixInCombinations { get; set; }
    public Dictionary<string, List<CombinationInfo>> AssignedReplacerCombinations { get; set; }

    public void Reinitialize() 
    {
        AssignedPrimaryCombinations = new Dictionary<string, List<CombinationInfo>>();
        AssignedMixInCombinations = new Dictionary<string, List<CombinationInfo>>();
        AssignedReplacerCombinations = new Dictionary<string, List<CombinationInfo>>();
    }

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

    public List<string> FormatAssetPackStats(CategorizedFlattenedAssetPacks assetPacks)
    {
        List<string> output = new();
        foreach (var ap in assetPacks.PrimaryMale.And(assetPacks.MixInFemale).And(assetPacks.PrimaryFemale).And(assetPacks.MixInFemale))
        {
            output.AddRange(FormatAssetPackStats(ap));
        }
        return output;
    }

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

    public void FormatCombinationInfoOutput(Dictionary<string, List<CombinationInfo>> combinationInfo, List<string> fileContents)
    {
        foreach (var entry in combinationInfo)
        {
            fileContents.Add("Generated combinations for Config File: " + entry.Key);
            foreach (var combination in entry.Value.OrderBy(x => x.SubgroupIDs))
            {
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
                combination.AssignedRecords.UnionWith(resolvedSubRecords);

                foreach (var record in combination.AssignedRecords)
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

    public void ResolveSubRecords(GeneratedRecordInfo recordInfo, HashSet<GeneratedRecordInfo> subRecords)
    {
        foreach (var containedFormLink in recordInfo.SubRecords)
        {
            if (_environmentProvider.LinkCache.TryResolve(containedFormLink.FormKey, containedFormLink.Type, out var resolvedSubRecord))
            {
                var loggedSubRecord = new GeneratedRecordInfo() { EditorID =  EditorIDHandler.GetEditorIDSafely(resolvedSubRecord), FormKey = resolvedSubRecord.FormKey.ToString(), SubRecords = resolvedSubRecord.EnumerateFormLinks().Where(x => x.FormKey.ModKey == resolvedSubRecord.FormKey.ModKey).ToHashSet() };
                    
                if (!subRecords.Contains(loggedSubRecord))
                {
                    subRecords.Add(loggedSubRecord);
                }

                ResolveSubRecords(loggedSubRecord, subRecords);
            }
        }
    }

    public void LogAssignment(NPCInfo npcInfo, List<SubgroupCombination> combinations, List<FilePathReplacementParsed> assignedPaths)
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
            if (combinationDict.ContainsKey(combination.AssetPackName))
            {
                currentAssetPackCombinations = combinationDict[combination.AssetPackName];
            }
            else
            {
                currentAssetPackCombinations = new List<CombinationInfo>();
                combinationDict.Add(combination.AssetPackName, currentAssetPackCombinations);
            }

            if (!combination.Signature.Contains(':')) { _logger.LogError("Couldn't record combination with signature: " + combination.Signature); continue; }

            string currentSubgroupIDs = combination.Signature.Split(':')[1];
            var currentCombinationRecord = currentAssetPackCombinations.Where(x => x.SubgroupIDs == currentSubgroupIDs).FirstOrDefault();
            if (currentCombinationRecord == null)
            {
                currentCombinationRecord = new CombinationInfo() { SubgroupIDs = currentSubgroupIDs, SubgroupDeepNames = combination.ContainedSubgroups.Select(x => x.DeepNamesString).ToList() };
                currentAssetPackCombinations.Add(currentCombinationRecord);
            }

            currentCombinationRecord.NPCsAssignedTo.Add(npcInfo.LogIDstring);

            var pathsForThisCombination = assignedPaths.Where(x => x.ParentCombination.Signature == combination.Signature).ToArray();
            foreach (var recordSet in pathsForThisCombination.Select(x => x.TraversedRecords).ToArray())
            {
                foreach (var recordInfo in recordSet)
                {
                    if (currentCombinationRecord.AssignedFormKeys.Contains(recordInfo.FormKey)) { continue; }

                    currentCombinationRecord.AssignedRecords.Add(recordInfo);
                    currentCombinationRecord.AssignedFormKeys.Add(recordInfo.FormKey);
                }
            }

            combination.AssetPack.AssignmentCount++;
            foreach (var subgroup in combination.ContainedSubgroups)
            {
                subgroup.AssignmentCount++;
            }
        }
    }
}

public class CombinationInfo
{
    public string SubgroupIDs { get; set; } = "";
    public List<string> SubgroupDeepNames { get; set; } = new();
    public HashSet<GeneratedRecordInfo> AssignedRecords { get; set; } = new(new GeneratedRecordInfo.CombinationRecordComparer());
    public HashSet<string> NPCsAssignedTo { get; set; } = new();
    public HashSet<string> AssignedFormKeys { get; set; } = new(); // same data as AssignedRecords but easier to check against
}

public class GeneratedRecordInfo
{
    public string FormKey { get; set; }
    public string EditorID { get; set; }
    public HashSet<IFormLinkGetter> SubRecords { get; set; }

    public class CombinationRecordComparer : IEqualityComparer<GeneratedRecordInfo>
    {
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

        public int GetHashCode([DisallowNull] GeneratedRecordInfo obj)
        {
            return obj.FormKey.GetHashCode();
        }
    }
}