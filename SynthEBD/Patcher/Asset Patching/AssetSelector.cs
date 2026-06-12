using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System.Linq;
using static SynthEBD.AssetPack;

namespace SynthEBD;

/// <summary>
/// Core asset-assignment engine. For a given NPC it walks the flattened asset packs, filters subgroups by the NPC's
/// race/attributes/body-shape/consistency rules, honors required/excluded subgroups, ForceIf weighting, linked-NPC and
/// same-name uniqueness, and probability weighting, ultimately producing a <see cref="SubgroupCombination"/> that the
/// <see cref="RecordGenerator"/> turns into record overrides. Invoked from the per-NPC assignment loop in the patcher.
/// </summary>
public class AssetSelector
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly AttributeMatcher _attributeMatcher;
    private readonly UniqueNPCData _uniqueNPCData;

    /// <summary>Resolves dependencies (environment/link cache, patcher state, logger, attribute matcher, unique-NPC tracker).</summary>
    public AssetSelector(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, AttributeMatcher attributeMatcher, UniqueNPCData uniqueNPCData)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _attributeMatcher = attributeMatcher;
        _uniqueNPCData = uniqueNPCData;
    }

    /// <summary>
    /// Which class of asset pack is being assigned. <see cref="Primary"/> is the NPC's main appearance config;
    /// <see cref="MixIn"/> is an additional opt-in config layered on top; <see cref="ReplacerVirtual"/> is a virtual
    /// asset-replacer group derived from an already-chosen primary asset pack.
    /// </summary>
    public enum AssetPackAssignmentMode
    {
        Primary,
        MixIn,
        ReplacerVirtual
    }

    /// <summary>Rebuilds the EasyNPC vanilla-skin EditorID set (each base skin name + "Patched") for the current run.</summary>
    public void Reinitialize()
    {
        _vanillaSkinsEasyNPC = _vanillaSkins.Select(x => x + "Patched").ToHashSet(); // EasyNPC transformation
    }

    /// <summary>
    /// Top-level entry point for assigning an asset-pack combination to a single NPC. Reuses a combination from a linked
    /// NPC group or a same-name unique NPC when applicable; otherwise filters the available packs for the NPC and
    /// iteratively generates a valid <see cref="SubgroupCombination"/>, relaxing the consistency filter if no combination
    /// can be found. For Mix-In mode it may decline assignment by probability. Opens/closes report log subsections.
    /// </summary>
    /// <param name="mode">Primary, MixIn, or ReplacerVirtual.</param>
    /// <param name="availableAssetPacks">Candidate flattened asset packs (for MixIn/Replacer there is exactly one).</param>
    /// <param name="assignedBodyGen">BodyGen morphs already assigned to the NPC, used to filter descriptor-restricted subgroups.</param>
    /// <param name="assignedBodySlides">BodySlide presets already assigned, used to filter descriptor-restricted subgroups.</param>
    /// <param name="mixInDeclined">True if a Mix-In was declined (by probability or specific assignment) rather than assigned.</param>
    /// <returns>The chosen combination, or null if none could be assigned or the Mix-In was declined.</returns>
    public SubgroupCombination AssignAssets(NPCInfo npcInfo, AssetPackAssignmentMode mode, HashSet<FlattenedAssetPack> availableAssetPacks, List<BodyGenConfig.BodyGenTemplate> assignedBodyGen, List<BodySlideSetting> assignedBodySlides, out bool mixInDeclined)
    {
        string subSectionLabel = string.Empty;
        string reportLine = string.Empty;
        if (mode == AssetPackAssignmentMode.Primary)
        {
            subSectionLabel = "Assets";
            reportLine = "Assigning Assets";
        }
        else if (mode == AssetPackAssignmentMode.MixIn)
        {
            subSectionLabel = "MixInAssets";
            reportLine = "Assigning Assets for Mix In";
        }
        else if (mode == AssetPackAssignmentMode.ReplacerVirtual) // this function should never be called on Asset Replacers if there is not an availableAssetPack so first is fine to use
        {
            subSectionLabel = "ReplacerAssets";
            reportLine = "Assigning Replacer Assets for " + availableAssetPacks.First().GroupName;
        }
        _logger.OpenReportSubsection(subSectionLabel, npcInfo);
        _logger.LogReport(reportLine, false, npcInfo);

        SubgroupCombination chosenCombination = null;
        mixInDeclined = false;

        bool selectedFromLinkedNPC = false;
        if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary)
        {
            chosenCombination = GetCombinationFromLinkedNPCGroup(npcInfo, mode, availableAssetPacks);
        }
        else if (_patcherState.GeneralSettings.bLinkNPCsWithSameName && npcInfo.IsValidLinkedUnique)
        {
            chosenCombination = GetCombinationFromSameNameNPC(npcInfo, mode, availableAssetPacks);
        }

        if (chosenCombination == null)
        {
            #region Opt out of Mix-In Asset Pack by probability here
            if (mode == AssetPackAssignmentMode.MixIn && availableAssetPacks.Any())
            {
                var specificMixInAssignment = npcInfo.SpecificNPCAssignment?.MixInAssignments.FirstOrDefault(x => x.AssetPackName == availableAssetPacks.First().GroupName);
                if (specificMixInAssignment == null)
                {
                    var mixInPack = availableAssetPacks.First();
                    var mixInConsistency = npcInfo.ConsistencyNPCAssignment?.MixInAssignments?.FirstOrDefault(x => x.AssetPackName == mixInPack.GroupName);
                    if (mixInConsistency != null)
                    {
                        mixInDeclined = mixInConsistency.DeclinedAssignment;
                    }
                    else
                    {
                        mixInDeclined = SkipMixInByProbability(availableAssetPacks.First(), npcInfo);
                    }
                }
                else if (specificMixInAssignment.DeclinedAssignment)
                {
                    mixInDeclined = true;
                    _logger.LogReport("Mix In " + specificMixInAssignment.AssetPackName + " was declined via Specific NPC Assignment.", false, npcInfo);
                }

                if (mixInDeclined)
                {
                    _logger.CloseReportSubsection(npcInfo);
                    return null;
                }
            }
            #endregion

            _logger.OpenReportSubsection("CombinationAssignment", npcInfo);
            _logger.LogReport("Assigning a new asset combination", false, npcInfo);
            AssignmentIteration iterationInfo = new AssignmentIteration();
            // remove subgroups or entire asset packs whose distribution rules are incompatible with the current NPC
            var filteredAssetPacks = FilterValidConfigsForNPC(availableAssetPacks, npcInfo, false, out bool wasFilteredByConsistency, mode, assignedBodyGen, assignedBodySlides);
            // initialize seeds
            iterationInfo.AvailableSeeds = GetAllSubgroups(filteredAssetPacks).OrderByDescending(x => x.ForceIfMatchCount).ToList();

            bool combinationIsValid = false;

            while (true)
            {
                if (!iterationInfo.AvailableSeeds.Any())
                {
                    if (wasFilteredByConsistency) // if no valid groups when filtering for consistency, try again without filtering for it
                    {
                        _logger.LogReport("Attempting to select a valid non-consistency Combination.", mode == AssetPackAssignmentMode.Primary, npcInfo);
                        filteredAssetPacks = FilterValidConfigsForNPC(availableAssetPacks, npcInfo, true, out wasFilteredByConsistency, mode, assignedBodyGen, assignedBodySlides);
                        iterationInfo.AvailableSeeds = GetAllSubgroups(filteredAssetPacks);
                    }
                    else // no other filters can be relaxed
                    {
                        _logger.LogReport("No more asset packs remain to select assets from. Terminating combination selection.", mode == AssetPackAssignmentMode.Primary && availableAssetPacks.Any(), npcInfo);
                        break;
                    }
                }

                // get an asset combination
                chosenCombination = GenerateCombination(npcInfo, iterationInfo, mode);

                if (chosenCombination == null)
                {
                    continue; // keep trying to generate a combination until all potential seed subgroups are depleted
                }
                else
                {
                    _logger.LogReport("Current combination is accepted.", false, npcInfo);
                    break;
                }
            }
            _logger.CloseReportSubsection(npcInfo);
        }

        _logger.CloseReportSubsection(npcInfo);
        return chosenCombination;
    }

    /// <summary>
    /// Returns the combination assigned to the primary member of the NPC's linked group (for the given mode/asset pack),
    /// or null if none exists. Logs whether the linked combination was compatible with this NPC's specific assignment.
    /// </summary>
    public SubgroupCombination GetCombinationFromLinkedNPCGroup(NPCInfo npcInfo, AssetPackAssignmentMode mode, HashSet<FlattenedAssetPack> availableAssetPacks)
    {
        SubgroupCombination linkedCombination = null;
        switch (mode)
        {
            case AssetPackAssignmentMode.Primary:
                linkedCombination = npcInfo?.AssociatedLinkGroup?.AssignedCombination ?? null;
                break;
            case AssetPackAssignmentMode.MixIn:
                if (npcInfo.AssociatedLinkGroup.MixInAssignments.ContainsKey(availableAssetPacks.First().GroupName))
                {
                    linkedCombination = npcInfo.AssociatedLinkGroup.MixInAssignments[availableAssetPacks.First().GroupName];
                }
                break;
            case AssetPackAssignmentMode.ReplacerVirtual:
                var linkedAssignmentGroup = npcInfo.AssociatedLinkGroup.ReplacerAssignments.FirstOrDefault(x => x.ReplacerName == availableAssetPacks.First().GroupName);
                if (linkedAssignmentGroup != null) { linkedCombination = linkedAssignmentGroup.AssignedReplacerCombination; }
                break;
        }

        if (linkedCombination != null && CombinationAllowedBySpecificNPCAssignment(npcInfo.SpecificNPCAssignment, linkedCombination, mode))
        {
            _logger.LogReport("Selected combination from NPC link group", false, npcInfo);
        }
        else if (linkedCombination != null)
        {
            _logger.LogReport("The linked combination (" + linkedCombination.Signature + ") assigned to the primary Link Group member was incompatible with the Specific Assignments for this NPC. Consider making Specific Assignments only for the primary link group member.", true, npcInfo);
        }
        return linkedCombination;
    }

    /// <summary>
    /// Returns the combination already assigned to another unique NPC sharing this NPC's name (for the given mode/asset
    /// pack), or null. Used so identically-named uniques get matching assets. Logs specific-assignment compatibility.
    /// </summary>
    public SubgroupCombination GetCombinationFromSameNameNPC(NPCInfo npcInfo, AssetPackAssignmentMode mode, HashSet<FlattenedAssetPack> availableAssetPacks)
    {
        SubgroupCombination linkedCombination = null;
        bool uniqueNPCFound = false;
        var uniqueFounderNPC = ""; // placeholder
        switch (mode)
        {
            case AssetPackAssignmentMode.Primary: uniqueNPCFound = _uniqueNPCData.TryGetUniqueNPCPrimaryAssets(npcInfo, out linkedCombination, out uniqueFounderNPC); break;
            case AssetPackAssignmentMode.MixIn:
                uniqueNPCFound = _uniqueNPCData.TryGetUniqueNPCMixInAssets(npcInfo, out var linkedCombinationDict, out uniqueFounderNPC);
                if (linkedCombinationDict.ContainsKey(availableAssetPacks.First().GroupName)) // availableAssetPacks only contains the current Mix-in
                {
                    linkedCombination = linkedCombinationDict[availableAssetPacks.First().GroupName];
                }
                break;
            case AssetPackAssignmentMode.ReplacerVirtual:
                uniqueNPCFound = _uniqueNPCData.TryGetUniqueNPCReplacerAssets(npcInfo, out var linkedReplacerCombinations, out uniqueFounderNPC);
                if (linkedReplacerCombinations != null)
                {
                    var linkedAssignmentGroup = linkedReplacerCombinations.FirstOrDefault(x => x.ReplacerName == availableAssetPacks.First().GroupName); // availableAssetPacks only contains the current replacer
                    if (linkedAssignmentGroup != null) { linkedCombination = linkedAssignmentGroup.AssignedReplacerCombination; }
                }
                break;
        }

        if (uniqueNPCFound && linkedCombination != null && CombinationAllowedBySpecificNPCAssignment(npcInfo.SpecificNPCAssignment, linkedCombination, mode))
        {
            _logger.LogReport("Another unique NPC with the same name (" + uniqueFounderNPC + ")  was assigned a combination. Using that combination for current NPC.", false, npcInfo);
        }
        else if (uniqueNPCFound && linkedCombination != null)
        {
            _logger.LogReport("The linked combination (" + linkedCombination.Signature + ") assigned to another unique NPC with the same name was incompatible with the Specific Assignments for this NPC. Consider making Specific Assignments only for the primary link group member.", true, npcInfo);
        }
        return linkedCombination;
    }

    /// <summary>The weighted-selection weight of a subgroup for this NPC: its base ProbabilityWeighting times the product
    /// of any matching ProbabilityWeightModifier factors. Used for BOTH seed selection and the per-position walk so the
    /// modifier is applied consistently (the seed stage previously ignored it, diluting the modifier's effect).</summary>
    private double GetSubgroupSelectionWeight(FlattenedSubgroup x, NPCInfo npcInfo)
    {
        return x.ProbabilityWeighting * ProbabilityWeighting.GetProbabilityModifierFactor(
            x.ProbabilityWeightModifiers, npcInfo.NPC, npcInfo.AssetsRace,
            x.ParentAssetPack.Source.AttributeGroups, _attributeMatcher,
            _patcherState.GeneralSettings.VerboseModeDetailedAttributes, _logger, npcInfo, x.Id);
    }

    /// <summary>The weighted-selection weight of a whole asset pack for this NPC: its DistributionRules
    /// ProbabilityWeighting times the product of any matching config-level ProbabilityWeightModifier factors. Used when
    /// choosing the seed pack so a config-level modifier is honored there too (it matches how the MixIn path scales pack
    /// probability; the seed-pack selection previously ignored it).</summary>
    private double GetAssetPackSelectionWeight(FlattenedAssetPack pack, NPCInfo npcInfo)
    {
        return pack.DistributionRules.ProbabilityWeighting * ProbabilityWeighting.GetProbabilityModifierFactor(
            pack.DistributionRules.ProbabilityWeightModifiers, npcInfo.NPC, npcInfo.AssetsRace,
            pack.Source.AttributeGroups, _attributeMatcher,
            _patcherState.GeneralSettings.VerboseModeDetailedAttributes, _logger, npcInfo, pack.GroupName);
    }

    /// <summary>
    /// Builds a single candidate combination from the current iteration state: chooses a seed subgroup (preferring the
    /// most matched ForceIf attributes, else weighted-random), then fills each remaining position by probability,
    /// conforming to required/excluded subgroup rules and backtracking when a position has no valid options. Records the
    /// generated signature to avoid re-generating the same combination. Mutates <paramref name="iterationInfo"/> and logs.
    /// </summary>
    /// <returns>A complete combination, or null to signal the caller to retry (e.g. seed removed) or that none is possible.</returns>
    public SubgroupCombination GenerateCombination(NPCInfo npcInfo, AssignmentIteration iterationInfo, AssetPackAssignmentMode mode)
    {
        SubgroupCombination generatedCombination = new SubgroupCombination();
        string generatedSignature = "";
        bool combinationAlreadyTried = false;

        FlattenedSubgroup nextSubgroup;

        _logger.OpenReportSubsection("CombinationGeneration", npcInfo);
        _logger.LogReport("Generating a new combination", false, npcInfo);

        #region Choose New Seed
        if (iterationInfo.ChosenSeed == null)
        {
            if (!iterationInfo.AvailableSeeds.Any())
            {
                _logger.LogReport("No seed subgroups remain. A valid combination cannot be assigned with the given filters.", mode == AssetPackAssignmentMode.Primary, npcInfo);
                _logger.CloseReportSubsectionsToParentOf("CombinationGeneration", npcInfo);
                return null;
            }

            _logger.LogReport(() => "Choosing a new seed subgroup from the following list of available seeds and (matched ForceIf attributes):" + Environment.NewLine + string.Join(Environment.NewLine, iterationInfo.AvailableSeeds.Select(x => (x.ParentAssetPack?.GroupName + "::" ?? string.Empty) + x.Id + ": " + x.Name + " (" + x.ForceIfMatchCount + ")")), false, npcInfo);

            if (iterationInfo.AvailableSeeds.Max(x => x.ForceIfMatchCount) is var matchedForceIfCount && matchedForceIfCount > 0)
            {
                var forceIfFilteredSubgroups = iterationInfo.AvailableSeeds.Where(x =>
                    x.ForceIfMatchCount == matchedForceIfCount);

                iterationInfo.ChooseSeedSubgroup(forceIfFilteredSubgroups, p => GetAssetPackSelectionWeight(p, npcInfo), x => GetSubgroupSelectionWeight(x, npcInfo));
                
                _logger.LogReport(() => "Chose seed subgroup " + iterationInfo.ChosenSeed.GetDetailedID_NameString(false) + " in " + iterationInfo.ChosenAssetPack?.GroupName + " because it had the most matched ForceIf attributes (" + iterationInfo.ChosenSeed.ForceIfMatchCount + ").", false, npcInfo);
            }
            else
            {
                iterationInfo.ChooseSeedSubgroup(iterationInfo.AvailableSeeds, p => GetAssetPackSelectionWeight(p, npcInfo), x => GetSubgroupSelectionWeight(x, npcInfo));
                
                _logger.LogReport(() => "Chose seed subgroup " + iterationInfo.ChosenSeed.GetDetailedID_NameString(false) + " in " + iterationInfo.ChosenAssetPack.GroupName + " at random", false, npcInfo);
            }
            iterationInfo.ChosenAssetPack = iterationInfo.ChosenSeed.ParentAssetPack.ShallowCopy();

            _logger.OpenReportSubsection("Seed-" + iterationInfo.ChosenSeed.Id.Replace('.', '_'), npcInfo);

            GenerateSubgroupPlaceHolders(generatedCombination, iterationInfo.ChosenAssetPack);

            iterationInfo.ChosenAssetPack.Subgroups[iterationInfo.ChosenSeed.TopLevelSubgroupIndex] = new List<FlattenedSubgroup>() { iterationInfo.ChosenSeed }; // filter the seed index so that the seed is the only option

            iterationInfo.RemainingVariantsByIndex = new Dictionary<int, FlattenedAssetPack>(); // tracks the available subgroups as the combination gets built up to enable backtracking if the patcher chooses an invalid combination
            for (int i = 0; i < iterationInfo.ChosenAssetPack.Subgroups.Count; i++)
            {
                iterationInfo.RemainingVariantsByIndex.Add(i, null); // set up placeholders for backtracking
            }
            iterationInfo.RemainingVariantsByIndex[0] = iterationInfo.ChosenAssetPack.ShallowCopy(); // initial state of the chosen asset pack
            if (!ConformRequiredExcludedSubgroups(generatedCombination, iterationInfo.ChosenSeed, iterationInfo.ChosenAssetPack, npcInfo, out var filteredAssetPack))
            {
                _logger.LogReport("Cannot create a combination with the chosen seed subgroup due to conflicting required/excluded subgroup rules. Selecting a different seed.", false, npcInfo);
                _logger.CloseReportSubsectionsToParentOf("CombinationGeneration", npcInfo);
                return RemoveInvalidSeed(iterationInfo.AvailableSeeds, iterationInfo); // exit this function and re-enter from caller to choose a new seed
            }
            else
            {
                iterationInfo.ChosenAssetPack = filteredAssetPack;
            }
        }
        #endregion
        else
        {
            GenerateSubgroupPlaceHolders(generatedCombination, iterationInfo.ChosenAssetPack);
        }

        _logger.LogReport(() => "Available Subgroups:" + Logger.SpreadFlattenedAssetPack(iterationInfo.ChosenAssetPack, 0, false), false, npcInfo);

        for (int i = 0; i < iterationInfo.ChosenAssetPack.Subgroups.Count; i++) // iterate through each position within the combination
        {
            if (generatedCombination.ContainedSubgroups.Count > 0)
            {
                _logger.LogReport(() => "Current Combination: " + String.Join(" , ", generatedCombination.ContainedSubgroups.Where(x => x != null).Select(x => x.Id)) + Environment.NewLine, false, npcInfo);
            }
            var currentPosition = i;
            _logger.LogReport(() => "Available Subgroups:" + Logger.SpreadFlattenedAssetPack(iterationInfo.ChosenAssetPack, currentPosition, true), false, npcInfo);

            #region BackTrack if no options remain
            if (iterationInfo.ChosenAssetPack.Subgroups[i].Count == 0)
            {
                if (i == 0 || (i == 1 && iterationInfo.ChosenSeed.TopLevelSubgroupIndex == 0))
                {
                    _logger.LogReport("Cannot backtrack further with " + iterationInfo.ChosenSeed.Id + " as seed. Selecting a new seed.", false, npcInfo);
                    _logger.CloseReportSubsectionsToParentOf("CombinationGeneration", npcInfo);
                    return RemoveInvalidSeed(iterationInfo.AvailableSeeds, iterationInfo); // exit this function and re-enter from caller to choose a new seed
                }
                else if ((i - 1) == iterationInfo.ChosenSeed.TopLevelSubgroupIndex) // skip over the seed subgroup
                {
                    _logger.LogReport("No subgroups remain at position (" + i + "). Selecting a different subgroup at position " + (i - 2), false, npcInfo);
                    i = AssignmentIteration.BackTrack(iterationInfo, generatedCombination.ContainedSubgroups[i - 2], i, 2);
                }
                else
                {
                    _logger.LogReport("No subgroups remain at position (" + i + "). Selecting a different subgroup at position " + (i - 1), false, npcInfo);
                    i = AssignmentIteration.BackTrack(iterationInfo, generatedCombination.ContainedSubgroups[i - 1], i, 1);
                }
                continue;
            }
            #endregion

            #region Pick next subgroup
            if (iterationInfo.ChosenAssetPack.Subgroups[i].Max(x => x.ForceIfMatchCount) is var matchedForceIfCount && matchedForceIfCount > 0)
            {
                var forceIfFilteredSubgroups = iterationInfo.ChosenAssetPack.Subgroups[i].Where(x =>
                    x.ForceIfMatchCount == matchedForceIfCount);
                nextSubgroup = ProbabilityWeighting.SelectByProbability(forceIfFilteredSubgroups, x => GetSubgroupSelectionWeight(x, npcInfo));
                var chosenSubgroup = nextSubgroup;
                _logger.LogReport(() => "Chose next subgroup: " + chosenSubgroup.GetDetailedID_NameString(true) + " at position " + currentPosition + " because it had the most matched ForceIf Attributes (" + chosenSubgroup.ForceIfMatchCount + ")." + Environment.NewLine, false, npcInfo);
            }
            else
            {
                nextSubgroup = ProbabilityWeighting.SelectByProbability(iterationInfo.ChosenAssetPack.Subgroups[i], x => GetSubgroupSelectionWeight(x, npcInfo));
                var chosenSubgroup = nextSubgroup;
                _logger.LogReport(() => "Chose next subgroup: " + chosenSubgroup.GetDetailedID_NameString(true) + " at position " + currentPosition + " at random." + Environment.NewLine, false, npcInfo);
            }
            #endregion

            bool nextSubgroupCompatible = ConformRequiredExcludedSubgroups(generatedCombination, nextSubgroup, iterationInfo.ChosenAssetPack, npcInfo, out var filteredAssetPack);
            if (nextSubgroupCompatible)
            {
                iterationInfo.ChosenAssetPack = filteredAssetPack;
                generatedCombination.ContainedSubgroups[i] = nextSubgroup;
                if (generatedCombination.ContainedSubgroups.Last() != null) // if this is the last position in the combination, check if the combination has already been processed during a previous iteration of the calling function with stricter filtering
                {
                    generatedSignature = iterationInfo.ChosenAssetPack.GroupName + ":" + String.Join('|', generatedCombination.ContainedSubgroups.Where(x => x.Id != AssetPack.ConfigDistributionRules.SubgroupIDString).Select(x => x.Id));
                    if (iterationInfo.PreviouslyGeneratedCombinations.Contains(generatedSignature))
                    {
                        combinationAlreadyTried = true;
                    }
                }
            }

            // backtrack if no combinations are valid after filtering by required/excluded subgroups
            if (!nextSubgroupCompatible || combinationAlreadyTried)
            {
                if (iterationInfo.ChosenAssetPack == null)
                {
                    _logger.LogReport("No combination could be produced in accordance with the current set of required/excluded subgroup rules", false, npcInfo);
                }
                else if (combinationAlreadyTried)
                {
                    _logger.LogReport("This combination (" + generatedSignature + ") has previously been generated", false, npcInfo);
                }

                _logger.LogReport("Selecting a different subgroup at position " + i + ".", false, npcInfo);
                i = AssignmentIteration.BackTrack(iterationInfo, nextSubgroup, i, 0);
                continue;
            }
            else
            {
                iterationInfo.RemainingVariantsByIndex[i + 1] = iterationInfo.ChosenAssetPack.ShallowCopy(); // store the current state of the current asset pack for backtracking in future iterations if necessary
            }
        }

        iterationInfo.PreviouslyGeneratedCombinations.Add(generatedSignature);
        generatedCombination.AssetPack = iterationInfo.ChosenAssetPack;
        generatedCombination.AssignmentName = iterationInfo.ChosenAssetPack.GroupName;
        generatedCombination.Signature = generatedSignature;

        _logger.LogReport("Successfully generated combination: " + generatedSignature, false, npcInfo);
        foreach(var subgroup in generatedCombination.ContainedSubgroups)
        {
            _logger.LogReport(() => subgroup.ContainedSubgroupNames.First() + ": " + subgroup.GetNestedNameString(true), false, npcInfo);
        }
        GenerateDescriptorLog(generatedCombination, npcInfo);
        _logger.CloseReportSubsectionsToParentOf("CombinationGeneration", npcInfo);
        return generatedCombination;
    }

    /// <summary>Drops the current seed from the available-seeds list and clears it, then returns null so the caller re-enters and picks a new seed.</summary>
    private static SubgroupCombination RemoveInvalidSeed(List<FlattenedSubgroup> seedSubgroups, AssignmentIteration iterationInfo)
    {
        seedSubgroups.Remove(iterationInfo.ChosenSeed);
        iterationInfo.ChosenSeed = null;
        return null;
    }

    /// <summary>
    /// Tests whether <paramref name="targetSubgroup"/> can be added to <paramref name="currentCombination"/> given the
    /// required/excluded-subgroup rules of both the target and the already-assigned subgroups, and (if so) returns a copy
    /// of the asset pack with the remaining per-position options trimmed to satisfy the target's rules. User-forced
    /// (specific assignment) subgroups bypass the rule checks. Does not mutate the input pack on failure.
    /// </summary>
    /// <param name="filteredAssetPack">On success, the asset pack with subgroups trimmed by the target's required/excluded rules.</param>
    /// <returns>True if the target is compatible and a valid trimmed pack was produced; false otherwise.</returns>
    private bool ConformRequiredExcludedSubgroups(SubgroupCombination currentCombination, FlattenedSubgroup targetSubgroup, FlattenedAssetPack chosenAssetPack, NPCInfo npcInfo, out FlattenedAssetPack filteredAssetPack)
    {
        filteredAssetPack = chosenAssetPack;

        List<string> specificAssignmentIDs = new();
        if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.AssetPackName == chosenAssetPack.GroupName)
        {
            specificAssignmentIDs.AddRange(npcInfo.SpecificNPCAssignment.SubgroupIDs);
        }

        // check if incoming subgroup is allowed by all existing subgroups
        foreach (var subgroup in currentCombination.ContainedSubgroups.Where(x => x is not null).ToArray())
        {
            foreach (var requiredSubgroupsAtIndex in subgroup.RequiredSubgroupIDs)
            {
                if (requiredSubgroupsAtIndex.Key == targetSubgroup.TopLevelSubgroupIndex && !(requiredSubgroupsAtIndex.Value.Intersect(targetSubgroup.ContainedSubgroupIDs).Any() || specificAssignmentIDs.Contains(targetSubgroup.Id)))
                {
                    _logger.LogReport("\tSubgroup " + targetSubgroup.Id + " cannot be added because a different subgroup is required at position " + requiredSubgroupsAtIndex.Key + " by the already assigned subgroup " + subgroup.Id + Environment.NewLine, false, npcInfo);
                    return false;
                }
            }

            foreach (var index in subgroup.ExcludedSubgroupIDs)
            {
                if (index.Key == targetSubgroup.TopLevelSubgroupIndex && index.Value.Intersect(targetSubgroup.ContainedSubgroupIDs).Any() && !specificAssignmentIDs.Contains(targetSubgroup.Id))
                {
                    _logger.LogReport("\tSubgroup " + targetSubgroup.Id + " cannot be added because it is excluded at position " + index.Key + " by the already assigned subgroup " + subgroup.Id + Environment.NewLine, false, npcInfo);
                    return false;
                }
            }
        }

        _logger.LogReport("Trimming remaining subgroups within " + chosenAssetPack.GroupName + " by the required/excluded subgroups of " + targetSubgroup.Id + Environment.NewLine, false, npcInfo);

        // create a shallow copy of the subgroup list to avoid modifying the chosenAssetPack unless the result is confirmed to be valid.
        var trialSubgroups = new List<List<FlattenedSubgroup>>();
        for (int i = 0; i < chosenAssetPack.Subgroups.Count; i++)
        {
            trialSubgroups.Add(new List<FlattenedSubgroup>(chosenAssetPack.Subgroups[i]));
        }

        // check excluded subgroups of incoming subgroup
        foreach (var excludedSubgroupsAtIndex in targetSubgroup.ExcludedSubgroupIDs)
        {
            if (currentCombination.ContainedSubgroups[excludedSubgroupsAtIndex.Key] != null) // check currently assigned subgroup
            {
                if (excludedSubgroupsAtIndex.Value.Intersect(currentCombination.ContainedSubgroups[excludedSubgroupsAtIndex.Key].ContainedSubgroupIDs).Any() && !specificAssignmentIDs.Contains(targetSubgroup.Id))
                {
                    _logger.LogReport("\tSubgroup " + targetSubgroup.Id + " cannot be added because it excludes (" + String.Join(',', excludedSubgroupsAtIndex.Value) + ") which is incompatible with the currently assigned subgroup at position " + excludedSubgroupsAtIndex.Key + Environment.NewLine, false, npcInfo);
                    return false;
                }
            }


            // check candidate subgroups to be assigned

            trialSubgroups[excludedSubgroupsAtIndex.Key] = chosenAssetPack.Subgroups[excludedSubgroupsAtIndex.Key].Where(x => !excludedSubgroupsAtIndex.Value.Intersect(x.ContainedSubgroupIDs).Any()  || specificAssignmentIDs.Contains(x.Id)).ToList();
            if (!trialSubgroups[excludedSubgroupsAtIndex.Key].Any())
            {
                _logger.LogReport("\tSubgroup " + targetSubgroup.Id + " cannot be added because it excludes (" + String.Join(',', excludedSubgroupsAtIndex.Value) + ") which eliminates all options at position " + excludedSubgroupsAtIndex.Key + Environment.NewLine, false, npcInfo);
                return false;
            }
        }

        // check required subgroups of incoming subgroup
        foreach (var requiredSubgroupsAtIndex in targetSubgroup.RequiredSubgroupIDs)
        {
            // check currently assigned subgroup
            if (currentCombination.ContainedSubgroups[requiredSubgroupsAtIndex.Key] != null && 
                !(requiredSubgroupsAtIndex.Value.Intersect(currentCombination.ContainedSubgroups[requiredSubgroupsAtIndex.Key].ContainedSubgroupIDs).Any() 
                || specificAssignmentIDs.Contains(currentCombination.ContainedSubgroups[requiredSubgroupsAtIndex.Key].Id))
            ) 
            {
                _logger.LogReport("\tSubgroup " + targetSubgroup.Id + " cannot be added because it requires (" + String.Join('|', requiredSubgroupsAtIndex.Value) + ") which is incompatible with the currently assigned subgroup at position " + requiredSubgroupsAtIndex.Key + Environment.NewLine, false, npcInfo);
                return false;
            }

            // check candidate subgroups to be assigned
            trialSubgroups[requiredSubgroupsAtIndex.Key] = chosenAssetPack.Subgroups[requiredSubgroupsAtIndex.Key].Where(x => requiredSubgroupsAtIndex.Value.Intersect(x.ContainedSubgroupIDs).Any() || specificAssignmentIDs.Contains(x.Id)).ToList();
            if (!trialSubgroups[requiredSubgroupsAtIndex.Key].Any())
            {
                _logger.LogReport("\tSubgroup " + targetSubgroup.Id + " cannot be added because it requires (" + String.Join('|', requiredSubgroupsAtIndex.Value) + ") which eliminates all options at position " + requiredSubgroupsAtIndex.Key + Environment.NewLine, false, npcInfo);
                return false;
            }
        }

        filteredAssetPack.Subgroups = trialSubgroups;
        return true;
    }

    /// <summary>Seeds the combination's <see cref="SubgroupCombination.ContainedSubgroups"/> list with one null placeholder per top-level subgroup position.</summary>
    private static void GenerateSubgroupPlaceHolders(SubgroupCombination generatedCombination, FlattenedAssetPack chosenAssetPack)
    {
        for (int i = 0; i < chosenAssetPack.Subgroups.Count; i++)
        {
            generatedCombination.ContainedSubgroups.Add(null); // set up placeholders for subgroups
        }
    }

    /// <summary>Flattens every subgroup at every position across all the given asset packs into a single list (used to build the seed pool).</summary>
    public static List<FlattenedSubgroup> GetAllSubgroups(HashSet<FlattenedAssetPack> availableAssetPacks)
    {
        List<FlattenedSubgroup> subgroupSet = new List<FlattenedSubgroup>();

        foreach (var ap in availableAssetPacks)
        {
            foreach (var subgroupsAtIndex in ap.Subgroups)
            {
                foreach (var sg in subgroupsAtIndex)
                {
                    subgroupSet.Add(sg);
                }
            }
        }

        return subgroupSet;
    }

    /// <summary>
    /// Filters flattened asset packs to remove subgroups, or entire asset packs, that are incompatible with the selected NPC due to any subgroup's rule set
    /// Returns shallow copied FlattenedAssetPacks; input availableAssetPacks remain unmodified
    /// </summary>
    /// <param name="availableAssetPacks"></param>
    /// <param name="npcInfo"></param>
    /// <returns></returns>
    public HashSet<FlattenedAssetPack> FilterValidConfigsForNPC(HashSet<FlattenedAssetPack> availableAssetPacks, NPCInfo npcInfo, bool ignoreConsistency, out bool wasFilteredByConsistency, AssetPackAssignmentMode mode, List<BodyGenConfig.BodyGenTemplate> assignedBodyGen, List<BodySlideSetting> assignedBodySlides)
    {
        _logger.OpenReportSubsection("ConfigFiltering", npcInfo);
        HashSet<FlattenedAssetPack> assetPacksToBeFiltered = new HashSet<FlattenedAssetPack>(availableAssetPacks); // available asset packs filtered by Specific NPC Assignments and Consistency
        List<FlattenedAssetPack> filteredPacks = new List<FlattenedAssetPack>(); // available asset packs (further) filtered by current NPC's compliance with each subgroup's rule set
        wasFilteredByConsistency = false;
        List<List<FlattenedSubgroup>> forcedAssignments = null; // must be a nested list because if the user forces a non-bottom-level subgroup, then at a given index multiple options will be forced

        #region handle specific NPC assignments
        FlattenedAssetPack forcedAssetPack = null;
        if (npcInfo.SpecificNPCAssignment != null)
        {
            _logger.OpenReportSubsection("SpecificAssignments", npcInfo);
            // check to make sure forced asset pack exists
            switch (mode)
            {
                case AssetPackAssignmentMode.Primary:
                    if (npcInfo.SpecificNPCAssignment.AssetPackName.IsNullOrWhitespace())
                    {
                        break;
                    }
                    forcedAssetPack = assetPacksToBeFiltered.FirstOrDefault(x => x.GroupName == npcInfo.SpecificNPCAssignment.AssetPackName);
                    if (forcedAssetPack != null)
                    {
                        forcedAssetPack = forcedAssetPack.ShallowCopy(); // don't forget to shallow copy or subsequent NPCs will get pruned asset packs
                        forcedAssignments = GetForcedSubgroupsAtIndex(forcedAssetPack, npcInfo.SpecificNPCAssignment.SubgroupIDs, npcInfo);
                        _logger.LogReport("Found Asset Pack set by Specific NPC Assignment: " + forcedAssetPack.GroupName, false, npcInfo);
                    }
                    else
                    {
                        _logger.LogMessage("Specific NPC Assignment for " + npcInfo.LogIDstring + " requests asset pack " + npcInfo.SpecificNPCAssignment.AssetPackName + " which does not exist or is disabled. Choosing a random asset pack.");
                        _logger.LogReport("Specific NPC Assignment for " + npcInfo.LogIDstring + " requests asset pack " + npcInfo.SpecificNPCAssignment.AssetPackName + " which does not exist or is disabled. Choosing a random asset pack.", false, npcInfo);
                    }
                    break;
                case AssetPackAssignmentMode.MixIn:
                    var forcedMixIn = npcInfo.SpecificNPCAssignment.MixInAssignments.FirstOrDefault(x => x.AssetPackName == availableAssetPacks.First().GroupName);
                    if (forcedMixIn != null)
                    {
                        forcedAssetPack = availableAssetPacks.First().ShallowCopy();
                        forcedAssignments = GetForcedSubgroupsAtIndex(forcedAssetPack, forcedMixIn.SubgroupIDs, npcInfo);
                        _logger.LogReport("Found Asset Pack set by Specific NPC Assignment: " + forcedMixIn.AssetPackName, false, npcInfo);
                    }
                    break;
                case AssetPackAssignmentMode.ReplacerVirtual:
                    var forcedReplacerGroup = npcInfo.SpecificNPCAssignment.AssetReplacerAssignments.FirstOrDefault(x => x.ReplacerName == availableAssetPacks.First().ReplacerName && x.AssetPackName == availableAssetPacks.First().GroupName); // Replacers are assigned from a pre-chosen asset pack so there must be exactly one in the set
                    if (forcedReplacerGroup != null)
                    {
                        forcedAssetPack = availableAssetPacks.First().ShallowCopy();
                        forcedAssignments = GetForcedSubgroupsAtIndex(forcedAssetPack, forcedReplacerGroup.SubgroupIDs, npcInfo);
                        _logger.LogReport("Found Asset Pack set by Specific NPC Assignment: " + forcedAssetPack.GroupName, false, npcInfo);
                    }
                    break;
            }

            if (forcedAssignments != null)
            {
                //Prune forced asset pack to only include forced subgroups at their respective indices
                for (int i = 0; i < forcedAssignments.Count; i++)
                {
                    if (forcedAssignments[i].Any())
                    {
                        _logger.LogReport("Specific NPC Assignment at position " + i.ToString() + " is requiring the following Subgroups: " + String.Join(" or ", forcedAssignments[i].Select(x => x.Id)), false, npcInfo);
                        forcedAssetPack.Subgroups[i] = forcedAssignments[i];
                    }
                }

                assetPacksToBeFiltered = new HashSet<FlattenedAssetPack>() { forcedAssetPack };
            }
            _logger.CloseReportSubsectionsTo("ConfigFiltering", npcInfo);
        }
        #endregion

        // evaluate config distribution rules
        _logger.OpenReportSubsection("RulesEvaluation", npcInfo);

        #region handle non-predefined asset packs
        _logger.OpenReportSubsection("ConfigDistributionRules", npcInfo);
        var filteredByMainConfigRules = new List<FlattenedAssetPack>();
        foreach (var ap in assetPacksToBeFiltered)
        {
            _logger.OpenReportSubsection("AssetPack", npcInfo);
            _logger.LogReport("Evaluating distribution rules for asset pack: " + ap.GroupName, false, npcInfo);
            var candidatePack = ap.ShallowCopy();
            if (forcedAssetPack != null && candidatePack.GroupName == forcedAssetPack.GroupName)
            {
                _logger.LogReport("Skipped evaluation of Whole Config Distribution Rules for Asset Pack " + ap.GroupName + " because it is forced by Specific NPC Assignments.", false, npcInfo);
                filteredByMainConfigRules.Add(candidatePack);
            }
            else if (!SubgroupValidForCurrentNPC(candidatePack.DistributionRules, npcInfo, mode, assignedBodyGen, assignedBodySlides, "Config File " + candidatePack.GroupName)) // check distribution rules for whole config
            {
                _logger.LogReport("Asset Pack " + ap.GroupName + " is invalid due to its main distribution rules.", false, npcInfo);
            }
            else
            {
                filteredByMainConfigRules.Add(candidatePack);
            }
            _logger.CloseReportSubsectionsTo("ConfigDistributionRules", npcInfo);
        }

        filteredByMainConfigRules = filteredByMainConfigRules.OrderByDescending(x => x.DistributionRules.ForceIfMatchCount).ToList(); // remove asset packs with less than the max ForceIf attributes
        if (filteredByMainConfigRules.Count > 1 && filteredByMainConfigRules[0].DistributionRules.ForceIfMatchCount > 0)
        {
            for (int i = 1; i < filteredByMainConfigRules.Count; i++)
            {
                if (filteredByMainConfigRules[i].DistributionRules.ForceIfMatchCount < filteredByMainConfigRules[0].DistributionRules.ForceIfMatchCount)
                {
                    _logger.LogReport("Asset Pack " + filteredByMainConfigRules[i].GroupName + " was removed because another Asset Pack has more matched ForceIf attributes for this NPC", false, npcInfo);
                    filteredByMainConfigRules.RemoveAt(i);
                    i--;
                }
            }
        }
        assetPacksToBeFiltered = filteredByMainConfigRules.ToHashSet();
        _logger.CloseReportSubsectionsTo("RulesEvaluation", npcInfo);
        #endregion

        #region Check distribution rules for each subgroup
        _logger.OpenReportSubsection("SubGroupDistributionRules", npcInfo);
        foreach (var ap in assetPacksToBeFiltered)
        {
            _logger.OpenReportSubsection("AssetPack", npcInfo);
            _logger.LogReport("Filtering subgroups within asset pack: " + ap.GroupName, false, npcInfo);
            var candidatePack = ap.ShallowCopy();
            bool isValid = true;
            List<string> subgroupsByPositionLog = new();

            //evaluate subgroup rules - first pass
            for (int i = 0; i < candidatePack.Subgroups.Count; i++)
            {
                for (int j = 0; j < candidatePack.Subgroups[i].Count; j++)
                {
                    bool isSpecificNPCAssignment = forcedAssetPack != null && forcedAssignments[i].Any();
                    if (!isSpecificNPCAssignment && !SubgroupValidForCurrentNPC(candidatePack.Subgroups[i][j], npcInfo, mode, assignedBodyGen, assignedBodySlides, "Subgroup" + " " + candidatePack.Subgroups[i][j].GetDetailedID_NameString(false)))
                    {
                        candidatePack.Subgroups[i].RemoveAt(j);
                        j--;
                    }
                    else
                    {
                        candidatePack.Subgroups[i][j].ParentAssetPack = candidatePack; // explicitly re-link ParentAssetPack - see note above
                    }
                }

                // if all subgroups at a given position are invalid, then the entire asset pack is invalid
                if (candidatePack.Subgroups[i].Count == 0)
                {
                    if (forcedAssetPack != null)
                    {
                        _logger.LogReport("Asset Pack " + ap.GroupName + " is forced for NPC " + npcInfo.LogIDstring + " but no subgroups within " + ap.Subgroups[i][0].Id + ":" + ap.Subgroups[i][0].Name + " are compatible with this NPC. Ignoring subgroup rules at this position.", true, npcInfo);
                        candidatePack.Subgroups[i] = new List<FlattenedSubgroup>(ap.Subgroups[i]); // revert list back to unfiltered version at this position
                    }
                    else
                    {
                        _logger.LogReport("Asset Pack " + ap.GroupName + " is invalid for NPC " + npcInfo.LogIDstring + " because no subgroups within " + ap.Source.Subgroups[i].ID + " (" + ap.Source.Subgroups[i].Name + ") are compatible with this NPC.", false, npcInfo);
                        isValid = false;
                        break;
                    }
                }
                else
                {
                    candidatePack.Subgroups[i] = candidatePack.Subgroups[i].OrderByDescending(x => x.ForceIfMatchCount).ToList();
                    // remove subgroups with less than maximal forceIf counts
                    if (candidatePack.Subgroups[i][0].ForceIfMatchCount > 0)
                    {
                        for (int j = 1; j < candidatePack.Subgroups[i].Count; j++)
                        {
                            if (candidatePack.Subgroups[i][j].ForceIfMatchCount < candidatePack.Subgroups[i][0].ForceIfMatchCount)
                            {
                                _logger.LogReport("Subgroup " + candidatePack.Subgroups[i][j].Id + "(" + candidatePack.Subgroups[i][j].Name + ") was removed because another subgroup in position " + (i + 1).ToString() + " had more matched ForceIf attributes.", false, npcInfo);
                                candidatePack.Subgroups[i].RemoveAt(j);
                                j--;
                            }
                        }
                    }
                    subgroupsByPositionLog.Add(i + " (" + candidatePack.Subgroups[i].First().ContainedSubgroupNames.First() + "): [" + string.Join(", ", candidatePack.Subgroups[i].Select(x => x.Id + " (" + x.Name + ")")) + "]");
                }
            }

            //evaluate subgroup rules- second pass - remove subgroups with no available linked Required Subgroups
            if (!RemoveInvalidLinkedSubgroups(candidatePack, npcInfo))
            {
                isValid = false;
            }

            if (isValid)
            {
                _logger.LogReport("Available Subgroups by index: " + Environment.NewLine + string.Join(Environment.NewLine, subgroupsByPositionLog), false, npcInfo);
                filteredPacks.Add(candidatePack);
            }
            _logger.CloseReportSubsectionsTo("SubGroupDistributionRules", npcInfo);
        }

        #endregion

        _logger.CloseReportSubsectionsTo("ConfigFiltering", npcInfo);

        #region handle consistency 
        if (_patcherState.GeneralSettings.bEnableConsistency && !ignoreConsistency && npcInfo.ConsistencyNPCAssignment != null && filteredPacks.Any()) // (must be last to ensure subordinance to ForceIf attribute count which is determined by evaluating all available subgroups)
        {
            _logger.OpenReportSubsection("Consistency", npcInfo);
            string consistencyAssetPackName = "";
            NPCAssignment.AssetReplacerAssignment consistencyReplacer = null;
            switch (mode)
            {
                case AssetPackAssignmentMode.Primary: consistencyAssetPackName = npcInfo.ConsistencyNPCAssignment.AssetPackName; break;
                case AssetPackAssignmentMode.MixIn:
                    var consistencyMixIn = npcInfo.ConsistencyNPCAssignment.MixInAssignments.FirstOrDefault(x => x.AssetPackName == availableAssetPacks.First().GroupName);
                    if (consistencyMixIn != null)
                    {
                        consistencyAssetPackName = availableAssetPacks.First().GroupName;
                    }
                    break;
                case AssetPackAssignmentMode.ReplacerVirtual:
                    consistencyReplacer = npcInfo.ConsistencyNPCAssignment.AssetReplacerAssignments.FirstOrDefault(x => x.ReplacerName == availableAssetPacks.First().ReplacerName && x.AssetPackName == availableAssetPacks.First().GroupName);
                    if (consistencyReplacer != null) { consistencyAssetPackName = consistencyReplacer.ReplacerName; }
                    break;
            }

            if (!string.IsNullOrWhiteSpace(consistencyAssetPackName))
            {
                // check to make sure consistency asset pack is compatible with the specific NPC assignment
                if (forcedAssetPack != null && forcedAssetPack.GroupName != "" && forcedAssetPack.GroupName != consistencyAssetPackName)
                {
                    _logger.LogReport("Asset Pack defined by forced asset pack (" + npcInfo.SpecificNPCAssignment.AssetPackName + ") supercedes consistency asset pack (" + consistencyAssetPackName + ")", false, npcInfo);
                }
                else
                {
                    // check to make sure consistency asset pack exists
                    var consistencyAssetPack = filteredPacks.FirstOrDefault(x => x.GroupName == consistencyAssetPackName);
                    if (consistencyAssetPack == null)
                    {
                        _logger.LogReport("The asset pack specified in the consistency file (" + npcInfo.ConsistencyNPCAssignment.AssetPackName + ") is not available.", true, npcInfo);
                    }
                    else
                    {
                        _logger.LogReport("Selecting consistency Asset Pack (" + consistencyAssetPackName + ").", false, npcInfo);

                        // check each subgroup against specific npc assignment
                        List<string> consistencySubgroupIDs = null;
                        switch (mode)
                        {
                            case AssetPackAssignmentMode.Primary: consistencySubgroupIDs = npcInfo.ConsistencyNPCAssignment.SubgroupIDs; break;
                            case AssetPackAssignmentMode.MixIn:
                                var consistencyMixIn = npcInfo.ConsistencyNPCAssignment.MixInAssignments.FirstOrDefault(x => x.AssetPackName == availableAssetPacks.First().GroupName);
                                if (consistencyMixIn != null) { consistencySubgroupIDs = consistencyMixIn.SubgroupIDs; }
                                break;
                            case AssetPackAssignmentMode.ReplacerVirtual: consistencySubgroupIDs = consistencyReplacer.SubgroupIDs; break;
                        }

                        if (forcedAssignments != null && consistencySubgroupIDs != null && forcedAssignments.Any() && forcedAssignments.Count != consistencySubgroupIDs.Count)
                        {
                            _logger.LogReport("Cannot assign consistency subgroups because the number of consistency subgroups does not equal the number of auto-generated forced assignments. Please report this problem.", true, npcInfo);
                        }
                        else if (consistencySubgroupIDs != null && consistencySubgroupIDs.Count != consistencyAssetPack.Subgroups.Count)
                        {
                            _logger.LogReport("Cannot assign consistency subgroups because the number of subgroups recorded in the consistency doesn't match the number of top-level subgroups in the consistency asset pack. This may be because the config file was edited after last running the patcher.", true, npcInfo);
                        }
                        else
                        {
                            for (int i = 0; i < consistencySubgroupIDs.Count; i++)
                            {
                                // make sure consistency subgroup doesn't conflict with user-forced subgroup if one exists
                                if (forcedAssignments != null && forcedAssignments[i].Any())
                                {
                                    if (forcedAssignments[i].Select(x => x.Id).Contains(consistencySubgroupIDs[i]))
                                    {
                                        consistencyAssetPack.Subgroups[i] = new List<FlattenedSubgroup>() { forcedAssignments[i].First(x => x.Id == consistencySubgroupIDs[i]) }; // guaranteed to have at least one subgroup or else the upstream if would fail, so use First instead of Where
                                        _logger.LogReport("Consistency subgroup " + consistencySubgroupIDs[i] + " is compatible with the Specific NPC Assignment.", false, npcInfo);
                                    }
                                    else
                                    {
                                        _logger.LogReport("Consistency subgroup " + consistencySubgroupIDs[i] + " is incompatible with the Specific NPC Assignment at position " + i + ".", true, npcInfo);
                                    }
                                }
                                // if no user-forced subgroup exists, simply make sure that the consistency subgroup exists
                                else
                                {
                                    FlattenedSubgroup consistencySubgroup = consistencyAssetPack.Subgroups[i].FirstOrDefault(x => x.Id == consistencySubgroupIDs[i]);
                                    if (consistencySubgroup == null)
                                    {
                                        _logger.LogReport("The consistency subgroup " + consistencySubgroupIDs[i] + " was either filtered out or no longer exists within the config file. Choosing a different subgroup at this position.", true, npcInfo);
                                    }
                                    else if (!SubgroupValidForCurrentNPC(consistencySubgroup, npcInfo, mode, assignedBodyGen, assignedBodySlides, "Subgroup" + " " + consistencySubgroup.GetDetailedID_NameString(false)))
                                    {
                                        _logger.LogReport("Consistency subgroup " + consistencySubgroup.Id + " (" + consistencySubgroup.Name + ") is no longer valid for this NPC. Choosing a different subgroup at this position", true, npcInfo);
                                        consistencyAssetPack.Subgroups[i].Remove(consistencySubgroup);
                                    }
                                    else
                                    {
                                        consistencyAssetPack.Subgroups[i] = new List<FlattenedSubgroup>() { consistencySubgroup };
                                        _logger.LogReport("Using consistency subgroup " + consistencySubgroupIDs[i] + ".", false, npcInfo);
                                    }
                                }
                            }
                            filteredPacks = new List<FlattenedAssetPack>() { consistencyAssetPack };
                            wasFilteredByConsistency = true;
                        }
                    }
                }
            }
            _logger.CloseReportSubsectionsTo("ConfigFiltering", npcInfo);
        }
        #endregion

        if (filteredPacks.Count == 0 && mode == AssetPackAssignmentMode.Primary)
        {
            _logger.LogMessage("None of your current installed config files can be applied to " + npcInfo.LogIDstring);
        }

        _logger.CloseReportSubsection(npcInfo);

        return filteredPacks.ToHashSet();
    }

    /// <summary>
    /// Second-pass subgroup filtering: removes any subgroup whose required subgroups are no longer available at the
    /// expected position, or which excludes the only remaining subgroup at a position. Re-runs until a full pass makes no
    /// removals (removals can post-hoc invalidate earlier-checked subgroups). Mutates <paramref name="assetPack"/>.
    /// </summary>
    /// <returns>False if the asset pack is invalid (a config-file error, or a position left with no subgroups); true otherwise.</returns>
    public bool RemoveInvalidLinkedSubgroups(FlattenedAssetPack assetPack, NPCInfo npcInfo)
    {
        bool allSubgroupsPassed = false;
        while (!allSubgroupsPassed) // during each cycle, any given subgroup might be removed. This may be a required subgroup for another subgroup that had been checked earlier in the cycle, thereby invalidating it post-hoc. Therefore, keep checking all subgroups in the config file until a cycle where each one remains valid
        {
            allSubgroupsPassed = true;
            for (int i = 0; i < assetPack.Subgroups.Count; i++)
            {
                for (int j = 0; j < assetPack.Subgroups[i].Count; j++)
                {
                    var currentSubgroup = assetPack.Subgroups[i][j];
                    bool currentSubgroupPassed = true;

                    if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.SubgroupIDs.Contains(currentSubgroup.Id))
                    {
                        continue;
                    }

                    // check required subgroups
                    foreach (int topLevelIndex in currentSubgroup.RequiredSubgroupIDs.Keys)
                    {
                        if (assetPack.Subgroups.Count < topLevelIndex + 1)
                        {
                            _logger.LogReport("Error trimming Required Subgroups: " + assetPack.GroupName + " Subgroup " + currentSubgroup.GetReportString() + " expects a required subgroup at position " + topLevelIndex + " but there are only " + assetPack.Subgroups.Count + " top level subgroups", true, npcInfo);
                            return false; // something wrong with config file
                        }

                        var requiredSubgroupIDsAtIndex = currentSubgroup.RequiredSubgroupIDs[topLevelIndex];
                        bool foundMatchAtIndex = assetPack.Subgroups[topLevelIndex]
                            .Where(subgroup => 
                            subgroup.ContainedSubgroupIDs.Intersect(requiredSubgroupIDsAtIndex).Any() // subgroup has contains IDs in its ID chain that are required at this index
                            ).Any(); // any subgroup at topLevelIndex satisfies the condition above

                        if (!foundMatchAtIndex)
                        {
                            _logger.LogReport("Subgroup " + currentSubgroup.GetDetailedID_NameString(false) + " is invalid because it requires [" + String.Join(" or ", requiredSubgroupIDsAtIndex) + "] at position " + topLevelIndex + " and none of these subgroups are available", false, npcInfo);
                            currentSubgroupPassed = false;
                            break;
                        }
                    }

                    if (!currentSubgroupPassed)
                    {
                        allSubgroupsPassed = false;
                        assetPack.Subgroups[i].RemoveAt(j);
                        j--;
                        continue;
                    }

                    // check excluded subgroups
                    foreach (int topLevelIndex in currentSubgroup.ExcludedSubgroupIDs.Keys)
                    {
                        if (assetPack.Subgroups.Count < topLevelIndex + 1)
                        {
                            _logger.LogReport("Error trimming Excluded Subgroups: " + assetPack.GroupName + " Subgroup " + currentSubgroup.GetReportString() + " expects a excluded subgroup at position " + topLevelIndex + " but there are only " + assetPack.Subgroups.Count + " top level subgroups", true, npcInfo);
                            return false; // something wrong with config file
                        }

                        // if subgroup X excludes Y and Y is the only remaining subgroup at that index, remove X
                        var excludedSubgroupsAtIndex = currentSubgroup.ExcludedSubgroupIDs[topLevelIndex];
                        if (assetPack.Subgroups[topLevelIndex].Count == 1 && excludedSubgroupsAtIndex.Intersect(assetPack.Subgroups[topLevelIndex][0].ContainedSubgroupIDs).Any())
                        {
                            _logger.LogReport("Subgroup " + currentSubgroup.GetDetailedID_NameString(false) + " is invalid because it excludes [" + String.Join(" or ", excludedSubgroupsAtIndex) + "] at position " + topLevelIndex + " which eliminates the only remaining subgroup at this position", false, npcInfo);
                            currentSubgroupPassed = false;
                            break;
                        }
                    }
                    if (!currentSubgroupPassed)
                    {
                        allSubgroupsPassed = false;
                        assetPack.Subgroups[i].RemoveAt(j);
                        j--;
                        continue; // just in case I add another check after this
                    }
                }

                if (!assetPack.Subgroups[i].Any())
                {
                    _logger.LogReport("Asset Pack " + assetPack.GroupName + " is invalid because no subgroups are available at position " + i + assetPack.GetSubgroupPositionString(i) + ".", false, npcInfo);
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Returns false if the given subgroup is incompatible with the given NPC due to any of the rules defined within the subgroup.
    /// </summary>
    /// <param name="subgroup"></param>
    /// <param name="npcInfo"></param>
    /// <param name="forceIfAttributeCount">The number of ForceIf attributes within this subgroup that were matched by the current NPC</param>
    /// <returns></returns>
    private bool SubgroupValidForCurrentNPC(FlattenedSubgroup subgroup, NPCInfo npcInfo, AssetPackAssignmentMode mode, List<BodyGenConfig.BodyGenTemplate> assignedBodyGen, List<BodySlideSetting> assignedBodySlides, string reportStringPrefix)
    {
        reportStringPrefix += " ";

        if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.SubgroupIDs.Contains(subgroup.Id))
        {
            _logger.LogReport(reportStringPrefix + "is valid because it is specifically assigned by user.", false, npcInfo);
            return true;
        }

        // Allow unique NPCs
        if (!subgroup.AllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            _logger.LogReport(reportStringPrefix + "is invalid because it is disallowed for unique NPCs", false, npcInfo);
            return false;
        }

        // Allow non-unique NPCs
        if (!subgroup.AllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            _logger.LogReport(reportStringPrefix + "is invalid because it is disallowed for non-unique NPCs", false, npcInfo);
            return false;
        }

        // Allowed Races
        if (!subgroup.AllowedRacesIsEmpty && !subgroup.AllowedRaces.Contains(npcInfo.AssetsRace))
        {
            _logger.LogReport(reportStringPrefix + "is invalid because its allowed races (" + Logger.GetRaceListLogStrings(subgroup.AllowedRaces, _environmentProvider.LinkCache, _patcherState) + ") do not include the current NPC's race (" + Logger.GetRaceLogString(npcInfo.AssetsRace, _environmentProvider.LinkCache, _patcherState) + ")", false, npcInfo);
            return false;
        }

        // Disallowed Races
        if (subgroup.DisallowedRaces.Contains(npcInfo.AssetsRace))
        {
            _logger.LogReport(reportStringPrefix + "is invalid because its disallowed races (" + Logger.GetRaceListLogStrings(subgroup.DisallowedRaces, _environmentProvider.LinkCache, _patcherState) + ") include the current NPC's race (" + Logger.GetRaceLogString(npcInfo.AssetsRace, _environmentProvider.LinkCache, _patcherState) + ")", false, npcInfo);
            return false;
        }

        // Weight Range
        if (npcInfo.NPC.Weight < subgroup.WeightRange.Lower || npcInfo.NPC.Weight > subgroup.WeightRange.Upper)
        {
            _logger.LogReport(reportStringPrefix + "is invalid because the current NPC's weight falls outside of the morph's allowed weight range", false, npcInfo);
            return false;
        }

        // Allowed and Forced Attributes
        subgroup.ForceIfMatchCount = 0;
        _attributeMatcher.MatchNPCtoAttributeList(subgroup.AllowedAttributes, npcInfo.NPC, npcInfo.AssetsRace, subgroup.ParentAssetPack.Source.AttributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfWeightedCount, out string _, out string unmatchedLog, out string forceIfLog, null);
        if (hasAttributeRestrictions && !matchesAttributeRestrictions)
        {
            _logger.LogReport(reportStringPrefix + " is invalid because the NPC does not match any of its allowed attributes: " + unmatchedLog, false, npcInfo);
            return false;
        }
        else
        {
            subgroup.ForceIfMatchCount = matchedForceIfWeightedCount;
        }

        if (subgroup.ForceIfMatchCount > 0)
        {
            _logger.LogReport(reportStringPrefix + " Current NPC matches the following forced attributes: " + forceIfLog, false, npcInfo);
        }

        // Disallowed Attributes
        _attributeMatcher.MatchNPCtoAttributeList(subgroup.DisallowedAttributes, npcInfo.NPC, npcInfo.AssetsRace, subgroup.ParentAssetPack.Source.AttributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out hasAttributeRestrictions, out matchesAttributeRestrictions, out int dummy, out string matchLog, out string _, out string _, null);
        if (hasAttributeRestrictions && matchesAttributeRestrictions)
        {
            _logger.LogReport(reportStringPrefix + " is invalid because the NPC matches one of its disallowed attributes: " + matchLog, false, npcInfo);
            return false;
        }

        // if the current subgroup's forceIf attributes match the current NPC, skip the checks for Distribution Enabled

        // Distribution Enabled
        if (subgroup.ForceIfMatchCount == 0 && !subgroup.DistributionEnabled)
        {
            _logger.LogReport(reportStringPrefix + "is invalid because its distribution is disabled to random NPCs, it is not a Specific NPC Assignment, and the NPC does not match any of its ForceIf attributes.", false, npcInfo);
            return false;
        }

        if (mode != AssetPackAssignmentMode.Primary)
        {
            switch (_patcherState.GeneralSettings.BodySelectionMode)
            {
                case BodyShapeSelectionMode.BodyGen:
                    if (assignedBodyGen != null)
                    {
                        foreach (var bodyGenTemplate in assignedBodyGen)
                        {
                            if (subgroup.AllowedBodyGenDescriptors.Any() && !BodyShapeDescriptor.DescriptorsMatch(subgroup.AllowedBodyGenDescriptors, bodyGenTemplate.BodyShapeDescriptors, subgroup.AllowedBodyGenMatchMode, out _))
                            {
                                _logger.LogReport(reportStringPrefix + " is invalid because its allowed descriptors do not include any of those annotated in the descriptors of assigned morph " + bodyGenTemplate.Label + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(subgroup.AllowedBodyGenDescriptors), false, npcInfo);
                                return false;
                            }

                            if (BodyShapeDescriptor.DescriptorsMatch(subgroup.DisallowedBodyGenDescriptors, bodyGenTemplate.BodyShapeDescriptors, subgroup.DisallowedBodyGenMatchMode, out string matchedDescriptor))
                            {
                                _logger.LogReport(reportStringPrefix + " is invalid because its descriptor [" + matchedDescriptor + "] is disallowed by assigned morph " + bodyGenTemplate.Label + "'s descriptors", false, npcInfo);
                                return false;
                            }
                        }
                    }
                    break;
                case BodyShapeSelectionMode.BodySlide:
                    if (assignedBodySlides != null)
                    {
                        foreach (var assignedBodySlide in assignedBodySlides)
                        {
                            var assignedDescriptorsAtWeight = PerWeightDescriptorLookup.GetDescriptorsForWeight(assignedBodySlide, npcInfo.NPC.Weight);
                            if (subgroup.AllowedBodySlideDescriptors.Any() && !BodyShapeDescriptor.DescriptorsMatch(subgroup.AllowedBodySlideDescriptors, assignedDescriptorsAtWeight, subgroup.AllowedBodySlideMatchMode, out _))
                            {
                                _logger.LogReport(reportStringPrefix + " is invalid because its allowed descriptors do not include any of those annotated in the descriptors of assigned bodyslide " + assignedBodySlide.Label + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(subgroup.AllowedBodyGenDescriptors), false, npcInfo);
                                return false;
                            }

                            if (BodyShapeDescriptor.DescriptorsMatch(subgroup.DisallowedBodySlideDescriptors, assignedDescriptorsAtWeight, subgroup.DisallowedBodySlideMatchMode, out string matchedDescriptor))
                            {
                                _logger.LogReport(reportStringPrefix + " is invalid because its descriptor [" + matchedDescriptor + "] is disallowed by assigned bodyslide " + assignedBodySlide.Label + "'s descriptors", false, npcInfo);
                                return false;
                            }
                        }
                    }
                    break;
            }
        }

        // If the subgroup is still valid
        return true;
    }

    /// <summary>
    /// For each top-level position, returns the subgroups whose ID chain contains one of the user-forced
    /// <paramref name="forcedSubgroupIDs"/> (a nested list because forcing a non-bottom-level subgroup can match several
    /// options at a position). Logs a warning for any forced ID not found in the asset pack.
    /// </summary>
    private List<List<FlattenedSubgroup>> GetForcedSubgroupsAtIndex(FlattenedAssetPack input, List<string> forcedSubgroupIDs, NPCInfo npcInfo)
    {
        List<List<FlattenedSubgroup>> forcedOrEmpty = new List<List<FlattenedSubgroup>>();

        List<string> matchedIDs = new List<string>();

        foreach (List<FlattenedSubgroup> variantsAtIndex in input.Subgroups)
        {
            var specifiedSubgroups = variantsAtIndex.Where(x => forcedSubgroupIDs.Intersect(x.ContainedSubgroupIDs).Any()).ToList();
            foreach (var specified in specifiedSubgroups)
            {
                matchedIDs.AddRange(specified.ContainedSubgroupIDs);
            }
            forcedOrEmpty.Add(specifiedSubgroups);
        }

        foreach (string id in forcedSubgroupIDs)
        {
            if (matchedIDs.Contains(id) == false)
            {
                _logger.LogReport("Subgroup " + id + " requested by Specific NPC Assignment was not found in Asset Pack " + input.GroupName, true, npcInfo);
            }
        }

        return forcedOrEmpty;
    }

    /// <summary>
    /// Determines if a given combination (pre-determined from Consistency or a Linked NPC Group) is compatible with the Specific NPC Assignment for the current NPC if one exists
    /// </summary>
    /// <param name="specificAssignment">Specific NPC Assignment for the current NPC</param>
    /// <param name="selectedCombination">Candidate subgroup combination</param>
    /// <returns></returns>
    public static bool CombinationAllowedBySpecificNPCAssignment(NPCAssignment specificAssignment, SubgroupCombination selectedCombination, AssetPackAssignmentMode mode)
    {
        if (specificAssignment == null) { return true; }

        switch (mode)
        {
            case AssetPackAssignmentMode.Primary:
                if (specificAssignment.AssetPackName == "") { return true; }
                else
                {
                    if (specificAssignment.AssetPackName != selectedCombination.AssignmentName) { return false; }
                    if (!CombinationContainsForcedSubgroups(selectedCombination.ContainedSubgroups.Select(x => x.Id), specificAssignment.SubgroupIDs))
                    {
                        return false;
                    }
                }
                break;

            case AssetPackAssignmentMode.MixIn:
                var forcedMixIn = specificAssignment.MixInAssignments.FirstOrDefault(x => x.AssetPackName == selectedCombination.AssignmentName);
                if (forcedMixIn != null)
                {
                    if (!CombinationContainsForcedSubgroups(selectedCombination.ContainedSubgroups.Select(x => x.Id), forcedMixIn.SubgroupIDs))
                    {
                        return false;
                    }
                }
                break;
            case AssetPackAssignmentMode.ReplacerVirtual:
                var forcedReplacer = specificAssignment.AssetReplacerAssignments.FirstOrDefault(x => x.ReplacerName == selectedCombination.AssignmentName);
                if (forcedReplacer != null)
                {
                    if (forcedReplacer.SubgroupIDs.Count != selectedCombination.ContainedSubgroups.Count) { return false; }
                    for (int i = 0; i < forcedReplacer.SubgroupIDs.Count; i++)
                    {
                        if (forcedReplacer.SubgroupIDs[i] != selectedCombination.ContainedSubgroups[i].Id)
                        {
                            return false;
                        }
                    }
                }
                break;
        }
        return true;
    }

    /// <summary>
    /// Returns whether <paramref name="combinationSubgroupIds"/> contains every id in
    /// <paramref name="forcedSubgroupIds"/>. A Specific NPC Assignment's forced subgroup IDs (for Primary and
    /// MixIn packs) are a subset that must all be present in the combination — unlike a Replacer assignment,
    /// which requires an exact, ordered match. A null/empty forced set imposes no constraint. Extracted for testability.
    /// </summary>
    public static bool CombinationContainsForcedSubgroups(IEnumerable<string> combinationSubgroupIds, IEnumerable<string> forcedSubgroupIds)
    {
        if (forcedSubgroupIds == null) { return true; }
        var present = combinationSubgroupIds.ToHashSet();
        return forcedSubgroupIds.All(present.Contains);
    }

    /// <summary>
    /// Persists a Primary-mode assignment: writes the chosen asset pack + subgroup IDs into the NPC's consistency record,
    /// stores the combination on the linked group if this NPC is the primary member, and seeds same-name unique-NPC data.
    /// </summary>
    public void RecordPrimaryAssetConsistencyAndLinkedNPCs(SubgroupCombination assignedCombination, NPCInfo npcInfo) // Primary 
    {
        if (_patcherState.GeneralSettings.bEnableConsistency)
        {
            npcInfo.ConsistencyNPCAssignment.AssetPackName = assignedCombination.AssignmentName;
            npcInfo.ConsistencyNPCAssignment.SubgroupIDs = assignedCombination.ContainedSubgroups.Where(x => x.Id != AssetPack.ConfigDistributionRules.SubgroupIDString).Select(x => x.Id).ToList();
        }
        if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Primary && assignedCombination != null)
        {
            npcInfo.AssociatedLinkGroup.AssignedCombination = assignedCombination;
        }

        if (_patcherState.GeneralSettings.bLinkNPCsWithSameName)
        {
            _uniqueNPCData.InitializeUnsetUniqueNPCPrimaryAssets(npcInfo, assignedCombination);
        }
    }

    /// <summary>
    /// Persists a MixIn-mode assignment (or a probability-declined MixIn) into the NPC's consistency record, stores the
    /// combination on the linked group if this NPC is the primary member, and seeds same-name unique-NPC MixIn data.
    /// </summary>
    public void RecordMixInAssetConsistencyAndLinkedNPCs(SubgroupCombination assignedCombination, NPCInfo npcInfo, string mixInName, bool declinedViaProbability) // MixIn 
    {
        bool addMixInAssignmentToConsistency = assignedCombination != null || declinedViaProbability;
        if (_patcherState.GeneralSettings.bEnableConsistency && addMixInAssignmentToConsistency)
        {
            var consistencyMixIn = npcInfo.ConsistencyNPCAssignment.MixInAssignments.FirstOrDefault(x => x.AssetPackName == mixInName);
            if (consistencyMixIn == null)
            {
                consistencyMixIn = new NPCAssignment.MixInAssignment();
                npcInfo.ConsistencyNPCAssignment.MixInAssignments.Add(consistencyMixIn);
            }
            consistencyMixIn.AssetPackName = mixInName;
            if (declinedViaProbability)
            {
                consistencyMixIn.DeclinedAssignment = true;
            }
            else if (assignedCombination != null)
            {
                consistencyMixIn.SubgroupIDs = assignedCombination.ContainedSubgroups.Where(x => x.Id != AssetPack.ConfigDistributionRules.SubgroupIDString).Select(x => x.Id).ToList();
                consistencyMixIn.DeclinedAssignment = false;
            }
        }

        if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Primary && assignedCombination != null)
        {
            if (npcInfo.AssociatedLinkGroup.MixInAssignments.ContainsKey(mixInName))
            {
                npcInfo.AssociatedLinkGroup.MixInAssignments[mixInName] = assignedCombination;
            }
            else
            {
                npcInfo.AssociatedLinkGroup.MixInAssignments.Add(mixInName, assignedCombination);
            }
        }

        if (_patcherState.GeneralSettings.bLinkNPCsWithSameName && assignedCombination != null)
        {
            _uniqueNPCData.InitializeUnsetUniqueNPCPMixInAssets(npcInfo, mixInName, assignedCombination);
        }
    }

    /// <summary>
    /// Persists a Replacer-mode assignment into the NPC's consistency record (adding or updating the matching replacer
    /// entry), mirrors it onto the linked group for the primary member, and seeds same-name unique-NPC replacer data.
    /// </summary>
    public void RecordReplacerAssetConsistencyAndLinkedNPCs(SubgroupCombination assignedCombination, NPCInfo npcInfo, FlattenedReplacerGroup replacerGroup) // Replacer
    {
        if (_patcherState.GeneralSettings.bEnableConsistency)
        {
            var existingAssignment = npcInfo.ConsistencyNPCAssignment.AssetReplacerAssignments.FirstOrDefault(x => x.ReplacerName == replacerGroup.Name);
            if (existingAssignment != null) { existingAssignment.SubgroupIDs = assignedCombination.ContainedSubgroups.Where(x => x.Id != AssetPack.ConfigDistributionRules.SubgroupIDString).Select(x => x.Id).ToList(); }
            else { npcInfo.ConsistencyNPCAssignment.AssetReplacerAssignments.Add(new NPCAssignment.AssetReplacerAssignment() { AssetPackName = replacerGroup.Source.GroupName, ReplacerName = replacerGroup.Name, SubgroupIDs = assignedCombination.ContainedSubgroups.Where(x => x.Id != AssetPack.ConfigDistributionRules.SubgroupIDString).Select(x => x.Id).ToList() }); }
        }
        if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Primary)
        {
            var existingAssignment = npcInfo.AssociatedLinkGroup.ReplacerAssignments.FirstOrDefault(x => x.ReplacerName == replacerGroup.Name);
            if (existingAssignment != null) { existingAssignment.AssignedReplacerCombination = assignedCombination; }
            else { npcInfo.AssociatedLinkGroup.ReplacerAssignments.Add(new LinkedNPCGroupInfo.LinkedAssetReplacerAssignment() { GroupName = replacerGroup.Source.GroupName, ReplacerName = replacerGroup.Name, AssignedReplacerCombination = assignedCombination }); }
        }

        if (_patcherState.GeneralSettings.bLinkNPCsWithSameName)
        {
            _uniqueNPCData.InitializeUnsetUniqueNPCPReplacerAssets(npcInfo, new() { GroupName = replacerGroup.Source.GroupName, ReplacerName = replacerGroup.Name, AssignedReplacerCombination = assignedCombination });
        }
    }

    /// <summary>
    /// Returns true if asset assignment should be skipped for this NPC because Texture/Mesh settings disallow patching
    /// NPCs that already have a custom (non-base-game, non-EasyNPC-vanilla) face texture or worn-armor skin. Logs the reason.
    /// </summary>
    public bool BlockAssetDistributionByExistingAssets(NPCInfo npcInfo)
    {
        if (!_patcherState.TexMeshSettings.bApplyToNPCsWithCustomFaces && 
            npcInfo.NPC.HeadTexture != null && 
            !npcInfo.NPC.HeadTexture.IsNull && 
            !BaseGamePlugins.Plugins.Contains(npcInfo.NPC.HeadTexture.FormKey.ModKey.FileName.String))
        {
            _logger.LogReport("Asset assignment is disabled for this NPC because the Texture/Mesh settings disallow patching of NPCs with custom face textures", false, npcInfo);
            return true;
        }
        if (!_patcherState.TexMeshSettings.bApplyToNPCsWithCustomSkins && 
            npcInfo.NPC.WornArmor != null && 
            !npcInfo.NPC.WornArmor.IsNull && 
            !BaseGamePlugins.Plugins.Contains(npcInfo.NPC.WornArmor.FormKey.ModKey.FileName.String) && 
            !IsEasyNPCVanillaSkin(npcInfo.NPC.WornArmor))
        {
            _logger.LogReport("Asset assignment is disabled for this NPC because the Texture/Mesh settings disallow patching of NPCs with custom skins", false, npcInfo);
            return true;
        }
        return false;
    }

    /// <summary>
    /// In EasyNPC compatibility mode, returns true if the given worn-armor link resolves to an EditorID in the
    /// EasyNPC vanilla-skin set (so it is treated as a vanilla skin rather than a custom one). Always false otherwise.
    /// </summary>
    public bool IsEasyNPCVanillaSkin(IFormLinkNullableGetter<IArmorGetter> wnam)
    {
        if (!_patcherState.TexMeshSettings.bEasyNPCCompatibilityMode)
        {
            return false; // only consider EasyNPC skins to be vanilla when in EasyNPC Compatibility Mode
        }

        if (wnam != null && 
            wnam.TryResolve(_environmentProvider.LinkCache, out var wnamGetter) &&
            wnamGetter.EditorID != null &&
            _vanillaSkinsEasyNPC.Contains(wnamGetter.EditorID))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Decides whether to skip this Mix-In for the NPC, rolling against the pack's inclusion probability scaled by any
    /// matched whole-config probability modifiers (clamped to 100). Logs when the Mix-In is randomly declined.
    /// </summary>
    /// <returns>True if the Mix-In should NOT be assigned.</returns>
    private bool SkipMixInByProbability(FlattenedAssetPack mixInPack, NPCInfo npcInfo)
    {
        // Scale the inclusion probability by any matched whole-config probability modifiers, clamped to 100.
        double modifierFactor = ProbabilityWeighting.GetProbabilityModifierFactor(
            mixInPack.DistributionRules.ProbabilityWeightModifiers, npcInfo.NPC, npcInfo.AssetsRace,
            mixInPack.Source.AttributeGroups, _attributeMatcher,
            _patcherState.GeneralSettings.VerboseModeDetailedAttributes, _logger, npcInfo, mixInPack.GroupName);
        double scaledProbability = Math.Min(100.0, mixInPack.DistributionRules.ProbabilityWeighting * modifierFactor);
        if (!BoolByProbability.Decide(scaledProbability))
        {
            _logger.LogReport("Mix In " + mixInPack.GroupName + " was chosen at random to NOT be assigned.", false, npcInfo);
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// Logs to the report the body-shape descriptor (allowed/disallowed) rules contributed by each subgroup in the
    /// generated combination, for the active body-selection mode (BodyGen or BodySlide). No-op when body selection is off.
    /// </summary>
    public void GenerateDescriptorLog(SubgroupCombination generatedCombination, NPCInfo npcInfo)
    {
        if (_patcherState.GeneralSettings.BodySelectionMode != BodyShapeSelectionMode.None)
        {
            Dictionary<string, string> descriptorRules = new();

            foreach (var subgroup in generatedCombination.ContainedSubgroups)
            {
                switch (_patcherState.GeneralSettings.BodySelectionMode)
                {
                    case BodyShapeSelectionMode.BodyGen:
                        GenerateDescriptorSubLog(descriptorRules, subgroup.Id, "Allowed", subgroup.AllowedBodyGenDescriptors);
                        GenerateDescriptorSubLog(descriptorRules, subgroup.Id, "Disallowed", subgroup.DisallowedBodyGenDescriptors);
                        break;
                    case BodyShapeSelectionMode.BodySlide:
                        GenerateDescriptorSubLog(descriptorRules, subgroup.Id, "Allowed", subgroup.AllowedBodySlideDescriptors);
                        GenerateDescriptorSubLog(descriptorRules, subgroup.Id, "Disallowed", subgroup.DisallowedBodySlideDescriptors);
                        break;
                    default: break;
                }
            }

            string descriptorLogStr = "Applied descriptor rules: ";
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

    /// <summary>Appends one subgroup's formatted descriptor set (prefixed by <paramref name="adj"/>, e.g. "Allowed"/"Disallowed") to the per-subgroup descriptor log accumulator.</summary>
    private void GenerateDescriptorSubLog(Dictionary<string, string> descriptorLog, string subgroupID, string adj, Dictionary<string, HashSet<string>> desciptorSet)
    {
        string descriptorStr = Logger.GetBodyShapeDescriptorString(desciptorSet);
        if (!descriptorStr.IsNullOrWhitespace())
        {
            if (descriptorLog.ContainsKey(subgroupID))
            {
                descriptorLog[subgroupID] += Environment.NewLine + adj + " Descriptors: " + descriptorStr;
            }
            else
            {
                descriptorLog.Add(subgroupID, adj + " Descriptors: " + descriptorStr);
            }
        }
    }

    /// <summary>Base-game skin EditorIDs treated as "vanilla" (naked / naked beast) for the custom-skin block check.</summary>
    private HashSet<string> _vanillaSkins = new() // may want to consider adding a UI element or Json file for this
    {
        "SkinNaked",
        "SkinNakedBeast"
    };

    /// <summary>EasyNPC-transformed equivalents of <see cref="_vanillaSkins"/> (each name + "Patched"); rebuilt in <see cref="Reinitialize"/>.</summary>
    private HashSet<string> _vanillaSkinsEasyNPC = new();
}