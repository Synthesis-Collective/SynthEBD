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
            }
            else
            {
                var filteredAssetPacks = FilterValidConfigsForNPC(availableAssetPacks, npcInfo);
                chosenCombination = ChooseRandomCombination(filteredAssetPacks, chosenMorphs);
            }

            return new Tuple<SubgroupCombination, HashSet<string>>(chosenCombination, chosenMorphs);
        }

        /// <summary>
        /// Filters flattened asset packs to remove subgroups, or entire asset packs, that are incompatible with the selected NPC due to any subgroup's rule set
        /// Respects user-forced asset packs and subgroups
        /// </summary>
        /// <param name="availableAssetPacks"></param>
        /// <param name="npcInfo"></param>
        /// <returns></returns>
        private static HashSet<FlattenedAssetPack> FilterValidConfigsForNPC(HashSet<FlattenedAssetPack> availableAssetPacks, NPCInfo npcInfo)
        {
            HashSet<FlattenedAssetPack> filteredPacks = new HashSet<FlattenedAssetPack>();

            string forcedAssetPack = null;
            if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.ForcedAssetPackName != "")
            {
                // check to make sure forced asset pack exists
                if (availableAssetPacks.Where(x => x.GroupName == npcInfo.SpecificNPCAssignment.ForcedAssetPackName).Any())
                { 
                    forcedAssetPack = npcInfo.SpecificNPCAssignment.ForcedAssetPackName; 
                }
            }

            foreach (var ap in availableAssetPacks)
            {
                if (forcedAssetPack != null && ap.GroupName != forcedAssetPack) { continue; }

                var candidatePack = ap.ShallowCopy();
                bool isValid = true;

                for (int i = 0; i < candidatePack.Subgroups.Count; i++)
                {
                    bool currentSubgroupsAreForced = false;
                    // checked for forced subgroups at this position
                    if (npcInfo.SpecificNPCAssignment != null)
                    {
                        var forcedSubgroups = FilterSubgroupsBySpecificNPCAssignment(candidatePack.Subgroups[i], npcInfo.SpecificNPCAssignment.ForcedSubgroupIDs, out currentSubgroupsAreForced);
                        if (currentSubgroupsAreForced)
                        {
                            candidatePack.Subgroups[i] = forcedSubgroups;
                        }
                    }

                    if (!currentSubgroupsAreForced)
                    {
                        for (int j = 0; j < candidatePack.Subgroups[i].Count; j++)
                        {
                            if (!SubgroupValidForCurrentNPC(candidatePack.Subgroups[i][j], npcInfo))
                            {
                                candidatePack.Subgroups[i].RemoveAt(j);
                                j--;
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

        private static SubgroupCombination ChooseRandomCombination(HashSet<FlattenedAssetPack> filteredAssetPacks, HashSet<string> chosenMorphs)
        {
            SubgroupCombination assignedCombination = new SubgroupCombination();


            return assignedCombination;
        }

        /// <summary>
        /// Returns false if the given subgroup is incompatible with the given NPC due to any of the rules defined within the subgroup.
        /// </summary>
        /// <param name="subgroup"></param>
        /// <param name="npcInfo"></param>
        /// <returns></returns>
        private static bool SubgroupValidForCurrentNPC(FlattenedSubgroup subgroup, NPCInfo npcInfo)
        {
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

            // Weight Range
            if (npcInfo.NPC.Weight < subgroup.WeightRange.Lower || npcInfo.NPC.Weight > subgroup.WeightRange.Upper)
            {
                return false;
            }

            // Disallowed Attributes
            if (AttributeMatcher.AttributeMatched(subgroup.DisallowedAttributes, npcInfo.NPC))
            {
                return false;
            }

            // if the current subgroup's forceIf attributes match the current NPC, skip the checks for Allowed Attributes and Distribution Enabled
            if (!AttributeMatcher.AttributeMatched(subgroup.ForceIfAttributes, npcInfo.NPC))
            {
                // Distribution Enabled
                if (!subgroup.DistributionEnabled)
                {
                    return false;
                }

                // Allowed Attributes
                if (subgroup.AllowedAttributes.Any() && !AttributeMatcher.AttributeMatched(subgroup.AllowedAttributes, npcInfo.NPC))
                {
                    return false;
                }
            }

            // If the subgroup is still valid
            return true;
        }

        /// <summary>
        /// If any of the subgroups within a given position in the combination are forced by a Specific NPC Assignment, filters the subgroup list at this position to only contain those forced subgroups
        /// </summary>
        /// <param name="subgroupsAtPosition">Subgroups at the given index within the combination</param>
        /// <param name="forcedSubgroupIDs">All subgroup IDs forced by the user</param>
        /// <param name="currentSubgroupsAreForced">true if any of the subgroups within subgroupsAtPosition are forced, otherwise false</param>
        /// <returns></returns>
        private static List<FlattenedSubgroup> FilterSubgroupsBySpecificNPCAssignment(List<FlattenedSubgroup> subgroupsAtPosition, HashSet<string> forcedSubgroupIDs, out bool currentSubgroupsAreForced)
        {
            currentSubgroupsAreForced = false;
            var specifiedSubgroups = subgroupsAtPosition.Where(x => forcedSubgroupIDs.Intersect(x.ContainedSubgroupIDs).Any()).ToList();
            if (specifiedSubgroups.Count > 0)
            {
                currentSubgroupsAreForced = true;
            }
            return specifiedSubgroups;
        }

        /// <summary>
        /// Determines if a given combination (pre-determined from Consistency or a Linked NPC Group) is compatible with the Specific NPC Assignment for the current NPC if one exists
        /// </summary>
        /// <param name="assignment">Specific NPC Assignment for the current NPC</param>
        /// <param name="selectedCombination">Candidate subgroup combination</param>
        /// <returns></returns>
        private static bool CombinationAllowedBySpecificNPCAssignment(SpecificNPCAssignment assignment, SubgroupCombination selectedCombination)
        {
            if (assignment == null) { return true; }
            if (assignment.ForcedAssetPackName == "") { return true; }
            else
            {
                if (assignment.ForcedAssetPackName != selectedCombination.AssetPackName) { return false; }
                foreach (var id in assignment.ForcedSubgroupIDs)
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
