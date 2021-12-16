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

            #region Specific NPC Assignments
            if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.BodyGenMorphNames.Any())
            {
                // first try getting combinations while adhering to the bodygen config's Racial Template Map
                availableCombinations = FilterBySpecificNPCAssignments(availableCombinations, npcInfo, out bool filterSuccess);
                if (!filterSuccess)
                {
                    // if that didn't work, try forming combination objects out of all templates regardless of the Racial Template Map
                    var availableTemplatesAll = InitializeMorphList(currentBodyGenConfig.Templates, npcInfo, ValidationIgnore.Race);
                    availableCombinations = FilterBySpecificNPCAssignments(availableCombinations, npcInfo, out filterSuccess);
                    if (!filterSuccess)
                    {
                        // if that still didn't work, ignore all 
                        availableTemplatesAll = InitializeMorphList(currentBodyGenConfig.Templates, npcInfo, ValidationIgnore.All);
                        availableCombinations = FilterBySpecificNPCAssignments(availableCombinations, npcInfo, out filterSuccess);
                        if (!filterSuccess)
                        {
                            Logger.LogReport("No morph combinations could be generated while respecting the Specific Assignments for " + npcInfo.LogIDstring + ". A random morph will be chosen.");
                        }
                    }
                }
            }
            #endregion

            // If not matched, choose random
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
                case Gender.male: allChosenMorphs = MainLoop.BodyGenTracker.AllChosenMorphsMale; break;
                case Gender.female: allChosenMorphs = MainLoop.BodyGenTracker.AllChosenMorphsFemale; break;
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
