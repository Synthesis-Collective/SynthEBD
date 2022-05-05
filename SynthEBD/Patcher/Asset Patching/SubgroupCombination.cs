using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

public class SubgroupCombination
{
    public SubgroupCombination()
    {
        this.Signature = "";
        this.ContainedSubgroups = new List<FlattenedSubgroup>();
        this.AssignedRecords = new HashSet<Tuple<string, FormKey>>();
        this.AssetPackName = "";
        this.AssetPack = null;
        this.DestinationType = DestinationSpecifier.Main;
    }
    public string Signature { get; set; }
    public List<FlattenedSubgroup> ContainedSubgroups { get; set; }
    public HashSet<Tuple<string, FormKey>> AssignedRecords { get; set; } // string is the location relative to the NPC.
    public string AssetPackName { get; set; }
    public FlattenedAssetPack AssetPack { get; set; }
    public DestinationSpecifier DestinationType { get; set; } // used by Asset Replacers only
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