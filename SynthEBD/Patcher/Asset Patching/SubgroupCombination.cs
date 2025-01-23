using Microsoft.CodeAnalysis;
using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

public class SubgroupCombination
{
    public string Signature { get; set; } = "";
    public List<FlattenedSubgroup> ContainedSubgroups { get; set; } = new();
    public HashSet<Tuple<string, FormKey>> AssignedRecords { get; set; } = new(); // string is the location relative to the NPC.
    public string AssignmentName { get; set; } = ""; // is the Asset Pack name unless the combination belongs to a Replacer, in which case it's the replacer name
    public FlattenedAssetPack AssetPack { get; set; } = null;
    public DestinationSpecifier DestinationType { get; set; } = DestinationSpecifier.Main; // used by Asset Replacers only
    public FormKey ReplacerDestinationFormKey { get; set; } // used by Asset Replacers only

    public enum DestinationSpecifier
    {
        Main,
        HeadPartFormKey,
        Generic,
        MarksFemaleHumanoid04RightGashR, // special cases because the corresponding headparts don't exist even though the textures do
        MarksFemaleHumanoid06RightGashR // special cases because the corresponding headparts don't exist even though the textures do
    }
}

public class SimplifiedSubgroupCombination
{
    public string AssignmentName { get; set; } = ""; // is the Asset Pack name unless the combination belongs to a Replacer, in which case it's the replacer name
    public string Signature { get; set; }
    public FlattenedAssetPack.AssetPackType AssetPackType { get; set; }
    public List<string> SubgroupDeepNames { get; set; } = new();
    public List<FilePathReplacementParsed> FilePathReplacements { get; set; }
    public int LongestPathLength { get; set; }
    public HashSet<Tuple<string, FormKey>> AssignedRecords { get; set; } = new(); // string is the location relative to the NPC.
    public List<string> AddKeywords { get; set; } = new();

    public SimplifiedSubgroupCombination(SubgroupCombination template, NPCInfo npcInfo, PatcherState patcherState, Logger logger)
    {
        AssignmentName = template.AssignmentName;
        Signature = template.Signature;
        AssetPackType = template.AssetPack.Type;
        SubgroupDeepNames = template.ContainedSubgroups.Select(x => x.DeepNamesString).ToList();
        (FilePathReplacements, LongestPathLength) = FilePathReplacementParsed.CombinationToPaths(template, npcInfo, patcherState, logger);
        foreach (var subgroup in template.ContainedSubgroups)
        {
            AddKeywords.AddRange(subgroup.AddKeywords);
        }
    }
}
