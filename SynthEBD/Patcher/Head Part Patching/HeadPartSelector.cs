using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class HeadPartSelector
    {
        public static HeadPartSelection AssignHeadParts(NPCInfo npcInfo, Settings_Headparts settings, BlockedNPC blockedNPCentry, BlockedPlugin blockedPluginEntry, BodySlideSetting? assignedBodySlide)
        {
            Logger.OpenReportSubsection("HeadParts", npcInfo);
            Logger.LogReport("Selecting Head Parts for Current NPC", false, npcInfo);
            HeadPartSelection selectedHeadParts = new();

            List<string> consistencyReportTriggers = new();

            bool recordDataForLinkedUniqueNPCs = false;
            var tempUniqueNPCDataRecorder = UniqueNPCData.CreateHeadPartTracker(); // keeps the actual tracker null until all head parts are assigned.
            if (PatcherSettings.General.bLinkNPCsWithSameName && npcInfo.IsValidLinkedUnique && UniqueNPCData.GetUniqueNPCTrackerData(npcInfo, AssignmentType.HeadParts) == null)
            {
                recordDataForLinkedUniqueNPCs = true;
            }

            foreach (var headPartType in settings.Types.Keys)
            {
                if (blockedNPCentry.HeadPartTypes[headPartType])
                {
                    Logger.LogReport(headPartType + " assignment is blocked for current NPC.", false, npcInfo);
                    continue;
                }
                if (blockedPluginEntry.HeadPartTypes[headPartType])
                {
                    Logger.LogReport(headPartType + " assignment is blocked for current NPC's plugin.", false, npcInfo);
                    continue;
                }

                HeadPartConsistency currentConsistency = null;
                if (PatcherSettings.General.bEnableConsistency && npcInfo.ConsistencyNPCAssignment != null)
                {
                    currentConsistency = npcInfo.ConsistencyNPCAssignment.HeadParts[headPartType];
                }
                IHeadPartGetter selection = AssignHeadPartType(settings.Types[headPartType], headPartType, npcInfo, assignedBodySlide, currentConsistency, out bool randomizedToNone);
                FormKey? selectedFK = null;
                if (selection != null) { selectedFK = selection.FormKey; }
                AllocateHeadPartSelection(selectedFK, headPartType, selectedHeadParts);

                // record consistency mismatches
                if (currentConsistency != null && currentConsistency.Initialized)
                {
                    var bothNull = currentConsistency.FormKey.IsNull && selection == null;
                    var bothMatch = selection != null && selection.FormKey.Equals(currentConsistency.FormKey);
                    if (!bothNull && !bothMatch)
                    {
                        consistencyReportTriggers.Add(headPartType.ToString());
                    }
                }

                // record new consistency
                if (PatcherSettings.General.bEnableConsistency)
                {
                    if (selection != null)
                    {
                        npcInfo.ConsistencyNPCAssignment.HeadParts[headPartType].EditorID = EditorIDHandler.GetEditorIDSafely(selection);
                        npcInfo.ConsistencyNPCAssignment.HeadParts[headPartType].FormKey = selection.FormKey;
                        npcInfo.ConsistencyNPCAssignment.HeadParts[headPartType].RandomizedToNone = false;
                    }
                    else if (randomizedToNone)
                    {
                        npcInfo.ConsistencyNPCAssignment.HeadParts[headPartType].EditorID = "None";
                        npcInfo.ConsistencyNPCAssignment.HeadParts[headPartType].FormKey = new FormKey();
                        npcInfo.ConsistencyNPCAssignment.HeadParts[headPartType].RandomizedToNone = true;
                    }
                    else
                    {
                        npcInfo.ConsistencyNPCAssignment.HeadParts[headPartType].EditorID = "Null";
                        npcInfo.ConsistencyNPCAssignment.HeadParts[headPartType].FormKey = new FormKey();
                        npcInfo.ConsistencyNPCAssignment.HeadParts[headPartType].RandomizedToNone = false;
                    }
                    npcInfo.ConsistencyNPCAssignment.HeadParts[headPartType].Initialized = true;
                }

                // assign linkage
                if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Primary)
                {
                    npcInfo.AssociatedLinkGroup.HeadPartAssignments[headPartType] = selection;
                }

                if (recordDataForLinkedUniqueNPCs)
                {
                    tempUniqueNPCDataRecorder[headPartType] = selection;
                }
            }

            if (consistencyReportTriggers.Any())
            {
                Logger.LogMessage(npcInfo.LogIDstring + ": (" + String.Join(", ", consistencyReportTriggers) + ") could not be assigned from Consistency and were re-randomized.");
            }

            if (recordDataForLinkedUniqueNPCs)
            {
                Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].HeadPartAssignments = tempUniqueNPCDataRecorder;
            }

            Logger.CloseReportSubsectionsToParentOf("HeadParts", npcInfo);
            return selectedHeadParts;
        }

        public static void AllocateHeadPartSelection(FormKey? selection, HeadPart.TypeEnum type, HeadPartSelection assignments)
        {
            switch(type)
            {
                case HeadPart.TypeEnum.Eyebrows: assignments.Brows = selection; break;
                case HeadPart.TypeEnum.Eyes: assignments.Eyes = selection; break;
                case HeadPart.TypeEnum.Face: assignments.Face = selection; break;
                case HeadPart.TypeEnum.FacialHair: assignments.Beard = selection; break;
                case HeadPart.TypeEnum.Hair: assignments.Hair = selection; break;
                case HeadPart.TypeEnum.Misc: assignments.Misc = selection; break;
                case HeadPart.TypeEnum.Scars: assignments.Scars = selection; break;
            }
        }

        public static IHeadPartGetter AssignHeadPartType(Settings_HeadPartType currentSettings, HeadPart.TypeEnum type, NPCInfo npcInfo, BodySlideSetting? assignedBodySlide, HeadPartConsistency currentConsistency, out bool randomizedToNone)
        {
            randomizedToNone = false;
            // if there are no head parts of this type at all, don't assign consistency.
            if (!currentSettings.HeadParts.Any())
            {
                Logger.LogReport("No " + type + " head parts have been imported.", false, npcInfo);
                return null;
            }

            IHeadPartGetter specificAssignment = null;
            if (npcInfo.SpecificNPCAssignment != null && !npcInfo.SpecificNPCAssignment.HeadParts[type].FormKey.IsNull)
            {
                var specificAssignmentSetting = currentSettings.HeadParts.Where(x => x.HeadPartFormKey.Equals(npcInfo.SpecificNPCAssignment.HeadParts[type].FormKey)).FirstOrDefault();
                if (specificAssignmentSetting != null && specificAssignmentSetting.ResolvedHeadPart != null)
                {
                    specificAssignment = specificAssignmentSetting.ResolvedHeadPart;
                    Logger.LogReport("Assigning " + type + ": " + (specificAssignment.EditorID ?? specificAssignment.FormKey.ToString()) + " via Specific NPC Assignment", false, npcInfo);
                    return specificAssignment;
                }
                else if (specificAssignmentSetting != null && specificAssignmentSetting.ResolvedHeadPart != null)
                {
                    Logger.LogReport("Specific NPC Assignment for " + type + " calls for " + npcInfo.SpecificNPCAssignment.HeadParts[type].FormKey.ToString() + " but this head part does not currently exist in the load order. Assigning a different head part.", true, npcInfo);
                }

                if (specificAssignment == null && !currentSettings.HeadPartsGendered[npcInfo.Gender].Any()) // if no specific NPC assignment, then headpart assignment is locked to gender.
                {
                    Logger.LogReport("No " + npcInfo.Gender.ToString() + " " + type + " head parts have been imported.", false, npcInfo);
                    return null;
                }
            }

            if (!CanGetThisHeadPartType(currentSettings, type, npcInfo))
            {
                return null;
            }

            if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary && npcInfo.AssociatedLinkGroup.HeadPartAssignments[type] != null)
            {
                var linkedHeadPart = npcInfo.AssociatedLinkGroup.HeadPartAssignments[type];
                Logger.LogReport("Assigning " + type + ": " + (linkedHeadPart.EditorID ?? linkedHeadPart.FormKey.ToString()) + " via the NPC's Link Group.", false, npcInfo);
                return linkedHeadPart;
            }
            else if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary && npcInfo.AssociatedLinkGroup.HeadPartAssignments[type] == null)
            {
                Logger.LogReport("Assigning " + type + ": NONE via the NPC's Link Group.", false, npcInfo);
                return null;
            }
            else if (PatcherSettings.General.bLinkNPCsWithSameName && npcInfo.IsValidLinkedUnique && UniqueNPCData.GetUniqueNPCTrackerData(npcInfo, AssignmentType.HeadParts) != null)
            {
                Dictionary<HeadPart.TypeEnum, IHeadPartGetter> uniqueAssignments = UniqueNPCData.GetUniqueNPCTrackerData(npcInfo, AssignmentType.HeadParts);
                var assignedHeadPart = uniqueAssignments[type];
                if (assignedHeadPart != null)
                {
                    Logger.LogReport("Another unique NPC with the same name was assigned a " + type + ": " + (assignedHeadPart.EditorID ?? assignedHeadPart.FormKey.ToString()) + ". Using that " + type + " for current NPC.", false, npcInfo);
                    return assignedHeadPart;
                }
                else
                {
                    Logger.LogReport("Another unique NPC with the same name was not assigned a " + type + ", so current NPC will also not be assigned a " + type, false, npcInfo);
                    return null;
                }
            }

            var availableHeadParts = currentSettings.HeadPartsGendered[npcInfo.Gender].Where(x => HeadPartIsValid(x, npcInfo, type, assignedBodySlide));
            var availableEDIDs = availableHeadParts.Select(x => x.EditorID ?? x.HeadPartFormKey.ToString());

            IHeadPartGetter consistencyHeadPart = null;
            if (currentConsistency != null && !currentConsistency.FormKey.IsNull)
            {
                if (!PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(npcInfo.ConsistencyNPCAssignment.HeadParts[type].FormKey, out consistencyHeadPart))
                {
                    Logger.LogReport("The consistency " + type + " head part " + npcInfo.ConsistencyNPCAssignment.HeadParts[type].FormKey.ToString() + " is no longer present in the load order.", true, npcInfo);
                }
            }

            Logger.LogReport("The following headparts are allowed under the current rule set: " + Environment.NewLine + String.Join(Environment.NewLine, availableEDIDs), false, npcInfo);

            IHeadPartGetter selectedHeadPart = null;

            var forceIfHeadParts = availableHeadParts.Where(x => x.MatchedForceIfCount > 0).OrderBy(x => x.MatchedForceIfCount);
            if (forceIfHeadParts.Any())
            {
                var forceIfHeadPartStrings = forceIfHeadParts.Select(x => (x.EditorID ?? x.HeadPartFormKey.ToString()) + ": " + x.MatchedForceIfCount);
                Logger.LogReport("The following headparts have matched ForceIf attributes:" + Environment.NewLine + String.Join(Environment.NewLine, forceIfHeadPartStrings), false, npcInfo);
                selectedHeadPart = ChooseHeadPart(forceIfHeadParts, consistencyHeadPart, npcInfo, type, 100, out randomizedToNone);
            }
            else if (currentConsistency != null && currentConsistency.RandomizedToNone)
            {
                Logger.LogReport("This NPC's consistency shows it was previously selected to NOT receive a " + type + ". Therefore, one will NOT be assigned unless overriden via a Specific or ForceIf assignment." + type, false, npcInfo);
                randomizedToNone = true;
            }
            else if (availableHeadParts.Any())
            { 
                selectedHeadPart = ChooseHeadPart(availableHeadParts, consistencyHeadPart, npcInfo, type, currentSettings.RandomizationPercentage, out randomizedToNone);
            }
            else
            {
                Logger.LogReport("No " + type.ToString() + "head parts are available for this NPC", false, npcInfo);
            }

            return selectedHeadPart;
        }

        public static IHeadPartGetter ChooseHeadPart(IEnumerable<HeadPartSetting> options, IHeadPartGetter consistencyHeadPart, NPCInfo npcInfo, HeadPart.TypeEnum type, double randomizationPercentage, out bool randomizedToNone)
        {
            randomizedToNone = false;
            if (consistencyHeadPart != null)
            {
                var consistencyAssignment = options.Where(x => x.HeadPartFormKey.Equals(consistencyHeadPart.FormKey)).FirstOrDefault();
                if (consistencyAssignment != null)
                {
                    Logger.LogReport("Assigning head part " + (consistencyAssignment.EditorID ?? consistencyAssignment.HeadPartFormKey.ToString()) + " from Consistency.", false, npcInfo);
                    return consistencyHeadPart;
                }
            }
            // if no consistency match, select at random

            if (!BoolByProbability.Decide(randomizationPercentage)) // controlled by headpart type's randomization percentage
            {
                Logger.LogReport("NPC's " + type + " was chosen at random to NOT be replaced.", false, npcInfo);
                randomizedToNone = true;
                return null;
            }

            var selectedAssignment = (HeadPartSetting)ProbabilityWeighting.SelectByProbability(options);
            Logger.LogReport("Selected " + type + ": " + EditorIDHandler.GetEditorIDSafely(selectedAssignment.ResolvedHeadPart) + " at random.", false, npcInfo);
            return selectedAssignment.ResolvedHeadPart;
        }
        public static bool CanGetThisHeadPartType(Settings_HeadPartType currentSettings, HeadPart.TypeEnum type, NPCInfo npcInfo)
        {
            if (!currentSettings.bAllowRandom && currentSettings.MatchedForceIfCount == 0) // don't need to check for specific assignment because it was evaluated just above
            {
                Logger.LogReport(type + " headparts will not be assigned because they can only be assigned via ForceIf attributes or Specific NPC Assignments", false, npcInfo);
                return false;
            }

            if (npcInfo.Gender == Gender.Male && !currentSettings.bAllowMale)
            {
                Logger.LogReport(type + " headparts cannot be distributed to male NPCs unless assigned via Specific Assignment.", false, npcInfo);
                return false;
            }
            else if (npcInfo.Gender == Gender.Female && !currentSettings.bAllowFemale)
            {
                Logger.LogReport(type + " headparts cannot be distributed to female NPCs unless assigned via Specific Assignment.", false, npcInfo);
                return false;
            }

            if (currentSettings.bRestrictToNPCsWithThisType)
            {
                var existingHeadParts = npcInfo.ExistingHeadParts.Where(x => x.Type != null && x.Type == type);
                if (!existingHeadParts.Any())
                {
                    Logger.LogReport(type + " headparts are restricted to NPCs which already have headparts of this type, which this NPC does not", false, npcInfo);
                    return false;
                }
                else if (existingHeadParts.Count() == 1)
                {
                    var existingHP = existingHeadParts.First();
                    bool hasNoneHeadPart = false;
                    switch(type)
                    {
                        case HeadPart.TypeEnum.Eyebrows: 
                            if (existingHP == Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.HeadPart.BrowsMaleHumanoid12NoBrow || existingHP == Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.HeadPart.FemaleBrowsHuman12NoBrow)
                            {
                                hasNoneHeadPart = true;
                            }
                            break;
                        case HeadPart.TypeEnum.FacialHair:
                            if (existingHP == Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.HeadPart.HumanBeard00NoBeard || existingHP == Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.HeadPart.KhajiitNoBeard)
                            {
                                hasNoneHeadPart = true;
                            }
                            break;
                        case HeadPart.TypeEnum.Scars:
                            if (existingHP == Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.HeadPart.MarksFemaleHumanoid00NoGash || existingHP == Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.HeadPart.MarksMaleHumanoid00NoScar)
                            {
                                hasNoneHeadPart = true;
                            }
                            break;
                        default: break;
                    }
                    if (hasNoneHeadPart)
                    {
                        Logger.LogReport(type + " headparts are restricted to NPCs which already have headparts of this type, and this NPC has the null-equivalent headpart: " + EditorIDHandler.GetEditorIDSafely(existingHP), false, npcInfo);
                        return false;
                    }
                }
            }

            // Allow unique NPCs
            if (!currentSettings.bAllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                Logger.LogReport(type + " is invalid because its distribution is disallowed for unique NPCs", false, npcInfo);
                return false;
            }

            // Allow non-unique NPCs
            if (!currentSettings.bAllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                Logger.LogReport(type + " is invalid because its distribution is disallowed for non-unique NPCs", false, npcInfo);
                return false;
            }

            // Allowed Races
            if (currentSettings.AllowedRaces.Any() && !currentSettings.AllowedRaces.Contains(npcInfo.HeadPartsRace))
            {
                Logger.LogReport(type + " is invalid because its allowed races do not include the current NPC's race", false, npcInfo);
                return false;
            }

            // Disallowed Races
            if (currentSettings.DisallowedRaces.Contains(npcInfo.HeadPartsRace))
            {
                Logger.LogReport(type + " is invalid because its disallowed races include the current NPC's race", false, npcInfo);
                return false;
            }

            // Weight Range
            if (npcInfo.NPC.Weight < currentSettings.WeightRange.Lower || npcInfo.NPC.Weight > currentSettings.WeightRange.Upper)
            {
                Logger.LogReport(type + " is invalid because the current NPC's weight falls outside of its allowed weight range", false, npcInfo);
                return false;
            }

            // Allowed and Forced Attributes
            AttributeMatcher.MatchNPCtoAttributeList(currentSettings.AllowedAttributes, npcInfo.NPC, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfWeightedCount, out string _, out string unmatchedLog, out string forceIfLog);
            if (hasAttributeRestrictions && !matchesAttributeRestrictions)
            {
                Logger.LogReport(type + " is invalid because the NPC does not match any of its allowed attributes: " + unmatchedLog, false, npcInfo);
                return false;
            }
            else
            {
                currentSettings.MatchedForceIfCount = matchedForceIfWeightedCount;
            }

            if (currentSettings.MatchedForceIfCount > 0)
            {
                Logger.LogReport(type + " Current NPC matches the following forced attributes: " + forceIfLog, false, npcInfo);
            }

            // Disallowed Attributes
            AttributeMatcher.MatchNPCtoAttributeList(currentSettings.DisallowedAttributes, npcInfo.NPC, out hasAttributeRestrictions, out matchesAttributeRestrictions, out int dummy, out string matchLog, out string _, out string _);
            if (hasAttributeRestrictions && matchesAttributeRestrictions)
            {
                Logger.LogReport(type + " is invalid because the NPC matches one of its disallowed attributes: " + matchLog, false, npcInfo);
                return false;
            }

            // If the head part type is still valid
            return true;
        }

        public static bool HeadPartIsValid(HeadPartSetting candidateHeadPart, NPCInfo npcInfo, HeadPart.TypeEnum type, BodySlideSetting? assignedBodySlide)
        {
            if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.HeadParts[type].FormKey.Equals(candidateHeadPart.HeadPartFormKey))
            {
                Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is valid because it is specifically assigned by user.", false, npcInfo);
                return true;
            }

            if (!candidateHeadPart.bAllowRandom && candidateHeadPart.MatchedForceIfCount == 0) // don't need to check for specific assignment because it was evaluated just above
            {
                Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because it can only be assigned via ForceIf attributes or Specific NPC Assignments", false, npcInfo);
                return false;
            }

            // Allow unique NPCs
            if (!candidateHeadPart.bAllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because the current Head Part is disallowed for unique NPCs", false, npcInfo);
                return false;
            }

            // Allow non-unique NPCs
            if (!candidateHeadPart.bAllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because the current Head Part is disallowed for non-unique NPCs", false, npcInfo);
                return false;
            }

            // Allowed Races
            if (candidateHeadPart.AllowedRaces.Any() && !candidateHeadPart.AllowedRaces.Contains(npcInfo.HeadPartsRace))
            {
                Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because its allowed races do not include the current NPC's race", false, npcInfo);
                return false;
            }

            // Disallowed Races
            if (candidateHeadPart.DisallowedRaces.Contains(npcInfo.HeadPartsRace))
            {
                Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because its disallowed races include the current NPC's race", false, npcInfo);
                return false;
            }

            // Weight Range
            if (npcInfo.NPC.Weight < candidateHeadPart.WeightRange.Lower || npcInfo.NPC.Weight > candidateHeadPart.WeightRange.Upper)
            {
                Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because the current NPC's weight falls outside of the Head Part's allowed weight range", false, npcInfo);
                return false;
            }

            // Allowed and Forced Attributes
            AttributeMatcher.MatchNPCtoAttributeList(candidateHeadPart.AllowedAttributes, npcInfo.NPC, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfWeightedCount, out string _, out string unmatchedLog, out string forceIfLog);
            if (hasAttributeRestrictions && !matchesAttributeRestrictions)
            {
                Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because the NPC does not match any of its allowed attributes: " + unmatchedLog, false, npcInfo);
                return false;
            }
            else
            {
                candidateHeadPart.MatchedForceIfCount = matchedForceIfWeightedCount;
            }

            if (candidateHeadPart.MatchedForceIfCount > 0)
            {
                Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " Current NPC matches the following forced attributes: " + forceIfLog, false, npcInfo);
            }

            // Disallowed Attributes
            AttributeMatcher.MatchNPCtoAttributeList(candidateHeadPart.DisallowedAttributes, npcInfo.NPC, out hasAttributeRestrictions, out matchesAttributeRestrictions, out int dummy, out string matchLog, out string _, out string _);
            if (hasAttributeRestrictions && matchesAttributeRestrictions)
            {
                Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because the NPC matches one of its disallowed attributes: " + matchLog, false, npcInfo);
                return false;
            }

            if (assignedBodySlide != null)
            {
                if (candidateHeadPart.AllowedBodySlideDescriptors.Any())
                {
                    if (!BodyShapeDescriptor.DescriptorsMatch(candidateHeadPart.AllowedBodySlideDescriptorDictionary, assignedBodySlide.BodyShapeDescriptors, out _))
                    {
                        Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because its allowed descriptors do not include those of the assigned BodySlide Preset (" + assignedBodySlide.Label +")" + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(candidateHeadPart.AllowedBodySlideDescriptorDictionary), false, npcInfo);
                        return false;
                    }
                }

                if (BodyShapeDescriptor.DescriptorsMatch(candidateHeadPart.DisallowedBodySlideDescriptorDictionary, assignedBodySlide.BodyShapeDescriptors, out string matchedDescriptor))
                {
                    Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because its descriptor [" + matchedDescriptor + "] disallows the assigned BodySlide Preset (" + assignedBodySlide.Label + ")", false, npcInfo);
                    return false;
                }
            }

            // If the head part is still valid
            return true;
        }

        public static void ResolveConflictsWithAssetAssignments(Dictionary<HeadPart.TypeEnum, HeadPart> assetAssignments, HeadPartSelection headPartAssignments)
        {
            foreach (var type in PatcherSettings.HeadParts.SourceConflictWinners.Keys)
            {
                if (assetAssignments[type] == null) { continue; }
                
                switch(type)
                {
                    case HeadPart.TypeEnum.Eyebrows: headPartAssignments.Misc = ResolveConflictWithAssetAssignment(assetAssignments[type], headPartAssignments.Brows, type); break;
                    case HeadPart.TypeEnum.Eyes: headPartAssignments.Eyes = ResolveConflictWithAssetAssignment(assetAssignments[type], headPartAssignments.Eyes, type); break;
                    case HeadPart.TypeEnum.Face: headPartAssignments.Face = ResolveConflictWithAssetAssignment(assetAssignments[type], headPartAssignments.Face, type); break;
                    case HeadPart.TypeEnum.FacialHair: headPartAssignments.Beard = ResolveConflictWithAssetAssignment(assetAssignments[type], headPartAssignments.Beard, type); break;
                    case HeadPart.TypeEnum.Hair: headPartAssignments.Hair = ResolveConflictWithAssetAssignment(assetAssignments[type], headPartAssignments.Hair, type); break;
                    case HeadPart.TypeEnum.Misc: headPartAssignments.Misc = ResolveConflictWithAssetAssignment(assetAssignments[type], headPartAssignments.Misc, type); break;
                    case HeadPart.TypeEnum.Scars: headPartAssignments.Scars = ResolveConflictWithAssetAssignment(assetAssignments[type], headPartAssignments.Scars, type); break;
                }
            }
        }

        public static FormKey? ResolveConflictWithAssetAssignment(HeadPart assetAssignment, FormKey? headPartAssignment, HeadPart.TypeEnum type)
        {
            if (headPartAssignment == null && assetAssignment != null) { return assetAssignment.FormKey; }
            else if (headPartAssignment != null && assetAssignment == null) { return headPartAssignment.Value; }

            var conflictWinner = PatcherSettings.HeadParts.SourceConflictWinners[type];
            switch (conflictWinner)
            {
                case HeadPartSourceCandidate.AssetPack: return assetAssignment.FormKey; 
                case HeadPartSourceCandidate.HeadPartsMenu: return headPartAssignment.Value;
                default: return null;
            }
        }
    }

    public class HeadPartSelection // intentionally formatted this way rather than using HeadPart.TypeEnum to match EBD Papyrus formatting
    {
        public FormKey? Face { get; set; } = null;
        public FormKey? Eyes { get; set; } = null;
        public FormKey? Beard { get; set; } = null;
        public FormKey? Scars { get; set; } = null;
        public FormKey? Brows { get; set; } = null;
        public FormKey? Hair { get; set; } = null;
        public FormKey? Misc { get; set; } = null;
    }
}
