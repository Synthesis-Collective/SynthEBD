using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;

namespace SynthEBD;

public class UniqueNPCData
{
    private readonly PatcherState _patcherState;
    public UniqueNPCData(PatcherState patcherState)
    {
        _patcherState = patcherState;
    }

    public HashSet<string> UniqueNameExclusions { get; set; } = new();
    public Dictionary<string, 
        Dictionary<FormKey, // race for the given type
        Dictionary<Gender, UniqueNPCTracker>>> UniqueAssignmentsByName = new();

    public void Reinitialize()
    {
        UniqueAssignmentsByName.Clear();
        UniqueNameExclusions = _patcherState.GeneralSettings.LinkedNPCNameExclusions.ToHashSet();
    }

    public class UniqueNPCTracker
    {
        public UniqueNPCTracker(INpcGetter founder)
        {
            Founder = Logger.GetNPCLogReportingString(founder);
        }
        public string Founder { get; set; }
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
    public bool IsValidUnique(INpcGetter npc, out string npcName)
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

    public dynamic GetUniqueNPCTrackerData(NPCInfo npcInfo, AssignmentType property, out string founder)
    {
        founder = "";
        var comparisonRace = GetComparisonRace(npcInfo, property);

        if (npcInfo.IsValidLinkedUnique && 
            UniqueAssignmentsByName.ContainsKey(npcInfo.Name) && 
            UniqueAssignmentsByName[npcInfo.Name].ContainsKey(comparisonRace) &&
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace].ContainsKey(npcInfo.Gender))
        {
            founder = UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].Founder;
            switch (property)
            {
                case AssignmentType.PrimaryAssets: return UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedCombination;
                case AssignmentType.MixInAssets: return UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].MixInAssignments;
                case AssignmentType.ReplacerAssets: return UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].ReplacerAssignments;
                case AssignmentType.BodyGen: return UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedMorphs;
                case AssignmentType.BodySlide: return UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedBodySlidePreset;
                case AssignmentType.Height: return UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedHeight;
                case AssignmentType.HeadParts: return UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].HeadPartAssignments;
                default: return null;
            }
        }
        else
        {
            return null;
        }
    }

    public static FormKey GetComparisonRace(NPCInfo npcInfo, AssignmentType property)
    {
        switch (property)
        {
            case AssignmentType.PrimaryAssets: return npcInfo.AssetsRace;
            case AssignmentType.MixInAssets: return npcInfo.AssetsRace;
            case AssignmentType.ReplacerAssets: return npcInfo.AssetsRace;
            case AssignmentType.BodyGen: return npcInfo.BodyShapeRace;
            case AssignmentType.BodySlide: return npcInfo.BodyShapeRace;
            case AssignmentType.Height: return npcInfo.HeightRace;
            case AssignmentType.HeadParts: return npcInfo.HeadPartsRace;
        }
        return Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DefaultRace.FormKey;
    }

    public void InitializeUniqueNPC(NPCInfo npcInfo)
    {
        if (!UniqueAssignmentsByName.ContainsKey(npcInfo.Name))
        {
            UniqueAssignmentsByName.Add(npcInfo.Name, new());
        }

        foreach (var type in Enum.GetValues(typeof(AssignmentType)))
        {
            var comparisonRace = GetComparisonRace(npcInfo, (AssignmentType)type);

            if (!UniqueAssignmentsByName[npcInfo.Name].ContainsKey(comparisonRace))
            {
                UniqueAssignmentsByName[npcInfo.Name].Add(comparisonRace, new());
            }

            if (!UniqueAssignmentsByName[npcInfo.Name][comparisonRace].ContainsKey(npcInfo.Gender))
            {
                UniqueAssignmentsByName[npcInfo.Name][comparisonRace].Add(npcInfo.Gender, new(npcInfo.NPC));
            }
        }   
    }
    public void InitializeHeadPartTracker(NPCInfo npcInfo)
    {
        if (npcInfo.IsValidLinkedUnique && 
            UniqueAssignmentsByName.ContainsKey(npcInfo.Name) && 
            UniqueAssignmentsByName[npcInfo.Name].ContainsKey(npcInfo.HeadPartsRace) &&
            UniqueAssignmentsByName[npcInfo.Name][npcInfo.HeadPartsRace].ContainsKey(npcInfo.Gender))
        {
            UniqueAssignmentsByName[npcInfo.Name][npcInfo.HeadPartsRace][npcInfo.Gender].HeadPartAssignments = CreateHeadPartTracker();
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