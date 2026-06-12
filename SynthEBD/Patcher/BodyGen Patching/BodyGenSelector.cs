using Noggog;

namespace SynthEBD;

/// <summary>
/// Per-NPC BodyGen assignment axis of the patcher. Chooses one or more BodyGen morph templates for an
/// NPC from the gender-appropriate <see cref="BodyGenConfig"/>, by building category combinations from
/// the config's racial template map, filtering candidate morphs by rule (random/unique/race/weight/
/// attribute/descriptor), then resolving Specific assignments, link-group and unique-NPC inheritance,
/// and consistency before selecting by probability weighting.
/// </summary>
public class BodyGenSelector
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly AttributeMatcher _attributeMatcher;
    private readonly UniqueNPCData _uniqueNPCData;
    /// <summary>Injects patcher state, environment, logging, attribute matching, and unique-NPC tracking dependencies.</summary>
    public BodyGenSelector(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, AttributeMatcher attributeMatcher, UniqueNPCData uniqueNPCData)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _attributeMatcher = attributeMatcher;  
        _uniqueNPCData = uniqueNPCData;
    }

    /// <summary>
    /// Main entry point: selects the BodyGen morph(s) for an NPC. Resolves the applicable
    /// <see cref="BodyGenConfig"/> (from the assigned asset pack or the default config for the gender),
    /// builds valid category combinations, then applies (in order) Specific assignments, link-group and
    /// unique-NPC inheritance, and consistency, before choosing via <see cref="ChooseMorphs"/>. Records the
    /// chosen morphs into the run-wide BodyGen tracker and sets consistency-match status flags.
    /// </summary>
    /// <param name="selectionMade">True if at least one morph was assigned.</param>
    /// <param name="assignedPrimaryCombination">The NPC's assigned asset combination, used to pick a linked BodyGen config.</param>
    /// <param name="statusFlags">Outputs consistency-related status flags for the body-shape selector.</param>
    /// <returns>The chosen morph templates (empty/null if none could be assigned).</returns>
    /// <remarks>Mutates the global <c>Patcher.BodyGenTracker</c> and logs.</remarks>
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
                case Gender.Male: currentBodyGenConfig = bodyGenConfigs.Male.FirstOrDefault(x => x.Label == assignedPrimaryCombination.AssetPack.AssociatedBodyGenConfigName); break;
                case Gender.Female: currentBodyGenConfig = bodyGenConfigs.Female.FirstOrDefault(x => x.Label == assignedPrimaryCombination.AssetPack.AssociatedBodyGenConfigName); break;
            }
        }
        if (currentBodyGenConfig == null)
        {
            switch (npcInfo.Gender)
            {
                case Gender.Male: currentBodyGenConfig = bodyGenConfigs.Male.FirstOrDefault(x => x.Label == _patcherState.BodyGenSettings.CurrentMaleConfig); break;
                case Gender.Female: currentBodyGenConfig = bodyGenConfigs.Female.FirstOrDefault(x => x.Label == _patcherState.BodyGenSettings.CurrentFemaleConfig); break;
            }
        }
        if (currentBodyGenConfig == null)
        {
            selectionMade = false;
            _logger.LogReport("No BodyGen configs are available for NPCs of the current gender.", false, npcInfo);
            _logger.CloseReportSubsection(npcInfo);
            return new List<BodyGenConfig.BodyGenTemplate>();
        }

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
        else if (!assignmentsSpecified && _uniqueNPCData.IsValidUnique(npcInfo.NPC, out var npcName))
        {
            if (_uniqueNPCData.TryGetUniqueNPCBodyGenAssignments(npcInfo, out var uniqueBodyGenAssignment, out string uniqueFounderNPC) && uniqueBodyGenAssignment.Any())
            {
                availableTemplatesAll = InitializeMorphList(currentBodyGenConfig.Templates, npcInfo, ValidationIgnore.All, null, currentBodyGenConfig);
                var allCombinations = GetAllCombinations(genderedBodyGenConfigs, npcInfo, ValidationIgnore.All);
                var linkedCombinations = GetLinkedCombination(allCombinations, uniqueBodyGenAssignment);
                if (linkedCombinations != null)
                {
                    availableCombinations = linkedCombinations;
                    _logger.LogReport("Another unique NPC with the same name (" + uniqueFounderNPC + ") was assigned a morph. Using that morph for current NPC.", false, npcInfo);
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

    /// <summary>
    /// Picks the final morphs from the available combinations: prioritizes combinations by their maximum
    /// matched ForceIf count, then selects a combination and one morph per category position by probability
    /// weighting (with attribute modifiers).
    /// </summary>
    /// <returns>The list of chosen morph templates, or null if no combinations are available.</returns>
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

        // Prefer the combinations with the highest ForceIf-match count, then pick ONE combination from that
        // top tier (when nothing ForceIf-matches, every combination shares tier 0, so this is the full set).
        // Previously this looped over EVERY tier and selected from the full availableCombinations set, which
        // ignored the ForceIf priority and stacked morphs from multiple combinations onto the NPC.
        var prioritizedCombinations = availableCombinations.GroupBy(x => x.MaxMatchedForceIfAttributes).OrderByDescending(x => x.Key);
        var topPriorityCombinations = prioritizedCombinations.First();

        var currentCombination = ProbabilityWeighting.SelectByProbability(topPriorityCombinations,
            c => c.ProbabilityWeighting * ProbabilityWeighting.GetProbabilityModifierFactor(
                c.ProbabilityWeightModifiers, npcInfo.NPC, npcInfo.BodyShapeRace,
                c.Templates.FirstOrDefault(g => g.Any())?.First().ParentConfig?.AttributeGroups ?? _patcherState.GeneralSettings.AttributeGroups,
                _attributeMatcher, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, _logger, npcInfo, null));

        foreach (var availableMorphsAtPosition in currentCombination.Templates)
        {
            var candidateMorph = ProbabilityWeighting.SelectByProbability(availableMorphsAtPosition,
                t => t.ProbabilityWeighting * ProbabilityWeighting.GetProbabilityModifierFactor(
                    t.ProbabilityWeightModifiers, npcInfo.NPC, npcInfo.BodyShapeRace,
                    t.ParentConfig?.AttributeGroups ?? _patcherState.GeneralSettings.AttributeGroups,
                    _attributeMatcher, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, _logger, npcInfo, t.Label));
            chosenMorphs.Add(candidateMorph);
        }

        return chosenMorphs;
    }

    /// <summary>
    /// Prunes each combination's per-position template sets down to morphs whose labels appear in the NPC's
    /// Specific assignment, keeping only combinations that still have a morph at every position.
    /// </summary>
    /// <param name="success">True if any combination satisfied the specific assignment.</param>
    /// <returns>Pruned copies of the matching combinations, or the original set when none matched.</returns>
    public HashSet<GroupCombinationObject> FilterBySpecificNPCAssignments (HashSet<GroupCombinationObject> allCombinations, NPCInfo npcInfo, out bool success)
    {
        var output = FilterCombinationsByMorphNames(allCombinations, npcInfo.SpecificNPCAssignment.BodyGenMorphNames, out success);
        if (!success)
        {
            _logger.LogReport("Could not apply specific BodyGen morph assignment to NPC " + npcInfo.LogIDstring + " because no valid combinations contained the specified morphs", true, npcInfo);
        }
        return output;
    }

    /// <summary>
    /// Pure core of <see cref="FilterBySpecificNPCAssignments"/>: for each combination whose every position
    /// contains at least one morph named in <paramref name="morphNames"/>, emits a copy with each position
    /// pruned to only those named morphs. The input combinations are never modified, so callers can safely
    /// reuse them on the no-match fallback path.
    /// </summary>
    /// <param name="success">True if any combination satisfied the name filter.</param>
    /// <returns>The pruned copies, or the original set (untouched) when none matched.</returns>
    public static HashSet<GroupCombinationObject> FilterCombinationsByMorphNames(HashSet<GroupCombinationObject> allCombinations, ICollection<string> morphNames, out bool success)
    {
        HashSet<GroupCombinationObject> output = new HashSet<GroupCombinationObject>();
        success = true;
        foreach (var candidateCombo in allCombinations)
        {
            var newCombo = new GroupCombinationObject(candidateCombo);
            bool newComboIsValid = true;
            for (int i = 0; i < newCombo.Templates.Count; i++)
            {
                newCombo.Templates[i] = newCombo.Templates[i].Where(x => morphNames.Contains(x.Label)).ToHashSet();
                if (!newCombo.Templates[i].Any())
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
            success = false;
            return allCombinations;
        }

        return output;
    }

    /// <summary>
    /// Finds a single combination that can reproduce the morphs assigned to a parent/founder NPC (used for
    /// link groups and unique-NPC replication), matching morph labels position-by-position.
    /// </summary>
    /// <param name="searchMorphs">The morphs to reproduce, in order.</param>
    /// <returns>A one-element set with the pinned combination, or null if none matched.</returns>
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

    /// <summary>
    /// Filters combinations to those matching the NPC's stored consistency morphs. Prefers fully matching
    /// combinations; otherwise falls back to partial matches; otherwise returns the original set. Sets the
    /// MatchesConsistency / ConsistencyMorphIsInvalid status flags accordingly.
    /// </summary>
    /// <param name="updatedStatusFlags">The input flags OR-ed with the consistency outcome.</param>
    /// <returns>The consistency-matched, partial-match, or original combination set.</returns>
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
    /// Filters a BodyGenConfig's template list and records each template's ForceIf match count in npcInfo.ForceIfMatches
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

    /// <summary>
    /// Validates a single morph against the NPC: random/unique/race/weight/allowed-disallowed attribute rules
    /// (tallying ForceIf matches in <c>npcInfo.ForceIfMatches</c>), the morph's own descriptor rules, and the
    /// allowed/disallowed BodyGen descriptors of every assigned asset combination and its subgroups.
    /// <paramref name="ignoredFactors"/> can skip race checks or bypass validation entirely.
    /// </summary>
    /// <returns>True if the morph may be assigned to the NPC.</returns>
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
                _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because its allowed races (" + Logger.GetRaceListLogStrings(candidateMorph.AllowedRaces, _environmentProvider.LinkCache, _patcherState) + ") do not include the current NPC's race", false, npcInfo);
                return false;
            }

            // Disallowed Races
            if (candidateMorph.DisallowedRaces.Contains(npcInfo.BodyShapeRace))
            {
                _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because its disallowed races (" + Logger.GetRaceListLogStrings(candidateMorph.DisallowedRaces, _environmentProvider.LinkCache, _patcherState) + ") include the current NPC's race", false, npcInfo);
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
        npcInfo.ForceIfMatches.Set(candidateMorph, 0);
        _attributeMatcher.MatchNPCtoAttributeList(candidateMorph.AllowedAttributes, npcInfo.NPC, npcInfo.BodyShapeRace, bodyGenConfig.AttributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfWeightedCount, out string _, out string unmatchedLog, out string forceIfLog, null);
        if (hasAttributeRestrictions && !matchesAttributeRestrictions)
        {
            _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because the NPC does not match any of its allowed attributes: " + unmatchedLog, false, npcInfo);
            return false;
        }
        else
        {
            npcInfo.ForceIfMatches.Set(candidateMorph, matchedForceIfWeightedCount);
        }

        if (npcInfo.ForceIfMatches.Get(candidateMorph) > 0)
        {
            _logger.LogReport("Morph " + candidateMorph.Label + " Current NPC matches the following forced attributes: " + forceIfLog, false, npcInfo);
        }

        // Disallowed Attributes
        _attributeMatcher.MatchNPCtoAttributeList(candidateMorph.DisallowedAttributes, npcInfo.NPC, npcInfo.BodyShapeRace, bodyGenConfig.AttributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out hasAttributeRestrictions, out matchesAttributeRestrictions, out int dummy, out string matchLog, out string _, out string _, null);
        if (hasAttributeRestrictions && matchesAttributeRestrictions)
        {
            _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because the NPC matches one of its disallowed attributes: " + matchLog, false, npcInfo);
            return false;
        }

        // Repeat the above checks for the morph's descriptor rules
        foreach (var descriptorLabel in candidateMorph.BodyShapeDescriptors)
        {
            var associatedDescriptor = bodyGenConfig.TemplateDescriptors.Flatten().FirstOrDefault(x => x.ID.MapsTo(descriptorLabel));
            if (associatedDescriptor is not null)
            {
                if (associatedDescriptor.PermitNPC(npcInfo, bodyGenConfig.AttributeGroups, _attributeMatcher, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out string reportStr, out int descriptorForceIfCount))
                {
                    if (descriptorForceIfCount > 0)
                    {
                        npcInfo.ForceIfMatches.Add(candidateMorph, descriptorForceIfCount);
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
                    _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because its descriptors do not match allowed descriptors from assigned Asset Pack " + assignedAssetCombination.AssignmentName + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(assignedAssetCombination.AssetPack.DistributionRules.AllowedBodySlideDescriptors), false, npcInfo);
                    return false;
                }
            }

            if (BodyShapeDescriptor.DescriptorsMatch(assignedAssetCombination.AssetPack.DistributionRules.DisallowedBodySlideDescriptors, candidateMorph.BodyShapeDescriptors, assignedAssetCombination.AssetPack.DistributionRules.DisallowedBodyGenMatchMode, out string matchedDescriptor))
            {
                _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because its descriptor [" + matchedDescriptor + "] is disallowed by assigned Asset Pack " + assignedAssetCombination.AssignmentName, false, npcInfo);
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

        // must run after attribute/descriptor matching above so the gate sees the current NPC's ForceIf match count (B59)
        if (!candidateMorph.AllowRandom && npcInfo.ForceIfMatches.Get(candidateMorph) == 0) // don't need to check for specific assignment because it was evaluated at the top of this method
        {
            _logger.LogReport("Morph " + candidateMorph.Label + " is invalid because it can only be assigned via ForceIf attributes or Specific NPC Assignments", false, npcInfo);
            return false;
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
            if (candidate.Races.Contains(npcInfo.BodyShapeRace))
            {
                foreach (var stringCombination in candidate.Combinations)
                {
                    var comboObject = new GroupCombinationObject(stringCombination, availableTemplates, npcInfo.ForceIfMatches);
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
                var collection = _patcherState.GeneralSettings.RaceGroupings.FirstOrDefault(x => x.Label == raceGrouping);
                if (collection == null) { continue; }
                if (collection.Races.Contains(npcInfo.BodyShapeRace))
                {
                    foreach (var stringCombination in candidate.Combinations)
                    {
                        var comboObject = new GroupCombinationObject(stringCombination, availableTemplates, npcInfo.ForceIfMatches);
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

    /// <summary>
    /// Builds the union of all category combinations across every supplied config's racial template map,
    /// regardless of the NPC's race, de-duplicating by combination membership. Used as the unfiltered pool
    /// for Specific/link-group/unique resolution.
    /// </summary>
    /// <returns>All distinct combination objects across the configs.</returns>
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
                        var newCombination = new GroupCombinationObject(stringCombination, bodyGenConfig.Templates, npcInfo.ForceIfMatches);
                        output.Add(newCombination);
                        addedCombinations.Add(stringCombination.Members);
                    }
                }
            }
        }
        return output;
    }

    /// <summary>
    /// Returns true if any already-added combination contains every member of <paramref name="currentCombination"/>
    /// (order-independent membership test used to de-duplicate combinations).
    /// </summary>
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

    /// <summary>Controls which validity checks <see cref="MorphIsValid"/> skips.</summary>
    public enum ValidationIgnore
    {
        /// <summary>Apply all validity checks.</summary>
        None,
        /// <summary>Skip the allowed/disallowed race checks.</summary>
        Race,
        /// <summary>Bypass validation entirely (treat every morph as valid).</summary>
        All
    }

    /// <summary>
    /// A concrete, NPC-resolvable BodyGen combination: for each category in a config's combination it holds the
    /// set of available templates belonging to that category. Carries probability weighting and the maximum
    /// matched ForceIf count used to prioritize combinations during selection.
    /// </summary>
    public class GroupCombinationObject : IProbabilityWeighted
    {
        /// <summary>
        /// Builds a combination from a config's string-category combination, populating each position with the
        /// available templates in that category. Sets <see cref="InitializedSuccessfully"/> to false if any
        /// category resolves to no templates, and computes <see cref="MaxMatchedForceIfAttributes"/> from the
        /// current NPC's <paramref name="forceIfMatches"/> tally.
        /// </summary>
        public GroupCombinationObject(BodyGenConfig.RacialMapping.BodyGenCombination bodyGenCombination, HashSet<BodyGenConfig.BodyGenTemplate> availableTemplates, ForceIfMatchTally forceIfMatches)
        {
            MaxMatchedForceIfAttributes = 0;
            ProbabilityWeighting = bodyGenCombination.ProbabilityWeighting;
            ProbabilityWeightModifiers = bodyGenCombination.ProbabilityWeightModifiers.Select(AttributeWeightModifier.CloneAsNew).ToList();
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
                    if (forceIfMatches.Get(template) > MaxMatchedForceIfAttributes) { MaxMatchedForceIfAttributes = forceIfMatches.Get(template); }
                }
                Templates.Add(templatesInGroup);
            }
            InitializedSuccessfully = true;
        }

        /// <summary>Deep-copies a combination, cloning the per-position template sets and weight modifiers so the copy can be pruned independently.</summary>
        public GroupCombinationObject(GroupCombinationObject template)
        {
            MaxMatchedForceIfAttributes = template.MaxMatchedForceIfAttributes;
            ProbabilityWeighting = template.ProbabilityWeighting;
            ProbabilityWeightModifiers = template.ProbabilityWeightModifiers.Select(AttributeWeightModifier.CloneAsNew).ToList();
            InitializedSuccessfully = template.InitializedSuccessfully;
            foreach (var setAtPosition in template.Templates)
            {
                Templates.Add(new HashSet<BodyGenConfig.BodyGenTemplate>(setAtPosition));
            }
            Categories = new(template.Categories);
        }

        /// <summary>Highest matched ForceIf count among this combination's templates; used to prioritize selection.</summary>
        public int MaxMatchedForceIfAttributes { get; set; }
        public double ProbabilityWeighting { get; set; }
        public List<AttributeWeightModifier> ProbabilityWeightModifiers { get; set; } = new();
        /// <summary>Candidate templates per category position; one element per category in <see cref="Categories"/>.</summary>
        public List<HashSet<BodyGenConfig.BodyGenTemplate>> Templates { get; set; } = new();
        public bool InitializedSuccessfully { get; set; } // false if one or more of the template sublists contains no templates.
        /// <summary>The combination's category labels, in position order.</summary>
        public List<string> Categories { get; set; }
    }

    /// <summary>Returns true if any BodyGen config exists for the given gender.</summary>
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

    /// <summary>
    /// Persists the assigned morphs as the NPC's consistency record and propagates them to the NPC's link
    /// group (if primary) and unique-NPC tracker (when same-name linking is enabled).
    /// </summary>
    /// <remarks>Mutates <paramref name="npcInfo"/>'s consistency/link-group state and the unique-NPC tracker.</remarks>
    public void RecordBodyGenConsistencyAndLinkedNPCs(List<BodyGenConfig.BodyGenTemplate> assignedMorphs, NPCInfo npcInfo)
    {
        npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames = assignedMorphs.Select(x => x.Label).ToList();

        // assign to linked group if necessary
        if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Primary)
        {
            npcInfo.AssociatedLinkGroup.AssignedMorphs = assignedMorphs;
        }
        // assign to unique NPC list if necessary
        if (_patcherState.GeneralSettings.bLinkNPCsWithSameName)
        {
            _uniqueNPCData.InitializeUnsetUniqueNPCBodyGen(npcInfo, assignedMorphs);
        }
    }

    /// <summary>Logs the body-shape descriptors carried by each chosen morph (or "None") to the NPC's report.</summary>
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