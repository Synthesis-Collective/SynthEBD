using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;

namespace SynthEBD;

/// <summary>
/// Tracks shared appearance assignments for "unique" NPCs (those flagged Unique, sharing a name across
/// multiple FormKeys). The first such NPC processed becomes the "founder" and its assignments (assets, body
/// shape, height, head parts) are reused for every later NPC of the same name/race/gender, keeping linked
/// uniques consistent. Keyed by NPC name → comparison race → gender. Per-patcher-run state; reset via
/// <see cref="Reinitialize"/>.
/// </summary>
public class UniqueNPCData
{
    private readonly PatcherState _patcherState;
    /// <summary>Captures the patcher state used to read general settings (name exclusions, race grouping).</summary>
    public UniqueNPCData(PatcherState patcherState)
    {
        _patcherState = patcherState;
    }

    /// <summary>Names that should NOT be treated as linked uniques (e.g. generic "Courier"), case-insensitively compared.</summary>
    public HashSet<string> UniqueNameExclusions { get; set; } = new(StringComparer.CurrentCultureIgnoreCase);
    /// <summary>Founder assignments indexed by NPC name, then comparison race (per assignment type), then gender.</summary>
    public Dictionary<string, 
        Dictionary<FormKey, // race for the given type
        Dictionary<Gender, UniqueNPCTracker>>> UniqueAssignmentsByName = new();

    /// <summary>Clears all tracked assignments and reloads the name-exclusion set from general settings. Call at the start of a patcher run.</summary>
    public void Reinitialize()
    {
        UniqueAssignmentsByName.Clear();
        UniqueNameExclusions = _patcherState.GeneralSettings.LinkedNPCNameExclusions.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Holds the founder NPC's chosen assignments for one name/race/gender bucket. Later same-name uniques
    /// copy these values so their appearance stays consistent with the founder.
    /// </summary>
    public class UniqueNPCTracker
    {
        /// <summary>Records the founder NPC's log-reporting string for diagnostics.</summary>
        public UniqueNPCTracker(INpcGetter founder)
        {
            Founder = Logger.GetNPCLogReportingString(founder);
        }
        /// <summary>Human-readable identifier of the founder NPC, used in log/report messages.</summary>
        public string Founder { get; set; }
        /// <summary>The primary asset combination assigned to the founder (null until set).</summary>
        public SubgroupCombination AssignedCombination { get; set; } = null;
        /// <summary>BodyGen morphs assigned to the founder.</summary>
        public List<BodyGenConfig.BodyGenTemplate> AssignedMorphs { get; set; } = new();
        /// <summary>BodySlide presets assigned to the founder.</summary>
        public List<BodySlideSetting> AssignedBodySlidePresets { get; set; } = new();
        /// <summary>Height assigned to the founder; -1 means unset.</summary>
        public float AssignedHeight { get; set; } = -1;
        /// <summary>Asset-replacer combinations assigned to the founder, one per replacer group.</summary>
        public List<LinkedAssetReplacerAssignment> ReplacerAssignments { get; set; } = new();
        /// <summary>Mix-in asset combinations assigned to the founder, keyed by mix-in config name.</summary>
        public Dictionary<string, SubgroupCombination> MixInAssignments { get; set; } = new();

        /// <summary>Head parts assigned to the founder, keyed by head part type.</summary>
        public Dictionary<HeadPart.TypeEnum, IHeadPartGetter> HeadPartAssignments { get; set; } = new();

        /// <summary>One linked-unique asset-replacer assignment: the chosen combination for a named replacer within an asset-pack group.</summary>
        public class LinkedAssetReplacerAssignment
        {
            /// <summary>Name of the asset-pack group that owns the replacer.</summary>
            public string GroupName { get; set; } = "";
            /// <summary>Name of the replacer config.</summary>
            public string ReplacerName { get; set; } = "";
            /// <summary>The chosen replacer combination (null until set).</summary>
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

        if (UniqueNameExclusions.Contains(npcName))
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

    /// <summary>
    /// True if this NPC is a valid linked unique and a founder tracker already exists for its
    /// name/comparison-race/gender bucket. <paramref name="comparisonRace"/> returns the race used as the
    /// dictionary key for <paramref name="assignmentType"/>.
    /// </summary>
    private bool HasUniqueAssignment(NPCInfo npcInfo, AssignmentType assignmentType, out FormKey comparisonRace)
    {
        comparisonRace = GetComparisonRace(npcInfo, assignmentType);
        return npcInfo.IsValidLinkedUnique &&
            UniqueAssignmentsByName.ContainsKey(npcInfo.Name) &&
            UniqueAssignmentsByName[npcInfo.Name].ContainsKey(comparisonRace) &&
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace].ContainsKey(npcInfo.Gender);
    }

    /// <summary>
    /// Ensures the name → comparison-race → gender path exists in <see cref="UniqueAssignmentsByName"/>,
    /// creating empty levels and a founder tracker as needed. <paramref name="comparisonRace"/> returns the
    /// race key for <paramref name="assignmentType"/>. Mutates the dictionary.
    /// </summary>
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

    /// <summary>Retrieves the founder's primary asset combination for this NPC's bucket, if one was recorded.</summary>
    /// <returns>True and outputs the assignment and founder name; false (with defaults) if no founder assignment exists.</returns>
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

    /// <summary>Records <paramref name="primaryAssets"/> as the founder's primary combination if the NPC is a linked unique and none was set yet. No-op otherwise. Mutates tracked state.</summary>
    public void InitializeUnsetUniqueNPCPrimaryAssets(NPCInfo npcInfo, SubgroupCombination primaryAssets)
    {
        if (!npcInfo.IsValidLinkedUnique) { return; }
        CreateUnqiueAssignmentIfNeeded(npcInfo, AssignmentType.PrimaryAssets, out var comparisonRace);
        if (UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedCombination == null)
        {
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedCombination = primaryAssets;
        }
    }

    /// <summary>Retrieves the founder's mix-in asset combinations (keyed by mix-in name) for this NPC's bucket, if recorded.</summary>
    /// <returns>True with the assignments and founder name; false (with defaults) if no founder assignment exists.</returns>
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

    /// <summary>Records a mix-in combination under <paramref name="mixInName"/> for the founder if the NPC is a linked unique and that mix-in isn't already set. Mutates tracked state.</summary>
    public void InitializeUnsetUniqueNPCPMixInAssets(NPCInfo npcInfo, string mixInName, SubgroupCombination mixInAssignment)
    {
        if (!npcInfo.IsValidLinkedUnique) { return; }
        CreateUnqiueAssignmentIfNeeded(npcInfo, AssignmentType.MixInAssets, out var comparisonRace);
        if (!UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].MixInAssignments.ContainsKey(mixInName))
        {
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].MixInAssignments.Add(mixInName, mixInAssignment);
        }
    }

    /// <summary>Retrieves the founder's asset-replacer assignments for this NPC's bucket, if recorded.</summary>
    /// <returns>True with the assignments and founder name; false (with defaults) if no founder assignment exists.</returns>
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

    /// <summary>Adds <paramref name="replacerAssignment"/> to the founder's replacer list if the NPC is a linked unique and no assignment with the same ReplacerName exists yet. Mutates tracked state.</summary>
    public void InitializeUnsetUniqueNPCPReplacerAssets(NPCInfo npcInfo, UniqueNPCTracker.LinkedAssetReplacerAssignment replacerAssignment)
    {
        if (!npcInfo.IsValidLinkedUnique) { return; }
        CreateUnqiueAssignmentIfNeeded(npcInfo, AssignmentType.ReplacerAssets, out var comparisonRace);
        if (!UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].ReplacerAssignments.Any(x => x.ReplacerName == replacerAssignment.ReplacerName))
        {
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].ReplacerAssignments.Add(replacerAssignment);
        }
    }

    /// <summary>Retrieves the founder's BodyGen morphs for this NPC's body-shape bucket, if recorded.</summary>
    /// <returns>True with the morphs and founder name; false (with defaults) if no founder assignment exists.</returns>
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

    /// <summary>Records BodyGen morphs for the founder if the NPC is a linked unique and none were set yet. Mutates tracked state.</summary>
    public void InitializeUnsetUniqueNPCBodyGen(NPCInfo npcInfo, List<BodyGenConfig.BodyGenTemplate> bodyGenAssignments)
    {
        if (!npcInfo.IsValidLinkedUnique) { return; }
        CreateUnqiueAssignmentIfNeeded(npcInfo, AssignmentType.BodyGen, out var comparisonRace);
        if (!UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedMorphs.Any())
        {
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedMorphs = bodyGenAssignments;
        }
    }

    /// <summary>Retrieves the founder's BodySlide presets for this NPC's body-shape bucket, if recorded.</summary>
    /// <returns>True with the presets and founder name; false (with defaults) if no founder assignment exists.</returns>
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

    /// <summary>Records BodySlide presets for the founder if the NPC is a linked unique and none were set yet. Mutates tracked state.</summary>
    public void InitializeUnsetUniqueNPCBodySlide(NPCInfo npcInfo, List<BodySlideSetting> bodyslideAssignments)
    {
        if (!npcInfo.IsValidLinkedUnique) { return; }
        CreateUnqiueAssignmentIfNeeded(npcInfo, AssignmentType.BodySlide, out var comparisonRace);
        if (!UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedBodySlidePresets.Any())
        {
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedBodySlidePresets = bodyslideAssignments;
        }
    }

    /// <summary>Retrieves the founder's assigned height for this NPC's height bucket, if recorded.</summary>
    /// <returns>True with the height and founder name; false (with -1 / empty) if no founder assignment exists.</returns>
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

    /// <summary>Records a height for the founder if the NPC is a linked unique and none was set yet (tracked value still -1). Mutates tracked state.</summary>
    public void InitializeUnsetUniqueNPCHeight(NPCInfo npcInfo, float heightAssignment)
    {
        if (!npcInfo.IsValidLinkedUnique) { return; }
        CreateUnqiueAssignmentIfNeeded(npcInfo, AssignmentType.Height, out var comparisonRace);
        if (UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedHeight == -1)
        {
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].AssignedHeight = heightAssignment;
        }
    }

    /// <summary>Retrieves the founder's head-part assignments (keyed by head part type) for this NPC's head-parts bucket, if recorded.</summary>
    /// <returns>True with the assignments and founder name; false (with defaults) if no founder assignment exists.</returns>
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

    /// <summary>Records head-part assignments for the founder if the NPC is a linked unique and none were set yet. Mutates tracked state.</summary>
    public void InitializeUnsetUniqueNPCHeadParts(NPCInfo npcInfo, Dictionary<HeadPart.TypeEnum, IHeadPartGetter> headpartAssignments)
    {
        if (!npcInfo.IsValidLinkedUnique) { return; }
        CreateUnqiueAssignmentIfNeeded(npcInfo, AssignmentType.HeadParts, out var comparisonRace);
        if (!UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].HeadPartAssignments.Any())
        {
            UniqueAssignmentsByName[npcInfo.Name][comparisonRace][npcInfo.Gender].HeadPartAssignments = headpartAssignments;
        }
    }

    /// <summary>
    /// Returns the per-axis race used as the dictionary key for the given assignment type (asset, body-shape,
    /// height, or head-parts race), since race aliasing can differ per axis. Falls back to DefaultRace.
    /// </summary>
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

    /// <summary>
    /// Pre-creates empty founder trackers for every assignment type's comparison-race/gender bucket for this
    /// NPC's name, so later lookups find a tracker. Mutates <see cref="UniqueAssignmentsByName"/>.
    /// </summary>
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
    /// <summary>Seeds the founder's head-part bucket with an all-null per-type tracker (via <see cref="CreateHeadPartTracker"/>) if the NPC is a linked unique with an existing bucket. Mutates tracked state.</summary>
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

    /// <summary>Builds a fresh head-part assignment map with an entry (initially null) for each supported head part type.</summary>
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