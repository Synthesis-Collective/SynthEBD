using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class AssetSelector
{
    public static SubgroupCombination GenerateCombination(HashSet<FlattenedAssetPack> availableAssetPacks, NPCInfo npcInfo, AssignmentIteration iterationInfo)
    {
        SubgroupCombination generatedCombination = new SubgroupCombination();
        string generatedSignature = "";
        bool combinationAlreadyTried = false;

        FlattenedSubgroup nextSubgroup;

        Logger.OpenReportSubsection("CombinationGeneration", npcInfo);
        Logger.LogReport("Generating a new combination", false, npcInfo);

        #region Choose New Seed
        if (iterationInfo.ChosenSeed == null)
        {
            if (!iterationInfo.AvailableSeeds.Any())
            {
                Logger.LogReport("No seed subgroups remain. A valid combination cannot be assigned with the given filters.", true, npcInfo);
                Logger.CloseReportSubsectionsToParentOf("CombinationGeneration", npcInfo);
                return null;
            }

            Logger.LogReport("Choosing a new seed subgroup from the following list of available seeds and (matched ForceIf attributes):" + Environment.NewLine + string.Join(Environment.NewLine, iterationInfo.AvailableSeeds.Select(x => x.Id + ": " + x.Name + " (" + x.ForceIfMatchCount + ")")), false, npcInfo);

            if (iterationInfo.AvailableSeeds[0].ForceIfMatchCount > 0)
            {
                iterationInfo.ChosenSeed = iterationInfo.AvailableSeeds[0];
                iterationInfo.ChosenAssetPack = iterationInfo.ChosenSeed.ParentAssetPack.ShallowCopy();
                Logger.LogReport("Chose seed subgroup " + iterationInfo.ChosenSeed.Id + " in " + iterationInfo.ChosenAssetPack.GroupName + " because it had the most matched ForceIf attributes (" + iterationInfo.ChosenSeed.ForceIfMatchCount + ").", false, npcInfo);
            }
            else
            {
                iterationInfo.ChosenSeed = (FlattenedSubgroup)ProbabilityWeighting.SelectByProbability(iterationInfo.AvailableSeeds);
                iterationInfo.ChosenAssetPack = iterationInfo.ChosenSeed.ParentAssetPack.ShallowCopy();
                Logger.LogReport("Chose seed subgroup " + iterationInfo.ChosenSeed.Id + " in " + iterationInfo.ChosenAssetPack.GroupName + " at random", false, npcInfo);
            }

            Logger.OpenReportSubsection("Seed-" + iterationInfo.ChosenSeed.Id.Replace('.', '_'), npcInfo);

            iterationInfo.ChosenAssetPack.Subgroups[iterationInfo.ChosenSeed.TopLevelSubgroupIndex] = new List<FlattenedSubgroup>() { iterationInfo.ChosenSeed }; // filter the seed index so that the seed is the only option
            generatedCombination.AssetPackName = iterationInfo.ChosenAssetPack.GroupName;

            iterationInfo.RemainingVariantsByIndex = new Dictionary<int, FlattenedAssetPack>(); // tracks the available subgroups as the combination gets built up to enable backtracking if the patcher chooses an invalid combination
            for (int i = 0; i < iterationInfo.ChosenAssetPack.Subgroups.Count; i++)
            {
                iterationInfo.RemainingVariantsByIndex.Add(i, null); // set up placeholders for backtracking
            }
            iterationInfo.RemainingVariantsByIndex[0] = iterationInfo.ChosenAssetPack.ShallowCopy(); // initial state of the chosen asset pack
            if (!ConformRequiredExcludedSubgroups(generatedCombination, iterationInfo.ChosenSeed, iterationInfo.ChosenAssetPack, npcInfo, out var filteredAssetPack))
            {
                Logger.LogReport("Cannot create a combination with the chosen seed subgroup due to conflicting required/excluded subgroup rules. Selecting a different seed.", false, npcInfo);
                Logger.CloseReportSubsectionsToParentOf("CombinationGeneration", npcInfo);
                return RemoveInvalidSeed(iterationInfo.AvailableSeeds, iterationInfo); // exit this function and re-enter from caller to choose a new seed
            }
            else
            {
                iterationInfo.ChosenAssetPack = filteredAssetPack;
            }
        }
        #endregion

        for (int i = 0; i < iterationInfo.ChosenAssetPack.Subgroups.Count; i++)
        {
            generatedCombination.ContainedSubgroups.Add(null); // set up placeholders for subgroups
        }
        Logger.LogReport("Available Subgroups:" + Logger.SpreadFlattenedAssetPack(iterationInfo.ChosenAssetPack, 0, false), false, npcInfo);

        for (int i = 0; i < iterationInfo.ChosenAssetPack.Subgroups.Count; i++) // iterate through each position within the combination
        {
            if (generatedCombination.ContainedSubgroups.Count > 0)
            {
                Logger.LogReport("Current Combination: " + String.Join(" , ", generatedCombination.ContainedSubgroups.Where(x => x != null).Select(x => x.Id)) + Environment.NewLine, false, npcInfo);
            }
            Logger.LogReport("Available Subgroups:" + Logger.SpreadFlattenedAssetPack(iterationInfo.ChosenAssetPack, i, true), false, npcInfo);

            #region BackTrack if no options remain
            if (iterationInfo.ChosenAssetPack.Subgroups[i].Count == 0)
            {
                if (i == 0 || (i == 1 && iterationInfo.ChosenSeed.TopLevelSubgroupIndex == 0))
                {
                    Logger.LogReport("Cannot backtrack further with " + iterationInfo.ChosenSeed.Id + " as seed. Selecting a new seed.", false, npcInfo);
                    Logger.CloseReportSubsectionsToParentOf("CombinationGeneration", npcInfo);
                    return RemoveInvalidSeed(iterationInfo.AvailableSeeds, iterationInfo); // exit this function and re-enter from caller to choose a new seed
                }
                else if ((i - 1) == iterationInfo.ChosenSeed.TopLevelSubgroupIndex) // skip over the seed subgroup
                {
                    Logger.LogReport("No subgroups remain at position (" + i + "). Selecting a different subgroup at position " + (i - 2), false, npcInfo);
                    i = AssignmentIteration.BackTrack(iterationInfo, generatedCombination, generatedCombination.ContainedSubgroups[i - 2], i, 2);
                }
                else
                {
                    Logger.LogReport("No subgroups remain at position (" + i + "). Selecting a different subgroup at position " + (i - 1), false, npcInfo);
                    i = AssignmentIteration.BackTrack(iterationInfo, generatedCombination, generatedCombination.ContainedSubgroups[i - 1], i, 1);
                }
                continue;
            }
            #endregion

            #region Pick next subgroup
            if (iterationInfo.ChosenAssetPack.Subgroups[i][0].ForceIfMatchCount > 0)
            {
                nextSubgroup = iterationInfo.ChosenAssetPack.Subgroups[i][0];
                Logger.LogReport("Chose next subgroup: " + nextSubgroup.Id + " at position " + i + " because it had the most matched ForceIf Attributes (" + nextSubgroup.ForceIfMatchCount + ")." + Environment.NewLine, false, npcInfo);
            }
            else
            {
                nextSubgroup = (FlattenedSubgroup)ProbabilityWeighting.SelectByProbability(iterationInfo.ChosenAssetPack.Subgroups[i]);
                Logger.LogReport("Chose next subgroup: " + nextSubgroup.Id + " at position " + i + " at random." + Environment.NewLine, false, npcInfo);
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
                    Logger.LogReport("No combination could be produced in accordance with the current set of required/excluded subgroup rules", false, npcInfo);
                }
                else if (combinationAlreadyTried)
                {
                    Logger.LogReport("This combination (" + generatedSignature + ") has previously been generated", false, npcInfo);
                }

                Logger.LogReport("Selecting a different subgroup at position " + i + ".", false, npcInfo);
                i = AssignmentIteration.BackTrack(iterationInfo, generatedCombination, nextSubgroup, i, 0);
                continue;
            }
            else
            {
                iterationInfo.RemainingVariantsByIndex[i + 1] = iterationInfo.ChosenAssetPack.ShallowCopy(); // store the current state of the current asset pack for backtracking in future iterations if necessary
            }
        }

        iterationInfo.PreviouslyGeneratedCombinations.Add(generatedSignature);
        generatedCombination.AssetPack = iterationInfo.ChosenAssetPack;
        generatedCombination.Signature = generatedSignature;
        Logger.LogReport("Successfully generated combination: " + generatedSignature, false, npcInfo);
        Logger.CloseReportSubsectionsToParentOf("CombinationGeneration", npcInfo);
        return generatedCombination;
    }

    private static SubgroupCombination RemoveInvalidSeed(List<FlattenedSubgroup> seedSubgroups, AssignmentIteration iterationInfo)
    {
        seedSubgroups.Remove(iterationInfo.ChosenSeed);
        iterationInfo.ChosenSeed = null;
        return null;
    }

    private static bool ConformRequiredExcludedSubgroups(SubgroupCombination currentCombination, FlattenedSubgroup targetSubgroup, FlattenedAssetPack chosenAssetPack, NPCInfo npcInfo, out FlattenedAssetPack filteredAssetPack)
    {
        filteredAssetPack = chosenAssetPack;
        if (!targetSubgroup.ExcludedSubgroupIDs.Any() && !targetSubgroup.RequiredSubgroupIDs.Any())
        {
            return true;
        }

        // check if incoming subgroup is allowed by all existing subgroups
        foreach (var subgroup in currentCombination.ContainedSubgroups.Where(x => x is not null))
        {
            foreach (var index in subgroup.RequiredSubgroupIDs)
            {
                var debug1 = index.Value.Intersect(targetSubgroup.ContainedSubgroupIDs);

                if (index.Key == targetSubgroup.TopLevelSubgroupIndex && !index.Value.Intersect(targetSubgroup.ContainedSubgroupIDs).Any())
                {
                    Logger.LogReport("\tSubgroup " + targetSubgroup.Id + " cannot be added because a different subgroup is required at position " + index.Key + " by the already assigned subgroup " + subgroup.Id + Environment.NewLine, false, npcInfo);
                    return false;
                }
            }

            foreach (var index in subgroup.ExcludedSubgroupIDs)
            {
                var debug2 = index.Value.Intersect(targetSubgroup.ContainedSubgroupIDs);

                if (index.Key == targetSubgroup.TopLevelSubgroupIndex && index.Value.Intersect(targetSubgroup.ContainedSubgroupIDs).Any())
                {
                    Logger.LogReport("\tSubgroup " + targetSubgroup.Id + " cannot be added because it is excluded at position " + index.Key + " by the already assigned subgroup " + subgroup.Id + Environment.NewLine, false, npcInfo);
                    return false;
                }
            }
        }

        Logger.LogReport("Trimming remaining subgroups within " + chosenAssetPack.GroupName + " by the required/excluded subgroups of " + targetSubgroup.Id + Environment.NewLine, false, npcInfo);

        // create a shallow copy of the subgroup list to avoid modifying the chosenAssetPack unless the result is confirmed to be valid.
        var trialSubgroups = new List<List<FlattenedSubgroup>>();
        for (int i = 0; i < chosenAssetPack.Subgroups.Count; i++)
        {
            trialSubgroups.Add(new List<FlattenedSubgroup>(chosenAssetPack.Subgroups[i]));
        }

        // check excluded subgroups of incoming subgroup
        foreach (var index in targetSubgroup.ExcludedSubgroupIDs)
        {
            trialSubgroups[index.Key] = chosenAssetPack.Subgroups[index.Key].Where(x => !index.Value.Intersect(x.ContainedSubgroupIDs).Any()).ToList();
            if (!trialSubgroups[index.Key].Any())
            {
                Logger.LogReport("\tSubgroup " + targetSubgroup.Id + " cannot be added because it excludes (" + String.Join(',', index.Value) + ") which eliminates all options at position " + index.Key + Environment.NewLine, false, npcInfo);
                return false;
            }
        }

        // check required subgroups of incoming subgroup
        foreach (var index in targetSubgroup.RequiredSubgroupIDs)
        {
            trialSubgroups[index.Key] = chosenAssetPack.Subgroups[index.Key].Where(x => index.Value.Intersect(x.ContainedSubgroupIDs).Any()).ToList();
            if (!trialSubgroups[index.Key].Any())
            {
                Logger.LogReport("\tSubgroup " + targetSubgroup.Id + " cannot be added because it requires (" + String.Join('|', index.Value) + ") which eliminates all options at position " + index.Key + Environment.NewLine, false, npcInfo);
                return false;
            }
        }

        filteredAssetPack.Subgroups = trialSubgroups;
        return true;
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
    public static HashSet<FlattenedAssetPack> FilterValidConfigsForNPC(HashSet<FlattenedAssetPack> availableAssetPacks, NPCInfo npcInfo, bool ignoreConsistency, out bool wasFilteredByConsistency, AssetAndBodyShapeSelector.AssetPackAssignmentMode mode, AssetAndBodyShapeSelector.AssetAndBodyShapeAssignment currentAssignments)
    {
        Logger.OpenReportSubsection("ConfigFiltering", npcInfo);
        HashSet<FlattenedAssetPack> assetPacksToBeFiltered = new HashSet<FlattenedAssetPack>(availableAssetPacks); // available asset packs filtered by Specific NPC Assignments and Consistency
        List<FlattenedAssetPack> filteredPacks = new List<FlattenedAssetPack>(); // available asset packs (further) filtered by current NPC's compliance with each subgroup's rule set
        wasFilteredByConsistency = false;
        List<List<FlattenedSubgroup>> forcedAssignments = null; // must be a nested list because if the user forces a non-bottom-level subgroup, then at a given index multiple options will be forced

        #region handle specific NPC assignments
        FlattenedAssetPack forcedAssetPack = null;
        if (npcInfo.SpecificNPCAssignment != null)
        {
            Logger.OpenReportSubsection("SpecificAssignments", npcInfo);
            // check to make sure forced asset pack exists
            switch (mode)
            {
                case AssetAndBodyShapeSelector.AssetPackAssignmentMode.Primary:
                    forcedAssetPack = assetPacksToBeFiltered.Where(x => x.GroupName == npcInfo.SpecificNPCAssignment.AssetPackName).FirstOrDefault();
                    if (forcedAssetPack != null)
                    {
                        forcedAssetPack = forcedAssetPack.ShallowCopy(); // don't forget to shallow copy or subsequent NPCs will get pruned asset packs
                        forcedAssignments = GetForcedSubgroupsAtIndex(forcedAssetPack, npcInfo.SpecificNPCAssignment.SubgroupIDs, npcInfo);
                    }
                    else if (!string.IsNullOrWhiteSpace(npcInfo.SpecificNPCAssignment.AssetPackName))
                    {
                        Logger.LogMessage("Specific NPC Assignment for " + npcInfo.LogIDstring + " requests asset pack " + forcedAssetPack + " which does not exist. Choosing a random asset pack.");
                    }
                    break;
                case AssetAndBodyShapeSelector.AssetPackAssignmentMode.MixIn:
                    var forcedMixIn = npcInfo.SpecificNPCAssignment.MixInAssignments.Where(x => x.AssetPackName == availableAssetPacks.First().GroupName).FirstOrDefault();
                    if (forcedMixIn != null)
                    {
                        forcedAssetPack = availableAssetPacks.First().ShallowCopy();
                        forcedAssignments = GetForcedSubgroupsAtIndex(forcedAssetPack, forcedMixIn.SubgroupIDs, npcInfo);
                    }
                    break;
                case AssetAndBodyShapeSelector.AssetPackAssignmentMode.ReplacerVirtual:
                    var forcedReplacerGroup = npcInfo.SpecificNPCAssignment.AssetReplacerAssignments.Where(x => x.ReplacerName == availableAssetPacks.First().ReplacerName && x.AssetPackName == availableAssetPacks.First().GroupName).FirstOrDefault(); // Replacers are assigned from a pre-chosen asset pack so there must be exactly one in the set
                    if (forcedReplacerGroup != null)
                    {
                        forcedAssetPack = availableAssetPacks.First().ShallowCopy();
                        forcedAssignments = GetForcedSubgroupsAtIndex(forcedAssetPack, forcedReplacerGroup.SubgroupIDs, npcInfo);
                    }
                    break;
            }

            if (forcedAssignments != null)
            {
                //Prune forced asset pack to only include forced subgroups at their respective indices
                forcedAssignments = GetForcedSubgroupsAtIndex(forcedAssetPack, npcInfo.SpecificNPCAssignment.SubgroupIDs, npcInfo);
                for (int i = 0; i < forcedAssignments.Count; i++)
                {
                    if (forcedAssignments[i].Any())
                    {
                        forcedAssetPack.Subgroups[i] = forcedAssignments[i];
                    }
                }

                assetPacksToBeFiltered = new HashSet<FlattenedAssetPack>() { forcedAssetPack };
            }
            Logger.CloseReportSubsectionsTo("ConfigFiltering", npcInfo);
        }
        #endregion

        // evaluate config distribution rules
        Logger.OpenReportSubsection("RulesEvaluation", npcInfo);

        #region handle non-predefined asset packs
        Logger.OpenReportSubsection("ConfigDistributionRules", npcInfo);
        var filteredByMainConfigRules = new List<FlattenedAssetPack>();
        foreach (var ap in assetPacksToBeFiltered)
        {
            Logger.OpenReportSubsection("AssetPack", npcInfo);
            Logger.LogReport("Evaluating distribution rules for asset pack: " + ap.GroupName, false, npcInfo);
            var candidatePack = ap.ShallowCopy();
            if (!SubgroupValidForCurrentNPC(candidatePack.DistributionRules, npcInfo, mode, currentAssignments)) // check distribution rules for whole config
            {
                Logger.LogReport("Asset Pack " + ap.GroupName + " is invalid due to its main distribution rules.", false, npcInfo);
            }
            else
            {
                filteredByMainConfigRules.Add(candidatePack);
            }
            Logger.CloseReportSubsectionsTo("ConfigDistributionRules", npcInfo);
        }

        filteredByMainConfigRules = filteredByMainConfigRules.OrderByDescending(x => x.DistributionRules.ForceIfMatchCount).ToList(); // remove asset packs with less than the max ForceIf attributes
        if (filteredByMainConfigRules.Count > 1 && filteredByMainConfigRules[0].DistributionRules.ForceIfMatchCount > 0)
        {
            for (int i = 1; i < filteredByMainConfigRules.Count; i++)
            {
                if (filteredByMainConfigRules[i].DistributionRules.ForceIfMatchCount < filteredByMainConfigRules[0].DistributionRules.ForceIfMatchCount)
                {
                    Logger.LogReport("Asset Pack " + filteredByMainConfigRules[i].GroupName + " was removed because another Asset Pack has more matched ForceIf attributes for this NPC", false, npcInfo);
                    filteredByMainConfigRules.RemoveAt(i);
                    i--;
                }
            }
        }
        assetPacksToBeFiltered = filteredByMainConfigRules.ToHashSet();
        Logger.CloseReportSubsectionsTo("RulesEvaluation", npcInfo);
        #endregion

        #region Check distribution rules for each subgroup
        Logger.OpenReportSubsection("SubGroupDistributionRules", npcInfo);
        foreach (var ap in assetPacksToBeFiltered)
        {
            Logger.OpenReportSubsection("AssetPack", npcInfo);
            Logger.LogReport("Filtering subgroups within asset pack: " + ap.GroupName, false, npcInfo);
            var candidatePack = ap.ShallowCopy();
            bool isValid = true;

            for (int i = 0; i < candidatePack.Subgroups.Count; i++)
            {
                for (int j = 0; j < candidatePack.Subgroups[i].Count; j++)
                {
                    bool isSpecificNPCAssignment = forcedAssetPack != null && forcedAssignments[i].Any();
                    if (!isSpecificNPCAssignment && !SubgroupValidForCurrentNPC(candidatePack.Subgroups[i][j], npcInfo, mode, currentAssignments))
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
                        Logger.LogReport("Asset Pack " + ap.GroupName + " is forced for NPC " + npcInfo.LogIDstring + " but no subgroups within " + ap.Subgroups[i][0].Id + ":" + ap.Subgroups[i][0].Name + " are compatible with this NPC. Ignoring subgroup rules at this position.", true, npcInfo);
                        candidatePack.Subgroups[i] = new List<FlattenedSubgroup>(ap.Subgroups[i]); // revert list back to unfiltered version at this position
                    }
                    else
                    {
                        Logger.LogReport("Asset Pack " + ap.GroupName + " is invalid for NPC " + npcInfo.LogIDstring + " because no subgroups within " + ap.Source.Subgroups[i].ID + " (" + ap.Source.Subgroups[i].Name + ") are compatible with this NPC.", false, npcInfo);
                        isValid = false;
                        break;
                    }
                }
                else
                {

                    if (mode != AssetAndBodyShapeSelector.AssetPackAssignmentMode.ReplacerVirtual && i == candidatePack.Subgroups.Count - 1 && ap.Subgroups[i].Count > 0) // record forceIf count for whole config file from the "fake" distribution rules subgroup
                    {
                        ap.MatchedWholeConfigForceIfs = ap.Subgroups[i][0].ForceIfMatchCount;
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
                                    Logger.LogReport("Subgroup: " + candidatePack.Subgroups[i][j].Id + "(" + candidatePack.Subgroups[i][j].Name + ") was removed because another subgroup in position " + (i + 1).ToString() + " had more matched ForceIf attributes.", false, npcInfo);
                                    candidatePack.Subgroups[i].RemoveAt(j);
                                    j--;
                                }
                            }
                        }
                    }
                }
            }
            if (isValid)
            {
                filteredPacks.Add(candidatePack);
            }
            Logger.CloseReportSubsectionsTo("SubGroupDistributionRules", npcInfo);
        }

        if (filteredPacks.Any())
        {
            int maxMatchedConfigForceIfs = filteredPacks.Select(x => x.MatchedWholeConfigForceIfs).Max();
            for (int i = 0; i < filteredPacks.Count; i++)
            {
                if (filteredPacks[i].MatchedWholeConfigForceIfs < maxMatchedConfigForceIfs)
                {
                    Logger.LogReport("Asset Pack: " + filteredPacks[i].GroupName + " was removed because it has fewer (" + filteredPacks[i].MatchedWholeConfigForceIfs + ") than the maximal (" + maxMatchedConfigForceIfs.ToString() + ") matched ForceIf attributes.", false, npcInfo);
                    filteredPacks.RemoveAt(i);
                    i--;
                }
            }
        }
        #endregion

        Logger.CloseReportSubsectionsTo("ConfigFiltering", npcInfo);

        #region handle consistency 
        if (!ignoreConsistency && npcInfo.ConsistencyNPCAssignment != null && filteredPacks.Any()) // (must be last to ensure subordinance to ForceIf attribute count which is determined by evaluating all available subgroups)
        {
            Logger.OpenReportSubsection("Consistency", npcInfo);
            string consistencyAssetPackName = "";
            NPCAssignment.AssetReplacerAssignment consistencyReplacer = null;
            switch (mode)
            {
                case AssetAndBodyShapeSelector.AssetPackAssignmentMode.Primary: consistencyAssetPackName = npcInfo.ConsistencyNPCAssignment.AssetPackName; break;
                case AssetAndBodyShapeSelector.AssetPackAssignmentMode.MixIn:
                    var consistencyMixIn = npcInfo.ConsistencyNPCAssignment.MixInAssignments.Where(x => x.AssetPackName == availableAssetPacks.First().GroupName).FirstOrDefault();
                    if (consistencyMixIn != null)
                    {
                        consistencyAssetPackName = availableAssetPacks.First().GroupName;
                    }
                    break;
                case AssetAndBodyShapeSelector.AssetPackAssignmentMode.ReplacerVirtual:
                    consistencyReplacer = npcInfo.ConsistencyNPCAssignment.AssetReplacerAssignments.Where(x => x.ReplacerName == availableAssetPacks.First().ReplacerName && x.AssetPackName == availableAssetPacks.First().GroupName).FirstOrDefault();
                    if (consistencyReplacer != null) { consistencyAssetPackName = consistencyReplacer.ReplacerName; }
                    break;
            }

            if (!string.IsNullOrWhiteSpace(consistencyAssetPackName))
            {
                // check to make sure consistency asset pack is compatible with the specific NPC assignment
                if (forcedAssetPack != null && forcedAssetPack.GroupName != "" && forcedAssetPack.GroupName != consistencyAssetPackName)
                {
                    Logger.LogReport("Asset Pack defined by forced asset pack (" + npcInfo.SpecificNPCAssignment.AssetPackName + ") supercedes consistency asset pack (" + npcInfo.ConsistencyNPCAssignment.AssetPackName + ")", false, npcInfo);
                }
                else
                {
                    // check to make sure consistency asset pack exists
                    var consistencyAssetPack = filteredPacks.FirstOrDefault(x => x.GroupName == consistencyAssetPackName);
                    if (consistencyAssetPack == null)
                    {
                        Logger.LogReport("The asset pack specified in the consistency file (" + npcInfo.ConsistencyNPCAssignment.AssetPackName + ") is not available.", true, npcInfo);
                    }
                    else
                    {
                        Logger.LogReport("Selecting consistency Asset Pack (" + npcInfo.ConsistencyNPCAssignment.AssetPackName + ").", false, npcInfo);
                        //consistencyAssetPack = consistencyAssetPack.ShallowCopy(); // otherwise subsequent NPCs will get pruned packs as the consistency pack is modified in the current round of patching

                        // check each subgroup against specific npc assignment
                        List<string> consistencySubgroupIDs = null;
                        switch (mode)
                        {
                            case AssetAndBodyShapeSelector.AssetPackAssignmentMode.Primary: consistencySubgroupIDs = npcInfo.ConsistencyNPCAssignment.SubgroupIDs; break;
                            case AssetAndBodyShapeSelector.AssetPackAssignmentMode.MixIn:
                                var consistencyMixIn = npcInfo.ConsistencyNPCAssignment.MixInAssignments.Where(x => x.AssetPackName == availableAssetPacks.First().GroupName).FirstOrDefault();
                                if (consistencyMixIn != null) { consistencySubgroupIDs = consistencyMixIn.SubgroupIDs; }
                                break;
                            case AssetAndBodyShapeSelector.AssetPackAssignmentMode.ReplacerVirtual: consistencySubgroupIDs = consistencyReplacer.SubgroupIDs; break;
                        }

                        for (int i = 0; i < consistencySubgroupIDs.Count; i++)
                        {
                            // make sure consistency subgroup doesn't conflict with user-forced subgroup if one exists
                            if (forcedAssignments != null && forcedAssignments[i].Any())
                            {
                                if (forcedAssignments[i].Select(x => x.Id).Contains(consistencySubgroupIDs[i]))
                                {
                                    consistencyAssetPack.Subgroups[i] = new List<FlattenedSubgroup>() { forcedAssignments[i].First(x => x.Id == consistencySubgroupIDs[i]) }; // guaranteed to have at least one subgroup or else the upstream if would fail, so use First instead of Where
                                    Logger.LogReport("Consistency subgroup " + consistencySubgroupIDs[i] + " is compatible with the Specific NPC Assignment.", false, npcInfo);
                                }
                                else
                                {
                                    Logger.LogReport("Consistency subgroup " + consistencySubgroupIDs[i] + " is incompatible with the Specific NPC Assignment at position " + i + ".", true, npcInfo);
                                }
                            }
                            // if no user-forced subgroup exists, simply make sure that the consistency subgroup exists
                            else
                            {
                                FlattenedSubgroup consistencySubgroup = consistencyAssetPack.Subgroups[i].FirstOrDefault(x => x.Id == consistencySubgroupIDs[i]);
                                if (consistencySubgroup == null)
                                {
                                    Logger.LogReport("The consistency subgroup " + consistencySubgroupIDs[i] + " was either filtered out or no longer exists within the config file. Choosing a different subgroup at this position.", true, npcInfo);
                                }
                                else if (!SubgroupValidForCurrentNPC(consistencySubgroup, npcInfo, mode, currentAssignments))
                                {
                                    Logger.LogReport("Consistency subgroup " + consistencySubgroup.Id + " (" + consistencySubgroup.Name + ") is no longer valid for this NPC. Choosing a different subgroup at this position", true, npcInfo);
                                    consistencyAssetPack.Subgroups[i].Remove(consistencySubgroup);
                                }
                                else
                                {
                                    consistencyAssetPack.Subgroups[i] = new List<FlattenedSubgroup>() { consistencySubgroup };
                                    Logger.LogReport("Using consistency subgroup " + consistencySubgroupIDs[i] + ".", true, npcInfo);
                                }
                            }
                        }
                        filteredPacks = new List<FlattenedAssetPack>() { consistencyAssetPack };
                        wasFilteredByConsistency = true;
                    }
                }
            }
            Logger.CloseReportSubsectionsTo("ConfigFiltering", npcInfo);
        }
        #endregion

        if (filteredPacks.Count == 0 && mode == AssetAndBodyShapeSelector.AssetPackAssignmentMode.Primary)
        {
            Logger.LogMessage("No valid asset packs could be found for NPC " + npcInfo.LogIDstring);
        }

        Logger.CloseReportSubsection(npcInfo);

        return filteredPacks.ToHashSet();
    }

    /// <summary>
    /// Returns false if the given subgroup is incompatible with the given NPC due to any of the rules defined within the subgroup.
    /// </summary>
    /// <param name="subgroup"></param>
    /// <param name="npcInfo"></param>
    /// <param name="forceIfAttributeCount">The number of ForceIf attributes within this subgroup that were matched by the current NPC</param>
    /// <returns></returns>
    private static bool SubgroupValidForCurrentNPC(FlattenedSubgroup subgroup, NPCInfo npcInfo, AssetAndBodyShapeSelector.AssetPackAssignmentMode mode, AssetAndBodyShapeSelector.AssetAndBodyShapeAssignment currentAssignments)
    {
        var reportString = "Subgroup " + subgroup.Id + "(" + subgroup.Name + ") ";
        if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.SubgroupIDs.Contains(subgroup.Id))
        {
            Logger.LogReport(reportString + "is valid because it is specifically assigned by user.", false, npcInfo);
            return true;
        }

        // Allow unique NPCs
        if (!subgroup.AllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            Logger.LogReport(reportString + "is invalid because the current morph is disallowed for unique NPCs", false, npcInfo);
            return false;
        }

        // Allow non-unique NPCs
        if (!subgroup.AllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            Logger.LogReport(reportString + "is invalid because the current morph is disallowed for non-unique NPCs", false, npcInfo);
            return false;
        }

        // Allowed Races
        if (!subgroup.AllowedRacesIsEmpty && !subgroup.AllowedRaces.Contains(npcInfo.AssetsRace))
        {
            Logger.LogReport(reportString + "is invalid because its allowed races do not include the current NPC's race", false, npcInfo);
            return false;
        }

        // Disallowed Races
        if (subgroup.DisallowedRaces.Contains(npcInfo.AssetsRace))
        {
            Logger.LogReport(reportString + "is invalid because its disallowed races include the current NPC's race", false, npcInfo);
            return false;
        }

        // Weight Range
        if (npcInfo.NPC.Weight < subgroup.WeightRange.Lower || npcInfo.NPC.Weight > subgroup.WeightRange.Upper)
        {
            Logger.LogReport(reportString + "is invalid because the current NPC's weight falls outside of the morph's allowed weight range", false, npcInfo);
            return false;
        }

        // Allowed Attributes
        if (subgroup.AllowedAttributes.Any() && !AttributeMatcher.HasMatchedAttributes(subgroup.AllowedAttributes, npcInfo.NPC, LogMatchType.Unmatched, out string unmatchedAttributes))
        {
            Logger.LogReport(reportString + " is invalid because the NPC does not match any of its allowed attributes: " + unmatchedAttributes, false, npcInfo);
            return false;
        }

        // Disallowed Attributes
        if (AttributeMatcher.HasMatchedAttributes(subgroup.DisallowedAttributes, npcInfo.NPC, LogMatchType.Matched, out string matchedAttributes))
        {
            Logger.LogReport(reportString + " is invalid because the NPC matches one of its disallowed attributes: " + matchedAttributes, false, npcInfo);
            return false;
        }

        // if the current subgroup's forceIf attributes match the current NPC, skip the checks for Distribution Enabled

        subgroup.ForceIfMatchCount = AttributeMatcher.GetForceIfAttributeCount(subgroup.AllowedAttributes, npcInfo.NPC, out string forceIfAttributes);
        if (subgroup.ForceIfMatchCount > 0)
        {
            Logger.LogReport(reportString + ": current NPC matches the following ForceIf attributes: " + forceIfAttributes, false, npcInfo);
        }

        // Distribution Enabled
        if (subgroup.ForceIfMatchCount == 0 && !subgroup.DistributionEnabled)
        {
            Logger.LogReport(reportString + "is invalid because its distribution is disabled to random NPCs, it is not a Specific NPC Assignment, and the NPC does not match and of its ForceIf attributes.", false, npcInfo);
            return false;
        }

        if (mode != AssetAndBodyShapeSelector.AssetPackAssignmentMode.Primary)
        {
            switch (PatcherSettings.General.BodySelectionMode)
            {
                case BodyShapeSelectionMode.BodyGen:
                    if (currentAssignments != null && currentAssignments.AssignedBodyGenMorphs != null)
                    {
                        foreach (var bodyGenTemplate in currentAssignments.AssignedBodyGenMorphs)
                        {
                            if (subgroup.AllowedBodyGenDescriptors.Any() && !BodyShapeDescriptor.DescriptorsMatch(subgroup.AllowedBodyGenDescriptors, bodyGenTemplate.BodyShapeDescriptors))
                            {
                                Logger.LogReport(reportString + " is invalid because its allowed descriptors do not include any of those annotated in the descriptors of assigned morph " + bodyGenTemplate.Label, false, npcInfo);
                                return false;
                            }

                            if (BodyShapeDescriptor.DescriptorsMatch(subgroup.DisallowedBodyGenDescriptors, bodyGenTemplate.BodyShapeDescriptors))
                            {
                                Logger.LogReport(reportString + " is invalid because its disallowed descriptors include one of those annotated in the assigned morph " + bodyGenTemplate.Label + "'s descriptors", false, npcInfo);
                                return false;
                            }
                        }
                    }
                    break;
                case BodyShapeSelectionMode.BodySlide:
                    if (currentAssignments != null && currentAssignments.AssignedOBodyPreset != null)
                    {
                        if (subgroup.AllowedBodySlideDescriptors.Any() && !BodyShapeDescriptor.DescriptorsMatch(subgroup.AllowedBodySlideDescriptors, currentAssignments.AssignedOBodyPreset.BodyShapeDescriptors))
                        {
                            Logger.LogReport(reportString + " is invalid because its allowed descriptors do not include any of those annotated in the assigned bodyslide's descriptors", false, npcInfo);
                            return false;
                        }

                        if (BodyShapeDescriptor.DescriptorsMatch(subgroup.DisallowedBodySlideDescriptors, currentAssignments.AssignedOBodyPreset.BodyShapeDescriptors))
                        {
                            Logger.LogReport(reportString + " is invalid because its disallowed descriptors include one of those annotated in the assigned bodyslide's descriptors", false, npcInfo);
                            return false;
                        }
                    }
                    break;
            }
        }

        // If the subgroup is still valid
        return true;
    }

    private static List<List<FlattenedSubgroup>> GetForcedSubgroupsAtIndex(FlattenedAssetPack input, List<string> forcedSubgroupIDs, NPCInfo npcInfo)
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
                Logger.LogReport("Subgroup " + id + " requested by Specific NPC Assignment was not found in Asset Pack " + input.GroupName, true, npcInfo);
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
    public static bool CombinationAllowedBySpecificNPCAssignment(NPCAssignment specificAssignment, SubgroupCombination selectedCombination, AssetAndBodyShapeSelector.AssetPackAssignmentMode mode)
    {
        if (specificAssignment == null) { return true; }

        switch (mode)
        {

            case AssetAndBodyShapeSelector.AssetPackAssignmentMode.Primary:
                if (specificAssignment.AssetPackName == "") { return true; }
                else
                {
                    if (specificAssignment.AssetPackName != selectedCombination.AssetPackName) { return false; }
                    foreach (var id in specificAssignment.SubgroupIDs)
                    {
                        if (!selectedCombination.ContainedSubgroups.Select(x => x.Id).Any())
                        {
                            return false;
                        }
                    }
                }
                break;

            case AssetAndBodyShapeSelector.AssetPackAssignmentMode.MixIn:
                var forcedMixIn = specificAssignment.MixInAssignments.Where(x => x.AssetPackName == selectedCombination.AssetPackName).FirstOrDefault();
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
            case AssetAndBodyShapeSelector.AssetPackAssignmentMode.ReplacerVirtual:
                var forcedReplacer = specificAssignment.AssetReplacerAssignments.Where(x => x.ReplacerName == selectedCombination.AssetPackName).FirstOrDefault();
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
    public static HashSet<SubgroupCombination> SelectAssetReplacers(FlattenedAssetPack chosenAssetPack, NPCInfo npcInfo, AssetAndBodyShapeSelector.AssetAndBodyShapeAssignment currentAssignments)
    {
        HashSet<SubgroupCombination> combinations = new HashSet<SubgroupCombination>();
        // determine which replacer groups are valid for the current NPC
        foreach (var replacerGroup in chosenAssetPack.AssetReplacerGroups)
        {
            HashSet<string> targetPaths = new HashSet<string>();
            // get collection of paths that must be matched

            foreach (var subgroupsAtIndex in replacerGroup.Subgroups)
            {
                foreach (var subgroup in subgroupsAtIndex)
                {
                    foreach (var path in subgroup.Paths)
                    {
                        if (!targetPaths.Contains(path.Destination))
                        {
                            targetPaths.Add(path.Destination);
                        }
                    }
                }
            }

            // check if NPC has those paths

            bool assignReplacer = true;
            var destinationType = SelectRecordType(targetPaths, out FormKey destinationFK);
            if (destinationType == SubgroupCombination.DestinationSpecifier.HeadPartFormKey)
            {
                assignReplacer = CheckIfReplacerTargetExists(destinationFK, npcInfo.NPC);
            }
            else if (destinationType != SubgroupCombination.DestinationSpecifier.Generic)
            {
                assignReplacer = CheckIfReplacerTargetExists(destinationType, npcInfo.NPC);
            }
            else // destinationType = SubgroupCombination.DestinationSpecifier.Generic
            {
                foreach (string destPath in targetPaths)
                {
                    if (!(RecordPathParser.GetObjectAtPath(npcInfo.NPC, destPath, new Dictionary<string, dynamic>(), Patcher.MainLinkCache, true, Logger.GetNPCLogNameString(npcInfo.NPC), out dynamic objAtPath) && objAtPath is not null))
                    {
                        assignReplacer = false;
                        break;
                    }
                }
            }

            if (assignReplacer)
            {
                var virtualFlattenedAssetPack = FlattenedAssetPack.CreateVirtualFromReplacerGroup(replacerGroup, chosenAssetPack.GroupName);
                var assignedCombination = AssetAndBodyShapeSelector.GenerateCombinationWithBodyShape(new HashSet<FlattenedAssetPack>() { virtualFlattenedAssetPack }, null, null, null, npcInfo, true, AssetAndBodyShapeSelector.AssetPackAssignmentMode.ReplacerVirtual, currentAssignments);

                if (assignedCombination != null)
                {
                    assignedCombination.DestinationType = destinationType;
                    assignedCombination.ReplacerDestinationFormKey = destinationFK;
                    combinations.Add(assignedCombination);
                    RecordAssetConsistencyAndLinkedNPCs(assignedCombination, npcInfo, replacerGroup);
                }
            }
        }

        return combinations;
    }

    public static SubgroupCombination.DestinationSpecifier SelectRecordType(HashSet<string> targetPaths, out FormKey fkToMatch)
    {
        fkToMatch = new FormKey();
        foreach (var specifier in AssetReplacerHardcodedPaths.ReplacersByPaths)
        {
            if (MiscFunctions.StringHashSetsEqualCaseInvariant(targetPaths, specifier.Paths))
            {
                if (specifier.DestSpecifier == SubgroupCombination.DestinationSpecifier.HeadPartFormKey)
                {
                    fkToMatch = specifier.DestFormKeySpecifier;
                }
                return specifier.DestSpecifier;
            }
        }

        return SubgroupCombination.DestinationSpecifier.Generic;
    }

    public static bool CheckIfReplacerTargetExists(SubgroupCombination.DestinationSpecifier specifier, INpcGetter npc)
    {
        switch (specifier)
        {
            case SubgroupCombination.DestinationSpecifier.MarksFemaleHumanoid04RightGashR: return HasSpecialHeadPartTexture(npc, "actors\\character\\female\\facedetails\\facefemalerightsidegash_04.dds"); // none of the vanilla records use this texture so can't check for FormKey
            case SubgroupCombination.DestinationSpecifier.MarksFemaleHumanoid06RightGashR: return HasSpecialHeadPartTexture(npc, "actors\\character\\female\\facedetails\\facefemalerightsidegash_06.dds"); // none of the vanilla records use this texture so can't check for FormKey
            default: return false;
        }
    }

    public static bool CheckIfReplacerTargetExists(FormKey specifierFK, INpcGetter npc)
    {
        return npc.HeadParts.Where(x => x.FormKey == specifierFK).Any();
    }

    public static bool HasSpecialHeadPartTexture(INpcGetter npc, string diffusePath)
    {
        foreach (var part in npc.HeadParts)
        {
            if (Patcher.MainLinkCache.TryResolve<IHeadPartGetter>(part.FormKey, out var headPartGetter) && Patcher.MainLinkCache.TryResolve<ITextureSetGetter>(headPartGetter.TextureSet.FormKey, out var headPartTextureSetGetter) && headPartTextureSetGetter.Diffuse.Equals(diffusePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static void RecordAssetConsistencyAndLinkedNPCs(SubgroupCombination assignedCombination, NPCInfo npcInfo) // Primary 
    {
        if (PatcherSettings.General.bEnableConsistency)
        {
            npcInfo.ConsistencyNPCAssignment.AssetPackName = assignedCombination.AssetPackName;
            npcInfo.ConsistencyNPCAssignment.SubgroupIDs = assignedCombination.ContainedSubgroups.Where(x => x.Id != AssetPack.ConfigDistributionRules.SubgroupIDString).Select(x => x.Id).ToList();
        }
        if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Primary && assignedCombination != null)
        {
            npcInfo.AssociatedLinkGroup.AssignedCombination = assignedCombination;
        }

        if (PatcherSettings.General.bLinkNPCsWithSameName && npcInfo.IsValidLinkedUnique && UniqueNPCData.GetUniqueNPCTrackerData(npcInfo, AssignmentType.PrimaryAssets) == null)
        {
            Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].AssignedCombination = assignedCombination;
        }
    }

    public static void RecordAssetConsistencyAndLinkedNPCs(SubgroupCombination assignedCombination, NPCInfo npcInfo, string mixInName) // MixIn 
    {
        if (PatcherSettings.General.bEnableConsistency)
        {
            var consistencyMixIn = npcInfo.ConsistencyNPCAssignment.MixInAssignments.Where(x => x.AssetPackName == mixInName).FirstOrDefault();
            if (consistencyMixIn == null)
            {
                var mixInAssignment = new NPCAssignment.MixInAssignment();
                mixInAssignment.AssetPackName = mixInName;
                mixInAssignment.SubgroupIDs = assignedCombination.ContainedSubgroups.Where(x => x.Id != AssetPack.ConfigDistributionRules.SubgroupIDString).Select(x => x.Id).ToList();
                npcInfo.ConsistencyNPCAssignment.MixInAssignments.Add(mixInAssignment);
            }
            else
            {
                consistencyMixIn.AssetPackName = mixInName;
                consistencyMixIn.SubgroupIDs = assignedCombination.ContainedSubgroups.Where(x => x.Id != AssetPack.ConfigDistributionRules.SubgroupIDString).Select(x => x.Id).ToList();
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

        if (PatcherSettings.General.bLinkNPCsWithSameName && npcInfo.IsValidLinkedUnique && UniqueNPCData.GetUniqueNPCTrackerData(npcInfo, AssignmentType.MixInAssets) == null)
        {
            if (Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].MixInAssignments.ContainsKey(mixInName))
            {
                Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].MixInAssignments[mixInName] = assignedCombination;
            }
            else
            {
                Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].MixInAssignments.Add(mixInName, assignedCombination);
            }
        }
    }

    public static void RecordAssetConsistencyAndLinkedNPCs(SubgroupCombination assignedCombination, NPCInfo npcInfo, FlattenedReplacerGroup replacerGroup) // Replacer
    {
        if (PatcherSettings.General.bEnableConsistency)
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

        if (PatcherSettings.General.bLinkNPCsWithSameName && npcInfo.IsValidLinkedUnique)
        {
            List<UniqueNPCData.UniqueNPCTracker.LinkedAssetReplacerAssignment> linkedAssetReplacerAssignments = UniqueNPCData.GetUniqueNPCTrackerData(npcInfo, AssignmentType.ReplacerAssets);
            var existingAssignment = linkedAssetReplacerAssignments.Where(x => x.ReplacerName == replacerGroup.Name).FirstOrDefault();
            if (existingAssignment != null) { existingAssignment.AssignedReplacerCombination = assignedCombination; }
            else { linkedAssetReplacerAssignments.Add(new UniqueNPCData.UniqueNPCTracker.LinkedAssetReplacerAssignment() { GroupName = replacerGroup.Source.GroupName, ReplacerName = replacerGroup.Name, AssignedReplacerCombination = assignedCombination }); }
        }
    }

    public static bool BlockAssetDistributionByExistingAssets(NPCInfo npcInfo)
    {
        if (!PatcherSettings.TexMesh.bApplyToNPCsWithCustomFaces && npcInfo.NPC.HeadTexture != null && !npcInfo.NPC.HeadTexture.IsNull && !BaseGamePlugins.Contains(npcInfo.NPC.HeadTexture.FormKey.ModKey.FileName.String))
        {
            return true;
        }
        if (!PatcherSettings.TexMesh.bApplyToNPCsWithCustomSkins && npcInfo.NPC.WornArmor != null && !npcInfo.NPC.WornArmor.IsNull && !BaseGamePlugins.Contains(npcInfo.NPC.WornArmor.FormKey.ModKey.FileName.String))
        {
            return true;
        }
        return false;
    }

    public static string[] BaseGamePlugins = new string[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" };

    public static void SetVanillaBodyPath(NPCInfo npcInfo, SkyrimMod outputMod)
    {
        if (npcInfo.NPC.WornArmor != null && !npcInfo.NPC.WornArmor.IsNull && PatcherEnvironmentProvider.Environment.LinkCache.TryResolve<IArmorGetter>(npcInfo.NPC.WornArmor.FormKey, out var skin))
        {
            foreach (var armaGetter in skin.Armature)
            { 
                if (PatcherEnvironmentProvider.Environment.LinkCache.TryResolve<IArmorAddonGetter>(armaGetter.FormKey, out var armature) && armature.BodyTemplate != null && armature.WorldModel != null && armature.BodyTemplate.FirstPersonFlags.HasFlag(BipedObjectFlag.Body) && Patcher.PatchableRaces.Contains(armature.Race))
                {
                    string vanillaPath;
                    switch(npcInfo.Gender)
                    {
                        case Gender.Male: 
                            vanillaPath = "Actors\\Character\\Character Assets\\MaleBody_1.NIF";
                            if (armature.WorldModel.Male != null && armature.WorldModel.Male.File != null && !armature.WorldModel.Male.File.Equals(vanillaPath, StringComparison.OrdinalIgnoreCase))
                            {
                                var moddedArmature = outputMod.ArmorAddons.GetOrAddAsOverride(armature);
                                moddedArmature.WorldModel.Male.File = vanillaPath;
                                return;
                            }
                            break;
                        case Gender.Female: 
                            vanillaPath = "Actors\\Character\\Character Assets\\FemaleBody_1.nif";
                            if (armature.WorldModel.Female != null && armature.WorldModel.Female.File != null && !armature.WorldModel.Female.File.Equals(vanillaPath, StringComparison.OrdinalIgnoreCase))
                            {
                                var moddedArmature = outputMod.ArmorAddons.GetOrAddAsOverride(armature);
                                moddedArmature.WorldModel.Female.File = vanillaPath;
                                return;
                            }
                            break;
                    }
                }
            }
        }
    }
}