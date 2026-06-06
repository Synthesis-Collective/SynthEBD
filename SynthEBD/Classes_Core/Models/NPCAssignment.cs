using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// A specific/forced NPC assignment: the user-pinned appearance choices for one NPC (asset pack and its
/// ordered subgroups, height, BodyGen morphs, BodySlide preset, asset-replacer and mix-in assignments,
/// per-type head parts, and the relative asset-application order). Also serves as the consistency record.
/// </summary>
public class NPCAssignment
{
    /// <summary>Display string for the NPC in the UI.</summary>
    public string DispName { get; set; } = "";
    /// <summary>The NPC this assignment targets.</summary>
    public FormKey NPCFormKey { get; set; } = new();
    /// <summary>Forced asset pack name (empty = none).</summary>
    public string AssetPackName { get; set; } = "";
    /// <summary>Forced subgroup IDs within the asset pack; order matters.</summary>
    public List<string> SubgroupIDs { get; set; } = null; // order matters
    /// <summary>Forced height multiplier (null = not forced).</summary>
    public float? Height { get; set; } = null;
    /// <summary>Forced BodyGen morph names; order matters.</summary>
    public List<string> BodyGenMorphNames { get; set; } = null; // order matters
    /// <summary>Forced BodySlide preset name.</summary>
    public string BodySlidePreset { get; set; } = "";
    public List<AssetReplacerAssignment> AssetReplacerAssignments { get; set; } = new();
    public List<MixInAssignment> MixInAssignments { get; set;} = new();
    /// <summary>Per-head-part-type consistency/forced selections.</summary>
    public Dictionary<HeadPart.TypeEnum, HeadPartConsistency> HeadParts { get; set; } = new()
    {
        { HeadPart.TypeEnum.Eyebrows, new() },
        { HeadPart.TypeEnum.Eyes, new() },
        { HeadPart.TypeEnum.Face, new() },
        { HeadPart.TypeEnum.FacialHair, new() },
        { HeadPart.TypeEnum.Hair, new() },
        { HeadPart.TypeEnum.Misc, new() },
        { HeadPart.TypeEnum.Scars, new() }
    };

    /// <summary>A forced asset-replacer selection: which replacer (within an asset pack) and its chosen subgroups.</summary>
    public class AssetReplacerAssignment
    {
        public string AssetPackName { get; set; } = "";
        public string ReplacerName { get; set; } = "";
        public List<string> SubgroupIDs { get; set; } = new();
    }

    /// <summary>A forced mix-in asset-pack selection (its subgroups and replacers), or an explicitly declined mix-in.</summary>
    public class MixInAssignment
    {
        public string AssetPackName { get; set; } = "";
        public List<string> SubgroupIDs { get; set; } = new();
        public List<AssetReplacerAssignment> AssetReplacerAssignments { get; set; } = new();
        /// <summary>True if the user explicitly declined this mix-in (so it is not reconsidered).</summary>
        public bool DeclinedAssignment { get; set; } = false;
    }

    /// <summary>Relative order in which asset packs/replacers are applied for this NPC.</summary>
    public List<string> AssetOrder { get; set; } = new();
}

/// <summary>Backwards-compatibility DTO mirroring the old zEBD specific-NPC-assignment JSON, convertible via <see cref="ToSynthEBDNPCAssignments"/>.</summary>
public class zEBDSpecificNPCAssignment
{
    public string name { get; set; } = "";
    public string formID { get; set; } = "";
    public string EDID { get; set; } = "";
    public string rootPlugin { get; set; } = "";
    public string race { get; set; } = "";
    public string gender { get; set; } = "";
    public string forcedAssetPack { get; set; } = "";
    public List<zEBDForcedSubgroup> forcedSubgroups { get; set; } = new();
    public string forcedHeight { get; set; } = "";
    public List<string> forcedBodyGenMorphs { get; set; } = new();
    public string displayString { get; set; } = "";

    /// <summary>Old zEBD forced-subgroup DTO.</summary>
    public class zEBDForcedSubgroup
    {
        public string id { get; set; }
        public string description { get; set; }
        public string topLevelSubgroup { get; set; }
    }

    /// <summary>Converts a set of legacy zEBD specific-NPC assignments into SynthEBD <see cref="NPCAssignment"/>s, resolving FormKeys and parsing the forced height.</summary>
    /// <param name="inputSet">The legacy assignments to convert.</param>
    /// <param name="logger">Logger for unparseable values.</param>
    /// <param name="converters">Helper for converting zEBD plugin/formID signatures to FormKeys.</param>
    /// <param name="environmentProvider">Supplies the load order used for FormKey resolution.</param>
    /// <returns>The converted SynthEBD assignments.</returns>
    /// <remarks><see cref="NPCAssignment.SubgroupIDs"/> defaults to null, so the forced-subgroup copy loop dereferences null when a legacy entry has forced subgroups — see review notes.</remarks>
    public static HashSet<NPCAssignment> ToSynthEBDNPCAssignments(HashSet<zEBDSpecificNPCAssignment> inputSet, Logger logger, Converters converters, IEnvironmentStateProvider environmentProvider)
    {
        var outputSet = new HashSet<NPCAssignment>();

        foreach (var z in inputSet)
        {
            NPCAssignment s = new NPCAssignment();
            s.NPCFormKey = converters.zEBDSignatureToFormKey(z.rootPlugin, z.formID, environmentProvider);
            s.AssetPackName = z.forcedAssetPack;
            foreach (var zFS in z.forcedSubgroups)
            {
                s.SubgroupIDs.Add(zFS.id);
            }

            if (float.TryParse(z.forcedHeight, out var forcedHeight))
            {
                s.Height = forcedHeight;
            }
            else
            {
                logger.LogError("Error in zEBD Specific NPC Assignment: Cannot interpret height" + z.forcedHeight);
            }

            s.BodyGenMorphNames = z.forcedBodyGenMorphs;
            outputSet.Add(s);
        }
        return outputSet;
    }

}