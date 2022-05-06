using Mutagen.Bethesda.Plugins;

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
    public BodySlideSetting AssignedBodySlide { get; set; } = null;
    public float AssignedHeight { get; set; } = -1;
    public List<LinkedAssetReplacerAssignment> ReplacerAssignments { get; set; } = new();
    public Dictionary<string, SubgroupCombination> MixInAssignments { get; set; } = new();

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