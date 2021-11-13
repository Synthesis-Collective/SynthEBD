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
        public static Tuple<SubgroupCombination, HashSet<string>> ChooseCombinationAndBodyGen(out bool bodyGenAssigned, HashSet<FlattenedAssetPack> availableAssetPacks, NPCInfo npcInfo)
        {
            SubgroupCombination chosenCombination = new SubgroupCombination();
            HashSet<string> chosenMorphs = new HashSet<string>();
            bodyGenAssigned = false;

            if (npcInfo.AssociatedLinkGroup != null && npcInfo.AssociatedLinkGroup.AssignedCombination != null && CombinationAllowedBySpecificNPCAssignment(npcInfo.SpecificNPCAssignment, npcInfo.AssociatedLinkGroup.AssignedCombination))
            {
                chosenCombination = npcInfo.AssociatedLinkGroup.AssignedCombination;
                chosenMorphs = npcInfo.AssociatedLinkGroup.AssignedMorphs;
            }
            else
            {
                Logger.LogReport("Choosing Asset Combination and BodyGen for " + npcInfo.LogIDstring);
                chosenCombination = ChooseRandomCombination(availableAssetPacks, chosenMorphs, npcInfo); // chosenMorphs is populated by reference within ChooseRandomCombination
                bodyGenAssigned = chosenMorphs.Any();
            }

            return new Tuple<SubgroupCombination, HashSet<string>>(chosenCombination, chosenMorphs);
        }

        /// <summary>
        /// Filters flattened asset packs to remove subgroups, or entire asset packs, that are incompatible with the selected NPC due to any subgroup's rule set
        /// Returns shallow copied FlattenedAssetPacks; input availableAssetPacks remain unmodified
        /// </summary>
        /// <param name="availableAssetPacks"></param>
        /// <param name="npcInfo"></param>
        /// <returns></returns>
        private static HashSet<FlattenedAssetPack> FilterValidConfigsForNPC(HashSet<FlattenedAssetPack> availableAssetPacks, NPCInfo npcInfo, bool ignoreConsistency, bool ignoreForceIf, out bool wasFilteredByConsistency, out bool wasFilteredByForceIf)
        {
            HashSet<FlattenedAssetPack> filteredPacks = new HashSet<FlattenedAssetPack>();
            wasFilteredByConsistency = false;
            wasFilteredByForceIf = false;

            string forcedAssetPack = null;
            if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.AssetPackName != "")
            {
                // check to make sure forced asset pack exists
                if (availableAssetPacks.Where(x => x.GroupName == npcInfo.SpecificNPCAssignment.AssetPackName).Any())
                {
                    forcedAssetPack = npcInfo.SpecificNPCAssignment.AssetPackName;
                }
                else
                {
                    Logger.LogMessage("Specific NPC Assignment for " + npcInfo.LogIDstring + " requests asset pack " + forcedAssetPack + " which does not exist. Choosing a random asset pack.");
                }
            }

            foreach (var ap in availableAssetPacks)
            {
                if (forcedAssetPack != null && ap.GroupName != forcedAssetPack) { continue; }

                // HANDLE CONSISTENCY!!
                wasFilteredByConsistency = false;
                //

                var candidatePack = ap.ShallowCopy();
                bool isValid = true;

                for (int i = 0; i < candidatePack.Subgroups.Count; i++)
                {                    
                    // checked if the subgroup at this position is specified via a Specific NPC Assignment
                    if (npcInfo.SpecificNPCAssignment != null)
                    {
                        var forcedSubgroup = GetSpecificNPCAssignmnentSubgroupAtIndex(candidatePack.Subgroups[i], npcInfo.SpecificNPCAssignment.SubgroupIDs);
                        if (forcedSubgroup != null)
                        {
                            candidatePack.Subgroups[i] = new List<FlattenedSubgroup>() { forcedSubgroup };
                        }
                    }
                    else
                    {
                        
                        List<int> forceIfAttributeMatchedCount = new List<int>();
                        for (int j = 0; j < candidatePack.Subgroups[i].Count; j++)
                        {
                            int matchedForceIfAttributes = 0;
                            if (!SubgroupValidForCurrentNPC(candidatePack.Subgroups[i][j], npcInfo, out matchedForceIfAttributes))
                            {
                                candidatePack.Subgroups[i].RemoveAt(j);
                                j--;
                            }
                            else
                            {
                                forceIfAttributeMatchedCount.Add(matchedForceIfAttributes);
                            }
                        }

                        if (ignoreForceIf == false)
                        {
                            // remove subgroups that contain the least ForceIf attributes
                            int maxMatchedForceIfs = 0;
                            if (forceIfAttributeMatchedCount.Any()) // forceIfAttributeMatchedCount.Max() will freeze on an empty set.
                            {
                                maxMatchedForceIfs = forceIfAttributeMatchedCount.Max();
                            }
                            if (maxMatchedForceIfs > 0)
                            {
                                var forceIfMatchedSubgroups = new List<FlattenedSubgroup>();
                                for (int j = 0; j < candidatePack.Subgroups[i].Count; j++)
                                {
                                    if (forceIfAttributeMatchedCount[j] == maxMatchedForceIfs)
                                    {
                                        forceIfMatchedSubgroups.Add(candidatePack.Subgroups[i][j]);
                                    }
                                }
                                candidatePack.Subgroups[i] = forceIfMatchedSubgroups;
                                wasFilteredByForceIf = true;
                            }
                        }
                    }

                    // if all subgroups at a given position are invalid, then the entire asset pack is invalid
                    if (candidatePack.Subgroups[i].Count == 0)
                    {
                        if (forcedAssetPack != null && ap.GroupName == forcedAssetPack)
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

        private static SubgroupCombination ChooseRandomCombination(HashSet<FlattenedAssetPack> availableAssetPacks, HashSet<string> chosenMorphs, NPCInfo npcInfo)
        {
            //READ THIS FROM STATIC SETTINGS WHEN FINISHED MAKING SETTINGS STATIC
            bool enableBodyGen = false;
            //
            // PUT THIS IN NPCinfo
            bool blockBodyGen = false;
            //

            bool assignBodyGen = enableBodyGen && !blockBodyGen;
            HashSet<string> candidateMorphs = new HashSet<string>();
            bool notifyOfPermutationMorphConflict = false;

            SubgroupCombination assignedCombination = new SubgroupCombination();
            HashSet<FlattenedAssetPack> filteredAssetPacks = new HashSet<FlattenedAssetPack>();
            HashSet<FlattenedSubgroup> seedSubgroups = new HashSet<FlattenedSubgroup>();
            AssignmentIteration iterationInfo = new AssignmentIteration();
            bool combinationIsValid = false;

            bool isFirstIteration = true;
            SubgroupCombination firstCombination = null;
            Tuple<SubgroupCombination, HashSet<string>> firstValidCombinationMorphPair = new Tuple<SubgroupCombination, HashSet<string>>(new SubgroupCombination(), new HashSet<string>());
            bool firstValidCombinationMorphPairInitialized = false;

            filteredAssetPacks = FilterValidConfigsForNPC(availableAssetPacks, npcInfo, false, false, out bool wasFilteredByConsistency, out bool wasFilteredByForceIf);
            seedSubgroups = GetAllSubgroups(filteredAssetPacks);

            while (!combinationIsValid)
            {
                if (!seedSubgroups.Any())
                {
                    Logger.LogMessage("No Asset Packs were able to be applied to " + npcInfo.LogIDstring);
                    break;
                }

                // get an asset combination
                assignedCombination = GenerateCombination(filteredAssetPacks, npcInfo, seedSubgroups, iterationInfo);

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
                if (!assignBodyGen)
                {
                    combinationIsValid = true;
                }
                else
                {
                    var bodyGenStatusFlags = new BodyGenSelector.BodyGenSelectorStatusFlag();
                    candidateMorphs = BodyGenSelector.SelectMorphs(npcInfo, out bool bodyGenAssigned, assignedCombination, bodyGenStatusFlags);
                    // Decision Tree

                    // Branch 1: No morphs could be assigned in conjuction with the current combination
                    if (!bodyGenAssigned)
                    {
                        // check if any morphs would be valid for the given NPC without any restrictions from the asset combination
                        candidateMorphs = BodyGenSelector.SelectMorphs(npcInfo, out bool bodyGenAssignable, null, bodyGenStatusFlags);
                        // if not, then the curent combination is fine because no other combination would be compatible with any BodyGen morphs anyway
                        if (!bodyGenAssignable)
                        {
                            chosenMorphs.UnionWith(candidateMorphs);
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
                        chosenMorphs.UnionWith(candidateMorphs);
                        combinationIsValid = true;
                    }

                    //Branch 3: A consistency morph exists, but the chosen combination is only compatible with a morph that is NOT the consistency morph
                    else if (npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames != null && !bodyGenStatusFlags.HasFlag(BodyGenSelector.BodyGenSelectorStatusFlag.MatchesConsistency))
                    {
                        firstValidCombinationMorphPair = new Tuple<SubgroupCombination, HashSet<string>>(assignedCombination, chosenMorphs);
                        firstValidCombinationMorphPairInitialized = true;
                    }
                }

                // Fallbacks if the chosen combination is invalid
                if (!combinationIsValid && filteredAssetPacks.Count == 0) // relax filters if possible
                {
                    if (wasFilteredByConsistency && wasFilteredByForceIf) // if no valid groups when filtering for consistency and forceIf attributes, try again without filtering for consistency
                    {
                        filteredAssetPacks = FilterValidConfigsForNPC(availableAssetPacks, npcInfo, true, false, out wasFilteredByConsistency, out wasFilteredByForceIf);
                        seedSubgroups = GetAllSubgroups(filteredAssetPacks);
                    }
                    else if (!wasFilteredByConsistency && wasFilteredByForceIf) // if no valid groups when filtering for forceIf attributes only, try again without filtering for them
                    {
                        filteredAssetPacks = FilterValidConfigsForNPC(availableAssetPacks, npcInfo, false, true, out wasFilteredByConsistency, out wasFilteredByForceIf);
                        seedSubgroups = GetAllSubgroups(filteredAssetPacks);
                    }
                    else if (wasFilteredByConsistency && !wasFilteredByForceIf) // if no valid groups when filtering for consistency only, try again without filtering for it
                    {
                        filteredAssetPacks = FilterValidConfigsForNPC(availableAssetPacks, npcInfo, true, true, out wasFilteredByConsistency, out wasFilteredByForceIf);
                        seedSubgroups = GetAllSubgroups(filteredAssetPacks);
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
                    chosenMorphs.UnionWith(firstValidCombinationMorphPair.Item2);
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

        private static SubgroupCombination GenerateCombination(HashSet<FlattenedAssetPack> availableAssetPacks, NPCInfo npcInfo, HashSet<FlattenedSubgroup> seedSubgroups, AssignmentIteration iterationInfo)
        {
            SubgroupCombination generatedCombination = new SubgroupCombination();
            string generatedSignature = "";
            bool combinationAlreadyTried = false;

            FlattenedSubgroup nextSubgroup;

            Logger.LogReport("Generating a new combination");

            if (iterationInfo.ChosenSeed == null)
            {
                if (!seedSubgroups.Any())
                {
                    Logger.LogReport("No seed subgroups remain. A valid combination cannot be assigned with the given filters.");
                    return null;
                }

                Logger.LogReport("Choosing a seed subgroup");
                iterationInfo.ChosenSeed = (FlattenedSubgroup)ProbabilityWeighting.SelectByProbability(seedSubgroups);
                iterationInfo.ChosenAssetPack = iterationInfo.ChosenSeed.ParentAssetPack.ShallowCopy();
                iterationInfo.ChosenAssetPack.Subgroups.RemoveAt(iterationInfo.ChosenSeed.TopLevelSubgroupIndex); // remove other subgroups at the seed index (this simplifies the algorithm for choosing remaining subgroups because it no longer has to skip over the seed, track the seed index, etc)
                generatedCombination.AssetPackName = iterationInfo.ChosenAssetPack.GroupName;
                generatedCombination.ContainedSubgroups.Add(iterationInfo.ChosenSeed);
                Logger.LogReport("Chose seed subgroup " + iterationInfo.ChosenSeed.Id + " in " + iterationInfo.ChosenAssetPack.GroupName);

                iterationInfo.RemainingVariantsByIndex = new Dictionary<int, FlattenedAssetPack>();
                for (int i = 0; i < iterationInfo.ChosenAssetPack.Subgroups.Count; i++)
                {
                    iterationInfo.RemainingVariantsByIndex.Add(i, null); // set up placeholders for backtracking
                    generatedCombination.ContainedSubgroups.Add(null); // set up placeholders for subgroups
                }
                iterationInfo.RemainingVariantsByIndex[0] = iterationInfo.ChosenAssetPack.ShallowCopy(); // initial state of the chosen asset pack
            }
            iterationInfo.ChosenAssetPack = ConformRequiredExcludedSubgroups(iterationInfo.ChosenSeed, iterationInfo.ChosenAssetPack);
            if (iterationInfo.ChosenAssetPack == null)
            {
                Logger.LogReport("Cannot create a combination with the chosen seed subgroup due to conflicting required/excluded subgroup rules. Selecting a different seed.");
                return RemoveInvalidSeed(seedSubgroups, iterationInfo); // exit this function and re-enter from caller to choose a new seed
            }

            for (int i = 0; i < iterationInfo.ChosenAssetPack.Subgroups.Count; i++) // iterate through each position within the combination
            {
                if (iterationInfo.ChosenAssetPack.Subgroups[i].Count == 0)
                {
                    Logger.LogReport("No subgroups remain at position (" + i + "). Selecting a different subgroup at position " + (i - 1));
                    i = AssignmentIteration.BackTrack(iterationInfo, generatedCombination.ContainedSubgroups[i], i, true, true);

                    if (i < 0) // if backtracking goes all the way back to seed
                    {
                        Logger.LogReport("No valid combination could be assigned with " + iterationInfo.ChosenSeed.Id + " as seed. Selecting a new seed.");
                        return RemoveInvalidSeed(seedSubgroups, iterationInfo); // exit this function and re-enter from caller to choose a new seed
                    }
                    else
                    {
                        continue;
                    }
                }

                nextSubgroup = (FlattenedSubgroup)ProbabilityWeighting.SelectByProbability(iterationInfo.ChosenAssetPack.Subgroups[i]);
                Logger.LogReport("Chose next subgroup: " + nextSubgroup.Id + " at position " + i);
                generatedCombination.ContainedSubgroups[i + 1] = nextSubgroup;

                iterationInfo.ChosenAssetPack = ConformRequiredExcludedSubgroups(nextSubgroup, iterationInfo.ChosenAssetPack);

                if (iterationInfo.ChosenAssetPack != null && i == iterationInfo.ChosenAssetPack.Subgroups.Count - 1) // if this is the last position in the combination, check if the combination has already been processed during a previous iteration of the calling function with stricter filtering
                {
                    generatedSignature = iterationInfo.ChosenAssetPack.GroupName + ":" + String.Join('|', generatedCombination.ContainedSubgroups.OrderBy(x => x.TopLevelSubgroupIndex).Select(x => x.Id));
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

                    if (i == 0)
                    {
                        Logger.LogReport("No valid combinations could be made with seed subgroup " + iterationInfo.ChosenSeed.Id + ". Choosing a different seed subgroup.");
                        return RemoveInvalidSeed(seedSubgroups, iterationInfo); // exit this function and re-enter from caller to choose a new seed
                    }
                    else
                    {
                        Logger.LogReport("Selecting a different subgroup at position " + i + ".");
                        i = AssignmentIteration.BackTrack(iterationInfo, nextSubgroup, i, false, false);
                    }
                }
                else if (i < iterationInfo.ChosenAssetPack.Subgroups.Count - 1) // will be true until the last subgroup is added
                {
                    iterationInfo.RemainingVariantsByIndex[i+1] = iterationInfo.ChosenAssetPack.ShallowCopy();
                }
            }

            iterationInfo.PreviouslyGeneratedCombinations.Add(generatedSignature);
            return generatedCombination;
        }

        private static SubgroupCombination RemoveInvalidSeed(HashSet<FlattenedSubgroup> seedSubgroups, AssignmentIteration iterationInfo)
        {
            seedSubgroups.Remove(iterationInfo.ChosenSeed);
            iterationInfo.ChosenSeed = null;
            return null;
        }

        private static FlattenedAssetPack ConformRequiredExcludedSubgroups(FlattenedSubgroup targetSubgroup, FlattenedAssetPack chosenAssetPack)
        {
            Logger.LogReport("Trimming remaining subgroups within " + chosenAssetPack.GroupName + " by the required/excluded subgroups of " + targetSubgroup.Id);
            foreach (var entry in targetSubgroup.ExcludedSubgroupIDs)
            {
                chosenAssetPack.Subgroups[entry.Key] = chosenAssetPack.Subgroups[entry.Key].Where(x => entry.Value.Contains(x.Id) == false).ToList();
                if(!chosenAssetPack.Subgroups[entry.Key].Any())
                {
                    return null;
                }
            }

            foreach (var entry in targetSubgroup.RequiredSubgroupIDs)
            {
                chosenAssetPack.Subgroups[entry.Key] = chosenAssetPack.Subgroups[entry.Key].Where(x => entry.Value.Contains(x.Id)).ToList();
                if (!chosenAssetPack.Subgroups[entry.Key].Any())
                {
                    return null;
                }
            }
            return chosenAssetPack;
        }

        private static HashSet<FlattenedSubgroup> GetAllSubgroups(HashSet<FlattenedAssetPack> availableAssetPacks)
        {
            HashSet<FlattenedSubgroup> subgroupSet = new HashSet<FlattenedSubgroup>();

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
        private static bool SubgroupValidForCurrentNPC(FlattenedSubgroup subgroup, NPCInfo npcInfo, out int forceIfAttributeCount)
        {
            forceIfAttributeCount = 0;

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

            // Disallowed Attributes
            if (AttributeMatcher.HasMatchedAttributes(subgroup.DisallowedAttributes, npcInfo.NPC))
            {
                return false;
            }

            // if the current subgroup's forceIf attributes match the current NPC, skip the checks for Allowed Attributes and Distribution Enabled
            forceIfAttributeCount = AttributeMatcher.GetMatchedAttributeCount(subgroup.ForceIfAttributes, npcInfo.NPC);
            if (forceIfAttributeCount > 0)
            {
                // Distribution Enabled
                if (!subgroup.DistributionEnabled)
                {
                    return false;
                }

                // Allowed Attributes
                if (subgroup.AllowedAttributes.Any() && !AttributeMatcher.HasMatchedAttributes(subgroup.AllowedAttributes, npcInfo.NPC))
                {
                    return false;
                }
            }

            // If the subgroup is still valid
            return true;
        }

        /// <summary>
        /// If any of the subgroups within a given position in the combination are forced by a Specific NPC Assignment, filters the subgroup list at this position to only contain that forced subgroup
        /// </summary>
        /// <param name="subgroupsAtPosition">Subgroups at the given index within the combination</param>
        /// <param name="forcedSubgroupIDs">All subgroup IDs forced by the user</param>
        /// <param name="currentSubgroupsAreForced">true if any of the subgroups within subgroupsAtPosition are forced, otherwise false</param>
        /// <returns></returns>
        private static FlattenedSubgroup GetSpecificNPCAssignmnentSubgroupAtIndex(List<FlattenedSubgroup> subgroupsAtPosition, HashSet<string> forcedSubgroupIDs)
        {
            var specifiedSubgroups = subgroupsAtPosition.Where(x => forcedSubgroupIDs.Intersect(x.ContainedSubgroupIDs).Any()).ToList(); // should only contain zero or one entry
            if (specifiedSubgroups.Count > 0)
            {
                return specifiedSubgroups[0];
            }
            return null;
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
