using Noggog;

namespace SynthEBD;

public class AssetAndBodyShapeSelector
{
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly AssetSelector _assetSelector;
    private readonly BodyGenSelector _bodyGenSelector;
    private readonly OBodySelector _oBodySelector;
    private readonly UniqueNPCData _uniqueNPCData;

    public AssetAndBodyShapeSelector(PatcherState patcherState, Logger logger, AssetSelector assetSelector, BodyGenSelector bodyGenSelector, OBodySelector oBodySelector, UniqueNPCData uniqueNPCData)
    {
        _patcherState = patcherState;
        _logger = logger;
        _assetSelector = assetSelector;
        _bodyGenSelector = bodyGenSelector;
        _oBodySelector = oBodySelector;
        _uniqueNPCData = uniqueNPCData;
    }

    public class AssetAndBodyShapeAssignment
    {
        public SubgroupCombination Assets { get; set; } = null;
        public List<BodyGenConfig.BodyGenTemplate> BodyGenMorphs { get; set; } = new();
        public List<BodySlideSetting> BodySlidePresets { get; set; } = new(); 
    }

    /// <summary>
    /// Assigns a SubgroupCombination to the given NPC
    /// If BodyGen integration is enabled, attempts to assign a morph that complies with the chosen combination's bodygen restrictions.
    /// </summary>
    /// <param name="bodyShapeAssigned">true if a BodyGen morph was able to be assigned. false if a morph could not be assigned and must be set independently of the SubgroupCombination</param>
    /// <param name="availableAssetPacks">Asset packs available to the current NPC</param>
    /// <param name="npcInfo">NPC info class</param>
    /// <returns></returns>
    public AssetAndBodyShapeAssignment ChooseCombinationAndBodyShape(out bool assetsAssigned, out bool bodyShapeAssigned, HashSet<FlattenedAssetPack> availableAssetPacks, BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings, NPCInfo npcInfo, AssetSelector.AssetPackAssignmentMode mode, List<SubgroupCombination> previousAssignments)
    {
        AssetAndBodyShapeAssignment assignment = new AssetAndBodyShapeAssignment();
        SubgroupCombination chosenCombination = new SubgroupCombination();
        assetsAssigned = false;
        bodyShapeAssigned = false;

        _logger.OpenReportSubsection("AssetsAndBody", npcInfo);
        _logger.LogReport("Assigning Assets and Body Shape Combination", false, npcInfo);

        bool selectedFromLinkedNPC = false;
        #region Get Assignments from Linked Group
        if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary)
        {
            var linkedCombination = _assetSelector.GetCombinationFromLinkedNPCGroup(npcInfo, AssetSelector.AssetPackAssignmentMode.Primary, availableAssetPacks);
            if (linkedCombination != null)
            {
                selectedFromLinkedNPC = true;
                assignment.Assets = linkedCombination;
                switch (_patcherState.GeneralSettings.BodySelectionMode)
                {
                    case BodyShapeSelectionMode.BodyGen:
                        assignment.BodyGenMorphs = npcInfo.AssociatedLinkGroup.AssignedMorphs;
                        bodyShapeAssigned = assignment.BodyGenMorphs.Any();
                        break;
                    case BodyShapeSelectionMode.BodySlide:
                        assignment.BodySlidePresets = npcInfo.AssociatedLinkGroup.AssignedBodySlides;
                        bodyShapeAssigned = assignment.BodySlidePresets.Any();
                        break;
                    default: break;
                }

                if (bodyShapeAssigned)
                {
                    _logger.LogReport("Selected body shape from NPC link group", false, npcInfo);
                }
            }
        }
        #endregion
        #region Get Assignments from Same-Name Unique NPC
        else if (_patcherState.GeneralSettings.bLinkNPCsWithSameName && npcInfo.IsValidLinkedUnique)
        {
            var linkedCombination = _assetSelector.GetCombinationFromSameNameNPC(npcInfo, mode, availableAssetPacks);
            if (linkedCombination != null)
            {
                selectedFromLinkedNPC = true;
                assignment.Assets = linkedCombination;
                string uniqueFounderNPC = "";
                switch (_patcherState.GeneralSettings.BodySelectionMode)
                {
                    case BodyShapeSelectionMode.BodyGen:
                        if(_uniqueNPCData.TryGetUniqueNPCBodyGenAssignments(npcInfo, out var uniqueLinkedMorphs, out uniqueFounderNPC))
                        {
                            assignment.BodyGenMorphs = uniqueLinkedMorphs;
                            bodyShapeAssigned = assignment.BodyGenMorphs.Any();
                        }
                        break;
                    case BodyShapeSelectionMode.BodySlide:
                        if (_uniqueNPCData.TryGetUniqueNPCBodySlideAssignments(npcInfo, out var uniqueLinkedBodySlides, out uniqueFounderNPC))
                        {
                            assignment.BodySlidePresets = uniqueLinkedBodySlides;
                            bodyShapeAssigned = assignment.BodySlidePresets.Any();
                        }
                        break;
                    default: break;
                }

                if (bodyShapeAssigned)
                {
                    _logger.LogReport("Another unique NPC with the same name (" + uniqueFounderNPC + ") was assigned a body shape. Using that body shape for current NPC.", false, npcInfo);
                }
            }
        }
        #endregion

        if (!selectedFromLinkedNPC)
        {
            _logger.LogReport("Choosing Asset Combination and BodyGen for " + npcInfo.LogIDstring, false, npcInfo);

            assignment = GenerateCombinationWithBodyShape(availableAssetPacks, bodyGenConfigs, oBodySettings, npcInfo, mode, previousAssignments);

            switch (_patcherState.GeneralSettings.BodySelectionMode)
            {
                case BodyShapeSelectionMode.BodyGen: bodyShapeAssigned = assignment.BodyGenMorphs.Any(); break;
                case BodyShapeSelectionMode.BodySlide: bodyShapeAssigned = assignment.BodySlidePresets.Any(); break;
                case BodyShapeSelectionMode.None: break;
            }
        }

        if (assignment.Assets != null && assignment.Assets.AssignmentName != "")
        {
            assetsAssigned = true;
        }
        if (bodyShapeAssigned)
        {
            switch(_patcherState.GeneralSettings.BodySelectionMode)
            {
                case BodyShapeSelectionMode.BodyGen: npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames = assignment.BodyGenMorphs.Select(x => x.Label).ToList(); break;
                case BodyShapeSelectionMode.BodySlide:
                    if (!(_patcherState.OBodySettings.OBodySelectionMode == OBodySelectionMode.Native && _patcherState.OBodySettings.OBodyEnableMultipleAssignments))
                    {
                        npcInfo.ConsistencyNPCAssignment.BodySlidePreset = assignment.BodySlidePresets.First().Label;
                    }
                    break;
            }
        }

        _logger.CloseReportSubsection(npcInfo);
        return assignment;
    }

    public AssetAndBodyShapeAssignment GenerateCombinationWithBodyShape(HashSet<FlattenedAssetPack> availableAssetPacks, BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings, NPCInfo npcInfo, AssetSelector.AssetPackAssignmentMode mode, List<SubgroupCombination> previousAssignments)
    {
        AssetAndBodyShapeAssignment output = new();
        List<BodyGenConfig.BodyGenTemplate> candidateMorphs = new();
        List<BodySlideSetting> candidatePresets = new();
        bool notifyOfPermutationMorphConflict = false;

        HashSet<FlattenedAssetPack> filteredAssetPacks = new HashSet<FlattenedAssetPack>();
        AssignmentIteration iterationInfo = new AssignmentIteration();
        bool combinationIsValid = false;

        bool isFirstIteration = true;
        SubgroupCombination firstCombination = null;
        Tuple<SubgroupCombination, object> firstValidCombinationShapePair = new Tuple<SubgroupCombination, object>(new SubgroupCombination(), new List<string>()); // object can be List<string> (BodyGen) or BodySlideSetting (OBody)
        bool firstValidCombinationShapePairInitialized = false;

        _logger.OpenReportSubsection("CombinationAssignment", npcInfo);
        _logger.LogReport("Assigning an asset combination", false, npcInfo);

        // remove subgroups or entire asset packs whose distribution rules are incompatible with the current NPC
        filteredAssetPacks = _assetSelector.FilterValidConfigsForNPC(availableAssetPacks, npcInfo, false, out bool wasFilteredByConsistency, mode, null, null);

        // initialize seeds
        iterationInfo.AvailableSeeds = AssetSelector.GetAllSubgroups(filteredAssetPacks).OrderByDescending(x => x.ForceIfMatchCount).ToList();

        while (!combinationIsValid)
        {
            if (!iterationInfo.AvailableSeeds.Any())
            {
                if (wasFilteredByConsistency) // if no valid groups when filtering for consistency, try again without filtering for it
                {
                    _logger.LogReport("Attempting to select a valid non-consistency Combination.", true, npcInfo);
                    filteredAssetPacks = _assetSelector.FilterValidConfigsForNPC(availableAssetPacks, npcInfo, true, out wasFilteredByConsistency, mode, null, null);
                    iterationInfo.AvailableSeeds = AssetSelector.GetAllSubgroups(filteredAssetPacks);
                }
                else // no other filters can be relaxed
                {
                    _logger.LogReport("No more asset packs remain to select assets from. Terminating combination selection.", mode == AssetSelector.AssetPackAssignmentMode.Primary, npcInfo);
                    break;
                }
            }

            // get an asset combination
            output.Assets = _assetSelector.GenerateCombination(npcInfo, iterationInfo, mode);

            if (output.Assets == null)
            {
                continue; // keep trying to generate a combination until all potential seed subgroups are depleted
            }

            if (isFirstIteration)
            {
                firstCombination = output.Assets;
                isFirstIteration = false;
            }

            // get a Body Shape assignment
            if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.None || (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodyGen && !BodyGenSelector.BodyGenAvailableForGender(npcInfo.Gender, bodyGenConfigs)) || (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodySlide && !_oBodySelector.CurrentNPCHasAvailablePresets(npcInfo, oBodySettings)))
            {
                combinationIsValid = true;
                _logger.LogReport("Current combination is accepted without body shape selection.", false, npcInfo);
            }
            else
            {
                bool bodyShapeAssigned = false;
                var bodyShapeStatusFlags = new BodyShapeSelectorStatusFlag();
                switch (_patcherState.GeneralSettings.BodySelectionMode)
                {
                    case BodyShapeSelectionMode.BodyGen: candidateMorphs = _bodyGenSelector.SelectMorphs(npcInfo, out bodyShapeAssigned, bodyGenConfigs, output.Assets, previousAssignments.And(output.Assets), out bodyShapeStatusFlags); break;
                    case BodyShapeSelectionMode.BodySlide: candidatePresets = _oBodySelector.SelectBodySlidePresets(npcInfo, out bodyShapeAssigned, oBodySettings, previousAssignments.And(output.Assets), out bodyShapeStatusFlags); break;
                }

                // Decision Tree

                bool npcHasBodyShapeConsistency = false;
                if (_patcherState.GeneralSettings.bEnableConsistency)
                {
                    switch (_patcherState.GeneralSettings.BodySelectionMode)
                    {
                        case BodyShapeSelectionMode.BodyGen: npcHasBodyShapeConsistency = npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames != null && npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames.Any(); break;
                        case BodyShapeSelectionMode.BodySlide: npcHasBodyShapeConsistency = npcInfo.ConsistencyNPCAssignment.BodySlidePreset != null && !string.IsNullOrWhiteSpace(npcInfo.ConsistencyNPCAssignment.BodySlidePreset) && !(_patcherState.OBodySettings.OBodySelectionMode == OBodySelectionMode.Native && _patcherState.OBodySettings.OBodyEnableMultipleAssignments); break;
                    }
                }

                // Branch 1: No body shape could be assigned in conjuction with the current combination
                if (!bodyShapeAssigned)
                {
                    // check if any body shape would be valid for the given NPC without any restrictions from the asset combination
                    _logger.LogReport("Checking if any body shapes would be valid without the restrictions imposed by the current combination.", false, npcInfo);
                    bool bodyShapeAssignable = false;
                    switch (_patcherState.GeneralSettings.BodySelectionMode)
                    {
                        case BodyShapeSelectionMode.BodyGen: candidateMorphs = _bodyGenSelector.SelectMorphs(npcInfo, out bodyShapeAssignable, bodyGenConfigs, null, new List<SubgroupCombination>(), out bodyShapeStatusFlags); break;
                        case BodyShapeSelectionMode.BodySlide: candidatePresets = _oBodySelector.SelectBodySlidePresets(npcInfo, out bodyShapeAssignable, oBodySettings, new List<SubgroupCombination>(), out bodyShapeStatusFlags); break;
                    }

                    // if not, then the curent combination is fine because no other combination would be compatible with any body shapes anyway
                    if (!bodyShapeAssignable)
                    {
                        _logger.LogReport("No body shapes would be assignable even without the restrictions imposed by the current combination. Keeping the current combination.", false, npcInfo);
                        combinationIsValid = true;
                    }
                    else // 
                    {
                        _logger.LogReport("At least one other body shape would be assignable without the restrictions imposed by the currently selected combination. Attempting to find another combination whose restrictions would be compatible with a body shape.", false, npcInfo);
                        notifyOfPermutationMorphConflict = true; // Try picking another combination. if no other SubgroupCombination is compatible with any morphs, the user will be warned that there is an asset/morph ruleset conflict
                    }
                }

                // Branch 2: A valid morph was selected based on any of the following criteria:
                // A) there is no consistency morph for this NPC
                // B) there is a consistency morph for this NPC and the chosen morph is the consistency morph
                // C) there is a consistency morph for this NPC but the consistency morph was INVALID for the given NPC irrespective of the chosen permutation's allowed/disallowed BodyGen rules, 
                else if (!npcHasBodyShapeConsistency || bodyShapeStatusFlags.HasFlag(BodyShapeSelectorStatusFlag.MatchesConsistency) || bodyShapeStatusFlags.HasFlag(BodyShapeSelectorStatusFlag.ConsistencyMorphIsInvalid))
                {
                    _logger.LogReport("Current combination is accepted along with the current body shape selection.", false, npcInfo);
                    switch (_patcherState.GeneralSettings.BodySelectionMode)
                    {
                        case BodyShapeSelectionMode.BodyGen: output.BodyGenMorphs.AddRange(candidateMorphs); break;
                        case BodyShapeSelectionMode.BodySlide: output.BodySlidePresets = candidatePresets; break;
                    }
                    combinationIsValid = true;
                }

                //Branch 3: A consistency morph exists, but the chosen combination is only compatible with a morph that is NOT the consistency morph
                else if (npcHasBodyShapeConsistency && !bodyShapeStatusFlags.HasFlag(BodyShapeSelectorStatusFlag.MatchesConsistency))
                {
                    _logger.LogReport("Current combination is valid along with the current body shape selection, but only if the current body shape is not the consistency body shape. Attempting to find a different combination whose restrictions permit the consistency body shape.", false, npcInfo);

                    if (!firstValidCombinationShapePairInitialized)
                    {
                        switch (_patcherState.GeneralSettings.BodySelectionMode)
                        {
                            case BodyShapeSelectionMode.BodyGen: firstValidCombinationShapePair = new Tuple<SubgroupCombination, object>(output.Assets, candidateMorphs); break;
                            case BodyShapeSelectionMode.BodySlide: firstValidCombinationShapePair = new Tuple<SubgroupCombination, object>(output.Assets, candidatePresets); break;
                        }
                    }
                    firstValidCombinationShapePairInitialized = true;
                }
            }
        }

        if (!combinationIsValid)
        {
            if (firstValidCombinationShapePairInitialized)
            {
                output.Assets = firstValidCombinationShapePair.Item1;
                _logger.LogMessage("Could not assign an asset combination to " + npcInfo.LogIDstring + " that is compatible with its consistency Body Shape. A valid combination was assigned, but Body Shape assignment was re-randomized.");
                _logger.LogReport("Could not assign an asset combination to " + npcInfo.LogIDstring + " that is compatible with its consistency Body Shape. A valid combination was assigned, but Body Shape assignment was re-randomized.", true, npcInfo);
                _logger.LogReport("Applied Asset Combination: " + output.Assets.Signature, false, npcInfo);
                _assetSelector.GenerateDescriptorLog(output.Assets, npcInfo);
                switch (_patcherState.GeneralSettings.BodySelectionMode)
                {
                    case BodyShapeSelectionMode.BodyGen:
                        output.BodyGenMorphs = (List<BodyGenConfig.BodyGenTemplate>)firstValidCombinationShapePair.Item2;
                        _logger.LogReport("Selected morphs: " + String.Join(", ", output.BodyGenMorphs.Select(x => x.Label)), false, npcInfo);
                        _bodyGenSelector.GenerateBodyGenDescriptorReport(output.BodyGenMorphs, npcInfo);
                        break;
                    case BodyShapeSelectionMode.BodySlide:
                        output.BodySlidePresets = (List<BodySlideSetting>)firstValidCombinationShapePair.Item2;
                        if (output.BodySlidePresets.Count == 1)
                        {
                            _logger.LogReport("Chose BodySlide Preset: " + output.BodySlidePresets.First().Label, false, npcInfo);
                        }
                        else
                        {
                            _logger.LogReport("Chose BodySlide Presets: " + String.Join(", ", output.BodySlidePresets.Select(x => x.Label)), false, npcInfo);
                        }
                        _oBodySelector.GenerateBodySlideDescriptorReport(output.BodySlidePresets, npcInfo);
                        break;
                }
            }
            else if (notifyOfPermutationMorphConflict)
            {
                _logger.LogMessage("Could not assign an asset combination to " + npcInfo.LogIDstring + " that was compatible with any valid Body Shapes. Assigning a valid asset combination. A Body Shape will be chosen without regard to this combination's constraints.");
                _logger.LogReport("Could not assign an asset combination to " + npcInfo.LogIDstring + " that was compatible with any valid Body Shape. Assigning a valid asset combination. A Body Shape will be chosen without regard to this combination's constraints.", true, npcInfo);
                output.Assets = firstCombination;
            }
        }

        _logger.CloseReportSubsection(npcInfo);
        return output;
    }

    [Flags]
    public enum BodyShapeSelectorStatusFlag
    {
        NoneValidForNPC = 1, // no morphs could be assigned irrespective of the rule set received from the assigned assetCombination
        MatchesConsistency = 2, // all selected morphs are present in consistency
        ConsistencyMorphIsInvalid = 4 // the consistency morph is no longer valid because its rule set no longer permits this NPC
    }

    public static void ClearStatusFlags(BodyShapeSelectorStatusFlag flags)
    {
        flags = ~BodyShapeSelectorStatusFlag.NoneValidForNPC;
        flags = ~BodyShapeSelectorStatusFlag.MatchesConsistency;
        flags = ~BodyShapeSelectorStatusFlag.ConsistencyMorphIsInvalid;
    }
}