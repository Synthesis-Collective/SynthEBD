using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class UniqueNPCData
{
    public static HashSet<string> UniqueNameExclusions { get; set; }
    public class UniqueNPCTracker
    {
        public UniqueNPCTracker()
        {
            AssignedCombination = null;
            AssignedMorphs = new List<BodyGenConfig.BodyGenTemplate>();
            AssignedBodySlidePreset = null;
            AssignedHeight = -1;
            this.ReplacerAssignments = new List<LinkedAssetReplacerAssignment>();
            this.MixInAssignments = new Dictionary<string, SubgroupCombination>();
        }
        public SubgroupCombination AssignedCombination { get; set; }
        public List<BodyGenConfig.BodyGenTemplate> AssignedMorphs { get; set; }
        public BodySlideSetting AssignedBodySlidePreset { get; set; }
        public float AssignedHeight { get; set; }
        public List<LinkedAssetReplacerAssignment> ReplacerAssignments { get; set; }
        public Dictionary<string, SubgroupCombination> MixInAssignments { get; set; }
        public class LinkedAssetReplacerAssignment
        {
            public LinkedAssetReplacerAssignment()
            {
                GroupName = "";
                ReplacerName = "";
                AssignedReplacerCombination = null;
            }
            public string GroupName { get; set; }
            public string ReplacerName { get; set; }
            public SubgroupCombination AssignedReplacerCombination { get; set; }
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
}