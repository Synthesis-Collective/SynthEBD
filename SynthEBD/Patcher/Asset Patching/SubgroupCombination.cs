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