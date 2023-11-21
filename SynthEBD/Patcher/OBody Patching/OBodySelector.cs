using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SynthEBD;

public class OBodySelector
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly AttributeMatcher _attributeMatcher;
    private readonly UniqueNPCData _uniqueNPCData;
    public OBodySelector(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger,AttributeMatcher attributeMatcher, UniqueNPCData uniqueNPCData)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _attributeMatcher = attributeMatcher;   
        _uniqueNPCData = uniqueNPCData;
    }
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

        AssetAndBodyShapeSelector.ClearStatusFlags(statusFlags);

        List<BodySlideSetting> selectedPresets = new();

        #region Specific NPC Assignments
        if (npcInfo.SpecificNPCAssignment != null && !npcInfo.SpecificNPCAssignment.BodySlidePreset.IsNullOrWhitespace())
        {
            var specifiedPreset = availablePresets.Where(x => x.Label == npcInfo.SpecificNPCAssignment.BodySlidePreset).FirstOrDefault();
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
                    _logger.LogReport("Assigned linked BodySlide presets " + String.Join(", ", linkedPresets) + " from primary NPC " + npcInfo.AssociatedLinkGroup.PrimaryNPCFormKey.ToString(), false, npcInfo);
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
                _logger.LogReport("Another unique NPC with the same name (" + uniqueFounderNPC + ") was assigned a BodySlide preset " + selectedPresets + ". Using that BodySlide for current NPC.", false, npcInfo);
            }
        }
        #endregion

        #region Random Selection

        if (selectedPresets == null)
        {
            var filteredPresets = new List<BodySlideSetting>(); // fall back if ForceIfs fail
            var forceIfPresets = new List<BodySlideSetting>();

            foreach (var preset in availablePresets)
            {
                if (PresetIsValid(preset, npcInfo, assignedAssetCombinations, oBodySettings))
                {
                    filteredPresets.Add(preset);
                    if (preset.MatchedForceIfCount > 0)
                    {
                        forceIfPresets.Add(preset);
                    }
                }
            }

            _logger.LogReport("Available BodySlides (Force If Attribute Count): " + Environment.NewLine + String.Join(Environment.NewLine, filteredPresets.OrderBy(x => x.MatchedForceIfCount).Select(x => x.Label + " (" + x.MatchedForceIfCount + ")")), false, npcInfo);

            if (forceIfPresets.Any())
            {
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
                    selectedPresets = new() { (BodySlideSetting)ProbabilityWeighting.SelectByProbability(forceIfPresets) };
                }
            }
            else
            {
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
                    selectedPresets = new() { (BodySlideSetting)ProbabilityWeighting.SelectByProbability(filteredPresets) };
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
                logText = "Chose BodySlide Presests: " + string.Join(", ", selectedPresets.Select(x => x.Label));
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

    public bool PresetIsValid(BodySlideSetting candidatePreset, NPCInfo npcInfo, IEnumerable<SubgroupCombination> assignedAssetCombinations, Settings_OBody oBodySettings)
    {
        if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.BodyGenMorphNames.Contains(candidatePreset.Label))
        {
            _logger.LogReport("Preset " + candidatePreset.Label + " is valid because it is specifically assigned by user.", false, npcInfo);
            return true;
        }

        // Allow unique NPCs
        if (!candidatePreset.AllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            _logger.LogReport("Preset " + candidatePreset.Label + " is invalid because it is disallowed for unique NPCs", false, npcInfo);
            return false;
        }

        // Allow non-unique NPCs
        if (!candidatePreset.AllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            _logger.LogReport("Preset " + candidatePreset.Label + " is invalid because it is disallowed for non-unique NPCs", false, npcInfo);
            return false;
        }

        // Allowed Races
        if (candidatePreset.AllowedRaces.Any() && !candidatePreset.AllowedRaces.Contains(npcInfo.BodyShapeRace))
        {
            _logger.LogReport("Preset " + candidatePreset.Label + " is invalid because its allowed races (" + Logger.GetRaceListLogStrings(candidatePreset.AllowedRaces, _environmentProvider.LinkCache, _patcherState) + ") do not include the current NPC's race", false, npcInfo);
            return false;
        }

        // Disallowed Races
        if (candidatePreset.DisallowedRaces.Contains(npcInfo.BodyShapeRace))
        {
            _logger.LogReport("Preset " + candidatePreset.Label + " is invalid because its disallowed races (" + Logger.GetRaceListLogStrings(candidatePreset.DisallowedRaces, _environmentProvider.LinkCache, _patcherState) + ") include the current NPC's race", false, npcInfo);
            return false;
        }

        // Weight Range
        if (npcInfo.NPC.Weight < candidatePreset.WeightRange.Lower || npcInfo.NPC.Weight > candidatePreset.WeightRange.Upper)
        {
            _logger.LogReport("Preset " + candidatePreset.Label + " is invalid because the current NPC's weight falls outside of the it's allowed weight range", false, npcInfo);
            return false;
        }

        // Allowed and Forced Attributes
        candidatePreset.MatchedForceIfCount = 0;
        _attributeMatcher.MatchNPCtoAttributeList(candidatePreset.AllowedAttributes, npcInfo.NPC, npcInfo.BodyShapeRace, _patcherState.OBodySettings.AttributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfWeightedCount, out string _, out string unmatchedLog, out string forceIfLog, null);
        if (hasAttributeRestrictions && !matchesAttributeRestrictions)
        {
            _logger.LogReport("Preset " + candidatePreset.Label + " is invalid because the NPC does not match any of its allowed attributes: " + unmatchedLog, false, npcInfo);
            return false;
        }
        else
        {
            candidatePreset.MatchedForceIfCount = matchedForceIfWeightedCount;
        }

        if (candidatePreset.MatchedForceIfCount > 0)
        {
            _logger.LogReport("Preset " + candidatePreset.Label + " Current NPC matches the following forced attributes: " + forceIfLog, false, npcInfo);
        }

        // Disallowed Attributes
        _attributeMatcher.MatchNPCtoAttributeList(candidatePreset.DisallowedAttributes, npcInfo.NPC, npcInfo.BodyShapeRace, _patcherState.OBodySettings.AttributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out hasAttributeRestrictions, out matchesAttributeRestrictions, out int dummy, out string matchLog, out string _, out string _, null);
        if (hasAttributeRestrictions && matchesAttributeRestrictions)
        {
            _logger.LogReport("Preset " + candidatePreset.Label + " is invalid because the NPC matches one of its disallowed attributes: " + matchLog, false, npcInfo);
            return false;
        }

        // Repeat the above checks for the preset's descriptor rules
        foreach (var descriptorLabel in candidatePreset.BodyShapeDescriptors)
        {
            var associatedDescriptor = oBodySettings.TemplateDescriptors.Where(x => x.ID.MapsTo(descriptorLabel)).FirstOrDefault();
            if (associatedDescriptor is not null)
            {
                if (associatedDescriptor.PermitNPC(npcInfo, oBodySettings.AttributeGroups, _attributeMatcher, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out string reportStr))
                {
                    if (associatedDescriptor.AssociatedRules.MatchedForceIfCount > 0)
                    {
                        candidatePreset.MatchedForceIfCount += associatedDescriptor.AssociatedRules.MatchedForceIfCount;
                        _logger.LogReport(reportStr, false, npcInfo);
                    }
                }
                else
                {
                    _logger.LogReport("Preset " + candidatePreset.Label + " is invalid because the rules for its descriptor " + reportStr, false, npcInfo);
                    return false;
                }
            }
        }

        foreach (var assignedAssetCombination in assignedAssetCombinations)
        {
            // check whole config rules
            if (assignedAssetCombination.AssetPack.DistributionRules.AllowedBodySlideDescriptors.Any())
            {
                if (!BodyShapeDescriptor.DescriptorsMatch(assignedAssetCombination.AssetPack.DistributionRules.AllowedBodySlideDescriptors, candidatePreset.BodyShapeDescriptors, assignedAssetCombination.AssetPack.DistributionRules.AllowedBodySlideMatchMode, out _))
                {
                    _logger.LogReport("Preset " + candidatePreset.Label + " is invalid because its descriptors do not match allowed descriptors from assigned Asset Pack " + assignedAssetCombination.AssetPackName + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(assignedAssetCombination.AssetPack.DistributionRules.AllowedBodySlideDescriptors), false, npcInfo);
                    return false;
                }
            }

            if (BodyShapeDescriptor.DescriptorsMatch(assignedAssetCombination.AssetPack.DistributionRules.DisallowedBodySlideDescriptors, candidatePreset.BodyShapeDescriptors, assignedAssetCombination.AssetPack.DistributionRules.DisallowedBodySlideMatchMode, out string matchedDescriptor))
            {
                _logger.LogReport("Preset " + candidatePreset.Label + " is invalid because its descriptor [" + matchedDescriptor + "] is disallowed by assigned Asset Pack " + assignedAssetCombination.AssetPackName, false, npcInfo);
                return false;
            }

            // check subgroups
            foreach (var subgroup in assignedAssetCombination.ContainedSubgroups)
            {
                if (subgroup.AllowedBodySlideDescriptors.Any())
                {
                    if (!BodyShapeDescriptor.DescriptorsMatch(subgroup.AllowedBodySlideDescriptors, candidatePreset.BodyShapeDescriptors, subgroup.AllowedBodySlideMatchMode, out _))
                    {
                        _logger.LogReport("Preset " + candidatePreset.Label + " is invalid because its descriptors do not match allowed descriptors from assigned subgroup " + Logger.GetSubgroupIDString(subgroup) + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(subgroup.AllowedBodySlideDescriptors), false, npcInfo);
                        return false;
                    }
                }

                if (BodyShapeDescriptor.DescriptorsMatch(subgroup.DisallowedBodySlideDescriptors, candidatePreset.BodyShapeDescriptors, subgroup.DisallowedBodySlideMatchMode, out matchedDescriptor))
                {
                    _logger.LogReport("Preset " + candidatePreset.Label + " is invalid because its descriptor [" + matchedDescriptor + "] is disallowed by assigned subgroup " + Logger.GetSubgroupIDString(subgroup), false, npcInfo);
                    return false;
                }
            }
        }

        // if the current Preset's forceIf attributes match the current NPC, skip the checks for Distribution Enabled

        if (!candidatePreset.AllowRandom && candidatePreset.MatchedForceIfCount == 0) // don't need to check for specific assignment because it was evaluated just above
        {
            _logger.LogReport("Preset " + candidatePreset.Label + " is invalid because it can only be assigned via ForceIf attributes or Specific NPC Assignments", false, npcInfo);
            return false;
        }

        // If the candidateMorph is still valid
        return true;
    }

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

    public void GenerateBodySlideDescriptorReport(List<BodySlideSetting> bodySlides, NPCInfo npcInfo)
    {
        foreach(var bodySlide in bodySlides)
        {
            string descriptorStr = Logger.GetBodyShapeDescriptorString(bodySlide.BodyShapeDescriptors);

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