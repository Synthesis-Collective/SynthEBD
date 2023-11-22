using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using static SynthEBD.AssetPack;

namespace SynthEBD;

public class AssetSelector
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly AttributeMatcher _attributeMatcher;
    private readonly UniqueNPCData _uniqueNPCData;

    public AssetSelector(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, AttributeMatcher attributeMatcher, UniqueNPCData uniqueNPCData)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _attributeMatcher = attributeMatcher;
        _uniqueNPCData = uniqueNPCData;
    }

    public enum AssetPackAssignmentMode
    {
        Primary,
        MixIn,
        ReplacerVirtual
    }

    public void Reinitialize()
    {
        _vanillaSkinsEasyNPC = _vanillaSkins.Select(x => x + "Patched").ToHashSet(); // EasyNPC transformation
    }

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
                var specificMixInAssignment = npcInfo.SpecificNPCAssignment?.MixInAssignments.Where(x => x.AssetPackName == availableAssetPacks.First().GroupName).FirstOrDefault();
                if (specificMixInAssignment == null)
                {
                    var mixInPack = availableAssetPacks.First();
                    var mixInConsistency = npcInfo.ConsistencyNPCAssignment?.MixInAssignments?.Where(x => x.AssetPackName == mixInPack.GroupName).FirstOrDefault();
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
                var linkedAssignmentGroup = npcInfo.AssociatedLinkGroup.ReplacerAssignments.Where(x => x.ReplacerName == availableAssetPacks.First().GroupName).FirstOrDefault();
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
                    var linkedAssignmentGroup = linkedReplacerCombinations.Where(x => x.ReplacerName == availableAssetPacks.First().GroupName).FirstOrDefault(); // availableAssetPacks only contains the current replacer
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

            _logger.LogReport("Choosing a new seed subgroup from the following list of available seeds and (matched ForceIf attributes):" + Environment.NewLine + string.Join(Environment.NewLine, iterationInfo.AvailableSeeds.Select(x => x.Id + ": " + x.Name + " (" + x.ForceIfMatchCount + ")")), false, npcInfo);

            if (iterationInfo.AvailableSeeds[0].ForceIfMatchCount > 0)
            {
                iterationInfo.ChosenSeed = ChooseForceIfSubgroup(iterationInfo.AvailableSeeds);
                iterationInfo.ChosenAssetPack = iterationInfo.ChosenSeed.ParentAssetPack.ShallowCopy();
                _logger.LogReport("Chose seed subgroup " + iterationInfo.ChosenSeed.GetDetailedID_NameString(false) + " in " + iterationInfo.ChosenAssetPack.GroupName + " because it had the most matched ForceIf attributes (" + iterationInfo.ChosenSeed.ForceIfMatchCount + ").", false, npcInfo);
            }
            else
            {
                iterationInfo.ChosenSeed = (FlattenedSubgroup)ProbabilityWeighting.SelectByProbability(iterationInfo.AvailableSeeds);
                iterationInfo.ChosenAssetPack = iterationInfo.ChosenSeed.ParentAssetPack.ShallowCopy();
                _logger.LogReport("Chose seed subgroup " + iterationInfo.ChosenSeed.GetDetailedID_NameString(false) + " in " + iterationInfo.ChosenAssetPack.GroupName + " at random", false, npcInfo);
            }

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

        _logger.LogReport("Available Subgroups:" + Logger.SpreadFlattenedAssetPack(iterationInfo.ChosenAssetPack, 0, false), false, npcInfo);

        for (int i = 0; i < iterationInfo.ChosenAssetPack.Subgroups.Count; i++) // iterate through each position within the combination
        {
            if (generatedCombination.ContainedSubgroups.Count > 0)
            {
                _logger.LogReport("Current Combination: " + String.Join(" , ", generatedCombination.ContainedSubgroups.Where(x => x != null).Select(x => x.Id)) + Environment.NewLine, false, npcInfo);
            }
            _logger.LogReport("Available Subgroups:" + Logger.SpreadFlattenedAssetPack(iterationInfo.ChosenAssetPack, i, true), false, npcInfo);

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
            if (iterationInfo.ChosenAssetPack.Subgroups[i][0].ForceIfMatchCount > 0)
            {
                nextSubgroup = ChooseForceIfSubgroup(iterationInfo.ChosenAssetPack.Subgroups[i]);
                _logger.LogReport("Chose next subgroup: " + nextSubgroup.GetDetailedID_NameString(true) + " at position " + i + " because it had the most matched ForceIf Attributes (" + nextSubgroup.ForceIfMatchCount + ")." + Environment.NewLine, false, npcInfo);
            }
            else
            {
                nextSubgroup = (FlattenedSubgroup)ProbabilityWeighting.SelectByProbability(iterationInfo.ChosenAssetPack.Subgroups[i]);
                _logger.LogReport("Chose next subgroup: " + nextSubgroup.GetDetailedID_NameString(true) + " at position " + i + " at random." + Environment.NewLine, false, npcInfo);
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
            _logger.LogReport(subgroup.ContainedSubgroupNames.First() + ": " + subgroup.GetNestedNameString(true), false, npcInfo);
        }
        GenerateDescriptorLog(generatedCombination, npcInfo);
        _logger.CloseReportSubsectionsToParentOf("CombinationGeneration", npcInfo);
        return generatedCombination;
    }

    private static FlattenedSubgroup ChooseForceIfSubgroup(List<FlattenedSubgroup> availableSeeds)
    {
        int maxMatchedForceIfCount = availableSeeds[0].ForceIfMatchCount;
        var candidateSubgroups = availableSeeds.Where(x => x.ForceIfMatchCount == maxMatchedForceIfCount).ToList(); // input list is sorted so that the subgroup with the most matched ForceIf attributes is first. Get other subgroups with the same number of matched ForceIf attributes (if any)
        return (FlattenedSubgroup)ProbabilityWeighting.SelectByProbability(candidateSubgroups);
    }

    private static SubgroupCombination RemoveInvalidSeed(List<FlattenedSubgroup> seedSubgroups, AssignmentIteration iterationInfo)
    {
        seedSubgroups.Remove(iterationInfo.ChosenSeed);
        iterationInfo.ChosenSeed = null;
        return null;
    }

    private bool ConformRequiredExcludedSubgroups(SubgroupCombination currentCombination, FlattenedSubgroup targetSubgroup, FlattenedAssetPack chosenAssetPack, NPCInfo npcInfo, out FlattenedAssetPack filteredAssetPack)
    {
        filteredAssetPack = chosenAssetPack;
        if (!targetSubgroup.ExcludedSubgroupIDs.Any() && !targetSubgroup.RequiredSubgroupIDs.Any())
        {
            return true;
        }

        // check if incoming subgroup is allowed by all existing subgroups
        foreach (var subgroup in currentCombination.ContainedSubgroups.Where(x => x is not null).ToArray())
        {
            foreach (var index in subgroup.RequiredSubgroupIDs)
            {
                if (index.Key == targetSubgroup.TopLevelSubgroupIndex && !index.Value.Intersect(targetSubgroup.ContainedSubgroupIDs).Any())
                {
                    _logger.LogReport("\tSubgroup " + targetSubgroup.Id + " cannot be added because a different subgroup is required at position " + index.Key + " by the already assigned subgroup " + subgroup.Id + Environment.NewLine, false, npcInfo);
                    return false;
                }
            }

            foreach (var index in subgroup.ExcludedSubgroupIDs)
            {
                if (index.Key == targetSubgroup.TopLevelSubgroupIndex && index.Value.Intersect(targetSubgroup.ContainedSubgroupIDs).Any())
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
                if (excludedSubgroupsAtIndex.Value.Intersect(currentCombination.ContainedSubgroups[excludedSubgroupsAtIndex.Key].ContainedSubgroupIDs).Any())
                {
                    _logger.LogReport("\tSubgroup " + targetSubgroup.Id + " cannot be added because it excludes (" + String.Join(',', excludedSubgroupsAtIndex.Value) + ") which is incompatible with the currently assigned subgroup at position " + excludedSubgroupsAtIndex.Key + Environment.NewLine, false, npcInfo);
                    return false;
                }
            }
            // check candidate subgroups to be assigned
            trialSubgroups[excludedSubgroupsAtIndex.Key] = chosenAssetPack.Subgroups[excludedSubgroupsAtIndex.Key].Where(x => !excludedSubgroupsAtIndex.Value.Intersect(x.ContainedSubgroupIDs).Any()).ToList();
            if (!trialSubgroups[excludedSubgroupsAtIndex.Key].Any())
            {
                _logger.LogReport("\tSubgroup " + targetSubgroup.Id + " cannot be added because it excludes (" + String.Join(',', excludedSubgroupsAtIndex.Value) + ") which eliminates all options at position " + excludedSubgroupsAtIndex.Key + Environment.NewLine, false, npcInfo);
                return false;
            }
        }

        // check required subgroups of incoming subgroup
        foreach (var requiredSubgroupsAtIndex in targetSubgroup.RequiredSubgroupIDs)
        {
            if (currentCombination.ContainedSubgroups[requiredSubgroupsAtIndex.Key] != null) // check currently assigned subgroup
            {
                if (!requiredSubgroupsAtIndex.Value.Intersect(currentCombination.ContainedSubgroups[requiredSubgroupsAtIndex.Key].ContainedSubgroupIDs).Any())
                {
                    _logger.LogReport("\tSubgroup " + targetSubgroup.Id + " cannot be added because it requires (" + String.Join('|', requiredSubgroupsAtIndex.Value) + ") which is incompatible with the currently assigned subgroup at position " + requiredSubgroupsAtIndex.Key + Environment.NewLine, false, npcInfo);
                    return false;
                }
            }
            // check candidate subgroups to be assigned
            trialSubgroups[requiredSubgroupsAtIndex.Key] = chosenAssetPack.Subgroups[requiredSubgroupsAtIndex.Key].Where(x => requiredSubgroupsAtIndex.Value.Intersect(x.ContainedSubgroupIDs).Any()).ToList();
            if (!trialSubgroups[requiredSubgroupsAtIndex.Key].Any())
            {
                _logger.LogReport("\tSubgroup " + targetSubgroup.Id + " cannot be added because it requires (" + String.Join('|', requiredSubgroupsAtIndex.Value) + ") which eliminates all options at position " + requiredSubgroupsAtIndex.Key + Environment.NewLine, false, npcInfo);
                return false;
            }
        }

        filteredAssetPack.Subgroups = trialSubgroups;
        return true;
    }

    private static void GenerateSubgroupPlaceHolders(SubgroupCombination generatedCombination, FlattenedAssetPack chosenAssetPack)
    {
        for (int i = 0; i < chosenAssetPack.Subgroups.Count; i++)
        {
            generatedCombination.ContainedSubgroups.Add(null); // set up placeholders for subgroups
        }
    }

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
                    forcedAssetPack = assetPacksToBeFiltered.Where(x => x.GroupName == npcInfo.SpecificNPCAssignment.AssetPackName).FirstOrDefault();
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
                    var forcedMixIn = npcInfo.SpecificNPCAssignment.MixInAssignments.Where(x => x.AssetPackName == availableAssetPacks.First().GroupName).FirstOrDefault();
                    if (forcedMixIn != null)
                    {
                        forcedAssetPack = availableAssetPacks.First().ShallowCopy();
                        forcedAssignments = GetForcedSubgroupsAtIndex(forcedAssetPack, forcedMixIn.SubgroupIDs, npcInfo);
                        _logger.LogReport("Found Asset Pack set by Specific NPC Assignment: " + forcedMixIn.AssetPackName, false, npcInfo);
                    }
                    break;
                case AssetPackAssignmentMode.ReplacerVirtual:
                    var forcedReplacerGroup = npcInfo.SpecificNPCAssignment.AssetReplacerAssignments.Where(x => x.ReplacerName == availableAssetPacks.First().ReplacerName && x.AssetPackName == availableAssetPacks.First().GroupName).FirstOrDefault(); // Replacers are assigned from a pre-chosen asset pack so there must be exactly one in the set
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
            else if (!SubgroupValidForCurrentNPC(candidatePack.DistributionRules, npcInfo, mode, assignedBodyGen, assignedBodySlides)) // check distribution rules for whole config
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

            for (int i = 0; i < candidatePack.Subgroups.Count; i++)
            {
                for (int j = 0; j < candidatePack.Subgroups[i].Count; j++)
                {
                    bool isSpecificNPCAssignment = forcedAssetPack != null && forcedAssignments[i].Any();
                    if (!isSpecificNPCAssignment && !SubgroupValidForCurrentNPC(candidatePack.Subgroups[i][j], npcInfo, mode, assignedBodyGen, assignedBodySlides))
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
            if (isValid)
            {
                _logger.LogReport("Available Subgroups by index: " + Environment.NewLine + string.Join(Environment.NewLine, subgroupsByPositionLog), false, npcInfo);
                filteredPacks.Add(candidatePack);
            }
            _logger.CloseReportSubsectionsTo("SubGroupDistributionRules", npcInfo);
        }

        if (filteredPacks.Any())
        {
            int maxMatchedConfigForceIfs = filteredPacks.Select(x => x.MatchedWholeConfigForceIfs).Max();
            for (int i = 0; i < filteredPacks.Count; i++)
            {
                if (filteredPacks[i].MatchedWholeConfigForceIfs < maxMatchedConfigForceIfs)
                {
                    _logger.LogReport("Asset Pack: " + filteredPacks[i].GroupName + " was removed because it has fewer (" + filteredPacks[i].MatchedWholeConfigForceIfs + ") than the maximal (" + maxMatchedConfigForceIfs.ToString() + ") matched ForceIf attributes.", false, npcInfo);
                    filteredPacks.RemoveAt(i);
                    i--;
                }
            }
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
                    var consistencyMixIn = npcInfo.ConsistencyNPCAssignment.MixInAssignments.Where(x => x.AssetPackName == availableAssetPacks.First().GroupName).FirstOrDefault();
                    if (consistencyMixIn != null)
                    {
                        consistencyAssetPackName = availableAssetPacks.First().GroupName;
                    }
                    break;
                case AssetPackAssignmentMode.ReplacerVirtual:
                    consistencyReplacer = npcInfo.ConsistencyNPCAssignment.AssetReplacerAssignments.Where(x => x.ReplacerName == availableAssetPacks.First().ReplacerName && x.AssetPackName == availableAssetPacks.First().GroupName).FirstOrDefault();
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
                                var consistencyMixIn = npcInfo.ConsistencyNPCAssignment.MixInAssignments.Where(x => x.AssetPackName == availableAssetPacks.First().GroupName).FirstOrDefault();
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
                                    else if (!SubgroupValidForCurrentNPC(consistencySubgroup, npcInfo, mode, assignedBodyGen, assignedBodySlides))
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
            _logger.LogMessage("No valid asset packs could be found for NPC " + npcInfo.LogIDstring);
        }

        _logger.CloseReportSubsection(npcInfo);

        return filteredPacks.ToHashSet();
    }

    /// <summary>
    /// Returns false if the given subgroup is incompatible with the given NPC due to any of the rules defined within the subgroup.
    /// </summary>
    /// <param name="subgroup"></param>
    /// <param name="npcInfo"></param>
    /// <param name="forceIfAttributeCount">The number of ForceIf attributes within this subgroup that were matched by the current NPC</param>
    /// <returns></returns>
    private bool SubgroupValidForCurrentNPC(FlattenedSubgroup subgroup, NPCInfo npcInfo, AssetPackAssignmentMode mode, List<BodyGenConfig.BodyGenTemplate> assignedBodyGen, List<BodySlideSetting> assignedBodySlides)
    {
        var reportString = "Subgroup " + subgroup.GetDetailedID_NameString(false);
        if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.SubgroupIDs.Contains(subgroup.Id))
        {
            _logger.LogReport(reportString + "is valid because it is specifically assigned by user.", false, npcInfo);
            return true;
        }

        // Allow unique NPCs
        if (!subgroup.AllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            _logger.LogReport(reportString + "is invalid because it is disallowed for unique NPCs", false, npcInfo);
            return false;
        }

        // Allow non-unique NPCs
        if (!subgroup.AllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            _logger.LogReport(reportString + "is invalid because it is disallowed for non-unique NPCs", false, npcInfo);
            return false;
        }

        // Allowed Races
        if (!subgroup.AllowedRacesIsEmpty && !subgroup.AllowedRaces.Contains(npcInfo.AssetsRace))
        {
            _logger.LogReport(reportString + "is invalid because its allowed races (" + Logger.GetRaceListLogStrings(subgroup.AllowedRaces, _environmentProvider.LinkCache, _patcherState) + ") do not include the current NPC's race (" + Logger.GetRaceLogString(npcInfo.AssetsRace, _environmentProvider.LinkCache, _patcherState) + ")", false, npcInfo);
            return false;
        }

        // Disallowed Races
        if (subgroup.DisallowedRaces.Contains(npcInfo.AssetsRace))
        {
            _logger.LogReport(reportString + "is invalid because its disallowed races (" + Logger.GetRaceListLogStrings(subgroup.DisallowedRaces, _environmentProvider.LinkCache, _patcherState) + ") include the current NPC's race (" + Logger.GetRaceLogString(npcInfo.AssetsRace, _environmentProvider.LinkCache, _patcherState) + ")", false, npcInfo);
            return false;
        }

        // Weight Range
        if (npcInfo.NPC.Weight < subgroup.WeightRange.Lower || npcInfo.NPC.Weight > subgroup.WeightRange.Upper)
        {
            _logger.LogReport(reportString + "is invalid because the current NPC's weight falls outside of the morph's allowed weight range", false, npcInfo);
            return false;
        }

        // Allowed and Forced Attributes
        subgroup.ForceIfMatchCount = 0;
        _attributeMatcher.MatchNPCtoAttributeList(subgroup.AllowedAttributes, npcInfo.NPC, npcInfo.AssetsRace, subgroup.ParentAssetPack.Source.AttributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfWeightedCount, out string _, out string unmatchedLog, out string forceIfLog, null);
        if (hasAttributeRestrictions && !matchesAttributeRestrictions)
        {
            _logger.LogReport(reportString + " is invalid because the NPC does not match any of its allowed attributes: " + unmatchedLog, false, npcInfo);
            return false;
        }
        else
        {
            subgroup.ForceIfMatchCount = matchedForceIfWeightedCount;
        }

        if (subgroup.ForceIfMatchCount > 0)
        {
            _logger.LogReport(reportString + " Current NPC matches the following forced attributes: " + forceIfLog, false, npcInfo);
        }

        // Disallowed Attributes
        _attributeMatcher.MatchNPCtoAttributeList(subgroup.DisallowedAttributes, npcInfo.NPC, npcInfo.AssetsRace, subgroup.ParentAssetPack.Source.AttributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out hasAttributeRestrictions, out matchesAttributeRestrictions, out int dummy, out string matchLog, out string _, out string _, null);
        if (hasAttributeRestrictions && matchesAttributeRestrictions)
        {
            _logger.LogReport(reportString + " is invalid because the NPC matches one of its disallowed attributes: " + matchLog, false, npcInfo);
            return false;
        }

        // if the current subgroup's forceIf attributes match the current NPC, skip the checks for Distribution Enabled

        // Distribution Enabled
        if (subgroup.ForceIfMatchCount == 0 && !subgroup.DistributionEnabled)
        {
            _logger.LogReport(reportString + "is invalid because its distribution is disabled to random NPCs, it is not a Specific NPC Assignment, and the NPC does not match and of its ForceIf attributes.", false, npcInfo);
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
                                _logger.LogReport(reportString + " is invalid because its allowed descriptors do not include any of those annotated in the descriptors of assigned morph " + bodyGenTemplate.Label + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(subgroup.AllowedBodyGenDescriptors), false, npcInfo);
                                return false;
                            }

                            if (BodyShapeDescriptor.DescriptorsMatch(subgroup.DisallowedBodyGenDescriptors, bodyGenTemplate.BodyShapeDescriptors, subgroup.DisallowedBodyGenMatchMode, out string matchedDescriptor))
                            {
                                _logger.LogReport(reportString + " is invalid because its descriptor [" + matchedDescriptor + "] is disallowed by assigned morph " + bodyGenTemplate.Label + "'s descriptors", false, npcInfo);
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
                            if (subgroup.AllowedBodySlideDescriptors.Any() && !BodyShapeDescriptor.DescriptorsMatch(subgroup.AllowedBodySlideDescriptors, assignedBodySlide.BodyShapeDescriptors, subgroup.AllowedBodySlideMatchMode, out _))
                            {
                                _logger.LogReport(reportString + " is invalid because its allowed descriptors do not include any of those annotated in the descriptors of assigned bodyslide " + assignedBodySlide.Label + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(subgroup.AllowedBodyGenDescriptors), false, npcInfo);
                                return false;
                            }

                            if (BodyShapeDescriptor.DescriptorsMatch(subgroup.DisallowedBodySlideDescriptors, assignedBodySlide.BodyShapeDescriptors, subgroup.DisallowedBodySlideMatchMode, out string matchedDescriptor))
                            {
                                _logger.LogReport(reportString + " is invalid because its descriptor [" + matchedDescriptor + "] is disallowed by assigned bodyslide " + assignedBodySlide.Label + "'s descriptors", false, npcInfo);
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
                    foreach (var id in specificAssignment.SubgroupIDs)
                    {
                        if (!selectedCombination.ContainedSubgroups.Select(x => x.Id).Any())
                        {
                            return false;
                        }
                    }
                }
                break;

            case AssetPackAssignmentMode.MixIn:
                var forcedMixIn = specificAssignment.MixInAssignments.Where(x => x.AssetPackName == selectedCombination.AssignmentName).FirstOrDefault();
                if (forcedMixIn != null)
                {
                    foreach (var id in forcedMixIn.SubgroupIDs)
                    {
                        if (!selectedCombination.ContainedSubgroups.Select(x => x.Id).Any())
                        {
                            return false;
                        }
                    }
                }
                break;
            case AssetPackAssignmentMode.ReplacerVirtual:
                var forcedReplacer = specificAssignment.AssetReplacerAssignments.Where(x => x.ReplacerName == selectedCombination.AssignmentName).FirstOrDefault();
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

    public void RecordMixInAssetConsistencyAndLinkedNPCs(SubgroupCombination assignedCombination, NPCInfo npcInfo, string mixInName, bool declinedViaProbability) // MixIn 
    {
        bool addMixInAssignmentToConsistency = assignedCombination != null || declinedViaProbability;
        if (_patcherState.GeneralSettings.bEnableConsistency && addMixInAssignmentToConsistency)
        {
            var consistencyMixIn = npcInfo.ConsistencyNPCAssignment.MixInAssignments.Where(x => x.AssetPackName == mixInName).FirstOrDefault();
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

    public void RecordReplacerAssetConsistencyAndLinkedNPCs(SubgroupCombination assignedCombination, NPCInfo npcInfo, FlattenedReplacerGroup replacerGroup) // Replacer
    {
        if (_patcherState.GeneralSettings.bEnableConsistency)
        {
            var existingAssignment = npcInfo.ConsistencyNPCAssignment.AssetReplacerAssignments.Where(x => x.ReplacerName == replacerGroup.Name).FirstOrDefault();
            if (existingAssignment != null) { existingAssignment.SubgroupIDs = assignedCombination.ContainedSubgroups.Where(x => x.Id != AssetPack.ConfigDistributionRules.SubgroupIDString).Select(x => x.Id).ToList(); }
            else { npcInfo.ConsistencyNPCAssignment.AssetReplacerAssignments.Add(new NPCAssignment.AssetReplacerAssignment() { AssetPackName = replacerGroup.Source.GroupName, ReplacerName = replacerGroup.Name, SubgroupIDs = assignedCombination.ContainedSubgroups.Where(x => x.Id != AssetPack.ConfigDistributionRules.SubgroupIDString).Select(x => x.Id).ToList() }); }
        }
        if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Primary)
        {
            var existingAssignment = npcInfo.AssociatedLinkGroup.ReplacerAssignments.Where(x => x.ReplacerName == replacerGroup.Name).FirstOrDefault();
            if (existingAssignment != null) { existingAssignment.AssignedReplacerCombination = assignedCombination; }
            else { npcInfo.AssociatedLinkGroup.ReplacerAssignments.Add(new LinkedNPCGroupInfo.LinkedAssetReplacerAssignment() { GroupName = replacerGroup.Source.GroupName, ReplacerName = replacerGroup.Name, AssignedReplacerCombination = assignedCombination }); }
        }

        if (_patcherState.GeneralSettings.bLinkNPCsWithSameName)
        {
            _uniqueNPCData.InitializeUnsetUniqueNPCPReplacerAssets(npcInfo, new() { GroupName = replacerGroup.Source.GroupName, ReplacerName = replacerGroup.Name, AssignedReplacerCombination = assignedCombination });
        }
    }

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

    private bool SkipMixInByProbability(FlattenedAssetPack mixInPack, NPCInfo npcInfo)
    {
        if (!BoolByProbability.Decide(mixInPack.DistributionRules.ProbabilityWeighting))
        {
            _logger.LogReport("Mix In " + mixInPack.GroupName + " was chosen at random to NOT be assigned.", false, npcInfo);
            return true;
        }
        else
        {
            return false;
        }
    }

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

    private HashSet<string> _vanillaSkins = new() // may want to consider adding a UI element or Json file for this
    {
        "SkinNaked",
        "SkinNakedBeast"
    };

    private HashSet<string> _vanillaSkinsEasyNPC = new();
}