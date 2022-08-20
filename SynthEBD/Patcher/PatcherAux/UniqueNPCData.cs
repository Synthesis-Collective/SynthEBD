using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class UniqueNPCData
{
    public static HashSet<string> UniqueNameExclusions { get; set; }
    public class UniqueNPCTracker
    {
        public SubgroupCombination AssignedCombination { get; set; } = null;
        public List<BodyGenConfig.BodyGenTemplate> AssignedMorphs { get; set; } = new();
        public BodySlideSetting AssignedBodySlidePreset { get; set; } = null;
        public float AssignedHeight { get; set; } = -1;
        public List<LinkedAssetReplacerAssignment> ReplacerAssignments { get; set; } = new();
        public Dictionary<string, SubgroupCombination> MixInAssignments { get; set; } = new();

        public Dictionary<HeadPart.TypeEnum, IHeadPartGetter> HeadPartAssignments { get; set; } = null;

        public class LinkedAssetReplacerAssignment
        {
            public string GroupName { get; set; } = "";
            public string ReplacerName { get; set; } = "";
            public SubgroupCombination AssignedReplacerCombination { get; set; } = null;
        }
    }

    /// <summary>
    /// Determines if a given NPC should be treated as a linked unique NPC
    /// </summary>
    /// <param name="npc"></param>
    /// <param name="npcName"></param>
    /// <returns></returns>
    public static bool IsValidUnique(INpcGetter npc, out string npcName)
    {
        if (npc.Name == null)
        {
            npcName = "";
            return false;
        }
        else
        {
            npcName = npc.Name.ToString();
        }

        if (UniqueNameExclusions.Contains(npcName, StringComparer.CurrentCultureIgnoreCase))
        {
            return false;
        }

        if (npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public static dynamic GetUniqueNPCTrackerData(NPCInfo npcInfo, AssignmentType property)
    {
        if (npcInfo.IsValidLinkedUnique && Patcher.UniqueAssignmentsByName.ContainsKey(npcInfo.Name) && Patcher.UniqueAssignmentsByName[npcInfo.Name].ContainsKey(npcInfo.Gender))
        {
            switch (property)
            {
                case AssignmentType.PrimaryAssets: return Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].AssignedCombination;
                case AssignmentType.MixInAssets: return Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].MixInAssignments;
                case AssignmentType.ReplacerAssets: return Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].ReplacerAssignments;
                case AssignmentType.BodyGen: return Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].AssignedMorphs;
                case AssignmentType.BodySlide: return Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].AssignedBodySlidePreset;
                case AssignmentType.Height: return Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].AssignedHeight;
                case AssignmentType.HeadParts: return Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].HeadPartAssignments;
                default: return null;
            }
        }
        else
        {
            return null;
        }
    }

    public static void InitializeUniqueNPC(NPCInfo npcInfo)
    {
        if (Patcher.UniqueAssignmentsByName.ContainsKey(npcInfo.Name))
        {
            if (!Patcher.UniqueAssignmentsByName[npcInfo.Name].ContainsKey(npcInfo.Gender))
            {
                Patcher.UniqueAssignmentsByName[npcInfo.Name].Add(npcInfo.Gender, new UniqueNPCTracker());
            }
        }
        else
        {
            Patcher.UniqueAssignmentsByName.Add(npcInfo.Name, new Dictionary<Gender, UniqueNPCData.UniqueNPCTracker>() { { npcInfo.Gender, new UniqueNPCData.UniqueNPCTracker() } });
        }
    }
    public static void InitializeHeadPartTracker(NPCInfo npcInfo)
    {
        if (npcInfo.IsValidLinkedUnique && Patcher.UniqueAssignmentsByName.ContainsKey(npcInfo.Name) && Patcher.UniqueAssignmentsByName[npcInfo.Name].ContainsKey(npcInfo.Gender))
        {
            Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].HeadPartAssignments = CreateHeadPartTracker();
        }
    }

    public static Dictionary<HeadPart.TypeEnum, IHeadPartGetter> CreateHeadPartTracker()
    {
        return new Dictionary<HeadPart.TypeEnum, IHeadPartGetter>()
        {
            { HeadPart.TypeEnum.Eyebrows, null },
            { HeadPart.TypeEnum.Eyes, null },
            { HeadPart.TypeEnum.Face, null },
            { HeadPart.TypeEnum.FacialHair, null },
            { HeadPart.TypeEnum.Hair, null },
            { HeadPart.TypeEnum.Misc, null },
            { HeadPart.TypeEnum.Scars, null }
        };
    }
}