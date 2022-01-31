using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class CombinationLog
    {
        public CombinationLog()
        {
            AssignedPrimaryCombinations = new Dictionary<string, List<CombinationInfo>>();
            AssignedMixInCombinations = new Dictionary<string, List<CombinationInfo>>();
            AssignedReplacerCombinations = new Dictionary<string, List<CombinationInfo>>();
        }
        public static Dictionary<string, List<CombinationInfo>> AssignedPrimaryCombinations { get; set; }
        public static Dictionary<string, List<CombinationInfo>> AssignedMixInCombinations { get; set; }
        public static Dictionary<string, List<CombinationInfo>> AssignedReplacerCombinations { get; set; }

        public static void Reinitialize() 
        {
            AssignedPrimaryCombinations = new Dictionary<string, List<CombinationInfo>>();
            AssignedMixInCombinations = new Dictionary<string, List<CombinationInfo>>();
            AssignedReplacerCombinations = new Dictionary<string, List<CombinationInfo>>();
        }

        public static void WriteToFile()
        {
            if (!PatcherSettings.TexMesh.bGenerateAssignmentLog) { return; }
            string outputFile = System.IO.Path.Combine(PatcherSettings.Paths.LogFolderPath, Logger.Instance.PatcherExecutionStart.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture), "Generated Combinations.txt");

            List<string> output = new List<string>();

            output.Add("----------------Primary Combinations:----------------" + Environment.NewLine);
            FormatCombinationInfoOutput(AssignedPrimaryCombinations, output);

            output.Add("----------------MixIn Combinations:----------------" + Environment.NewLine);
            FormatCombinationInfoOutput(AssignedMixInCombinations, output);

            output.Add("----------------Replacer Combinations:----------------" + Environment.NewLine);
            FormatCombinationInfoOutput(AssignedReplacerCombinations, output);

            Task.Run(() => PatcherIO.WriteTextFile(outputFile, output));
        }

        public static void FormatCombinationInfoOutput(Dictionary<string, List<CombinationInfo>> combinationInfo, List<string> fileContents)
        {
            foreach (var entry in combinationInfo)
            {
                fileContents.Add("Generated combinations for Config File: " + entry.Key);
                foreach (var combination in entry.Value.OrderBy(x => x.SubgroupIDs))
                {
                    fileContents.Add("\tCombination: " + combination.SubgroupIDs);
                    fileContents.Add("\tAssigned to NPCs:");
                    foreach (var npcString in combination.NPCsAssignedTo)
                    {
                        fileContents.Add("\t\t" + npcString);
                    }
                    fileContents.Add("\tRecords Belonging to this combination");
                    foreach (var record in combination.AssignedRecords)
                    {
                        if (Converters.FormKeyStringToFormIDString(record.FormKey, out string formID))
                        {
                            fileContents.Add("\t\t" + record.EditorID + "(" + formID + ")");
                        }
                    }
                }
                fileContents.Add(Environment.NewLine);
            }
        }

        public static void LogAssignment(NPCInfo npcInfo, List<SubgroupCombination> combinations, List<FilePathReplacementParsed> assignedPaths)
        {
            if (!PatcherSettings.TexMesh.bGenerateAssignmentLog) { return; }

            Dictionary<string, List<CombinationInfo>> combinationDict = null;

            foreach (var combination in combinations)
            {
                switch (combination.AssetPack.Type)
                {
                    case FlattenedAssetPack.AssetPackType.Primary: combinationDict = CombinationLog.AssignedPrimaryCombinations; break;
                    case FlattenedAssetPack.AssetPackType.MixIn: combinationDict = CombinationLog.AssignedMixInCombinations; break;
                    case FlattenedAssetPack.AssetPackType.ReplacerVirtual: combinationDict = CombinationLog.AssignedReplacerCombinations; break;
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

                if (!combination.Signature.Contains(':')) { Logger.LogError("Couldn't record combination with signature: " + combination.Signature); continue; }

                string currentSubgroupIDs = combination.Signature.Split(':')[1];
                var currentCombinationRecord = currentAssetPackCombinations.Where(x => x.SubgroupIDs == currentSubgroupIDs).FirstOrDefault();
                if (currentCombinationRecord == null)
                {
                    currentCombinationRecord = new CombinationInfo() { SubgroupIDs = currentSubgroupIDs };
                    currentAssetPackCombinations.Add(currentCombinationRecord);
                }

                currentCombinationRecord.NPCsAssignedTo.Add(npcInfo.LogIDstring);

                var pathsForThisCombination = assignedPaths.Where(x => x.ParentCombination.Signature == combination.Signature);
                foreach (var recordSet in pathsForThisCombination.Select(x => x.TraversedRecords))
                {
                    foreach (var recordInfo in recordSet)
                    {
                        if (currentCombinationRecord.AssignedFormKeys.Contains(recordInfo.FormKey)) { continue; }

                        currentCombinationRecord.AssignedRecords.Add(recordInfo);
                        currentCombinationRecord.AssignedFormKeys.Add(recordInfo.FormKey);
                    }
                }
            }
        }
    }

    public class CombinationInfo
    {
        public CombinationInfo()
        {
            SubgroupIDs = "";
            AssignedRecords = new HashSet<GeneratedRecordInfo>(new GeneratedRecordInfo.CombinationRecordComparer());
            NPCsAssignedTo = new HashSet<string>();
            AssignedFormKeys = new HashSet<string>();
        }
        public string SubgroupIDs { get; set; }
        public HashSet<GeneratedRecordInfo> AssignedRecords { get; set; }
        public HashSet<string> NPCsAssignedTo { get; set; }
        public HashSet<string> AssignedFormKeys { get; set; } // same data as AssignedRecords but easier to check against
    }

    public class GeneratedRecordInfo
    {
        public string FormKey { get; set; }
        public string EditorID { get; set; }

        public class CombinationRecordComparer : IEqualityComparer<GeneratedRecordInfo>
        {
            public bool Equals(GeneratedRecordInfo x, GeneratedRecordInfo y)
            {
                return x.FormKey == y.FormKey && x.EditorID == y.EditorID;
            }

            public int GetHashCode([DisallowNull] GeneratedRecordInfo obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
