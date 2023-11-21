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
        public List<BodySlideSetting> AssignedBodySlidePresets { get; set; } = new();
        public float AssignedHeight { get; set; } = -1;
        public List<LinkedAssetReplacerAssignment> ReplacerAssignments { get; set; } = new();
        public Dictionary<string, SubgroupCombination> MixInAssignments { get; set; } = new();

        public Dictionary<HeadPart.TypeEnum, IHeadPartGetter> HeadPartAssignments { get; set; } = new();

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

    private bool HasUniqueAssignment(NPCInfo npcInfo, AssignmentType assignmentType, out FormKey comparisonRace)
    {
        comparisonRace = GetComparisonRace(npcInfo, assignmentType);
        return npcInfo.IsValidLinkedUnique &&
            UniqueAssignmentsByName.ContainsKey(npcInfo.Name) &&
            UniqueAssignmentsByName[npcInfo.Name].ContainsKey(comparisonRace) &&
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace].ContainsKey(npcInfo.Gender);
    }

    private void CreateUnqiueAssignmentIfNeeded(NPCInfo npcInfo, AssignmentType assignmentType, out FormKey comparisonRace)
    {
        comparisonRace = GetComparisonRace(npcInfo, assignmentType);

        if (!UniqueAssignmentsByName.ContainsKey(npcInfo.Name))
        {
            UniqueAssignmentsByName.Add(npcInfo.Name, new());
        }
        
        if (!UniqueAssignmentsByName[npcInfo.Name].ContainsKey(comparisonRace))
        {
            UniqueAssignmentsByName[npcInfo.Name].Add(comparisonRace, new());
        }

        if (!UniqueAssignmentsByName[npcInfo.Name][comparisonRace].ContainsKey(npcInfo.Gender))
        {
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace].Add(npcInfo.Gender, new(npcInfo.NPC));
        }
    }

    public bool TryGetUniqueNPCPrimaryAssets(NPCInfo npcInfo, out SubgroupCombination primaryAssets, out string founder)
    {
        founder = String.Empty;
        primaryAssets = null;
        if (!HasUniqueAssignment(npcInfo, AssignmentType.PrimaryAssets, out var comparisonRace))
        {
            return false;
        }

        founder = UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].Founder;
        primaryAssets = UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedCombination;
        return true;
    }

    public void InitializeUnsetUniqueNPCPrimaryAssets(NPCInfo npcInfo, SubgroupCombination primaryAssets)
    {
        if (!npcInfo.IsValidLinkedUnique) { return; }
        CreateUnqiueAssignmentIfNeeded(npcInfo, AssignmentType.PrimaryAssets, out var comparisonRace);
        if (UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedCombination == null)
        {
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedCombination = primaryAssets;
        }
    }

    public bool TryGetUniqueNPCMixInAssets(NPCInfo npcInfo, out Dictionary<string, SubgroupCombination> mixInAssignments, out string founder)
    {
        founder = String.Empty;
        mixInAssignments = new();
        if (!HasUniqueAssignment(npcInfo, AssignmentType.MixInAssets, out var comparisonRace))
        {
            return false;
        }

        founder = UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].Founder;
        mixInAssignments = UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].MixInAssignments;
        return true;
    }

    public void InitializeUnsetUniqueNPCPMixInAssets(NPCInfo npcInfo, string mixInName, SubgroupCombination mixInAssignment)
    {
        if (!npcInfo.IsValidLinkedUnique) { return; }
        CreateUnqiueAssignmentIfNeeded(npcInfo, AssignmentType.MixInAssets, out var comparisonRace);
        if (!UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].MixInAssignments.ContainsKey(mixInName))
        {
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].MixInAssignments.Add(mixInName, mixInAssignment);
        }
    }

    public bool TryGetUniqueNPCReplacerAssets(NPCInfo npcInfo, out List<UniqueNPCTracker.LinkedAssetReplacerAssignment> replacerAssignments, out string founder)
    {
        founder = String.Empty;
        replacerAssignments = new();
        if (!HasUniqueAssignment(npcInfo, AssignmentType.ReplacerAssets, out var comparisonRace))
        {
            return false;
        }

        founder = UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].Founder;
        replacerAssignments = UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].ReplacerAssignments;
        return true;
    }

    public void InitializeUnsetUniqueNPCPReplacerAssets(NPCInfo npcInfo, UniqueNPCTracker.LinkedAssetReplacerAssignment replacerAssignment)
    {
        if (!npcInfo.IsValidLinkedUnique) { return; }
        CreateUnqiueAssignmentIfNeeded(npcInfo, AssignmentType.ReplacerAssets, out var comparisonRace);
        if (!UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].ReplacerAssignments.Where(x => x.ReplacerName == replacerAssignment.ReplacerName).Any())
        {
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].ReplacerAssignments.Add(replacerAssignment);
        }
    }

    public bool TryGetUniqueNPCBodyGenAssignments(NPCInfo npcInfo, out List<BodyGenConfig.BodyGenTemplate> bodyGenAsignments, out string founder)
    {
        founder = String.Empty;
        bodyGenAsignments = new();
        if (!HasUniqueAssignment(npcInfo, AssignmentType.BodyGen, out var comparisonRace))
        {
            return false;
        }

        founder = UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].Founder;
        bodyGenAsignments = UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedMorphs;
        return true;
    }

    public void InitializeUnsetUniqueNPCBodyGen(NPCInfo npcInfo, List<BodyGenConfig.BodyGenTemplate> bodyGenAssignments)
    {
        if (!npcInfo.IsValidLinkedUnique) { return; }
        CreateUnqiueAssignmentIfNeeded(npcInfo, AssignmentType.BodyGen, out var comparisonRace);
        if (!UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedMorphs.Any())
        {
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedMorphs = bodyGenAssignments;
        }
    }

    public bool TryGetUniqueNPCBodySlideAssignments(NPCInfo npcInfo, out List<BodySlideSetting> bodyslideAssignments, out string founder)
    {
        founder = String.Empty;
        bodyslideAssignments = new();
        if (!HasUniqueAssignment(npcInfo, AssignmentType.BodySlide, out var comparisonRace))
        {
            return false;
        }

        founder = UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].Founder;
        bodyslideAssignments = UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedBodySlidePresets;
        return true;
    }

    public void InitializeUnsetUniqueNPCBodySlide(NPCInfo npcInfo, List<BodySlideSetting> bodyslideAssignments)
    {
        if (!npcInfo.IsValidLinkedUnique) { return; }
        CreateUnqiueAssignmentIfNeeded(npcInfo, AssignmentType.BodySlide, out var comparisonRace);
        if (!UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedBodySlidePresets.Any())
        {
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedBodySlidePresets = bodyslideAssignments;
        }
    }

    public bool TryGetUniqueNPCHeight(NPCInfo npcInfo, out float assignedHeight, out string founder)
    {
        founder = String.Empty;
        assignedHeight = -1;
        if (!HasUniqueAssignment(npcInfo, AssignmentType.Height, out var comparisonRace))
        {
            return false;
        }

        founder = UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].Founder;
        assignedHeight = UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedHeight;
        return true;
    }

    public void InitializeUnsetUniqueNPCHeight(NPCInfo npcInfo, float heightAssignment)
    {
        if (!npcInfo.IsValidLinkedUnique) { return; }
        CreateUnqiueAssignmentIfNeeded(npcInfo, AssignmentType.Height, out var comparisonRace);
        if (UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedHeight == -1)
        {
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedHeight = heightAssignment;
        }
    }

    public bool TryGetUniqueNPCHeadParts(NPCInfo npcInfo, out Dictionary<HeadPart.TypeEnum, IHeadPartGetter> headpartAssignments, out string founder)
    {
        founder = String.Empty;
        headpartAssignments = new();
        if (!HasUniqueAssignment(npcInfo, AssignmentType.HeadParts, out var comparisonRace))
        {
            return false;
        }

        founder = UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].Founder;
        headpartAssignments = UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].HeadPartAssignments;
        return true;
    }

    public void InitializeUnsetUniqueNPCHeadParts(NPCInfo npcInfo, Dictionary<HeadPart.TypeEnum, IHeadPartGetter> headpartAssignments)
    {
        if (!npcInfo.IsValidLinkedUnique) { return; }
        CreateUnqiueAssignmentIfNeeded(npcInfo, AssignmentType.HeadParts, out var comparisonRace);
        if (!UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].HeadPartAssignments.Any())
        {
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].HeadPartAssignments = headpartAssignments;
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