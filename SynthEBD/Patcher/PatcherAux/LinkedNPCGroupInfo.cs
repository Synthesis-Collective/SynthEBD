using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Runtime counterpart of a UI-defined <see cref="LinkedNPCGroup"/>: holds the appearance assignments
/// (assets, body shape, height, replacers, mix-ins, head parts) shared across a group of linked NPCs so
/// that secondary members reuse the primary member's choices.
/// </summary>
public class LinkedNPCGroupInfo
{
    /// <summary>Creates a group info seeded from a defined <see cref="LinkedNPCGroup"/>, copying its member form keys and primary NPC.</summary>
    public LinkedNPCGroupInfo(LinkedNPCGroup sourceGroup)
    {
        this.NPCFormKeys = sourceGroup.NPCFormKeys;
        this.PrimaryNPCFormKey = sourceGroup.Primary;
    }

    /// <summary>Form keys of all NPCs that belong to this linked group.</summary>
    public HashSet<FormKey> NPCFormKeys { get; set; }
    /// <summary>Form key of the group's primary NPC, whose assignments the others inherit.</summary>
    public FormKey PrimaryNPCFormKey { get; set; }
    /// <summary>The shared primary asset combination assigned to the group.</summary>
    public SubgroupCombination AssignedCombination { get; set; } = null;
    /// <summary>The shared BodyGen morphs assigned to the group.</summary>
    public List<BodyGenConfig.BodyGenTemplate> AssignedMorphs { get; set; } = new();
    /// <summary>The shared BodySlide presets assigned to the group.</summary>
    public List<BodySlideSetting> AssignedBodySlides { get; set; } = new();
    /// <summary>The shared height assigned to the group; -1 indicates unassigned.</summary>
    public float AssignedHeight { get; set; } = -1;
    /// <summary>The shared asset-replacer assignments for the group.</summary>
    public List<LinkedAssetReplacerAssignment> ReplacerAssignments { get; set; } = new();
    /// <summary>The shared mix-in asset combinations, keyed by mix-in asset pack name.</summary>
    public Dictionary<string, SubgroupCombination> MixInAssignments { get; set; } = new();
    /// <summary>The shared head part assignments for the group, keyed by head part type (null = none assigned).</summary>
    public Dictionary<HeadPart.TypeEnum, IHeadPartGetter> HeadPartAssignments { get; set; } = new()
    {
        { HeadPart.TypeEnum.Eyebrows, null },
        { HeadPart.TypeEnum.Eyes, null },
        { HeadPart.TypeEnum.Face, null },
        { HeadPart.TypeEnum.FacialHair, null },
        { HeadPart.TypeEnum.Hair, null },
        { HeadPart.TypeEnum.Misc, null },
        { HeadPart.TypeEnum.Scars, null }
    };
    /// <summary>A single shared asset-replacer combination assigned to the group, scoped by its owning asset pack and replacer name.</summary>
    public class LinkedAssetReplacerAssignment
    {
        /// <summary>Name of the asset pack that owns the replacer.</summary>
        public string GroupName { get; set; } = "";
        /// <summary>Name of the replacer within the asset pack.</summary>
        public string ReplacerName { get; set; } = "";
        /// <summary>The chosen replacer subgroup combination.</summary>
        public SubgroupCombination AssignedReplacerCombination { get; set; } = null;
    }

    /// <summary>
    /// Links the UI-defined <see cref="LinkedNPCGroup"/> (which only lists NPCs) to the corresponding generated
    /// <see cref="LinkedNPCGroupInfo"/> (which holds the patcher assignments). Returns the existing generated info
    /// for the NPC's group if one has been created, otherwise a fresh info seeded from the defined group, or null
    /// if the NPC is not part of any defined group.
    /// </summary>
    /// <param name="definedGroups">The UI-defined linked NPC groups.</param>
    /// <param name="createdGroups">The already-generated group infos.</param>
    /// <param name="npcFormKey">The NPC to look up.</param>
    public static LinkedNPCGroupInfo GetInfoFromLinkedNPCGroup(HashSet<LinkedNPCGroup> definedGroups, HashSet<LinkedNPCGroupInfo> createdGroups, FormKey npcFormKey) // links the UI-defined LinkedNPCGroup (which only contains NPCs) to the corresponding generated LinkedNPCGroupInfo (which contains patcher assignments)
    {
        foreach (var group in definedGroups)
        {
            if (group.NPCFormKeys.Contains(npcFormKey))
            {
                var associatedGroup = createdGroups.Where(x => x.NPCFormKeys.Contains(npcFormKey)).FirstOrDefault();
                if (associatedGroup == null)
                {
                    return new LinkedNPCGroupInfo(group);
                }
                else
                {
                    return associatedGroup;
                }
            }
        }
        return null;
    }
}