using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class AssetAndBodyGenSelector
    {
        /// <summary>
        /// Assigns a SubgroupCombination to the given NPC
        /// If BodyGen integration is enabled, attempts to assign a morph that complies with the chosen combination's bodygen restrictions.
        /// </summary>
        /// <param name="bodyGenAssigned">true if a BodyGen morph was able to be assigned. false if a morph could not be assigned and must be set independently of the SubgroupCombination</param>
        /// <param name="availableAssetPacks">Asset packs available to the current NPC</param>
        /// <param name="npcInfo">NPC info class</param>
        /// <returns></returns>
        public static Tuple<SubgroupCombination, List<string>> ChooseCombinationAndBodyGen(out bool bodyGenAssigned, HashSet<FlattenedAssetPack> availableAssetPacks, BodyGenConfigs bodyGenConfigs, NPCInfo npcInfo, bool blockBodyGen)
        {
            SubgroupCombination chosenCombination = new SubgroupCombination();
            List<string> chosenMorphs = new List<string>();
            bodyGenAssigned = false;

            if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary && npcInfo.AssociatedLinkGroup.AssignedCombination != null && CombinationAllowedBySpecificNPCAssignment(npcInfo.SpecificNPCAssignment, npcInfo.AssociatedLinkGroup.AssignedCombination))
            {
                chosenCombination = npcInfo.AssociatedLinkGroup.AssignedCombination;
                chosenMorphs = npcInfo.AssociatedLinkGroup.AssignedMorphs;
            }
            else
            {
                Logger.LogReport("Choosing Asset Combination and BodyGen for " + npcInfo.LogIDstring);
                chosenCombination = ChooseRandomCombination(availableAssetPacks, bodyGenConfigs, chosenMorphs, npcInfo, blockBodyGen); // chosenMorphs is populated by reference within ChooseRandomCombination
                bodyGenAssigned = chosenMorphs.Any();
            }

            if (chosenCombination != null)
            {
                npcInfo.ConsistencyNPCAssignment.AssetPackName = chosenCombination.AssetPackName;
                npcInfo.ConsistencyNPCAssignment.SubgroupIDs = chosenCombination.ContainedSubgroups.Select(x => x.Id).ToList();

                if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Primary && chosenCombination != null)
                {
                    npcInfo.AssociatedLinkGroup.AssignedCombination = chosenCombination;
                }
            }
            if (bodyGenAssigned)
            {
                npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames = chosenMorphs.ToList();
            }

            return new Tuple<SubgroupCombination, List<string>>(chosenCombination, chosenMorphs);
        }

        /// <summary>
        /// Filters flattened asset packs to remove subgroups, or entire asset packs, that are incompatible with the selected NPC due to any subgroup's rule set
        /// Returns shallow copied FlattenedAssetPacks; input availableAssetPacks remain unmodified
        /// </summary>
        /// <param name="availableAssetPacks"></param>
        /// <param name="npcInfo"></param>
        /// <returns></returns>
        private static HashSet<FlattenedAssetPack> FilterValidConfigsForNPC(HashSet<FlattenedAssetPack> availableAssetPacks, NPCInfo npcInfo, bool ignoreConsistency, out bool wasFilteredByConsistency)
        {
            HashSet<FlattenedAssetPack> preFilteredPacks = new HashSet<FlattenedAssetPack>(availableAssetPacks); // available asset packs filtered by Specific NPC Assignments and Consistency
            HashSet<FlattenedAssetPack> filteredPacks = new HashSet<FlattenedAssetPack>(); // available asset packs (further) filtered by current NPC's compliance with each subgroup's rule set
            wasFilteredByConsistency = false;
            List<List<FlattenedSubgroup>> forcedAssignments = null; // must be a nested list because if the user forces a non-bottom-level subgroup, then at a given index multiple options will be forced

            //handle specific NPC assignments
            FlattenedAssetPack forcedAssetPack = null;
            if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.AssetPackName != "")
            {
                // check to make sure forced asset pack exists
                forcedAssetPack = preFilteredPacks.Where(x => x.GroupName == npcInfo.SpecificNPCAssignment.AssetPackName).FirstOrDefault().ShallowCopy(); // don't forget to shallow copy or subsequent NPCs will get pruned asset packs
                if (forcedAssetPack != null)
                {
                    //Prune forced asset pack to only include forced subgroups at their respective indices
                    forcedAssignments = GetForcedSubgroupsAtIndex(forcedAssetPack, npcInfo.SpecificNPCAssignment.SubgroupIDs);
                    for (int i = 0; i < forcedAssignments.Count; i++)
                    {
                        if (forcedAssignments[i].Any())
                        {
                            forcedAssetPack.Subgroups[i] = forcedAssignments[i];
                        }
                    }

                    preFilteredPacks = new HashSet<FlattenedAssetPack>() { forcedAssetPack };
                }
                else
                {
                    Logger.LogMessage("Specific NPC Assignment for " + npcInfo.LogIDstring + " requests asset pack " + forcedAssetPack + " which does not exist. Choosing a random asset pack.");
                }
            }

            //handle consistency
            if (!ignoreConsistency && npcInfo.ConsistencyNPCAssignment != null && npcInfo.ConsistencyNPCAssignment.AssetPackName != "")
            {
                // check to make sure consistencya asset pack is compatible with the specific NPC assignment
                if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.AssetPackName != "" && npcInfo.SpecificNPCAssignment.AssetPackName != npcInfo.ConsistencyNPCAssignment.AssetPackName)
                {
                    Logger.LogReport("Asset Pack defined by forced asset pack (" + npcInfo.SpecificNPCAssignment.AssetPackName + ") supercedes consistency asset pack (" + npcInfo.ConsistencyNPCAssignment.AssetPackName + ")");
                }
                else
                {
                    // check to make sure consistency asset pack exists
                    var consistencyAssetPack = preFilteredPacks.FirstOrDefault(x => x.GroupName == npcInfo.ConsistencyNPCAssignment.AssetPackName);
                    if (consistencyAssetPack == null)
                    {
                        Logger.LogReport("Could not find the asset pack specified in the consistency file: " + npcInfo.ConsistencyNPCAssignment.AssetPackName);
                    }
                    else
                    {
                        consistencyAssetPack = consistencyAssetPack.ShallowCopy(); // otherwise subsequent NPCs will get pruned packs as the consistency pack is modified in the current round of patching
                        // check each subgroup against specific npc assignment
                        for (int i = 0; i < npcInfo.ConsistencyNPCAssignment.SubgroupIDs.Count; i++)
                        {
                            // make sure consistency subgroup doesn't conflict with user-forced subgroup if one exists
                            if (forcedAssignments != null && forcedAssignments[i].Any())
                            {
                                if (forcedAssignments[i].Select(x => x.Id).Contains(npcInfo.ConsistencyNPCAssignment.SubgroupIDs[i]))
                                {
                                    consistencyAssetPack.Subgroups[i] = new List<FlattenedSubgroup>() { forcedAssignments[i].First(x => x.Id == npcInfo.ConsistencyNPCAssignment.SubgroupIDs[i]) }; // guaranteed to have at least one subgroup or else the upstream if would fail, so use First instead of Where
                                }
                                else
                                {
                                    Logger.LogReport("Consistency subgroup " + npcInfo.ConsistencyNPCAssignment.SubgroupIDs[i] + " is incompatible with the Specific NPC Assignment at position " + i + ".");
                                }
                            }
                            // if no user-forced subgroup exists, simply make sure that the consistency subgroup exists
                            else
                            {
                                FlattenedSubgroup consistencySubgroup = consistencyAssetPack.Subgroups[i].FirstOrDefault(x => x.Id == npcInfo.ConsistencyNPCAssignment.SubgroupIDs[i]);
                                if (consistencySubgroup == null)
                                {
                                    Logger.LogReport("Could not find the subgroup specified in the consistency file: " + npcInfo.ConsistencyNPCAssignment.SubgroupIDs[i]);
                                }
                                else
                                {
                                    consistencyAssetPack.Subgroups[i] = new List<FlattenedSubgroup>() { consistencySubgroup };
                                }
                            }
                        }
                        preFilteredPacks = new HashSet<FlattenedAssetPack>() { consistencyAssetPack };
                        wasFilteredByConsistency = true;
                    }
                }
            }

            //handle non-predefined subgroups
            foreach (var ap in preFilteredPacks)
            {
                var candidatePack = ap.ShallowCopy();
                bool isValid = true;

                for (int i = 0; i < candidatePack.Subgroups.Count; i++)
                {
                    for (int j = 0; j < candidatePack.Subgroups[i].Count; j++)
                    {
                        bool isSpecificNPCAssignment = forcedAssetPack != null && forcedAssignments[i].Any();
                        if (!isSpecificNPCAssignment && !SubgroupValidForCurrentNPC(candidatePack.Subgroups[i][j], npcInfo))
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
                            Logger.LogMessage("Asset Pack " + ap.GroupName + " is forced for NPC " + npcInfo.LogIDstring + " but no subgroups within " + ap.Subgroups[i][0].Id + ":" + ap.Subgroups[i][0].Name + " are compatible with this NPC. Ignoring subgroup rules at this position.");
                            candidatePack.Subgroups[i] = new List<FlattenedSubgroup>(ap.Subgroups[i]); // revert list back to unfiltered version at this position
                        }
                        else
                        {
                            isValid = false;
                            break;
                        }
                    }
                    else
                    {
                        candidatePack.Subgroups[i].OrderByDescending(x => x.ForceIfMatchCount);
                    }
                }
                if (isValid)
                {
                    filteredPacks.Add(candidatePack);
                }

            }

            if (filteredPacks.Count == 0)
            {
                Logger.LogMessage("No valid asset packs could be found for NPC " + npcInfo.LogIDstring);
            }

            return filteredPacks;
        }

        private static SubgroupCombination ChooseRandomCombination(HashSet<FlattenedAssetPack> availableAssetPacks, BodyGenConfigs bodyGenConfigs, List<string> chosenMorphs, NPCInfo npcInfo, bool blockBodyGen)
        {
            List<string> candidateMorphs = new List<string>();
            bool notifyOfPermutationMorphConflict = false;

            SubgroupCombination assignedCombination = new SubgroupCombination();
            HashSet<FlattenedAssetPack> filteredAssetPacks = new HashSet<FlattenedAssetPack>();
            AssignmentIteration iterationInfo = new AssignmentIteration();
            bool combinationIsValid = false;

            bool isFirstIteration = true;
            SubgroupCombination firstCombination = null;
            Tuple<SubgroupCombination, List<string>> firstValidCombinationMorphPair = new Tuple<SubgroupCombination, List<string>>(new SubgroupCombination(), new List<string>());
            bool firstValidCombinationMorphPairInitialized = false;

            // remove subgroups or entire asset packs whose distribution rules are incompatible with the current NPC
            filteredAssetPacks = FilterValidConfigsForNPC(availableAssetPacks, npcInfo, false, out bool wasFilteredByConsistency);

            // initialize seeds
            iterationInfo.AvailableSeeds = GetAllSubgroups(filteredAssetPacks).OrderByDescending(x => x.ForceIfMatchCount).ToList();

            while (!combinationIsValid)
            {
                if (!iterationInfo.AvailableSeeds.Any())
                {
                    Logger.LogMessage("No Asset Packs were able to be applied to " + npcInfo.LogIDstring);
                    break;
                }

                // get an asset combination
                assignedCombination = GenerateCombination(filteredAssetPacks, npcInfo, iterationInfo);

                if (assignedCombination == null) 
                {
                    continue; // keep trying to generate a combination until all potential seed subgroups are depleted
                } 

                if (isFirstIteration)
                {
                    firstCombination = assignedCombination;
                    isFirstIteration = false;
                }

                // get a BodyGen assignment
                if (!PatcherSettings.General.bEnableBodyGenIntegration || blockBodyGen || !BodyGenSelector.BodyGenAvailableForGender(npcInfo.Gender, bodyGenConfigs))
                {
                    combinationIsValid = true;
                }
                else
                {
                    var bodyGenStatusFlags = new BodyGenSelector.BodyGenSelectorStatusFlag();
                    candidateMorphs = BodyGenSelector.SelectMorphs(npcInfo, out bool bodyGenAssigned, bodyGenConfigs, assignedCombination, bodyGenStatusFlags);
                    // Decision Tree

                    // Branch 1: No morphs could be assigned in conjuction with the current combination
                    if (!bodyGenAssigned)
                    {
                        // check if any morphs would be valid for the given NPC without any restrictions from the asset combination
                        candidateMorphs = BodyGenSelector.SelectMorphs(npcInfo, out bool bodyGenAssignable, bodyGenConfigs, null, bodyGenStatusFlags);
                        // if not, then the curent combination is fine because no other combination would be compatible with any BodyGen morphs anyway
                        if (!bodyGenAssignable)
                        {
                            chosenMorphs.AddRange(candidateMorphs);
                            combinationIsValid = true;
                        }
                        else // 
                        {
                            notifyOfPermutationMorphConflict = true; // Try picking another combination. if no other SubgroupCombination is compatible with any morphs, the user will be warned that there is an asset/morph ruleset conflict
                        }
                    }

                    // Branch 2: A valid morph was selected based on any of the following criteria:
                    // A) there is no consistency morph for this NPC
                    // B) there is a consistency morph for this NPC and the chosen morph is the consistency morph
                    // C) there is a consistency morph for this NPC but the consistency morph was INVALID for the given NPC irrespective of the chosen permutation's allowed/disallowed BodyGen rules, 
                    else if (npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames == null || bodyGenStatusFlags.HasFlag(BodyGenSelector.BodyGenSelectorStatusFlag.MatchesConsistency) || bodyGenStatusFlags.HasFlag(BodyGenSelector.BodyGenSelectorStatusFlag.ConsistencyMorphIsInvalid))
                    {
                        chosenMorphs.AddRange(candidateMorphs);
                        combinationIsValid = true;
                    }

                    //Branch 3: A consistency morph exists, but the chosen combination is only compatible with a morph that is NOT the consistency morph
                    else if (npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames != null && !bodyGenStatusFlags.HasFlag(BodyGenSelector.BodyGenSelectorStatusFlag.MatchesConsistency))
                    {
                        firstValidCombinationMorphPair = new Tuple<SubgroupCombination, List<string>>(assignedCombination, chosenMorphs);
                        firstValidCombinationMorphPairInitialized = true;
                    }
                }

                // Fallbacks if the chosen combination is invalid
                if (!combinationIsValid && filteredAssetPacks.Count == 0) // relax filters if possible
                {
                    if (wasFilteredByConsistency) // if no valid groups when filtering for consistency, try again without filtering for it
                    {
                        filteredAssetPacks = FilterValidConfigsForNPC(availableAssetPacks, npcInfo, true, out wasFilteredByConsistency);
                        iterationInfo.AvailableSeeds = GetAllSubgroups(filteredAssetPacks);
                    }
                    else // no other filters can be relaxed
                    {
                        break;
                    }
                }
            }

            if (!combinationIsValid)
            {
                if (firstValidCombinationMorphPairInitialized)
                {
                    Logger.LogMessage("Could not assign an asset combination to " + npcInfo.LogIDstring + " that is compatible with its consistency BodyGen morph. A valid combination was assigned, but BodyGen assignment will be re-randomized.");
                    assignedCombination = firstValidCombinationMorphPair.Item1;
                    chosenMorphs.AddRange(firstValidCombinationMorphPair.Item2);
                }
                else if (notifyOfPermutationMorphConflict)
                {
                    Logger.LogMessage("Could not assign an asset combination to " + npcInfo.LogIDstring + " that was compatible with any valid BodyGen morphs. Assigning a valid asset combination. A BodyGen morph will be chosen without regard to this combination's constraints.");
                    assignedCombination = firstCombination;
                }
                else
                {
                    return null;
                }
            }

            return assignedCombination;
        }

        private static SubgroupCombination GenerateCombination(HashSet<FlattenedAssetPack> availableAssetPacks, NPCInfo npcInfo, AssignmentIteration iterationInfo)
        {
            SubgroupCombination generatedCombination = new SubgroupCombination();
            string generatedSignature = "";
            bool combinationAlreadyTried = false;

            FlattenedSubgroup nextSubgroup;

            Logger.LogReport("Generating a new combination");

            #region Choose New Seed
            if (iterationInfo.ChosenSeed == null)
            {
                if (!iterationInfo.AvailableSeeds.Any())
                {
                    Logger.LogReport("No seed subgroups remain. A valid combination cannot be assigned with the given filters.");
                    return null;
                }

                if (iterationInfo.AvailableSeeds[0].ForceIfMatchCount > 0)
                {
                    iterationInfo.ChosenSeed = iterationInfo.AvailableSeeds[0];
                    iterationInfo.ChosenAssetPack = iterationInfo.ChosenSeed.ParentAssetPack.ShallowCopy();
                    Logger.LogReport("Chose seed subgroup " + iterationInfo.ChosenSeed.Id + " in " + iterationInfo.ChosenAssetPack.GroupName + " because it had the most matched ForceIf attributes ("  + iterationInfo.ChosenSeed.ForceIfMatchCount + ").");
                }
                else
                {
                    iterationInfo.ChosenSeed = (FlattenedSubgroup)ProbabilityWeighting.SelectByProbability(iterationInfo.AvailableSeeds);
                    iterationInfo.ChosenAssetPack = iterationInfo.ChosenSeed.ParentAssetPack.ShallowCopy();
                    Logger.LogReport("Chose seed subgroup " + iterationInfo.ChosenSeed.Id + " in " + iterationInfo.ChosenAssetPack.GroupName + " at random");
                }
                iterationInfo.ChosenAssetPack.Subgroups[iterationInfo.ChosenSeed.TopLevelSubgroupIndex] = new List<FlattenedSubgroup>() { iterationInfo.ChosenSeed }; // filter the seed index so that the seed is the only option
                generatedCombination.AssetPackName = iterationInfo.ChosenAssetPack.GroupName;
                
                iterationInfo.RemainingVariantsByIndex = new Dictionary<int, FlattenedAssetPack>(); // tracks the available subgroups as the combination gets built up to enable backtracking if the patcher chooses an invalid combination
                for (int i = 0; i < iterationInfo.ChosenAssetPack.Subgroups.Count; i++)
                {
                    iterationInfo.RemainingVariantsByIndex.Add(i, null); // set up placeholders for backtracking
                }
                iterationInfo.RemainingVariantsByIndex[0] = iterationInfo.ChosenAssetPack.ShallowCopy(); // initial state of the chosen asset pack
                iterationInfo.ChosenAssetPack = ConformRequiredExcludedSubgroups(iterationInfo.ChosenSeed, iterationInfo.ChosenAssetPack);
                if (iterationInfo.ChosenAssetPack == null)
                {
                    Logger.LogReport("Cannot create a combination with the chosen seed subgroup due to conflicting required/excluded subgroup rules. Selecting a different seed.");
                    return RemoveInvalidSeed(iterationInfo.AvailableSeeds, iterationInfo); // exit this function and re-enter from caller to choose a new seed
                }               
            }
            #endregion

            for (int i = 0; i < iterationInfo.ChosenAssetPack.Subgroups.Count; i++)
            {
                generatedCombination.ContainedSubgroups.Add(null); // set up placeholders for subgroups
            }
            Logger.LogReport("Available Subgroups:" + Logger.SpreadFlattenedAssetPack(iterationInfo.ChosenAssetPack, 0, false));

            int debugCounter = 0;

            for (int i = 0; i < iterationInfo.ChosenAssetPack.Subgroups.Count; i++) // iterate through each position within the combination
            {
                //DEBUGGING
                if (generatedCombination.ContainedSubgroups.Count > 0) 
                { 
                    Logger.LogReport("Current Combination: " + String.Join(" , ", generatedCombination.ContainedSubgroups.Where(x => x != null).Select(x => x.Id)) + "\n"); 
                }
                Logger.LogReport("Available Subgroups:" + Logger.SpreadFlattenedAssetPack(iterationInfo.ChosenAssetPack, i, true));
                debugCounter++;

                #region BackTrack if no options remain
                if (iterationInfo.ChosenAssetPack.Subgroups[i].Count == 0)
                {
                    if (i == 0 || (i == 1 && iterationInfo.ChosenSeed.TopLevelSubgroupIndex == 0))
                    {
                        Logger.LogReport("Cannot backtrack further with " + iterationInfo.ChosenSeed.Id + " as seed. Selecting a new seed.");
                        return RemoveInvalidSeed(iterationInfo.AvailableSeeds, iterationInfo); // exit this function and re-enter from caller to choose a new seed
                    }
                    else if ((i - 1) == iterationInfo.ChosenSeed.TopLevelSubgroupIndex) // skip over the seed subgroup
                    {
                        Logger.LogReport("No subgroups remain at position (" + i + "). Selecting a different subgroup at position " + (i - 2));
                        i = AssignmentIteration.BackTrack(iterationInfo, generatedCombination, i, 2);
                    }
                    else
                    {
                        Logger.LogReport("No subgroups remain at position (" + i + "). Selecting a different subgroup at position " + (i - 1));
                        i = AssignmentIteration.BackTrack(iterationInfo, generatedCombination, i, 1);
                    }
                    continue;
                }
                #endregion

                #region Pick next subgroup
                if (iterationInfo.ChosenAssetPack.Subgroups[i][0].ForceIfMatchCount > 0)
                {
                    nextSubgroup = iterationInfo.ChosenAssetPack.Subgroups[i][0];
                    Logger.LogReport("Chose next subgroup: " + nextSubgroup.Id + " at position " + i + " because it had the most matched ForceIf Attributes (" + nextSubgroup.ForceIfMatchCount + ").\n");
                }
                else
                {
                    nextSubgroup = (FlattenedSubgroup)ProbabilityWeighting.SelectByProbability(iterationInfo.ChosenAssetPack.Subgroups[i]);
                    Logger.LogReport("Chose next subgroup: " + nextSubgroup.Id + " at position " + i + " at random.\n");
                }
                #endregion

                generatedCombination.ContainedSubgroups[i] = nextSubgroup;
                iterationInfo.ChosenAssetPack = ConformRequiredExcludedSubgroups(nextSubgroup, iterationInfo.ChosenAssetPack);

                if (generatedCombination.ContainedSubgroups.Last() != null) // if this is the last position in the combination, check if the combination has already been processed during a previous iteration of the calling function with stricter filtering
                {
                    generatedSignature = iterationInfo.ChosenAssetPack.GroupName + ":" + String.Join('|', generatedCombination.ContainedSubgroups.Select(x => x.Id));
                    if (iterationInfo.PreviouslyGeneratedCombinations.Contains(generatedSignature))
                    {
                        combinationAlreadyTried = true;
                    }
                }

                // backtrack if no combinations are valid after filtering by required/excluded subgroups
                if (iterationInfo.ChosenAssetPack == null || combinationAlreadyTried)
                {
                    if (iterationInfo.ChosenAssetPack == null)
                    {
                        Logger.LogReport("No combination could be produced in accordance with the current set of required/excluded subgroup rules");
                    }
                    else if (combinationAlreadyTried)
                    {
                        Logger.LogError("This combination (" + generatedSignature + ") has previously been generated");
                    }

                    Logger.LogReport("Selecting a different subgroup at position " + i + ".");
                    i = AssignmentIteration.BackTrack(iterationInfo, generatedCombination, i, 0);
                    continue;
                }
                else
                {
                    iterationInfo.RemainingVariantsByIndex[i + 1] = iterationInfo.ChosenAssetPack.ShallowCopy(); // store the current state of the current asset pack for backtracking in future iterations if necessary
                }
            }

            iterationInfo.PreviouslyGeneratedCombinations.Add(generatedSignature);
            generatedCombination.AssetPack = iterationInfo.ChosenAssetPack;
            return generatedCombination;
        }

        private static SubgroupCombination RemoveInvalidSeed(List<FlattenedSubgroup> seedSubgroups, AssignmentIteration iterationInfo)
        {
            seedSubgroups.Remove(iterationInfo.ChosenSeed);
            iterationInfo.ChosenSeed = null;
            return null;
        }

        private static FlattenedAssetPack ConformRequiredExcludedSubgroups(FlattenedSubgroup targetSubgroup, FlattenedAssetPack chosenAssetPack)
        {
            if (!targetSubgroup.ExcludedSubgroupIDs.Any() && !targetSubgroup.RequiredSubgroupIDs.Any())
            {
                return chosenAssetPack;
            }

            Logger.LogReport("Trimming remaining subgroups within " + chosenAssetPack.GroupName + " by the required/excluded subgroups of " + targetSubgroup.Id + "\n");

            // create a shallow copy of the subgroup list to avoid modifying the chosenAssetPack unless the result is confirmed to be valid.
            var trialSubgroups = new List<List<FlattenedSubgroup>>();
            for (int i = 0; i < chosenAssetPack.Subgroups.Count; i++)
            {
                trialSubgroups.Add(new List<FlattenedSubgroup>(chosenAssetPack.Subgroups[i]));
            }

            // check excluded subgroups
            foreach (var entry in targetSubgroup.ExcludedSubgroupIDs)
            {
                trialSubgroups[entry.Key] = chosenAssetPack.Subgroups[entry.Key].Where(x => entry.Value.Contains(x.Id) == false).ToList();
                if(!trialSubgroups[entry.Key].Any())
                {
                    Logger.LogReport("Subgroup " + targetSubgroup.Id + " cannot be added because it excludes (" + String.Join(',', entry.Value) + ") which eliminates all options at position " + entry.Key + ".\n");
                    return null;
                }
            }

            // check required subgroups
            foreach (var entry in targetSubgroup.RequiredSubgroupIDs)
            {
                trialSubgroups[entry.Key] = chosenAssetPack.Subgroups[entry.Key].Where(x => entry.Value.Contains(x.Id)).ToList();
                if (!trialSubgroups[entry.Key].Any())
                {
                    Logger.LogReport("Subgroup " + targetSubgroup.Id + " cannot be added because it requires (" + String.Join('|', entry.Value) + ") which eliminates all options at position " + entry.Key + ".\n");
                    return null;
                }
            }

            chosenAssetPack.Subgroups = trialSubgroups;
            return chosenAssetPack;
        }

        private static List<FlattenedSubgroup> GetAllSubgroups(HashSet<FlattenedAssetPack> availableAssetPacks)
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
        /// Returns false if the given subgroup is incompatible with the given NPC due to any of the rules defined within the subgroup.
        /// </summary>
        /// <param name="subgroup"></param>
        /// <param name="npcInfo"></param>
        /// <param name="forceIfAttributeCount">The number of ForceIf attributes within this subgroup that were matched by the current NPC</param>
        /// <returns></returns>
        private static bool SubgroupValidForCurrentNPC(FlattenedSubgroup subgroup, NPCInfo npcInfo)
        {
            if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.SubgroupIDs.Contains(subgroup.Id))
            {
                return true;
            }

            // Allow unique NPCs
            if (!subgroup.AllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                return false;
            }

            // Allow non-unique NPCs
            if (!subgroup.AllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                return false;
            }

            // Allowed Races
            if (!subgroup.AllowedRacesIsEmpty && !subgroup.AllowedRaces.Contains(npcInfo.AssetsRace))
            {
                return false;
            }

            // Disallowed Races
            if (subgroup.DisallowedRaces.Contains(npcInfo.AssetsRace))
            {
                return false;
            }

            // Weight Range
            if (npcInfo.NPC.Weight < subgroup.WeightRange.Lower || npcInfo.NPC.Weight > subgroup.WeightRange.Upper)
            {
                return false;
            }

            // Allowed Attributes
            if (subgroup.AllowedAttributes.Any() && !AttributeMatcher.HasMatchedAttributes(subgroup.AllowedAttributes, npcInfo.NPC))
            {
                return false;
            }

            // Disallowed Attributes
            if (AttributeMatcher.HasMatchedAttributes(subgroup.DisallowedAttributes, npcInfo.NPC))
            {
                return false;
            }

            // if the current subgroup's forceIf attributes match the current NPC, skip the checks for Distribution Enabled
            
            subgroup.ForceIfMatchCount = AttributeMatcher.GetForceIfAttributeCount(subgroup.AllowedAttributes, npcInfo.NPC);

            // Distribution Enabled
            if (subgroup.ForceIfMatchCount == 0 && !subgroup.DistributionEnabled)
            {
                return false;
            }

            // If the subgroup is still valid
            return true;
        }

        private static List<List<FlattenedSubgroup>> GetForcedSubgroupsAtIndex(FlattenedAssetPack input, List<string> forcedSubgroupIDs)
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
                    Logger.LogReport("Subgroup " + id + " requested by Specific NPC Assignment was not found in Asset Pack " + input.GroupName);
                }
            }

            return forcedOrEmpty;
        }

        /// <summary>
        /// Determines if a given combination (pre-determined from Consistency or a Linked NPC Group) is compatible with the Specific NPC Assignment for the current NPC if one exists
        /// </summary>
        /// <param name="assignment">Specific NPC Assignment for the current NPC</param>
        /// <param name="selectedCombination">Candidate subgroup combination</param>
        /// <returns></returns>
        private static bool CombinationAllowedBySpecificNPCAssignment(NPCAssignment assignment, SubgroupCombination selectedCombination)
        {
            if (assignment == null) { return true; }
            if (assignment.AssetPackName == "") { return true; }
            else
            {
                if (assignment.AssetPackName != selectedCombination.AssetPackName) { return false; }
                foreach (var id in assignment.SubgroupIDs)
                {
                    if (!selectedCombination.ContainedSubgroups.Select(x => x.Id).Any())
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
