using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class LinkedNPCGroupInfo
{
    public LinkedNPCGroupInfo(LinkedNPCGroup sourceGroup)
    {
        this.NPCFormKeys = sourceGroup.NPCFormKeys;
        this.PrimaryNPCFormKey = sourceGroup.Primary;
    }

    public HashSet<FormKey> NPCFormKeys { get; set; }
    public FormKey PrimaryNPCFormKey { get; set; }
    public SubgroupCombination AssignedCombination { get; set; } = null;
    public List<BodyGenConfig.BodyGenTemplate> AssignedMorphs { get; set; } = new();
    public List<BodySlideSetting> AssignedBodySlides { get; set; } = new();
    public float AssignedHeight { get; set; } = -1;
    public List<LinkedAssetReplacerAssignment> ReplacerAssignments { get; set; } = new();
    public Dictionary<string, SubgroupCombination> MixInAssignments { get; set; } = new();
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
    public class LinkedAssetReplacerAssignment
    {
        public string GroupName { get; set; } = "";
        public string ReplacerName { get; set; } = "";
        public SubgroupCombination AssignedReplacerCombination { get; set; } = null;
    }

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