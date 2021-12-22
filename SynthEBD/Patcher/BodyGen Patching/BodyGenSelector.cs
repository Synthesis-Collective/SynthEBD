using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class BodyGenSelector
    {
        public static List<string> SelectMorphs(NPCInfo npcInfo, out bool success, BodyGenConfigs bodyGenConfigs, SubgroupCombination assetCombination, BodyGenSelectorStatusFlag statusFlags)
        {
            BodyGenConfig currentBodyGenConfig = null;
            var genderedBodyGenConfigs = new HashSet<BodyGenConfig>();
            switch(npcInfo.Gender)
            {
                case Gender.male: genderedBodyGenConfigs = bodyGenConfigs.Male; break;
                case Gender.female: genderedBodyGenConfigs = bodyGenConfigs.Female; break;
            }

            if (assetCombination != null)
            {
                switch (npcInfo.Gender)
                {
                    case Gender.male: currentBodyGenConfig = bodyGenConfigs.Male.Where(x => x.Label == assetCombination.AssetPack.AssociatedBodyGenConfigName).FirstOrDefault(); break;
                    case Gender.female: currentBodyGenConfig = bodyGenConfigs.Female.Where(x => x.Label == assetCombination.AssetPack.AssociatedBodyGenConfigName).FirstOrDefault(); break;
                }
            }
            if (currentBodyGenConfig == null)
            {
                switch (npcInfo.Gender)
                {
                    case Gender.male: currentBodyGenConfig = bodyGenConfigs.Male.Where(x => x.Label == PatcherSettings.BodyGen.CurrentMaleConfig).FirstOrDefault(); break;
                    case Gender.female: currentBodyGenConfig = bodyGenConfigs.Female.Where(x => x.Label == PatcherSettings.BodyGen.CurrentFemaleConfig).FirstOrDefault(); break;
                }
            }
            if (currentBodyGenConfig == null)
            {
                success = false;
                return null;
            }

            ClearStatusFlags(statusFlags);
            List<string> chosenMorphs = new List<string>();

            var availableTemplatesGlobal = InitializeMorphList(currentBodyGenConfig.Templates, npcInfo, ValidationIgnore.None);
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
                    var availableTemplatesRaceIgnore = InitializeMorphList(currentBodyGenConfig.Templates, npcInfo, ValidationIgnore.Race);
                    availableCombinations = GetAvailableCombinations(currentBodyGenConfig, npcInfo, availableTemplatesRaceIgnore);
                    availableCombinations = FilterBySpecificNPCAssignments(availableCombinations, npcInfo, out assignmentsSpecified);
                    if (!assignmentsSpecified)
                    {
                        // if that still didn't work, ignore all 
                        availableTemplatesAll = InitializeMorphList(currentBodyGenConfig.Templates, npcInfo, ValidationIgnore.All);
                        availableCombinations = GetAllCombinations(genderedBodyGenConfigs, npcInfo, ValidationIgnore.All);
                        availableCombinations = FilterBySpecificNPCAssignments(availableCombinations, npcInfo, out assignmentsSpecified);
                        if (!assignmentsSpecified)
                        {
                            Logger.LogReport("No morph combinations could be generated while respecting the Specific Assignments for " + npcInfo.LogIDstring + ". A random morph will be chosen.");
                            availableCombinations = GetAvailableCombinations(currentBodyGenConfig, npcInfo, availableTemplatesGlobal); // revert to original
                        }
                    }
                }
            }
            #endregion

            #region Linked NPC Group
            if (!assignmentsSpecified && npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary)
            {
                availableTemplatesAll = InitializeMorphList(currentBodyGenConfig.Templates, npcInfo, ValidationIgnore.All);
                var allCombinations = GetAllCombinations(genderedBodyGenConfigs, npcInfo, ValidationIgnore.All);
                var linkedCombinations = GetLinkedCombination(allCombinations, npcInfo.AssociatedLinkGroup.AssignedMorphs);
                if (linkedCombinations != null)
                {
                    availableCombinations = linkedCombinations;
                }
                else
                {
                    Logger.LogReport("Could not find any combinations containing the morphs applied to the specified parent NPC.");
                }
            }
            #endregion

            #region Unique NPC replicates
            else if (UniqueNPCData.IsValidUnique(npcInfo.NPC, out var npcName) && UniqueNPCData.GetUniqueNPCTracker(npcInfo, AssignmentType.BodyGen) != null)
            {
                availableTemplatesAll = InitializeMorphList(currentBodyGenConfig.Templates, npcInfo, ValidationIgnore.All);
                var allCombinations = GetAllCombinations(genderedBodyGenConfigs, npcInfo, ValidationIgnore.All);
                var linkedCombinations = GetLinkedCombination(allCombinations, Patcher.UniqueAssignmentsByName[npcName][npcInfo.Gender].AssignedMorphs);
                if (linkedCombinations != null)
                {
                    availableCombinations = linkedCombinations;
                    Logger.LogReport("Another unique NPC with the same name was assigned a morph. Using that morph for current NPC.");
                }

            }
            #endregion

            #region Consistency
            if (npcInfo.ConsistencyNPCAssignment != null && npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames != null)
            {
                availableCombinations = GetConsistencyCombinations(availableCombinations, npcInfo);
            }
            #endregion

            chosenMorphs = ChooseMorphs(availableCombinations, npcInfo);

            if (chosenMorphs == null)
            {
                Logger.LogReport("Could not choose any valid morphs for NPC " + npcInfo.LogIDstring);
                success = false;
                return chosenMorphs;
            }
            else
            {
                success = true;
            }

            //store selected morphs
            Dictionary<string, HashSet<string>> allChosenMorphs = null;
            switch(npcInfo.Gender)
            {
                case Gender.male: allChosenMorphs = Patcher.BodyGenTracker.AllChosenMorphsMale; break;
                case Gender.female: allChosenMorphs = Patcher.BodyGenTracker.AllChosenMorphsFemale; break;
            }
            if (!allChosenMorphs.ContainsKey(currentBodyGenConfig.Label))
            {
                allChosenMorphs.Add(currentBodyGenConfig.Label, new HashSet<string>());
            }
            allChosenMorphs[currentBodyGenConfig.Label].UnionWith(chosenMorphs);

            if (npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames != null && !npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames.Except(chosenMorphs).Any()) // https://stackoverflow.com/questions/407729/determine-if-a-sequence-contains-all-elements-of-another-sequence-using-linq
            {
                statusFlags |= BodyGenSelectorStatusFlag.MatchesConsistency;
            }

            return chosenMorphs;
        }

        public static List<string> ChooseMorphs(HashSet<GroupCombinationObject> availableCombinations, NPCInfo npcInfo)
        {
            List<string> chosenMorphs = new List<string>();
            
            if (availableCombinations.Count == 0)
            {
                Logger.LogError("Could not get a BodyGen combination for Race " + npcInfo.BodyGenRace.ToString() + " ( NPC " + npcInfo.LogIDstring + ")");
                return null;
            }

            var prioritizedCombinations = availableCombinations.GroupBy(x => x.MaxMatchedForceIfAttributes).OrderByDescending(x => x.Key);

            foreach (var combinationList in prioritizedCombinations)
            {
                var currentCombination = (GroupCombinationObject)ProbabilityWeighting.SelectByProbability(availableCombinations);

                foreach (var availableMorphsAtPosition in currentCombination.Templates)
                {
                    var candidateMorph = (BodyGenConfig.BodyGenTemplate)ProbabilityWeighting.SelectByProbability(availableMorphsAtPosition);
                    chosenMorphs.Add(candidateMorph.Label);
                }
            }

            return chosenMorphs;
        }

        public static HashSet<GroupCombinationObject> FilterBySpecificNPCAssignments (HashSet<GroupCombinationObject> allCombinations, NPCInfo npcInfo, out bool success)
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
                Logger.LogError("Could not apply specific BodyGen morph assignment to NPC " + npcInfo.LogIDstring + " because no valid combinations contained the specified morphs");
                success = false;
                return allCombinations;
            }

            return output;
        }

        public static HashSet<GroupCombinationObject> GetLinkedCombination(HashSet<GroupCombinationObject> availableCombinations, List<string> searchMorphs)
        {
            HashSet<GroupCombinationObject> output = new HashSet<GroupCombinationObject>();

            foreach (var combination in availableCombinations)
            {
                if (combination.Templates.Count != searchMorphs.Count) { continue; }
                GroupCombinationObject linkedCombination = new GroupCombinationObject(combination);
                List<string> requiredMorphs = new List<string>(searchMorphs);
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

        public static HashSet<GroupCombinationObject> GetConsistencyCombinations(HashSet<GroupCombinationObject> availableCombinations, NPCInfo npcInfo)
        {
            var consistencyMorphs = npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames;
            if (consistencyMorphs.Count == 0) { return availableCombinations; }

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
                return consistencyCombinations;
            }
            else if (partialMatches.Any())
            {
                Logger.LogReport("NPC " + npcInfo.LogIDstring + "'s consistency morph could not be fully matched. Attempting to assign the closest available partial match.");
                return partialMatches;
            }
            else
            {
                Logger.LogReport("NPC " + npcInfo.LogIDstring + "'s consistency morph could not be matched. Assigning a random morph");
                return availableCombinations;
            }
        }

        /// <summary>
        /// Filters a BodyGenConfig's template list and assigns each template's MatchedForceIfCount
        /// </summary>
        /// <param name="allMorphs">All templated contained within a BodyGenConfig</param>
        /// <param name="npcInfo"></param>
        /// <returns></returns>
        public static HashSet<BodyGenConfig.BodyGenTemplate> InitializeMorphList(HashSet<BodyGenConfig.BodyGenTemplate> allMorphs, NPCInfo npcInfo, ValidationIgnore ignoredFactors)
        {
            HashSet<BodyGenConfig.BodyGenTemplate> outputMorphs = new HashSet<BodyGenConfig.BodyGenTemplate>();
            foreach (var candidateMorph in allMorphs)
            {
                if (MorphIsValid(candidateMorph, npcInfo, ignoredFactors))
                {
                    candidateMorph.MatchedForceIfCount = AttributeMatcher.GetForceIfAttributeCount(candidateMorph.AllowedAttributes, npcInfo.NPC);
                    outputMorphs.Add(candidateMorph);
                }
            }
            return outputMorphs;
        }

        public static bool MorphIsValid(BodyGenConfig.BodyGenTemplate candidateMorph, NPCInfo npcInfo, ValidationIgnore ignoredFactors)
        {
            if (ignoredFactors == ValidationIgnore.All)
            {
                return true;
            }

            if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.BodyGenMorphNames.Contains(candidateMorph.Label))
            {
                return true;
            }

            // Allow unique NPCs
            if (!candidateMorph.AllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                return false;
            }

            // Allow non-unique NPCs
            if (!candidateMorph.AllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                return false;
            }

            if (ignoredFactors != ValidationIgnore.Race)
            {
                // Allowed Races
                if (!candidateMorph.CompiledAllowedRaces.Contains(npcInfo.BodyGenRace))
                {
                    return false;
                }

                // Disallowed Races
                if (candidateMorph.CompiledDisallowedRaces.Contains(npcInfo.BodyGenRace))
                {
                    return false;
                }
            }
            // Weight Range
            if (npcInfo.NPC.Weight < candidateMorph.WeightRange.Lower || npcInfo.NPC.Weight > candidateMorph.WeightRange.Upper)
            {
                return false;
            }

            // Allowed Attributes
            if (candidateMorph.AllowedAttributes.Any() && !AttributeMatcher.HasMatchedAttributes(candidateMorph.AllowedAttributes, npcInfo.NPC))
            {
                return false;
            }

            // Disallowed Attributes
            if (AttributeMatcher.HasMatchedAttributes(candidateMorph.DisallowedAttributes, npcInfo.NPC))
            {
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
        public static HashSet<GroupCombinationObject> GetAvailableCombinations(BodyGenConfig bodyGenConfig, NPCInfo npcInfo, HashSet<BodyGenConfig.BodyGenTemplate> availableTemplates)
        {
            HashSet<GroupCombinationObject> output = new HashSet<GroupCombinationObject>();

            foreach (var candidate in bodyGenConfig.RacialTemplateGroupMap)
            {
                bool candidateMatched = false;
                // first try to get grouping by Race
                if (FormKeyHashSetComparer.Contains(candidate.Races, npcInfo.BodyGenRace))
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
                    var collection = PatcherSettings.General.RaceGroupings.Where(x => x.Label == raceGrouping).FirstOrDefault();
                    if (collection == null) { continue; }
                    if (FormKeyHashSetComparer.Contains(collection.Races, npcInfo.BodyGenRace))
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
            HashSet<GroupCombinationObject> output = new HashSet<GroupCombinationObject>();
            HashSet<HashSet<string>> addedCombinations = new HashSet<HashSet<string>>();
            
            foreach (var bodyGenConfig in bodyGenConfigs)
            {
                foreach (var racialMapping in bodyGenConfig.RacialTemplateGroupMap)
                {
                    foreach (var stringCombination in racialMapping.Combinations)
                    {
                        if (!HashSetContainsCombination(stringCombination.Members, addedCombinations))
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

        private static bool HashSetContainsCombination(HashSet<string> currentCombination, HashSet<HashSet<string>> addedCombinations)
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

        [Flags]
        public enum BodyGenSelectorStatusFlag
        {
            NoneValidForNPC = 1, // no morphs could be assigned irrespective of the rule set received from the assigned assetCombination
            MatchesConsistency = 2, // all selected morphs are present in consistency
            ConsistencyMorphIsInvalid = 4 // the consistency morph is no longer valid because its rule set no longer permits this NPC
        }

        public static void ClearStatusFlags(BodyGenSelectorStatusFlag flags)
        {
            flags = ~BodyGenSelectorStatusFlag.NoneValidForNPC;
            flags = ~BodyGenSelectorStatusFlag.MatchesConsistency;
            flags = ~BodyGenSelectorStatusFlag.ConsistencyMorphIsInvalid;
        }

        public class GroupCombinationObject : IProbabilityWeighted
        {
            public GroupCombinationObject(BodyGenConfig.RacialMapping.BodyGenCombination bodyGenCombination, HashSet<BodyGenConfig.BodyGenTemplate> availableTemplates)
            {
                MaxMatchedForceIfAttributes = 0;
                ProbabilityWeighting = bodyGenCombination.ProbabilityWeighting;
                Templates = new List<HashSet<BodyGenConfig.BodyGenTemplate>>();

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
                Templates = new List<HashSet<BodyGenConfig.BodyGenTemplate>>();
                foreach (var setAtPosition in template.Templates)
                {
                    Templates.Add(new HashSet<BodyGenConfig.BodyGenTemplate>(setAtPosition));
                }
            }

            public int MaxMatchedForceIfAttributes { get; set; }
            public int ProbabilityWeighting { get; set; }
            public List<HashSet<BodyGenConfig.BodyGenTemplate>> Templates { get; set; }
            public bool InitializedSuccessfully { get; set; } // false if one or more of the template sublists contains no templates.
        }

        public static bool BodyGenAvailableForGender(Gender gender, BodyGenConfigs bodyGenConfigs)
        {
            switch (gender)
            {
                case Gender.male:
                    if (bodyGenConfigs.Male.Any())
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                case Gender.female:
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
    }
}
