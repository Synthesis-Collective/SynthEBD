using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SynthEBD;

/// <summary>
/// Per-NPC BodySlide (OBody/AutoBody) assignment axis of the patcher. Chooses one or more BodySlide
/// presets for an NPC from the gender-appropriate <see cref="Settings_OBody"/> list, honoring Specific
/// assignments, link-group and unique-NPC inheritance, rule validity (unique/race/weight/attribute/
/// descriptor), asset-imposed descriptor priorities, consistency, and probability weighting. Supports a
/// multiple-assignment mode where OBody itself picks at runtime from the candidate set.
/// </summary>
public class OBodySelector
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly AttributeMatcher _attributeMatcher;
    private readonly UniqueNPCData _uniqueNPCData;
    private readonly BodyShapeCandidateValidator _candidateValidator;
    /// <summary>Injects patcher state, environment, logging, attribute matching, unique-NPC tracking, and the shared candidate-validator dependencies.</summary>
    public OBodySelector(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger,AttributeMatcher attributeMatcher, UniqueNPCData uniqueNPCData, BodyShapeCandidateValidator candidateValidator)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _attributeMatcher = attributeMatcher;   
        _uniqueNPCData = uniqueNPCData;
        _candidateValidator = candidateValidator;
    }
    /// <summary>
    /// Main entry point: selects the BodySlide preset(s) for an NPC from the gender-appropriate list. Resolves
    /// (in order) Specific assignment, link-group and unique-NPC inheritance, then a random path that filters
    /// presets by <see cref="PresetIsValid"/>, prefers ForceIf-matched presets, applies asset-imposed
    /// descriptor priorities, and honors consistency or probability weighting. In multiple-assignment mode it
    /// returns the whole candidate set for OBody to choose from at runtime.
    /// </summary>
    /// <param name="selectionMade">True if at least one preset was assigned.</param>
    /// <param name="assignedAssetCombinations">Asset combinations assigned to this NPC (drive descriptor rules/priorities).</param>
    /// <param name="statusFlags">Outputs consistency-related status flags for the body-shape selector.</param>
    /// <returns>The chosen presets, or null if none could be assigned.</returns>
    /// <remarks>Records each candidate preset's ForceIf match count in <c>npcInfo.ForceIfMatches</c> and logs.</remarks>
    public List<BodySlideSetting> SelectBodySlidePresets(NPCInfo npcInfo, out bool selectionMade, Settings_OBody oBodySettings, IEnumerable<SubgroupCombination> assignedAssetCombinations,  out AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag statusFlags)
    {
        selectionMade = false;

        _logger.OpenReportSubsection("OBodySelection", npcInfo);
        _logger.LogReport("Selecting a BodySlide preset for the current NPC", false, npcInfo);
        List<BodySlideSetting> availablePresets = null;
        statusFlags = new AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag();
        switch (npcInfo.Gender)
        {
            case Gender.Male: availablePresets = new List<BodySlideSetting>(oBodySettings.BodySlidesMale); break; // shallow copy to allow pruning
            case Gender.Female: availablePresets = new List<BodySlideSetting>(oBodySettings.BodySlidesFemale); break;
        }

        if (!availablePresets.Any())
        {
            selectionMade = false;
            _logger.LogReport("No BodySlide presets are available for NPCs of the current gender.", false, npcInfo);
            _logger.CloseReportSubsection(npcInfo);
            return null;
        }

        List<BodySlideSetting> selectedPresets = new();

        #region Specific NPC Assignments
        if (npcInfo.SpecificNPCAssignment != null && !npcInfo.SpecificNPCAssignment.BodySlidePreset.IsNullOrWhitespace())
        {
            var specifiedPreset = availablePresets.FirstOrDefault(x => x.Label == npcInfo.SpecificNPCAssignment.BodySlidePreset);
            if (specifiedPreset != null)
            {
                _logger.LogReport("Assigned forced BodySlide preset " + specifiedPreset.Label, false, npcInfo);
                selectedPresets.Add(specifiedPreset);
            }
            else
            {
                _logger.LogReport("Could not find the forced BodySlide preset \"" + npcInfo.SpecificNPCAssignment.BodySlidePreset + "\" within the available presets. Attempting to assign another preset.", true, npcInfo);
            }
        }
        #endregion

        #region Linked NPC Group
        if (!selectedPresets.Any() && // check for availablePresets.Any() to avoid overwriting Specific Assignment
            npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary)
        {
            var linkedPresets = npcInfo.AssociatedLinkGroup.AssignedBodySlides;
            if (linkedPresets.Any())
            {
                selectedPresets.AddRange(linkedPresets);
                if (selectedPresets.Count == 1)
                {
                    _logger.LogReport("Assigned linked BodySlide preset " + linkedPresets.First().Label + " from primary NPC " + npcInfo.AssociatedLinkGroup.PrimaryNPCFormKey.ToString(), false, npcInfo);
                }
                else
                {
                    _logger.LogReport("Assigned linked BodySlide presets " + String.Join(", ", linkedPresets.Select(x => x.Label)) + " from primary NPC " + npcInfo.AssociatedLinkGroup.PrimaryNPCFormKey.ToString(), false, npcInfo);
                }
            }
            else
            {
                _logger.LogReport("No BodySlides were assigned to primary Linked NPC " + npcInfo.AssociatedLinkGroup.PrimaryNPCFormKey.ToString() + ". Attempting to assign another preset.", true, npcInfo);
            }
        }
        #endregion

        #region Unique NPC replicates
        else if (!selectedPresets.Any() && _uniqueNPCData.IsValidUnique(npcInfo.NPC, out var npcName) && _uniqueNPCData.TryGetUniqueNPCBodySlideAssignments(npcInfo, out selectedPresets, out string uniqueFounderNPC)) // check for selectedPreset == null to avoid overwriting Specific Assignment
        {
            if (selectedPresets.Any())
            {
                _logger.LogReport("Another unique NPC with the same name (" + uniqueFounderNPC + ") was assigned a BodySlide preset " + String.Join(", ", selectedPresets.Select(x => x.Label)) + ". Using that BodySlide for current NPC.", false, npcInfo);
            }
        }
        #endregion

        #region Random Selection

        if (!selectedPresets.Any())
        {
            var filteredPresets = new List<BodySlideSetting>(); // fall back if ForceIfs fail
            var forceIfPresets = new List<BodySlideSetting>();

            var validationContext = BuildValidationContext(npcInfo, oBodySettings);
            foreach (var preset in availablePresets)
            {
                if (_candidateValidator.CandidateIsValid(preset, npcInfo, validationContext, assignedAssetCombinations))
                {
                    filteredPresets.Add(preset);
                    if (npcInfo.ForceIfMatches.Get(preset) > 0)
                    {
                        forceIfPresets.Add(preset);
                    }
                }
            }

            _logger.LogReport(() => "Available BodySlides (Force If Attribute Count): " + Environment.NewLine + String.Join(Environment.NewLine, filteredPresets.OrderBy(x => npcInfo.ForceIfMatches.Get(x)).Select(x => x.Label + " (" + npcInfo.ForceIfMatches.Get(x) + ")")), false, npcInfo);

            if (forceIfPresets.Any())
            {
                #region Filter By Descriptor Priority
                FilterPresetsByPreferredDescriptors(npcInfo, forceIfPresets, assignedAssetCombinations);
                #endregion

                #region Consistency (With ForceIf)
                if (_patcherState.GeneralSettings.bEnableConsistency && npcInfo.ConsistencyNPCAssignment != null && npcInfo.ConsistencyNPCAssignment.BodySlidePreset != "" && forceIfPresets.Select(x => x.Label).Contains(npcInfo.ConsistencyNPCAssignment.BodySlidePreset) && !_patcherState.OBodySettings.OBodyEnableMultipleAssignments)
                {
                    selectedPresets = forceIfPresets.Where(x => x.Label == npcInfo.ConsistencyNPCAssignment.BodySlidePreset).ToList();
                    if (selectedPresets.Any())
                    {
                        _logger.LogReport("Found consistency BodySlide: " + selectedPresets.First().Label, false, npcInfo);
                    }
                    else
                    {
                        _logger.LogReport("Consistency BodySlide " + npcInfo.ConsistencyNPCAssignment.BodySlidePreset + " is no longer available.", false, npcInfo);
                    }
                }
                #endregion
                else if (_patcherState.OBodySettings.OBodyEnableMultipleAssignments)
                {
                    selectedPresets = forceIfPresets;
                }
                else
                {
                    var selectedPreset = ProbabilityWeighting.SelectByProbability(forceIfPresets,
                        x => x.ProbabilityWeighting * ProbabilityWeighting.GetProbabilityModifierFactor(
                            x.ProbabilityWeightModifiers, npcInfo.NPC, npcInfo.BodyShapeRace,
                            _patcherState.OBodySettings.AttributeGroups, _attributeMatcher,
                            _patcherState.GeneralSettings.VerboseModeDetailedAttributes, _logger, npcInfo, x.Label));
                    if (selectedPreset != null)
                    {
                        selectedPresets.Add(selectedPreset);
                    }
                }
            }
            else
            {
                #region Filter By Descriptor Priority
                FilterPresetsByPreferredDescriptors(npcInfo, filteredPresets, assignedAssetCombinations);
                #endregion

                #region Consistency (Without ForceIf)
                if (_patcherState.GeneralSettings.bEnableConsistency && npcInfo.ConsistencyNPCAssignment != null && npcInfo.ConsistencyNPCAssignment.BodySlidePreset != "" && filteredPresets.Select(x => x.Label).Contains(npcInfo.ConsistencyNPCAssignment.BodySlidePreset) && !_patcherState.OBodySettings.OBodyEnableMultipleAssignments)
                {
                    selectedPresets = filteredPresets.Where(x => x.Label == npcInfo.ConsistencyNPCAssignment.BodySlidePreset).ToList();
                    if (selectedPresets.Any())
                    {
                        _logger.LogReport("Found consistency BodySlide: " + selectedPresets.First().Label, false, npcInfo);
                    }
                    else
                    {
                        _logger.LogReport("Consistency BodySlide " + npcInfo.ConsistencyNPCAssignment.BodySlidePreset + " is no longer available.", false, npcInfo);
                    }
                }
                #endregion
                else if (_patcherState.OBodySettings.OBodyEnableMultipleAssignments)
                {
                    selectedPresets = filteredPresets;
                }
                else
                {
                    var selectedPreset = ProbabilityWeighting.SelectByProbability(filteredPresets,
                        x => x.ProbabilityWeighting * ProbabilityWeighting.GetProbabilityModifierFactor(
                            x.ProbabilityWeightModifiers, npcInfo.NPC, npcInfo.BodyShapeRace,
                            _patcherState.OBodySettings.AttributeGroups, _attributeMatcher,
                            _patcherState.GeneralSettings.VerboseModeDetailedAttributes, _logger, npcInfo, x.Label));
                    if (selectedPreset != null)
                    {
                        selectedPresets.Add(selectedPreset);
                    }
                }
            }
        }
        #endregion

        if (!selectedPresets.Any())
        {
            _logger.LogReport("Could not choose any valid BodySlide presets for NPC " + npcInfo.LogIDstring, true, npcInfo);
            _logger.CloseReportSubsection(npcInfo);
            selectionMade = false;
            return null;
        }
        else
        {
            string logText;
            if (selectedPresets.Count == 1)
            {
                logText = "Chose BodySlide Preset: " + selectedPresets.First().Label;
            }
            else
            {
                logText = "Chose BodySlide Presets: " + string.Join(", ", selectedPresets.Select(x => x.Label));
            }
            _logger.LogReport(logText, false, npcInfo);
            selectionMade = true;

            if (_patcherState.GeneralSettings.bEnableConsistency && npcInfo.ConsistencyNPCAssignment != null && !_patcherState.OBodySettings.OBodyEnableMultipleAssignments)
            {
                if (selectedPresets.First().Label != npcInfo.ConsistencyNPCAssignment.BodySlidePreset)
                {
                    statusFlags |= AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag.ConsistencyMorphIsInvalid;
                    if (availablePresets.Select(x => x.Label).Contains(npcInfo.ConsistencyNPCAssignment.BodySlidePreset))
                    {
                        _logger.LogReport("The consistency BodySlide preset " + npcInfo.ConsistencyNPCAssignment.BodySlidePreset + " could not be chosen because it no longer complied with the current distribution rules so a new BodySlide was selected.", true, npcInfo);
                    }                    
                }
                else
                {
                    statusFlags |= AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag.MatchesConsistency;
                }
            }   
        }

        //store selected bodyslide
        if (npcInfo.ConsistencyNPCAssignment != null && npcInfo.ConsistencyNPCAssignment.BodySlidePreset != null && npcInfo.ConsistencyNPCAssignment.BodySlidePreset != "" && !_patcherState.OBodySettings.OBodyEnableMultipleAssignments && selectedPresets.Any() && npcInfo.ConsistencyNPCAssignment.BodySlidePreset == selectedPresets.First().Label)
        {
            statusFlags |= AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag.MatchesConsistency;
        }

        GenerateBodySlideDescriptorReport(selectedPresets, npcInfo);

        _logger.CloseReportSubsection(npcInfo);

        return selectedPresets;
    }

    /// <summary>
    /// Validates a single BodySlide preset against the NPC via the shared <see cref="BodyShapeCandidateValidator"/>
    /// rule battery (R18): random/unique/race/weight/attribute rules (tallying ForceIf matches in
    /// <c>npcInfo.ForceIfMatches</c>), the preset's per-weight descriptor rules, and the allowed/disallowed
    /// BodySlide descriptors of every assigned asset combination and its subgroups.
    /// </summary>
    /// <returns>True if the preset may be distributed to the NPC.</returns>
    public bool PresetIsValid(BodySlideSetting candidatePreset, NPCInfo npcInfo, IEnumerable<SubgroupCombination> assignedAssetCombinations, Settings_OBody oBodySettings)
    {
        return _candidateValidator.CandidateIsValid(candidatePreset, npcInfo, BuildValidationContext(npcInfo, oBodySettings), assignedAssetCombinations);
    }

    /// <summary>Builds the per-call validation context for the OBody settings: BodySlide axis, the settings' attribute groups, their descriptor catalog (flattened once), and the Specific-assignment exemption test.</summary>
    private BodyShapeCandidateValidator.ValidationContext BuildValidationContext(NPCInfo npcInfo, Settings_OBody oBodySettings)
    {
        return new BodyShapeCandidateValidator.ValidationContext()
        {
            Noun = "Preset",
            Axis = BodyShapeCandidateValidator.BodyShapeAxis.BodySlide,
            AttributeGroups = oBodySettings.AttributeGroups,
            DescriptorCatalog = oBodySettings.TemplateDescriptors.Flatten().ToList(),
            IgnoreRaceChecks = false,
            IsSpecificallyAssigned = x => npcInfo.SpecificNPCAssignment != null && !npcInfo.SpecificNPCAssignment.BodySlidePreset.IsNullOrWhitespace() && x.Label == npcInfo.SpecificNPCAssignment.BodySlidePreset,
        };
    }
    
    /// <summary>
    /// Narrows <paramref name="bodySlides"/> in place toward presets matching the body-shape descriptor
    /// priorities imposed by the assigned asset subgroups, processing priorities in order and only applying a
    /// filter step when at least one preset matches (so it never empties the list).
    /// </summary>
    /// <remarks>Mutates <paramref name="bodySlides"/> in place and logs.</remarks>
    public void FilterPresetsByPreferredDescriptors(NPCInfo npcInfo, List<BodySlideSetting> bodySlides, IEnumerable<SubgroupCombination> assignedAssetCombinations)
    {
        if (!bodySlides.Any())
        {
            return;
        }

        var subgroups = assignedAssetCombinations.SelectMany(combination => combination.ContainedSubgroups.Where(subgroup => subgroup.PrioritizedBodySlideDescriptors.Any())).ToList();

        var currentBodySlides = new List<BodySlideSetting>(bodySlides);

        if (subgroups.Any())
        {
            List<BodyShapeDescriptor.PrioritizedLabelSignature> priorities = new();

            var groupedByConfig = subgroups.GroupBy(x => x.ParentAssetPack.GroupName).ToArray();
            _logger.LogReport("The assigned combination(s) have imposed the following Body Shape Descriptor priorities: ", false, npcInfo);
            foreach (var config in groupedByConfig)
            {
                _logger.LogReport(config.Key + ":", false, npcInfo);

                foreach (var subgroup in config)
                {
                    _logger.LogReport("-" + subgroup.Id + ":", false, npcInfo);
                    foreach (var descriptor in subgroup.PrioritizedBodySlideDescriptors.SelectMany(x => x.Value).Where(x => x.Priority >= 0).ToArray())
                    {
                        _logger.LogReport("--" + descriptor.ToString(), false, npcInfo);
                        var existingDescriptor = priorities.FirstOrDefault(x => x.Equals(descriptor));
                        if (existingDescriptor == null)
                        {
                            priorities.Add(descriptor);
                        }
                        else
                        {
                            existingDescriptor.Priority += descriptor.Priority;
                        }
                    }
                }
            }

            // Highest-priority descriptor first: the loop below narrows lexicographically, so the strongest
            // preference must be applied before weaker ones. (OrderBy is non-mutating — must assign the result.)
            priorities = priorities.OrderByDescending(x => x.Priority).ToList();
            while(priorities.Any())
            {
                var currentSignature = priorities.First();
                var trialBodySlides = currentBodySlides.Where(x => currentSignature.CollectionContainsThisDescriptor(PerWeightDescriptorLookup.GetDescriptorsForWeight(x, npcInfo.NPC.Weight))).ToList();
                if (trialBodySlides.Any())
                {
                    _logger.LogReport("The following BodySlides match descriptor " + currentSignature.ToString() + Environment.NewLine + string.Join(Environment.NewLine, trialBodySlides.Select(x => "-" + x.Label).ToArray()), false, npcInfo);
                    currentBodySlides = trialBodySlides;
                }
                else
                {
                    _logger.LogReport("No BodySlides match the prioritized descriptor " + currentSignature.ToString(), false, npcInfo);
                }
                priorities.Remove(currentSignature);
            }

            bodySlides.Clear();
            bodySlides.AddRange(currentBodySlides);
        }
    }

    /// <summary>
    /// Lightweight pre-check (race-only) for whether any gender-appropriate BodySlide preset could possibly
    /// apply to the NPC, used to skip the full selection when nothing is available.
    /// </summary>
    /// <returns>True if at least one preset passes the allowed/disallowed race filter.</returns>
    public bool CurrentNPCHasAvailablePresets(NPCInfo npcInfo, Settings_OBody oBodySettings)
    {
        List<BodySlideSetting> currentBodySlides = new List<BodySlideSetting>();
        switch (npcInfo.Gender)
        {
            case Gender.Male: currentBodySlides = oBodySettings.BodySlidesMale; break;
            case Gender.Female: currentBodySlides = oBodySettings.BodySlidesFemale; break;
        }

        if (!currentBodySlides.Any())
        {
            return false;
        }

        else
        {
            foreach (var slide in currentBodySlides)
            {
                if ((!slide.AllowedRaces.Any() || slide.AllowedRaces.Contains(npcInfo.BodyShapeRace)) && !slide.DisallowedRaces.Contains(npcInfo.BodyShapeRace))
                {
                    return true;
                }
            }
        }

        _logger.LogReport("No BodySlide presets are available for this NPC.", false, npcInfo);

        return false;
    }
    /// <summary>
    /// Persists the assigned BodySlide as the NPC's consistency record and propagates it to the NPC's link
    /// group (if primary) and unique-NPC tracker (when same-name linking is enabled). No-ops in OBody native
    /// multiple-assignment mode, where the runtime picks at random.
    /// </summary>
    /// <remarks>Mutates <paramref name="npcInfo"/>'s consistency/link-group state and the unique-NPC tracker.</remarks>
    public void RecordBodySlideConsistencyAndLinkedNPCs(List<BodySlideSetting> assignedBodySlides, NPCInfo npcInfo)
    {
        if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodySlide && _patcherState.OBodySettings.OBodySelectionMode == OBodySelectionMode.Native && _patcherState.OBodySettings.OBodyEnableMultipleAssignments)
        {
            return; // don't enable consistency or linkage if giving OBody multiple BodySlides to choose from since OBody will choose at random anyway.
        }

        if (!assignedBodySlides.Any())
        {
            return;
        }

        var assignedBodySlide = assignedBodySlides.First();

        npcInfo.ConsistencyNPCAssignment.BodySlidePreset = assignedBodySlide.Label;

        // assign to linked group if necessary 
        if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Primary)
        {
            npcInfo.AssociatedLinkGroup.AssignedBodySlides = assignedBodySlides;
        }
        // assign to unique NPC list if necessary
        if (_patcherState.GeneralSettings.bLinkNPCsWithSameName)
        {
           _uniqueNPCData.InitializeUnsetUniqueNPCBodySlide(npcInfo, assignedBodySlides);
        }
    }

    /// <summary>Logs the per-weight body-shape descriptors that drove distribution for each chosen BodySlide (or "None").</summary>
    public void GenerateBodySlideDescriptorReport(List<BodySlideSetting> bodySlides, NPCInfo npcInfo)
    {
        foreach(var bodySlide in bodySlides)
        {
            // Log the descriptors that actually drove distribution for this NPC -- the per-weight
            // slot closest to the NPC's weight -- rather than the full union across every slot.
            string descriptorStr = Logger.GetBodyShapeDescriptorString(PerWeightDescriptorLookup.GetDescriptorsForWeight(bodySlide, npcInfo.NPC.Weight));

            string descriptorLogStr = string.Empty;
            if (bodySlides.Count > 1)
            {
                descriptorLogStr += bodySlide.Label + " | ";
            }
            
            descriptorLogStr += "Contained descriptors: ";

            if (!descriptorStr.IsNullOrWhitespace())
            {
                descriptorLogStr += Environment.NewLine + descriptorStr;
            }
            else
            {
                descriptorLogStr += "None";
            }

            _logger.LogReport(descriptorLogStr, false, npcInfo);
        } 
    }
}