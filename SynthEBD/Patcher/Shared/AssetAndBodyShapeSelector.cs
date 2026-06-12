using Noggog;

namespace SynthEBD;

/// <summary>
/// Coordinates choosing an asset-pack <see cref="SubgroupCombination"/> together with a body shape (BodyGen
/// morphs or BodySlide presets) for an NPC. Because the chosen asset combination can impose body-shape
/// constraints (and vice versa), this class drives the joint selection — including a decision tree that tries
/// to keep an NPC's consistency body shape while finding a compatible combination. Called from the asset
/// patching stage of the pipeline; delegates to <see cref="AssetSelector"/>, <see cref="BodyGenSelector"/>,
/// and <see cref="OBodySelector"/>.
/// </summary>
public class AssetAndBodyShapeSelector
{
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly AssetSelector _assetSelector;
    private readonly BodyGenSelector _bodyGenSelector;
    private readonly OBodySelector _oBodySelector;
    private readonly UniqueNPCData _uniqueNPCData;

    /// <summary>Captures patcher state, logger, and the per-axis selectors used during joint selection.</summary>
    public AssetAndBodyShapeSelector(PatcherState patcherState, Logger logger, AssetSelector assetSelector, BodyGenSelector bodyGenSelector, OBodySelector oBodySelector, UniqueNPCData uniqueNPCData)
    {
        _patcherState = patcherState;
        _logger = logger;
        _assetSelector = assetSelector;
        _bodyGenSelector = bodyGenSelector;
        _oBodySelector = oBodySelector;
        _uniqueNPCData = uniqueNPCData;
    }

    /// <summary>Result of a joint asset + body-shape selection: the chosen asset combination plus whichever body-shape representation applies.</summary>
    public class AssetAndBodyShapeAssignment
    {
        /// <summary>The chosen asset-pack subgroup combination (null if none assigned).</summary>
        public SubgroupCombination Assets { get; set; } = null;
        /// <summary>BodyGen morphs assigned (empty unless in BodyGen mode).</summary>
        public List<BodyGenConfig.BodyGenTemplate> BodyGenMorphs { get; set; } = new();
        /// <summary>BodySlide presets assigned (empty unless in BodySlide/OBody mode).</summary>
        public List<BodySlideSetting> BodySlidePresets { get; set; } = new(); 
    }

    /// <summary>
    /// Assigns a SubgroupCombination to the given NPC
    /// If BodyGen integration is enabled, attempts to assign a morph that complies with the chosen combination's bodygen restrictions.
    /// </summary>
    /// <param name="assetsAssigned">Output: true if a named asset combination was assigned.</param>
    /// <param name="bodyShapeAssigned">true if a BodyGen morph was able to be assigned. false if a morph could not be assigned and must be set independently of the SubgroupCombination</param>
    /// <param name="availableAssetPacks">Asset packs available to the current NPC</param>
    /// <param name="bodyGenConfigs">Available BodyGen configs (used when in BodyGen mode).</param>
    /// <param name="oBodySettings">OBody/BodySlide settings (used when in BodySlide mode).</param>
    /// <param name="npcInfo">NPC info class</param>
    /// <param name="mode">Primary vs. mix-in/replacer assignment mode.</param>
    /// <param name="previousAssignments">Combinations already assigned to this NPC, fed to body-shape selection for conflict avoidance.</param>
    /// <returns>The chosen asset combination and body shape. Side effect: writes the chosen body shape into the NPC's consistency record when one was assigned.</returns>
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

    /// <summary>
    /// Generates an asset combination and a compatible body shape from scratch (when the NPC isn't inheriting
    /// from a link group or same-name unique). Repeatedly draws candidate combinations and runs
    /// <see cref="RunBodyShapeDecisionTree"/> on each until one is accepted or the seed pool is exhausted
    /// (including the consistency-relaxation second pass — see <see cref="TryReplenishSeedsWithoutConsistency"/>).
    /// If the search ends without an accepted pairing, <see cref="ApplyFallbackAssignment"/> applies the best
    /// banked fallback and warns the user.
    /// </summary>
    /// <returns>The chosen combination plus its body-shape morphs/presets.</returns>
    public AssetAndBodyShapeAssignment GenerateCombinationWithBodyShape(HashSet<FlattenedAssetPack> availableAssetPacks, BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings, NPCInfo npcInfo, AssetSelector.AssetPackAssignmentMode mode, List<SubgroupCombination> previousAssignments)
    {
        AssetAndBodyShapeAssignment output = new();
        List<BodyGenConfig.BodyGenTemplate> candidateMorphs = new();
        List<BodySlideSetting> candidatePresets = new();

        AssignmentIteration iterationInfo = new AssignmentIteration();
        bool combinationIsValid = false;

        bool isFirstIteration = true;
        SubgroupCombination firstCombination = null;
        var decisionState = new BodyShapeDecisionState();

        _logger.OpenReportSubsection("CombinationAssignment", npcInfo);
        _logger.LogReport("Assigning an asset combination", false, npcInfo);

        // First filtering pass: remove subgroups or entire asset packs whose distribution rules are incompatible
        // with the current NPC. This pass honors the NPC's consistency assignment; if it leaves the seed pool
        // empty mid-search, TryReplenishSeedsWithoutConsistency runs the second, consistency-ignoring pass.
        var filteredAssetPacks = _assetSelector.FilterValidConfigsForNPC(availableAssetPacks, npcInfo, false, out bool wasFilteredByConsistency, mode, null, null);

        // initialize seeds
        iterationInfo.AvailableSeeds = AssetSelector.GetAllSubgroups(filteredAssetPacks).OrderByDescending(x => npcInfo.ForceIfMatches.Get(x)).ToList();

        bool npcHasBodyShapeConsistency = NpcHasBodyShapeConsistency(npcInfo);

        while (!combinationIsValid)
        {
            if (!iterationInfo.AvailableSeeds.Any() && !TryReplenishSeedsWithoutConsistency(availableAssetPacks, npcInfo, mode, iterationInfo, ref wasFilteredByConsistency))
            {
                break;
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
            if (BodyShapeSelectionIsIrrelevant(npcInfo, bodyGenConfigs, oBodySettings))
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

                combinationIsValid = RunBodyShapeDecisionTree(output, candidateMorphs, candidatePresets, bodyShapeAssigned, bodyShapeStatusFlags, npcHasBodyShapeConsistency, decisionState, npcInfo, bodyGenConfigs, oBodySettings);
            }
        }

        if (!combinationIsValid)
        {
            ApplyFallbackAssignment(output, decisionState, firstCombination, npcInfo);
        }

        _logger.CloseReportSubsection(npcInfo);
        return output;
    }

    /// <summary>
    /// Decision-tree state that persists across candidate combinations within a single
    /// <see cref="GenerateCombinationWithBodyShape"/> call.
    /// </summary>
    private class BodyShapeDecisionState
    {
        /// <summary>Cached result of the unconstrained feasibility probe ("would ANY body shape be valid for this NPC?"). Depends only on the NPC, so it is probed at most once per call.</summary>
        public bool? AnyBodyShapeValidWithoutAssetRestrictions;
        /// <summary>True once a candidate combination was rejected because its asset rules exclude every body shape the NPC could otherwise receive. If no compatible combination is ever found, the user is warned of the asset/body-shape ruleset conflict and body shape is later selected independently.</summary>
        public bool AssetRulesBlockAllBodyShapes;
        /// <summary>The first combination + body-shape pair that was valid except for not matching the NPC's consistency body shape (Branch 3). Used as the fallback when no consistency-matching combination exists. Null until banked.</summary>
        public AssetAndBodyShapeAssignment FirstValidCombinationShapePair;
    }

    /// <summary>True when no body-shape pairing constraints apply to asset selection: body-shape selection is disabled, or no BodyGen configs / BodySlide presets exist for this NPC's gender.</summary>
    private bool BodyShapeSelectionIsIrrelevant(NPCInfo npcInfo, BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings)
    {
        return _patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.None
            || (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodyGen && !BodyGenSelector.BodyGenAvailableForGender(npcInfo.Gender, bodyGenConfigs))
            || (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodySlide && !_oBodySelector.CurrentNPCHasAvailablePresets(npcInfo, oBodySettings));
    }

    /// <summary>True if consistency is enabled and the NPC's consistency record carries a body shape for the active body-selection mode (for BodySlide, multiple-assignment Native mode disqualifies).</summary>
    private bool NpcHasBodyShapeConsistency(NPCInfo npcInfo)
    {
        if (!_patcherState.GeneralSettings.bEnableConsistency)
        {
            return false;
        }
        switch (_patcherState.GeneralSettings.BodySelectionMode)
        {
            case BodyShapeSelectionMode.BodyGen: return npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames != null && npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames.Any();
            case BodyShapeSelectionMode.BodySlide: return npcInfo.ConsistencyNPCAssignment.BodySlidePreset != null && !string.IsNullOrWhiteSpace(npcInfo.ConsistencyNPCAssignment.BodySlidePreset) && !(_patcherState.OBodySettings.OBodySelectionMode == OBodySelectionMode.Native && _patcherState.OBodySettings.OBodyEnableMultipleAssignments);
            default: return false;
        }
    }

    /// <summary>
    /// Second pass of the two-pass consistency relaxation. The initial <c>FilterValidConfigsForNPC</c> call
    /// honors the NPC's consistency assignment, which can narrow the seed pool to nothing; when the pool runs
    /// dry and the first pass had filtered by consistency, refilter ignoring consistency and rebuild the seed
    /// list (ordered by ForceIf match count, like the initial pass).
    /// </summary>
    /// <returns>True if seeds were replenished; false when nothing further can be relaxed and the search must terminate.</returns>
    private bool TryReplenishSeedsWithoutConsistency(HashSet<FlattenedAssetPack> availableAssetPacks, NPCInfo npcInfo, AssetSelector.AssetPackAssignmentMode mode, AssignmentIteration iterationInfo, ref bool wasFilteredByConsistency)
    {
        if (wasFilteredByConsistency)
        {
            _logger.LogReport("Attempting to select a valid non-consistency Combination.", true, npcInfo);
            var filteredAssetPacks = _assetSelector.FilterValidConfigsForNPC(availableAssetPacks, npcInfo, true, out wasFilteredByConsistency, mode, null, null);
            iterationInfo.AvailableSeeds = AssetSelector.GetAllSubgroups(filteredAssetPacks).OrderByDescending(x => npcInfo.ForceIfMatches.Get(x)).ToList();
            return true;
        }
        else // no other filters can be relaxed
        {
            _logger.LogReport("No more asset packs remain to select assets from. Terminating combination selection.", mode == AssetSelector.AssetPackAssignmentMode.Primary, npcInfo);
            return false;
        }
    }

    /// <summary>
    /// Evaluates the body-shape selection result for one candidate combination. Three branches:
    /// (1) no body shape fits this combination — accept anyway if no body shape would fit ANY combination,
    /// otherwise reject and seek a compatible combination; (2) a body shape fits and consistency is satisfied
    /// or inapplicable — accept; (3) a body shape fits but is not the consistency shape — bank the pair as a
    /// fallback and keep searching for a consistency-matching combination.
    /// </summary>
    /// <returns>True when the current combination (already in <paramref name="output"/>.Assets) is accepted, ending the search.</returns>
    private bool RunBodyShapeDecisionTree(AssetAndBodyShapeAssignment output, List<BodyGenConfig.BodyGenTemplate> candidateMorphs, List<BodySlideSetting> candidatePresets, bool bodyShapeAssigned, BodyShapeSelectorStatusFlag bodyShapeStatusFlags, bool npcHasBodyShapeConsistency, BodyShapeDecisionState decisionState, NPCInfo npcInfo, BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings)
    {
        // Branch 1: No body shape could be assigned in conjunction with the current combination
        if (!bodyShapeAssigned)
        {
            return HandleNoCompatibleBodyShape(decisionState, npcInfo, bodyGenConfigs, oBodySettings);
        }

        // Branch 2: A valid body shape was selected based on any of the following criteria:
        // A) there is no consistency body shape for this NPC
        // B) there is a consistency body shape for this NPC and the chosen shape is the consistency shape
        // C) there is a consistency body shape for this NPC but it was INVALID for the given NPC irrespective of the chosen combination's allowed/disallowed body-shape rules
        if (!npcHasBodyShapeConsistency || bodyShapeStatusFlags.HasFlag(BodyShapeSelectorStatusFlag.MatchesConsistency) || bodyShapeStatusFlags.HasFlag(BodyShapeSelectorStatusFlag.ConsistencyMorphIsInvalid))
        {
            AcceptBodyShape(output, candidateMorphs, candidatePresets, npcInfo);
            return true;
        }

        // Branch 3 (the remaining case — a consistency body shape exists but the chosen combination is only
        // compatible with a non-consistency shape): bank the pair and keep searching.
        BankConsistencyMismatchedPair(output.Assets, candidateMorphs, candidatePresets, decisionState, npcInfo);
        return false;
    }

    /// <summary>
    /// Branch 1 of the decision tree: no body shape is compatible with the current combination. Probes (once
    /// per call — the answer depends only on the NPC) whether any body shape would be valid WITHOUT
    /// asset-imposed restrictions. If none would be, the current combination is accepted as-is (no combination
    /// could do better); otherwise the combination is rejected so the search can look for one whose rules
    /// permit a body shape.
    /// </summary>
    /// <returns>True to accept the current combination (with no body shape), false to seek another combination.</returns>
    private bool HandleNoCompatibleBodyShape(BodyShapeDecisionState decisionState, NPCInfo npcInfo, BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings)
    {
        if (decisionState.AnyBodyShapeValidWithoutAssetRestrictions == null)
        {
            _logger.LogReport("Checking if any body shapes would be valid without the restrictions imposed by the current combination.", false, npcInfo);
            bool bodyShapeAssignable = false;
            switch (_patcherState.GeneralSettings.BodySelectionMode)
            {
                case BodyShapeSelectionMode.BodyGen: _bodyGenSelector.SelectMorphs(npcInfo, out bodyShapeAssignable, bodyGenConfigs, null, new List<SubgroupCombination>(), out _); break;
                case BodyShapeSelectionMode.BodySlide: _oBodySelector.SelectBodySlidePresets(npcInfo, out bodyShapeAssignable, oBodySettings, new List<SubgroupCombination>(), out _); break;
            }
            decisionState.AnyBodyShapeValidWithoutAssetRestrictions = bodyShapeAssignable;
        }

        if (!decisionState.AnyBodyShapeValidWithoutAssetRestrictions.Value)
        {
            _logger.LogReport("No body shapes would be assignable even without the restrictions imposed by the current combination. Keeping the current combination.", false, npcInfo);
            return true;
        }
        else
        {
            _logger.LogReport("At least one other body shape would be assignable without the restrictions imposed by the currently selected combination. Attempting to find another combination whose restrictions would be compatible with a body shape.", false, npcInfo);
            decisionState.AssetRulesBlockAllBodyShapes = true; // if no other SubgroupCombination is compatible with any body shape either, the user will be warned of the asset/body-shape ruleset conflict
            return false;
        }
    }

    /// <summary>Branch 2 of the decision tree: copies the selected body shape (morphs or presets, per the active mode) into the output assignment.</summary>
    private void AcceptBodyShape(AssetAndBodyShapeAssignment output, List<BodyGenConfig.BodyGenTemplate> candidateMorphs, List<BodySlideSetting> candidatePresets, NPCInfo npcInfo)
    {
        _logger.LogReport("Current combination is accepted along with the current body shape selection.", false, npcInfo);
        switch (_patcherState.GeneralSettings.BodySelectionMode)
        {
            case BodyShapeSelectionMode.BodyGen: output.BodyGenMorphs.AddRange(candidateMorphs); break;
            case BodyShapeSelectionMode.BodySlide: output.BodySlidePresets = candidatePresets; break;
        }
    }

    /// <summary>Branch 3 of the decision tree: banks the first combination + body-shape pair that was valid except for missing the consistency shape, so it can serve as the fallback if no consistency-matching combination is found.</summary>
    private void BankConsistencyMismatchedPair(SubgroupCombination assets, List<BodyGenConfig.BodyGenTemplate> candidateMorphs, List<BodySlideSetting> candidatePresets, BodyShapeDecisionState decisionState, NPCInfo npcInfo)
    {
        _logger.LogReport("Current combination is valid along with the current body shape selection, but only if the current body shape is not the consistency body shape. Attempting to find a different combination whose restrictions permit the consistency body shape.", false, npcInfo);

        if (decisionState.FirstValidCombinationShapePair == null)
        {
            decisionState.FirstValidCombinationShapePair = new AssetAndBodyShapeAssignment() { Assets = assets };
            switch (_patcherState.GeneralSettings.BodySelectionMode)
            {
                case BodyShapeSelectionMode.BodyGen: decisionState.FirstValidCombinationShapePair.BodyGenMorphs = candidateMorphs; break;
                case BodyShapeSelectionMode.BodySlide: decisionState.FirstValidCombinationShapePair.BodySlidePresets = candidatePresets; break;
            }
        }
    }

    /// <summary>
    /// Applies the best available fallback when the search ended without an accepted pairing: prefer the
    /// banked Branch-3 pair (valid combination + non-consistency body shape; warns that consistency was lost);
    /// otherwise, when asset rules blocked all body shapes, assign the first generated combination and warn
    /// that the body shape will be chosen downstream without regard to its constraints.
    /// </summary>
    private void ApplyFallbackAssignment(AssetAndBodyShapeAssignment output, BodyShapeDecisionState decisionState, SubgroupCombination firstCombination, NPCInfo npcInfo)
    {
        if (decisionState.FirstValidCombinationShapePair != null)
        {
            output.Assets = decisionState.FirstValidCombinationShapePair.Assets;
            _logger.LogMessage("Could not assign an asset combination to " + npcInfo.LogIDstring + " that is compatible with its consistency Body Shape. A valid combination was assigned, but Body Shape assignment was re-randomized.");
            _logger.LogReport("Could not assign an asset combination to " + npcInfo.LogIDstring + " that is compatible with its consistency Body Shape. A valid combination was assigned, but Body Shape assignment was re-randomized.", true, npcInfo);
            _logger.LogReport("Applied Asset Combination: " + output.Assets.Signature, false, npcInfo);
            _assetSelector.GenerateDescriptorLog(output.Assets, npcInfo);
            switch (_patcherState.GeneralSettings.BodySelectionMode)
            {
                case BodyShapeSelectionMode.BodyGen:
                    output.BodyGenMorphs = decisionState.FirstValidCombinationShapePair.BodyGenMorphs;
                    _logger.LogReport("Selected morphs: " + String.Join(", ", output.BodyGenMorphs.Select(x => x.Label)), false, npcInfo);
                    _bodyGenSelector.GenerateBodyGenDescriptorReport(output.BodyGenMorphs, npcInfo);
                    break;
                case BodyShapeSelectionMode.BodySlide:
                    output.BodySlidePresets = decisionState.FirstValidCombinationShapePair.BodySlidePresets;
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
        else if (decisionState.AssetRulesBlockAllBodyShapes)
        {
            _logger.LogMessage("Could not assign an asset combination to " + npcInfo.LogIDstring + " that was compatible with any valid Body Shapes. Assigning a valid asset combination. A Body Shape will be chosen without regard to this combination's constraints.");
            _logger.LogReport("Could not assign an asset combination to " + npcInfo.LogIDstring + " that was compatible with any valid Body Shape. Assigning a valid asset combination. A Body Shape will be chosen without regard to this combination's constraints.", true, npcInfo);
            output.Assets = firstCombination;
        }
    }

    /// <summary>Status flags reported by body-shape selection, describing how the result relates to the NPC's consistency body shape.</summary>
    [Flags]
    public enum BodyShapeSelectorStatusFlag
    {
        NoneValidForNPC = 1, // no morphs could be assigned irrespective of the rule set received from the assigned assetCombination
        MatchesConsistency = 2, // all selected morphs are present in consistency
        ConsistencyMorphIsInvalid = 4 // the consistency morph is no longer valid because its rule set no longer permits this NPC
    }
}