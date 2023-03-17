using Noggog;

namespace SynthEBD;

public class BodyGenSelector
{
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly AttributeMatcher _attributeMatcher;
    public BodyGenSelector(PatcherState patcherState, Logger logger, SynthEBDPaths paths, AttributeMatcher attributeMatcher)
    {
        _patcherState = patcherState;
        _logger = logger;
        _paths = paths;
        _attributeMatcher = attributeMatcher;  
    }

    public List<BodyGenConfig.BodyGenTemplate> SelectMorphs(NPCInfo npcInfo, out bool selectionMade, BodyGenConfigs bodyGenConfigs, SubgroupCombination assignedPrimaryCombination, IEnumerable<SubgroupCombination> assignedAssetCombinations, out AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag statusFlags)
    {
        _logger.OpenReportSubsection("BodyGenSelection", npcInfo);
        _logger.LogReport("Selecting BodyGen morph(s) for the current NPC", false, npcInfo);
        BodyGenConfig currentBodyGenConfig = null;
        var genderedBodyGenConfigs = new HashSet<BodyGenConfig>();
        statusFlags = new AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag();
        switch(npcInfo.Gender)
        {
            case Gender.Male: genderedBodyGenConfigs = bodyGenConfigs.Male; break;
            case Gender.Female: genderedBodyGenConfigs = bodyGenConfigs.Female; break;
        }

        if (assignedPrimaryCombination != null)
        {
            switch (npcInfo.Gender)
            {
                case Gender.Male: currentBodyGenConfig = bodyGenConfigs.Male.Where(x => x.Label == assignedPrimaryCombination.AssetPack.AssociatedBodyGenConfigName).FirstOrDefault(); break;
                case Gender.Female: currentBodyGenConfig = bodyGenConfigs.Female.Where(x => x.Label == assignedPrimaryCombination.AssetPack.AssociatedBodyGenConfigName).FirstOrDefault(); break;
            }
        }
        if (currentBodyGenConfig == null)
        {
            switch (npcInfo.Gender)
            {
                case Gender.Male: currentBodyGenConfig = bodyGenConfigs.Male.Where(x => x.Label == _patcherState.BodyGenSettings.CurrentMaleConfig).FirstOrDefault(); break;
                case Gender.Female: currentBodyGenConfig = bodyGenConfigs.Female.Where(x => x.Label == _patcherState.BodyGenSettings.CurrentFemaleConfig).FirstOrDefault(); break;
            }
        }
        if (currentBodyGenConfig == null)
        {
            selectionMade = false;
            _logger.LogReport("No BodyGen configs are available for NPCs of the current gender.", false, npcInfo);
            _logger.CloseReportSubsection(npcInfo);
            return new List<BodyGenConfig.BodyGenTemplate>();
        }

        AssetAndBodyShapeSelector.ClearStatusFlags(statusFlags);
        List<BodyGenConfig.BodyGenTemplate> chosenMorphs = new List<BodyGenConfig.BodyGenTemplate>();

        var availableTemplatesGlobal = InitializeMorphList(currentBodyGenConfig.Templates, npcInfo, ValidationIgnore.None, assignedAssetCombinations, currentBodyGenConfig);
        var availableCombinations = GetAvailableCombinations(currentBodyGenConfig, npcInfo, availableTemplatesGlobal);

        HashSet<BodyGenConfig.BodyGenTemplate> availableTemplatesAll = new HashSet<BodyGenConfig.BodyGenTemplate>();

        #region Specific NPC Assignments
        bool assignmentsSpecified = false;
        if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.BodyGenMorphNames.Any())
        {
            // first try getting combinations while adhering to the bodygen config's Racial Template Map
            availableCombinations = FilterBySpecificNPCAssignments(availableCombinations, npcInfo, out assignmentsSpecified);
            if (!assignmentsSpecified)
            {
                // if that didn't work, try forming combination objects out of all templates regardless of the Racial Template Map
                var availableTemplatesRaceIgnore = InitializeMorphList(currentBodyGenConfig.Templates, npcInfo, ValidationIgnore.Race, assignedAssetCombinations, currentBodyGenConfig);
                availableCombinations = GetAvailableCombinations(currentBodyGenConfig, npcInfo, availableTemplatesRaceIgnore);
                availableCombinations = FilterBySpecificNPCAssignments(availableCombinations, npcInfo, out assignmentsSpecified);
                if (!assignmentsSpecified)
                {
                    // if that still didn't work, ignore all 
                    availableTemplatesAll = InitializeMorphList(currentBodyGenConfig.Templates, npcInfo, ValidationIgnore.All, null, currentBodyGenConfig);
                    availableCombinations = GetAllCombinations(genderedBodyGenConfigs, npcInfo, ValidationIgnore.All);
                    availableCombinations = FilterBySpecificNPCAssignments(availableCombinations, npcInfo, out assignmentsSpecified);
                    if (!assignmentsSpecified)
                    {
                        _logger.LogReport("No morph combinations could be generated while respecting the Specific Assignments for " + npcInfo.LogIDstring + ". A random morph will be chosen.", true, npcInfo);
                        availableCombinations = GetAvailableCombinations(currentBodyGenConfig, npcInfo, availableTemplatesGlobal); // revert to original
                    }
                }
            }
        }
        #endregion

        #region Linked NPC Group
        if (!assignmentsSpecified && npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary)
        {
            availableTemplatesAll = InitializeMorphList(currentBodyGenConfig.Templates, npcInfo, ValidationIgnore.All, null, currentBodyGenConfig);
            var allCombinations = GetAllCombinations(genderedBodyGenConfigs, npcInfo, ValidationIgnore.All);
            var linkedCombinations = GetLinkedCombination(allCombinations, npcInfo.AssociatedLinkGroup.AssignedMorphs);
            if (linkedCombinations != null)
            {
                availableCombinations = linkedCombinations;
            }
            else
            {
                _logger.LogReport("Could not find any combinations containing the morphs applied to the specified parent NPC.", true, npcInfo);
            }
        }
        #endregion

        #region Unique NPC replicates
        else if (UniqueNPCData.IsValidUnique(npcInfo.NPC, out var npcName))
        {
            var uniqueBodyGenAssignment = (List<BodyGenConfig.BodyGenTemplate>)UniqueNPCData.GetUniqueNPCTrackerData(npcInfo, AssignmentType.BodyGen);
            if (uniqueBodyGenAssignment != null && uniqueBodyGenAssignment.Any())
            {
                availableTemplatesAll = InitializeMorphList(currentBodyGenConfig.Templates, npcInfo, ValidationIgnore.All, null, currentBodyGenConfig);
                var allCombinations = GetAllCombinations(genderedBodyGenConfigs, npcInfo, ValidationIgnore.All);
                var linkedCombinations = GetLinkedCombination(allCombinations, uniqueBodyGenAssignment);
                if (linkedCombinations != null)
                {
                    availableCombinations = linkedCombinations;
                    _logger.LogReport("Another unique NPC with the same name was assigned a morph. Using that morph for current NPC.", false, npcInfo);
                }
            }
        }
        #endregion

        #region Consistency
        if (_patcherState.GeneralSettings.bEnableConsistency && npcInfo.ConsistencyNPCAssignment != null && npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames != null)
        {
            availableCombinations = GetConsistencyCombinations(availableCombinations, npcInfo, statusFlags, out statusFlags);
        }
        #endregion

        chosenMorphs = ChooseMorphs(availableCombinations, npcInfo);

        if (chosenMorphs == null)
        {
            _logger.LogReport("Could not choose any valid morphs for NPC " + npcInfo.LogIDstring, false, npcInfo);
            _logger.CloseReportSubsection(npcInfo);
            selectionMade = false;
            return chosenMorphs;
        }
        else
        {
            _logger.LogReport("Selected morphs: " + String.Join(", ", chosenMorphs.Select(x => x.Label)), false, npcInfo);
            selectionMade = true;
        }

        //store selected morphs
        var chosenMorphNames = chosenMorphs.Select(x => x.Label).ToList();
        Dictionary<string, HashSet<string>> allChosenMorphs = null;
        switch(npcInfo.Gender)
        {
            case Gender.Male: allChosenMorphs = Patcher.BodyGenTracker.AllChosenMorphsMale; break;
            case Gender.Female: allChosenMorphs = Patcher.BodyGenTracker.AllChosenMorphsFemale; break;
        }
        if (!allChosenMorphs.ContainsKey(currentBodyGenConfig.Label))
        {
            allChosenMorphs.Add(currentBodyGenConfig.Label, new HashSet<string>());
        }
        allChosenMorphs[currentBodyGenConfig.Label].UnionWith(chosenMorphNames);

        if (_patcherState.GeneralSettings.bEnableConsistency && npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames != null && !npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames.Except(chosenMorphNames).Any()) // https://stackoverflow.com/questions/407729/determine-if-a-sequence-contains-all-elements-of-another-sequence-using-linq
        {
            statusFlags |= AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag.MatchesConsistency;
        }

        GenerateBodyGenDescriptorReport(chosenMorphs, npcInfo);

        _logger.CloseReportSubsection(npcInfo);
        return chosenMorphs;
    }

    public List<BodyGenConfig.BodyGenTemplate> ChooseMorphs(HashSet<GroupCombinationObject> availableCombinations, NPCInfo npcInfo)
    {
        var chosenMorphs = new List<BodyGenConfig.BodyGenTemplate>();
            
        if (!availableCombinations.Any())
        {
            _logger.LogReport("Could not get a BodyGen combination for Race " + npcInfo.BodyShapeRace.ToString() + " ( NPC " + npcInfo.LogIDstring + ")", false, npcInfo);
            return null;
        }
        else
        {
            _logger.LogReport("Available BodyGen Combinations:", false, npcInfo);
            foreach (var combination in availableCombinations)
            {
                _logger.LogReport("Combination {" + string.Join(", ", combination.Categories) + "}", false, npcInfo);
                for (int i = 0; i < combination.Categories.Count; i++)
                {
                    string logStr = combination.Categories[i] + ": [" + string.Join(", ", combination.Templates[i].Select(x => x.Label)) + "]";
                    _logger.LogReport(logStr, false, npcInfo);
                }
            }
            //_logger.LogReport("Available BodySlides (Force If Attribute Count): " + Environment.NewLine + String.Join(Environment.NewLine, filteredPresets.OrderBy(x => x.MatchedForceIfCount).Select(x => x.Label + " (" + x.MatchedForceIfCount + ")")), false, npcInfo);
        }

        var prioritizedCombinations = availableCombinations.GroupBy(x => x.MaxMatchedForceIfAttributes).OrderByDescending(x => x.Key);

        foreach (var combinationList in prioritizedCombinations)
        {
            var currentCombination = (GroupCombinationObject)ProbabilityWeighting.SelectByProbability(availableCombinations);

            foreach (var availableMorphsAtPosition in currentCombination.Templates)
            {
                var candidateMorph = (BodyGenConfig.BodyGenTemplate)ProbabilityWeighting.SelectByProbability(availableMorphsAtPosition);
                chosenMorphs.Add(candidateMorph);
            }
        }

        return chosenMorphs;
    }

    public HashSet<GroupCombinationObject> FilterBySpecificNPCAssignments (HashSet<GroupCombinationObject> allCombinations, NPCInfo npcInfo, out bool success)
    {
        HashSet<GroupCombinationObject> output = new HashSet<GroupCombinationObject>();
        success = true;
        foreach (var candidateCombo in allCombinations)
        {
            var newCombo = new GroupCombinationObject(candidateCombo);
            bool newComboIsValid = true;
            for (int i = 0; i < candidateCombo.Templates.Count; i++)
            {
                candidateCombo.Templates[i] = candidateCombo.Templates[i].Where(x => npcInfo.SpecificNPCAssignment.BodyGenMorphNames.Contains(x.Label)).ToHashSet();
                if (!candidateCombo.Templates[i].Any())
                {
                    newComboIsValid = false;
                    break;
                }
            }
            if (newComboIsValid)
            {
                output.Add(newCombo);
            }
        }

        if (!output.Any())
        {
            _logger.LogReport("Could not apply specific BodyGen morph assignment to NPC " + npcInfo.LogIDstring + " because no valid combinations contained the specified morphs", true, npcInfo);
            success = false;
            return allCombinations;
        }

        return output;
    }

    public HashSet<GroupCombinationObject> GetLinkedCombination(HashSet<GroupCombinationObject> availableCombinations, List<BodyGenConfig.BodyGenTemplate> searchMorphs)
    {
        HashSet<GroupCombinationObject> output = new HashSet<GroupCombinationObject>();

        foreach (var combination in availableCombinations)
        {
            if (combination.Templates.Count != searchMorphs.Count) { continue; }
            GroupCombinationObject linkedCombination = new GroupCombinationObject(combination);
            List<string> requiredMorphs = searchMorphs.Select(x => x.Label).ToList();
            bool combinationValid = true;
            for (int i = 0; i < requiredMorphs.Count; i++)
            {
                if (combination.Templates[i].Select(x => x.Label).Contains(requiredMorphs[i]))
                {
                    linkedCombination.Templates[i] = new HashSet<BodyGenConfig.BodyGenTemplate>() { combination.Templates[i].First(x => x.Label == requiredMorphs[i]) };
                }
                else
                {
                    combinationValid = false;
                    break;
                }
            }

            if (combinationValid)
            {
                output = new HashSet<GroupCombinationObject>() { linkedCombination };
                break;
            }
        }

        if (output.Any())
        {
            return output;
        }
        else
        {
            return null;
        }
    }

    public HashSet<GroupCombinationObject> GetConsistencyCombinations(HashSet<GroupCombinationObject> availableCombinations, NPCInfo npcInfo, AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag statusFlags, out AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag updatedStatusFlags)
    {
        var consistencyMorphs = npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames;
        updatedStatusFlags = statusFlags;
        if (!consistencyMorphs.Any()) { return availableCombinations; }

        HashSet<GroupCombinationObject> consistencyCombinations = new HashSet<GroupCombinationObject>();
        HashSet<GroupCombinationObject> partialMatches = new HashSet<GroupCombinationObject>();

        foreach (var combination in availableCombinations)
        {
            var filteredcombination = new GroupCombinationObject(combination);
                
            var requiredConsistencyMorphs = new HashSet<string>(consistencyMorphs);

            for (int i = 0; i < filteredcombination.Templates.Count; i++)
            {
                var matchedMorphs = new HashSet<BodyGenConfig.BodyGenTemplate>();
                foreach (var morph in filteredcombination.Templates[i])
                {
                    if (requiredConsistencyMorphs.Contains(morph.Label))
                    {
                        matchedMorphs.Add(morph);
                        requiredConsistencyMorphs.Remove(morph.Label);
                        break; // can only match one morph per index
                    }
                }

                if (matchedMorphs.Any())
                {
                    filteredcombination.Templates[i] = matchedMorphs; // otherwise keep the original template list to use for complementing a partial match
                }
            }

            if (requiredConsistencyMorphs.Count == 0)
            {
                consistencyCombinations.Add(filteredcombination);
            }
            else if (requiredConsistencyMorphs.Count < consistencyMorphs.Count)
            {
                partialMatches.Add(filteredcombination);
            }
        }

        if (consistencyCombinations.Any())
        {
            updatedStatusFlags |= AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag.MatchesConsistency;
            return consistencyCombinations;
        }
        else if (partialMatches.Any())
        {
            _logger.LogReport("NPC " + npcInfo.LogIDstring + "'s consistency morph [" + String.Join(", ", consistencyMorphs) + "] could not be fully matched. Attempting to assign the closest available partial match.", true, npcInfo);
            updatedStatusFlags |= AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag.ConsistencyMorphIsInvalid;
            return partialMatches;
        }
        else
        {
            _logger.LogReport("NPC " + npcInfo.LogIDstring + "'s consistency morph [" + String.Join(", ", consistencyMorphs) + "] could not be matched. Assigning a random morph", true, npcInfo);
            updatedStatusFlags |= AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag.ConsistencyMorphIsInvalid;
            return availableCombinations;
        }
    }

    /// <summary>
    /// Filters a BodyGenConfig's template list and assigns each template's MatchedForceIfCount
    /// </summary>
    /// <param name="allMorphs">All templated contained within a BodyGenConfig</param>
    /// <param name="npcInfo"></param>
    /// <returns></returns>
    public HashSet<BodyGenConfig.BodyGenTemplate> InitializeMorphList(HashSet<BodyGenConfig.BodyGenTemplate> allMorphs, NPCInfo npcInfo, ValidationIgnore ignoredFactors, IEnumerable<SubgroupCombination> assignedAssetCombinations, BodyGenConfig bodyGenConfig)
    {
        HashSet<BodyGenConfig.BodyGenTemplate> outputMorphs = new HashSet<BodyGenConfig.BodyGenTemplate>();
        foreach (var candidateMorph in allMorphs)
        {
            if (MorphIsValid(candidateMorph, npcInfo, ignoredFactors, assignedAssetCombinations, bodyGenConfig))
            {
                outputMorphs.Add(candidateMorph);
            }
        }
        return outputMorphs;
    }

    public bool MorphIsValid(BodyGenConfig.BodyGenTemplate candidateMorph, NPCInfo npcInfo, ValidationIgnore ignoredFactors, IEnumerable<SubgroupCombination> assignedAssetCombinations, BodyGenConfig bodyGenConfig)
    {
        if (ignoredFactors == ValidationIgnore.All)
        {
            _logger.LogReport("Ignoring morph validation.", false, npcInfo);
            return true;
        }

        if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.BodyGenMorphNames.Contains(candidateMorph.Label))
        {
            _logger.LogReport("Morph " + candidateMorph.Label + " is valid because it is specifically assigned by user.", false, npcInfo);
            return true;
        }

        if (!candidateMorph.AllowRandom && candidateMorph.MatchedForceIfCount == 0) // don't need to check for specific assignment because it was evaluated just above
        {
            _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because it can only be assigned via ForceIf attributes or Specific NPC Assignments", false, npcInfo);
            return false;
        }

        // Allow unique NPCs
        if (!candidateMorph.AllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because the current morph is disallowed for unique NPCs", false, npcInfo);
            return false;
        }

        // Allow non-unique NPCs
        if (!candidateMorph.AllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because the current morph is disallowed for non-unique NPCs", false, npcInfo);
            return false;
        }

        if (ignoredFactors != ValidationIgnore.Race)
        {
            // Allowed Races
            if (candidateMorph.AllowedRaces.Any() && !candidateMorph.AllowedRaces.Contains(npcInfo.BodyShapeRace))
            {
                _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because its allowed races do not include the current NPC's race", false, npcInfo);
                return false;
            }

            // Disallowed Races
            if (candidateMorph.DisallowedRaces.Contains(npcInfo.BodyShapeRace))
            {
                _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because its disallowed races include the current NPC's race", false, npcInfo);
                return false;
            }
        }
        // Weight Range
        if (npcInfo.NPC.Weight < candidateMorph.WeightRange.Lower || npcInfo.NPC.Weight > candidateMorph.WeightRange.Upper)
        {
            _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because the current NPC's weight falls outside of the morph's allowed weight range", false, npcInfo);
            return false;
        }

        // Allowed and Forced Attributes
        candidateMorph.MatchedForceIfCount = 0;
        _attributeMatcher.MatchNPCtoAttributeList(candidateMorph.AllowedAttributes, npcInfo.NPC, bodyGenConfig.AttributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfWeightedCount, out string _, out string unmatchedLog, out string forceIfLog, null);
        if (hasAttributeRestrictions && !matchesAttributeRestrictions)
        {
            _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because the NPC does not match any of its allowed attributes: " + unmatchedLog, false, npcInfo);
            return false;
        }
        else
        {
            candidateMorph.MatchedForceIfCount = matchedForceIfWeightedCount;
        }

        if (candidateMorph.MatchedForceIfCount > 0)
        {
            _logger.LogReport("Morph " + candidateMorph.Label + " Current NPC matches the following forced attributes: " + forceIfLog, false, npcInfo);
        }

        // Disallowed Attributes
        _attributeMatcher.MatchNPCtoAttributeList(candidateMorph.DisallowedAttributes, npcInfo.NPC, bodyGenConfig.AttributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out hasAttributeRestrictions, out matchesAttributeRestrictions, out int dummy, out string matchLog, out string _, out string _, null);
        if (hasAttributeRestrictions && matchesAttributeRestrictions)
        {
            _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because the NPC matches one of its disallowed attributes: " + matchLog, false, npcInfo);
            return false;
        }

        // Repeat the above checks for the morph's descriptor rules
        foreach (var descriptorLabel in candidateMorph.BodyShapeDescriptors)
        {
            var associatedDescriptor = bodyGenConfig.TemplateDescriptors.Where(x => x.ID.MapsTo(descriptorLabel)).FirstOrDefault();
            if (associatedDescriptor is not null)
            {
                if (associatedDescriptor.PermitNPC(npcInfo, bodyGenConfig.AttributeGroups, _attributeMatcher, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out string reportStr))
                {
                    if (associatedDescriptor.AssociatedRules.MatchedForceIfCount > 0)
                    {
                        candidateMorph.MatchedForceIfCount += associatedDescriptor.AssociatedRules.MatchedForceIfCount;
                        _logger.LogReport(reportStr, false, npcInfo);
                    }
                }
                else
                {
                    _logger.LogReport("Preset " + candidateMorph.Label + " is invalid because the rules for its descriptor " + reportStr, false, npcInfo);
                    return false;
                }
            }
        }

        foreach (var assignedAssetCombination in assignedAssetCombinations)
        {
            // check whole config rules
            if (assignedAssetCombination.AssetPack.DistributionRules.AllowedBodyGenDescriptors.Any())
            {
                if (!BodyShapeDescriptor.DescriptorsMatch(assignedAssetCombination.AssetPack.DistributionRules.AllowedBodyGenDescriptors, candidateMorph.BodyShapeDescriptors, assignedAssetCombination.AssetPack.DistributionRules.AllowedBodyGenMatchMode, out _))
                {
                    _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because its descriptors do not match allowed descriptors from assigned Asset Pack " + assignedAssetCombination.AssetPackName + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(assignedAssetCombination.AssetPack.DistributionRules.AllowedBodySlideDescriptors), false, npcInfo);
                    return false;
                }
            }

            if (BodyShapeDescriptor.DescriptorsMatch(assignedAssetCombination.AssetPack.DistributionRules.DisallowedBodySlideDescriptors, candidateMorph.BodyShapeDescriptors, assignedAssetCombination.AssetPack.DistributionRules.DisallowedBodyGenMatchMode, out string matchedDescriptor))
            {
                _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because its descriptor [" + matchedDescriptor + "] is disallowed by assigned Asset Pack " + assignedAssetCombination.AssetPackName, false, npcInfo);
                return false;
            }

            // check subgroups
            foreach (var subgroup in assignedAssetCombination.ContainedSubgroups)
            {
                if (subgroup.AllowedBodyGenDescriptors.Any())
                {
                    if (!BodyShapeDescriptor.DescriptorsMatch(subgroup.AllowedBodyGenDescriptors, candidateMorph.BodyShapeDescriptors, subgroup.AllowedBodyGenMatchMode, out _))
                    {
                        _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because its descriptors do not match allowed descriptors from assigned subgroup " + Logger.GetSubgroupIDString(subgroup) + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(subgroup.AllowedBodyGenDescriptors), false, npcInfo);
                        return false;
                    }
                }

                if (BodyShapeDescriptor.DescriptorsMatch(subgroup.DisallowedBodyGenDescriptors, candidateMorph.BodyShapeDescriptors, subgroup.DisallowedBodyGenMatchMode, out matchedDescriptor))
                {
                    _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because its descriptor [" + matchedDescriptor + "] is disallowed by assigned subgroup " + Logger.GetSubgroupIDString(subgroup), false, npcInfo);
                    return false;
                }
            }
        }

        // If the candidateMorph is still valid
        return true;
    }

    /// <summary>
    /// Converts category combinations from BodyGenConfig (string HashSet) into a CombinationObject and initialize's the object's MaxMatchedForceIf count
    /// </summary>
    /// <param name="bodyGenConfig"></param>
    /// <param name="npcInfo"></param>
    /// <param name="availableTemplates">Set of pre-filtered BodyGen morphs that are compatible with the current NPC</param>
    /// <returns></returns>
    public HashSet<GroupCombinationObject> GetAvailableCombinations(BodyGenConfig bodyGenConfig, NPCInfo npcInfo, HashSet<BodyGenConfig.BodyGenTemplate> availableTemplates)
    {
        HashSet<GroupCombinationObject> output = new HashSet<GroupCombinationObject>();

        foreach (var candidate in bodyGenConfig.RacialTemplateGroupMap)
        {
            bool candidateMatched = false;
            // first try to get grouping by Race
            if (FormKeyHashSetComparer.Contains(candidate.Races, npcInfo.BodyShapeRace))
            {
                foreach (var stringCombination in candidate.Combinations)
                {
                    var comboObject = new GroupCombinationObject(stringCombination, availableTemplates);
                    if (comboObject.InitializedSuccessfully)
                    {
                        output.Add(comboObject);
                    }
                }
                candidateMatched = true;
            }

            if (candidateMatched) { continue; }
            // if Race didn't match, check the template's RaceGroupings to see if they include the NPC's race.
            foreach (var raceGrouping in candidate.RaceGroupings)
            {
                var collection = _patcherState.GeneralSettings.RaceGroupings.Where(x => x.Label == raceGrouping).FirstOrDefault();
                if (collection == null) { continue; }
                if (FormKeyHashSetComparer.Contains(collection.Races, npcInfo.BodyShapeRace))
                {
                    foreach (var stringCombination in candidate.Combinations)
                    {
                        var comboObject = new GroupCombinationObject(stringCombination, availableTemplates);
                        if (comboObject.InitializedSuccessfully)
                        {
                            output.Add(comboObject);
                        }
                    }
                }
            }
        }

        return output;
    }

    public static HashSet<GroupCombinationObject> GetAllCombinations(HashSet<BodyGenConfig> bodyGenConfigs, NPCInfo npcInfo, ValidationIgnore ignoreFlags)
    {
        HashSet<GroupCombinationObject> output = new();
        HashSet<List<string>> addedCombinations = new();
            
        foreach (var bodyGenConfig in bodyGenConfigs)
        {
            foreach (var racialMapping in bodyGenConfig.RacialTemplateGroupMap)
            {
                foreach (var stringCombination in racialMapping.Combinations)
                {
                    if (!CollectionContainsCombination(stringCombination.Members, addedCombinations))
                    {
                        var newCombination = new GroupCombinationObject(stringCombination, bodyGenConfig.Templates);
                        output.Add(newCombination);
                        addedCombinations.Add(stringCombination.Members);
                    }
                }
            }
        }
        return output;
    }

    private static bool CollectionContainsCombination(IEnumerable<string> currentCombination, IEnumerable<IEnumerable<string>> addedCombinations)
    {
        foreach (var combination in addedCombinations)
        {
            bool matched = true;
            foreach (string s in currentCombination)
            {
                if (!combination.Contains(s))
                {
                    matched = false;
                    break;
                }
            }
            if (matched == true)
            {
                return true;
            }
        }
        return false;
    }

    public enum ValidationIgnore
    {
        None,
        Race,
        All
    }

    public class GroupCombinationObject : IProbabilityWeighted
    {
        public GroupCombinationObject(BodyGenConfig.RacialMapping.BodyGenCombination bodyGenCombination, HashSet<BodyGenConfig.BodyGenTemplate> availableTemplates)
        {
            MaxMatchedForceIfAttributes = 0;
            ProbabilityWeighting = bodyGenCombination.ProbabilityWeighting;
            Categories = new(bodyGenCombination.Members);

            foreach (var templateGroup in bodyGenCombination.Members)
            {
                HashSet<BodyGenConfig.BodyGenTemplate> templatesInGroup = availableTemplates.Where(x => x.MemberOfTemplateGroups.Contains(templateGroup)).ToHashSet();
                if (!templatesInGroup.Any())
                {
                    InitializedSuccessfully = false;
                    return;
                }

                foreach (var template in templatesInGroup)
                {
                    if (template.MatchedForceIfCount > MaxMatchedForceIfAttributes) { MaxMatchedForceIfAttributes = template.MatchedForceIfCount; }
                }
                Templates.Add(templatesInGroup);
            }
            InitializedSuccessfully = true;
        }

        public GroupCombinationObject(GroupCombinationObject template)
        {
            MaxMatchedForceIfAttributes = template.MaxMatchedForceIfAttributes;
            ProbabilityWeighting = template.ProbabilityWeighting;
            InitializedSuccessfully = template.InitializedSuccessfully;
            foreach (var setAtPosition in template.Templates)
            {
                Templates.Add(new HashSet<BodyGenConfig.BodyGenTemplate>(setAtPosition));
            }
            Categories = new(template.Categories);
        }

        public int MaxMatchedForceIfAttributes { get; set; }
        public double ProbabilityWeighting { get; set; }
        public List<HashSet<BodyGenConfig.BodyGenTemplate>> Templates { get; set; } = new();
        public bool InitializedSuccessfully { get; set; } // false if one or more of the template sublists contains no templates.
        public List<string> Categories { get; set; }
    }

    public static bool BodyGenAvailableForGender(Gender gender, BodyGenConfigs bodyGenConfigs)
    {
        switch (gender)
        {
            case Gender.Male:
                if (bodyGenConfigs.Male.Any())
                {
                    return true;
                }
                else
                {
                    return false;
                }
            case Gender.Female:
                if (bodyGenConfigs.Female.Any())
                {
                    return true;
                }
                else
                {
                    return false;
                }
        }
        return false;
    }

    public void RecordBodyGenConsistencyAndLinkedNPCs(List<BodyGenConfig.BodyGenTemplate> assignedMorphs, NPCInfo npcInfo)
    {
        npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames = assignedMorphs.Select(x => x.Label).ToList();

        // assign to linked group if necessary
        if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Primary)
        {
            npcInfo.AssociatedLinkGroup.AssignedMorphs = assignedMorphs;
        }
        // assign to unique NPC list if necessary
        if (_patcherState.GeneralSettings.bLinkNPCsWithSameName && npcInfo.IsValidLinkedUnique && !Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].AssignedMorphs.Any())
        {
            Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].AssignedMorphs = assignedMorphs;
        }
    }

    public void GenerateBodyGenDescriptorReport(List<BodyGenConfig.BodyGenTemplate> chosenMorphs, NPCInfo npcInfo)
    {
        Dictionary<string, string> descriptorRules = new();

        foreach (var morph in chosenMorphs)
        {
            string descriptorStr = Logger.GetBodyShapeDescriptorString(morph.BodyShapeDescriptors);
            if (!descriptorStr.IsNullOrWhitespace())
            {
                if (descriptorRules.ContainsKey(morph.Label))
                {
                    descriptorRules[morph.Label] += descriptorStr;
                }
                else
                {
                    descriptorRules.Add(morph.Label, descriptorStr);
                }
            }
        }

        string descriptorLogStr = "Contained descriptors: ";

        if (descriptorRules.Any())
        {
            foreach (var entry in descriptorRules)
            {
                descriptorLogStr += Environment.NewLine + entry.Key + ": " + entry.Value;
            }
        }
        else
        {
            descriptorLogStr += "None";
        }

        _logger.LogReport(descriptorLogStr, false, npcInfo);
    }
}