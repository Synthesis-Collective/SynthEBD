using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>
/// One chosen combination of subgroups (one per top-level subgroup position) selected for an NPC during
/// asset patching, carrying the records it resolves to and the destination metadata used by asset replacers.
/// </summary>
public class SubgroupCombination
{
    /// <summary>Concatenated identifier of the contained subgroups, used to deduplicate/identify the combination.</summary>
    public string Signature { get; set; } = "";
    /// <summary>The chosen <see cref="FlattenedSubgroup"/> at each top-level subgroup position.</summary>
    public List<FlattenedSubgroup> ContainedSubgroups { get; set; } = new();
    /// <summary>Records assigned by this combination, keyed by destination path relative to the NPC plus the assigned form key.</summary>
    public HashSet<Tuple<string, FormKey>> AssignedRecords { get; set; } = new(); // string is the location relative to the NPC.
    /// <summary>Display name of the assignment: the asset pack name, or the replacer name when this combination belongs to a replacer.</summary>
    public string AssignmentName { get; set; } = ""; // is the Asset Pack name unless the combination belongs to a Replacer, in which case it's the replacer name
    /// <summary>The flattened asset pack this combination was drawn from.</summary>
    public FlattenedAssetPack AssetPack { get; set; } = null;
    /// <summary>Where the replaced assets should be applied; only meaningful for asset replacers.</summary>
    public DestinationSpecifier DestinationType { get; set; } = DestinationSpecifier.Main; // used by Asset Replacers only
    /// <summary>Form key of the replacer destination (e.g. a specific head part); only meaningful for asset replacers.</summary>
    public FormKey ReplacerDestinationFormKey { get; set; } // used by Asset Replacers only

    /// <summary>Identifies where an asset-replacer combination's assets are targeted.</summary>
    public enum DestinationSpecifier
    {
        /// <summary>The NPC's main/worn assets.</summary>
        Main,
        /// <summary>A specific head part identified by <see cref="ReplacerDestinationFormKey"/>.</summary>
        HeadPartFormKey,
        /// <summary>A generic, non-specific destination.</summary>
        Generic,
        /// <summary>Special case: female humanoid right-gash marks variant 04, whose head part record does not exist even though the texture does.</summary>
        MarksFemaleHumanoid04RightGashR, // special cases because the corresponding headparts don't exist even though the textures do
        /// <summary>Special case: female humanoid right-gash marks variant 06, whose head part record does not exist even though the texture does.</summary>
        MarksFemaleHumanoid06RightGashR // special cases because the corresponding headparts don't exist even though the textures do
    }
}