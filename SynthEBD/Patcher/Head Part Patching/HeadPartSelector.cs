using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class HeadPartSelector
    {
        private readonly IOutputEnvironmentStateProvider _environmentProvider;
        private readonly PatcherState _patcherState;
        private readonly Logger _logger;
        private readonly AttributeMatcher _attributeMatcher;
        private readonly UniqueNPCData _uniqueNPCData;
        public HeadPartSelector(IOutputEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, AttributeMatcher attributeMatcher, UniqueNPCData uniqueNPCData)
        {
            _environmentProvider = environmentProvider;
            _patcherState = patcherState;
            _logger = logger;
            _attributeMatcher = attributeMatcher;
            _uniqueNPCData = uniqueNPCData;
        }

        public void Reinitialize()
        {
            headPartFormLists = new();
        }

        public HeadPartSelection AssignHeadParts(NPCInfo npcInfo, Settings_Headparts settings, List<BodySlideSetting> assignedBodySlides, List<BodyGenConfig.BodyGenTemplate>? assignedBodyGenMorphs, ISkyrimMod outputMod)
        {
            _logger.OpenReportSubsection("HeadParts", npcInfo);
            _logger.LogReport("Selecting Head Parts for Current NPC", false, npcInfo);

            HeadPartSelection selectedHeadParts = new();

            List<string> consistencyReportTriggers = new();

            var tempUniqueNPCDataRecorder = UniqueNPCData.CreateHeadPartTracker(); // keeps the actual tracker null until all head parts are assigned.

            foreach (var headPartType in settings.Types.Keys)
            {
                if (npcInfo.BlockedNPCEntry.HeadParts && npcInfo.BlockedNPCEntry.HeadPartTypes[headPartType])
                {
                    _logger.LogReport(headPartType + " assignment is blocked for current NPC.", false, npcInfo);
                    continue;
                }
                if (npcInfo.BlockedPluginEntry.HeadParts && npcInfo.BlockedPluginEntry.HeadPartTypes[headPartType])
                {
                    _logger.LogReport(headPartType + " assignment is blocked for current NPC's plugin.", false, npcInfo);
                    continue;
                }

                HeadPartConsistency currentConsistency = null;
                if (_patcherState.GeneralSettings.bEnableConsistency && 
                    npcInfo.ConsistencyNPCAssignment != null &&
                    npcInfo.ConsistencyNPCAssignment.HeadParts != null && 
                    npcInfo.ConsistencyNPCAssignment.HeadParts.ContainsKey(headPartType)
                    )
                {
                    currentConsistency = npcInfo.ConsistencyNPCAssignment.HeadParts[headPartType];
                }
                IHeadPartGetter selection = AssignHeadPartType(settings.Types[headPartType], _patcherState.GeneralSettings.AttributeGroups, headPartType, npcInfo, assignedBodySlides, assignedBodyGenMorphs, currentConsistency, out bool randomizedToNone);
                FormKey? selectedFK = null;
                if (selection != null) { selectedFK = selection.FormKey; }
                AllocateHeadPartSelection(selectedFK, headPartType, selectedHeadParts);

                // record consistency mismatches
                if (currentConsistency != null && currentConsistency.Initialized)
                {
                    var bothNull = currentConsistency.FormKey.IsNull && selection == null;
                    var overwriteNull = selection != null && currentConsistency.FormKey.IsNull;
                    var bothMatch = selection != null && selection.FormKey.Equals(currentConsistency.FormKey);
                    if (!bothNull && !overwriteNull && !bothMatch)
                    {
                        consistencyReportTriggers.Add(headPartType.ToString());
                    }
                }

                // record new consistency
                if (_patcherState.GeneralSettings.bEnableConsistency)
                {
                    if (npcInfo.ConsistencyNPCAssignment == null)
                    {
                        npcInfo.ConsistencyNPCAssignment = new();
                    }
                    if (npcInfo.ConsistencyNPCAssignment.HeadParts == null)
                    {
                        npcInfo.ConsistencyNPCAssignment.HeadParts = new();
                    }
                    if (!npcInfo.ConsistencyNPCAssignment.HeadParts.ContainsKey(headPartType))
                    {
                        npcInfo.ConsistencyNPCAssignment.HeadParts.Add(headPartType, new());
                    }

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

                // unique NPC linkage
                tempUniqueNPCDataRecorder[headPartType] = selection;

                // add NPC's race to headpart races if necessary
                MakeRaceCompatible(selection, npcInfo.NPC.Race.FormKey, outputMod);
            }

            if (consistencyReportTriggers.Any())
            {
                _logger.LogMessage(npcInfo.LogIDstring + ": (" + String.Join(", ", consistencyReportTriggers) + ") could not be assigned from Consistency and were re-randomized.");
            }

            if (_patcherState.GeneralSettings.bLinkNPCsWithSameName)
            {
                _uniqueNPCData.InitializeUnsetUniqueNPCHeadParts(npcInfo, tempUniqueNPCDataRecorder);
            }

            _logger.CloseReportSubsectionsToParentOf("HeadParts", npcInfo);
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

        public IHeadPartGetter AssignHeadPartType(Settings_HeadPartType currentSettings, HashSet<AttributeGroup> attributeGroups, HeadPart.TypeEnum type, NPCInfo npcInfo, List<BodySlideSetting> assignedBodySlides, List<BodyGenConfig.BodyGenTemplate>? assignedBodyGenMorphs, HeadPartConsistency currentConsistency, out bool randomizedToNone)
        {
            randomizedToNone = false;
            // if there are no head parts of this type at all, don't assign consistency.
            if (!currentSettings.HeadParts.Any())
            {
                _logger.LogReport("No " + type + " head parts have been imported.", false, npcInfo);
                return null;
            }

            IHeadPartGetter specificAssignment = null;
            if (npcInfo.SpecificNPCAssignment != null && !npcInfo.SpecificNPCAssignment.HeadParts[type].FormKey.IsNull)
            {
                var specificAssignmentSetting = currentSettings.HeadParts.Where(x => x.HeadPartFormKey.Equals(npcInfo.SpecificNPCAssignment.HeadParts[type].FormKey)).FirstOrDefault();
                if (specificAssignmentSetting != null && specificAssignmentSetting.ResolvedHeadPart != null)
                {
                    specificAssignment = specificAssignmentSetting.ResolvedHeadPart;
                    _logger.LogReport("Assigning " + type + ": " + (specificAssignment.EditorID ?? specificAssignment.FormKey.ToString()) + " via Specific NPC Assignment", false, npcInfo);
                    return specificAssignment;
                }
                else if (specificAssignmentSetting != null && specificAssignmentSetting.ResolvedHeadPart != null)
                {
                    _logger.LogReport("Specific NPC Assignment for " + type + " calls for " + npcInfo.SpecificNPCAssignment.HeadParts[type].FormKey.ToString() + " but this head part does not currently exist in the load order. Assigning a different head part.", true, npcInfo);
                }

                if (specificAssignment == null && !currentSettings.HeadPartsGendered[npcInfo.Gender].Any()) // if no specific NPC assignment, then headpart assignment is locked to gender.
                {
                    _logger.LogReport("No " + npcInfo.Gender.ToString() + " " + type + " head parts have been imported.", false, npcInfo);
                    return null;
                }
            }

            if (!CanGetThisHeadPartType(currentSettings, type, npcInfo, assignedBodySlides, assignedBodyGenMorphs, attributeGroups))
            {
                return null;
            }

            if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary && npcInfo.AssociatedLinkGroup.HeadPartAssignments[type] != null)
            {
                var linkedHeadPart = npcInfo.AssociatedLinkGroup.HeadPartAssignments[type];
                _logger.LogReport("Assigning " + type + ": " + (linkedHeadPart.EditorID ?? linkedHeadPart.FormKey.ToString()) + " via the NPC's Link Group.", false, npcInfo);
                return linkedHeadPart;
            }
            else if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary && npcInfo.AssociatedLinkGroup.HeadPartAssignments[type] == null)
            {
                _logger.LogReport("Assigning " + type + ": NONE via the NPC's Link Group.", false, npcInfo);
                return null;
            }
            else if (_patcherState.GeneralSettings.bLinkNPCsWithSameName && npcInfo.IsValidLinkedUnique && _uniqueNPCData.TryGetUniqueNPCHeadParts(npcInfo, out var uniqueAssignments, out var uniqueFounderNPC) && uniqueAssignments.ContainsKey(type))
            {
                var assignedHeadPart = uniqueAssignments[type];
                if (assignedHeadPart != null)
                {
                    _logger.LogReport("Another unique NPC with the same name (" + uniqueFounderNPC + ") was assigned a " + type + ": " + (assignedHeadPart.EditorID ?? assignedHeadPart.FormKey.ToString()) + ". Using that " + type + " for current NPC.", false, npcInfo);
                    return assignedHeadPart;
                }
                else
                {
                    _logger.LogReport("Another unique NPC with the same name (" + uniqueFounderNPC + ") was not assigned a " + type + ", so current NPC will also not be assigned a " + type, false, npcInfo);
                    return null;
                }
            }

            var availableHeadParts = currentSettings.HeadPartsGendered[npcInfo.Gender].Where(x => HeadPartIsValid(x, npcInfo, type, assignedBodySlides, assignedBodyGenMorphs, attributeGroups)).ToHashSet();
            var availableEDIDs = availableHeadParts.Select(x => x.EditorID ?? x.HeadPartFormKey.ToString()).ToHashSet();

            IHeadPartGetter consistencyHeadPart = null;
            if (currentConsistency != null && !currentConsistency.FormKey.IsNull)
            {
                if (!_environmentProvider.LinkCache.TryResolve<IHeadPartGetter>(npcInfo.ConsistencyNPCAssignment.HeadParts[type].FormKey, out consistencyHeadPart))
                {
                    _logger.LogReport("The consistency " + type + " head part " + npcInfo.ConsistencyNPCAssignment.HeadParts[type].FormKey.ToString() + " is no longer present in the load order.", true, npcInfo);
                }
            }

            _logger.LogReport("The following headparts are allowed under the current rule set: " + Environment.NewLine + String.Join(Environment.NewLine, availableEDIDs), false, npcInfo);

            IHeadPartGetter selectedHeadPart = null;

            var forceIfHeadParts = availableHeadParts.Where(x => x.MatchedForceIfCount > 0).OrderBy(x => x.MatchedForceIfCount).ToArray();
            if (forceIfHeadParts.Any())
            {
                var forceIfHeadPartStrings = forceIfHeadParts.Select(x => (x.EditorID ?? x.HeadPartFormKey.ToString()) + ": " + x.MatchedForceIfCount).ToArray();
                _logger.LogReport("The following headparts have matched ForceIf attributes:" + Environment.NewLine + String.Join(Environment.NewLine, forceIfHeadPartStrings), false, npcInfo);
                selectedHeadPart = ChooseHeadPart(forceIfHeadParts, consistencyHeadPart, npcInfo, type, 100, out randomizedToNone);
            }
            else if (currentConsistency != null && currentConsistency.RandomizedToNone)
            {
                _logger.LogReport("This NPC's consistency shows it was previously selected to NOT receive a " + type + ". Therefore, one will NOT be assigned unless overriden via a Specific or ForceIf assignment." + type, false, npcInfo);
                randomizedToNone = true;
            }
            else if (availableHeadParts.Any())
            { 
                selectedHeadPart = ChooseHeadPart(availableHeadParts, consistencyHeadPart, npcInfo, type, currentSettings.RandomizationPercentage, out randomizedToNone);
                if (consistencyHeadPart != null && (selectedHeadPart == null || !selectedHeadPart.FormKey.Equals(consistencyHeadPart.FormKey)))
                {
                    _logger.LogReport("The consistency " + type + " was disallowed under the current rule set.", true, npcInfo);
                }
            }
            else
            {
                _logger.LogReport("No " + type.ToString() + "head parts are available for this NPC", false, npcInfo);
            }

            return selectedHeadPart;
        }

        public IHeadPartGetter ChooseHeadPart(IEnumerable<HeadPartSetting> options, IHeadPartGetter consistencyHeadPart, NPCInfo npcInfo, HeadPart.TypeEnum type, double randomizationPercentage, out bool randomizedToNone)
        {
            randomizedToNone = false;
            if (consistencyHeadPart != null)
            {
                var consistencyAssignment = options.Where(x => x.HeadPartFormKey.Equals(consistencyHeadPart.FormKey)).FirstOrDefault();
                if (consistencyAssignment != null)
                {
                    _logger.LogReport("Assigning head part " + (consistencyAssignment.EditorID ?? consistencyAssignment.HeadPartFormKey.ToString()) + " from Consistency.", false, npcInfo);
                    return consistencyHeadPart;
                }
            }
            // if no consistency match, select at random

            if (!BoolByProbability.Decide(randomizationPercentage)) // controlled by headpart type's randomization percentage
            {
                _logger.LogReport("NPC's " + type + " was chosen at random to NOT be replaced.", false, npcInfo);
                randomizedToNone = true;
                return null;
            }

            var selectedAssignment = (HeadPartSetting)ProbabilityWeighting.SelectByProbability(options);
            _logger.LogReport("Selected " + type + ": " + EditorIDHandler.GetEditorIDSafely(selectedAssignment.ResolvedHeadPart) + " at random.", false, npcInfo);
            return selectedAssignment.ResolvedHeadPart;
        }
        public bool CanGetThisHeadPartType(Settings_HeadPartType currentSettings, HeadPart.TypeEnum type, NPCInfo npcInfo, List<BodySlideSetting> assignedBodySlides, List<BodyGenConfig.BodyGenTemplate> assignedBodyGenMorphs, HashSet<AttributeGroup> attributeGroups)
        {
            if (!currentSettings.bAllowRandom && currentSettings.MatchedForceIfCount == 0) // don't need to check for specific assignment because it was evaluated just above
            {
                _logger.LogReport(type + " headparts will not be assigned because they can only be assigned via ForceIf attributes or Specific NPC Assignments", false, npcInfo);
                return false;
            }

            if (npcInfo.Gender == Gender.Male && !currentSettings.bAllowMale)
            {
                _logger.LogReport(type + " headparts cannot be distributed to male NPCs unless assigned via Specific Assignment.", false, npcInfo);
                return false;
            }
            else if (npcInfo.Gender == Gender.Female && !currentSettings.bAllowFemale)
            {
                _logger.LogReport(type + " headparts cannot be distributed to female NPCs unless assigned via Specific Assignment.", false, npcInfo);
                return false;
            }

            if (currentSettings.bRestrictToNPCsWithThisType)
            {
                var existingHeadParts = npcInfo.ExistingHeadParts.Where(x => x.Type != null && x.Type == type);
                if (!existingHeadParts.Any())
                {
                    _logger.LogReport(type + " headparts are restricted to NPCs which already have headparts of this type, which this NPC does not", false, npcInfo);
                    return false;
                }
                else if (existingHeadParts.Count() == 1)
                {
                    var existingHP = existingHeadParts.First();
                    bool hasNoneHeadPart = false;
                    switch(type)
                    {
                        case HeadPart.TypeEnum.Eyebrows: 
                            if (existingHP.Equals(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.HeadPart.BrowsMaleHumanoid12NoBrow) || existingHP.Equals(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.HeadPart.FemaleBrowsHuman12NoBrow))
                            {
                                hasNoneHeadPart = true;
                            }
                            break;
                        case HeadPart.TypeEnum.FacialHair:
                            if (existingHP.Equals(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.HeadPart.HumanBeard00NoBeard) || existingHP.Equals(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.HeadPart.KhajiitNoBeard))
                            {
                                hasNoneHeadPart = true;
                            }
                            break;
                        case HeadPart.TypeEnum.Scars:
                            if (existingHP.Equals(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.HeadPart.MarksFemaleHumanoid00NoGash) || existingHP.Equals(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.HeadPart.MarksMaleHumanoid00NoScar))
                            {
                                hasNoneHeadPart = true;
                            }
                            break;
                        default: break;
                    }
                    if (hasNoneHeadPart)
                    {
                        _logger.LogReport(type + " headparts are restricted to NPCs which already have headparts of this type, and this NPC has the null-equivalent headpart: " + EditorIDHandler.GetEditorIDSafely(existingHP), false, npcInfo);
                        return false;
                    }
                }
            }

            // Allow unique NPCs
            if (!currentSettings.bAllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                _logger.LogReport(type + " is invalid because its distribution is disallowed for unique NPCs", false, npcInfo);
                return false;
            }

            // Allow non-unique NPCs
            if (!currentSettings.bAllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                _logger.LogReport(type + " is invalid because its distribution is disallowed for non-unique NPCs", false, npcInfo);
                return false;
            }

            // Allowed Races
            if (currentSettings.AllowedRaces.Any() && !currentSettings.AllowedRaces.Contains(npcInfo.HeadPartsRace))
            {
                _logger.LogReport(type + " is invalid because its allowed races (" + Logger.GetRaceListLogStrings(currentSettings.AllowedRaces, _environmentProvider.LinkCache, _patcherState) + ") do not include the current NPC's race", false, npcInfo);
                return false;
            }

            // Disallowed Races
            if (currentSettings.DisallowedRaces.Contains(npcInfo.HeadPartsRace))
            {
                _logger.LogReport(type + " is invalid because its disallowed races (" + Logger.GetRaceListLogStrings(currentSettings.DisallowedRaces, _environmentProvider.LinkCache, _patcherState) + ") include the current NPC's race", false, npcInfo);
                return false;
            }

            // Weight Range
            if (npcInfo.NPC.Weight < currentSettings.WeightRange.Lower || npcInfo.NPC.Weight > currentSettings.WeightRange.Upper)
            {
                _logger.LogReport(type + " is invalid because the current NPC's weight falls outside of its allowed weight range", false, npcInfo);
                return false;
            }

            // Allowed and Forced Attributes
            currentSettings.MatchedForceIfCount = 0;
            _attributeMatcher.MatchNPCtoAttributeList(currentSettings.AllowedAttributes, npcInfo.NPC, npcInfo.HeadPartsRace, attributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfWeightedCount, out string _, out string unmatchedLog, out string forceIfLog, null);
            if (hasAttributeRestrictions && !matchesAttributeRestrictions)
            {
                _logger.LogReport(type + " is invalid because the NPC does not match any of its allowed attributes: " + unmatchedLog, false, npcInfo);
                return false;
            }
            else
            {
                currentSettings.MatchedForceIfCount = matchedForceIfWeightedCount;
            }

            if (currentSettings.MatchedForceIfCount > 0)
            {
                _logger.LogReport(type + " Current NPC matches the following forced attributes: " + forceIfLog, false, npcInfo);
            }

            // Disallowed Attributes
            _attributeMatcher.MatchNPCtoAttributeList(currentSettings.DisallowedAttributes, npcInfo.NPC, npcInfo.HeadPartsRace, attributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out hasAttributeRestrictions, out matchesAttributeRestrictions, out int dummy, out string matchLog, out string _, out string _, null);
            if (hasAttributeRestrictions && matchesAttributeRestrictions)
            {
                _logger.LogReport(type + " is invalid because the NPC matches one of its disallowed attributes: " + matchLog, false, npcInfo);
                return false;
            }

            // Descriptors
            foreach (var assignedBodySlide in assignedBodySlides)
            {
                if (currentSettings.AllowedBodySlideDescriptors.Any())
                {
                    if (!BodyShapeDescriptor.DescriptorsMatch(currentSettings.AllowedBodySlideDescriptorDictionary, assignedBodySlide.BodyShapeDescriptors, currentSettings.AllowedBodySlideMatchMode, out _))
                    {
                        _logger.LogReport(type + " is invalid because its allowed descriptors do not include those of the assigned BodySlide Preset (" + assignedBodySlide.Label + ")" + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(currentSettings.AllowedBodySlideDescriptorDictionary), false, npcInfo);
                        return false;
                    }
                }

                if (BodyShapeDescriptor.DescriptorsMatch(currentSettings.DisallowedBodySlideDescriptorDictionary, assignedBodySlide.BodyShapeDescriptors, currentSettings.DisallowedBodySlideMatchMode, out string matchedDescriptor))
                {
                    _logger.LogReport(type + "is invalid because its descriptor [" + matchedDescriptor + "] disallows the assigned BodySlide Preset (" + assignedBodySlide.Label + ")", false, npcInfo);
                    return false;
                }
            }

            if (assignedBodyGenMorphs != null)
            {
                Dictionary<string, HashSet<string>> genderedDescriptorsAllowed = new();
                Dictionary<string, HashSet<string>> genderedDescriptorsDisallowed = new();
                DescriptorMatchMode allowedMatchMode = DescriptorMatchMode.All;
                DescriptorMatchMode disallowedMatchMode = DescriptorMatchMode.All;

                switch (npcInfo.Gender)
                {
                    case Gender.Male:
                        genderedDescriptorsAllowed = currentSettings.AllowedBodyGenDescriptorDictionaryMale;
                        allowedMatchMode = currentSettings.AllowedBodyGenDescriptorMatchModeMale;
                        genderedDescriptorsDisallowed = currentSettings.DisallowedBodyGenDescriptorDictionaryMale;
                        disallowedMatchMode = currentSettings.DisallowedBodyGenDescriptorMatchModeMale;
                        break;
                    case Gender.Female:
                        genderedDescriptorsAllowed = currentSettings.AllowedBodyGenDescriptorDictionaryFemale;
                        allowedMatchMode = currentSettings.AllowedBodyGenDescriptorMatchModeFemale;
                        genderedDescriptorsDisallowed = currentSettings.DisallowedBodyGenDescriptorDictionaryFemale;
                        disallowedMatchMode = currentSettings.DisallowedBodyGenDescriptorMatchModeFemale;
                        break;
                }
                foreach (var morph in assignedBodyGenMorphs)
                {
                    if (genderedDescriptorsAllowed.Any())
                    {
                        if (!BodyShapeDescriptor.DescriptorsMatch(genderedDescriptorsAllowed, morph.BodyShapeDescriptors, allowedMatchMode, out _))
                        {
                            _logger.LogReport(type + " is invalid because its allowed descriptors do not include those of the assigned BodyGen Morph (" + morph.Label + ")" + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(genderedDescriptorsAllowed), false, npcInfo);
                            return false;
                        }
                    }

                    if (BodyShapeDescriptor.DescriptorsMatch(genderedDescriptorsDisallowed, morph.BodyShapeDescriptors, disallowedMatchMode, out string matchedDescriptor))
                    {
                        _logger.LogReport(type + " is invalid because its descriptor [" + matchedDescriptor + "] disallows the assigned BodyGen Morph (" + morph.Label + ")", false, npcInfo);
                        return false;
                    }
                }
            }

            // If the head part type is still valid
            return true;
        }

        public bool HeadPartIsValid(HeadPartSetting candidateHeadPart, NPCInfo npcInfo, HeadPart.TypeEnum type, List<BodySlideSetting> assignedBodySlides, List<BodyGenConfig.BodyGenTemplate> assignedBodyGenMorphs, HashSet<AttributeGroup> attributeGroups)
        {
            if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.HeadParts[type].FormKey.Equals(candidateHeadPart.HeadPartFormKey))
            {
                _logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is valid because it is specifically assigned by user.", false, npcInfo);
                return true;
            }

            if (!candidateHeadPart.bAllowRandom && candidateHeadPart.MatchedForceIfCount == 0) // don't need to check for specific assignment because it was evaluated just above
            {
                _logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because it can only be assigned via ForceIf attributes or Specific NPC Assignments", false, npcInfo);
                return false;
            }

            // Allow unique NPCs
            if (!candidateHeadPart.bAllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                _logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because the current Head Part is disallowed for unique NPCs", false, npcInfo);
                return false;
            }

            // Allow non-unique NPCs
            if (!candidateHeadPart.bAllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                _logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because the current Head Part is disallowed for non-unique NPCs", false, npcInfo);
                return false;
            }

            // Allowed Races
            if (candidateHeadPart.AllowedRaces.Any() && !candidateHeadPart.AllowedRaces.Contains(npcInfo.HeadPartsRace))
            {
                _logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because its allowed races (" + Logger.GetRaceListLogStrings(candidateHeadPart.AllowedRaces, _environmentProvider.LinkCache, _patcherState) + ") do not include the current NPC's race", false, npcInfo);
                return false;
            }

            // Disallowed Races
            if (candidateHeadPart.DisallowedRaces.Contains(npcInfo.HeadPartsRace))
            {
                _logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because its disallowed races (" + Logger.GetRaceListLogStrings(candidateHeadPart.DisallowedRaces, _environmentProvider.LinkCache, _patcherState) + ") include the current NPC's race", false, npcInfo);
                return false;
            }

            // Weight Range
            if (npcInfo.NPC.Weight < candidateHeadPart.WeightRange.Lower || npcInfo.NPC.Weight > candidateHeadPart.WeightRange.Upper)
            {
                _logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because the current NPC's weight falls outside of the Head Part's allowed weight range", false, npcInfo);
                return false;
            }

            // Allowed and Forced Attributes
            candidateHeadPart.MatchedForceIfCount = 0;
            _attributeMatcher.MatchNPCtoAttributeList(candidateHeadPart.AllowedAttributes, npcInfo.NPC, npcInfo.HeadPartsRace, attributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfWeightedCount, out string _, out string unmatchedLog, out string forceIfLog, null);
            if (hasAttributeRestrictions && !matchesAttributeRestrictions)
            {
                _logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because the NPC does not match any of its allowed attributes: " + unmatchedLog, false, npcInfo);
                return false;
            }
            else
            {
                candidateHeadPart.MatchedForceIfCount = matchedForceIfWeightedCount;
            }

            if (candidateHeadPart.MatchedForceIfCount > 0)
            {
                _logger.LogReport("Head Part " + candidateHeadPart.EditorID + " Current NPC matches the following forced attributes: " + forceIfLog, false, npcInfo);
            }

            // Disallowed Attributes
            _attributeMatcher.MatchNPCtoAttributeList(candidateHeadPart.DisallowedAttributes, npcInfo.NPC, npcInfo.HeadPartsRace, attributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out hasAttributeRestrictions, out matchesAttributeRestrictions, out int dummy, out string matchLog, out string _, out string _, null);
            if (hasAttributeRestrictions && matchesAttributeRestrictions)
            {
                _logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because the NPC matches one of its disallowed attributes: " + matchLog, false, npcInfo);
                return false;
            }

            foreach (var assignedBodySlide in assignedBodySlides)
            {
                if (candidateHeadPart.AllowedBodySlideDescriptors.Any())
                {
                    if (!BodyShapeDescriptor.DescriptorsMatch(candidateHeadPart.AllowedBodySlideDescriptorDictionary, assignedBodySlide.BodyShapeDescriptors, candidateHeadPart.AllowedBodySlideMatchMode, out _))
                    {
                        _logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because its allowed descriptors do not include those of the assigned BodySlide Preset (" + assignedBodySlide.Label +")" + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(candidateHeadPart.AllowedBodySlideDescriptorDictionary), false, npcInfo);
                        return false;
                    }
                }

                if (BodyShapeDescriptor.DescriptorsMatch(candidateHeadPart.DisallowedBodySlideDescriptorDictionary, assignedBodySlide.BodyShapeDescriptors, candidateHeadPart.DisallowedBodySlideMatchMode, out string matchedDescriptor))
                {
                    _logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because its descriptor [" + matchedDescriptor + "] disallows the assigned BodySlide Preset (" + assignedBodySlide.Label + ")", false, npcInfo);
                    return false;
                }
            }

            if (assignedBodyGenMorphs != null)
            {
                Dictionary<string, HashSet<string>> genderedDescriptorsAllowed = new();
                Dictionary<string, HashSet<string>> genderedDescriptorsDisallowed = new();
                DescriptorMatchMode allowedMatchMode = DescriptorMatchMode.All;
                DescriptorMatchMode disallowedMatchMode = DescriptorMatchMode.All;

                switch (npcInfo.Gender)
                {
                    case Gender.Male: 
                        genderedDescriptorsAllowed = candidateHeadPart.AllowedBodyGenDescriptorDictionaryMale;
                        allowedMatchMode = candidateHeadPart.AllowedBodyGenDescriptorMatchModeMale;
                        genderedDescriptorsDisallowed = candidateHeadPart.DisallowedBodyGenDescriptorDictionaryMale;
                        disallowedMatchMode = candidateHeadPart.DisallowedBodyGenDescriptorMatchModeMale;
                        break;
                    case Gender.Female: 
                        genderedDescriptorsAllowed = candidateHeadPart.AllowedBodyGenDescriptorDictionaryFemale;
                        allowedMatchMode = candidateHeadPart.AllowedBodyGenDescriptorMatchModeFemale;
                        genderedDescriptorsDisallowed = candidateHeadPart.DisallowedBodyGenDescriptorDictionaryFemale;
                        disallowedMatchMode = candidateHeadPart.DisallowedBodyGenDescriptorMatchModeFemale;
                        break;
                }
                foreach (var morph in assignedBodyGenMorphs)
                {
                    if (genderedDescriptorsAllowed.Any())
                    {
                        if (!BodyShapeDescriptor.DescriptorsMatch(genderedDescriptorsAllowed, morph.BodyShapeDescriptors, allowedMatchMode, out _))
                        {
                            _logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because its allowed descriptors do not include those of the assigned BodyGen Morph (" + morph.Label + ")" + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(genderedDescriptorsAllowed), false, npcInfo);
                            return false;
                        }
                    }

                    if (BodyShapeDescriptor.DescriptorsMatch(genderedDescriptorsDisallowed, morph.BodyShapeDescriptors, disallowedMatchMode, out string matchedDescriptor))
                    {
                        _logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because its descriptor [" + matchedDescriptor + "] disallows the assigned BodyGen Morph (" + morph.Label + ")", false, npcInfo);
                        return false;
                    }
                }
            }

            // If the head part is still valid
            return true;
        }

        public void ResolveConflictsWithAssetAssignments(Dictionary<HeadPart.TypeEnum, HeadPart> assetAssignments, HeadPartSelection headPartAssignments)
        {
            foreach (var type in _patcherState.HeadPartSettings.SourceConflictWinners.Keys)
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

        public FormKey? ResolveConflictWithAssetAssignment(HeadPart assetAssignment, FormKey? headPartAssignment, HeadPart.TypeEnum type)
        {
            if (headPartAssignment == null && assetAssignment != null) { return assetAssignment.FormKey; }
            else if (headPartAssignment != null && assetAssignment == null) { return headPartAssignment.Value; }

            var conflictWinner = _patcherState.HeadPartSettings.SourceConflictWinners[type];
            switch (conflictWinner)
            {
                case HeadPartSourceCandidate.AssetPack: return assetAssignment.FormKey; 
                case HeadPartSourceCandidate.HeadPartsMenu: return headPartAssignment.Value;
                default: return null;
            }
        }
        public void SetGeneratedHeadPart(HeadPart hp, Dictionary<HeadPart.TypeEnum, HeadPart> dict, NPCInfo npcInfo)
        {
            if (hp.Type != null)
            {
                if (npcInfo.BlockedNPCEntry.HeadParts && npcInfo.BlockedNPCEntry.HeadPartTypes[hp.Type.Value])
                {
                    _logger.LogReport(hp.Type.Value.ToString() + " assignment is blocked for current NPC.", false, npcInfo);
                    return;
                }
                if (npcInfo.BlockedPluginEntry.HeadParts && npcInfo.BlockedPluginEntry.HeadPartTypes[hp.Type.Value])
                {
                    _logger.LogReport(hp.Type.Value.ToString() + " assignment is blocked for current NPC's plugin.", false, npcInfo);
                    return;
                }
                dict[hp.Type.Value] = hp;
            }
            else
            {
                _logger.LogMessage("Cannot assign a head part replacer for head part " + EditorIDHandler.GetEditorIDSafely(hp) + " because it does not have a specified Type.");
            }
        }

        public bool BlockNPCWithCustomFaceGen(NPCInfo npcInfo) // currently incredibly inefficient - will try to speed up later.
        {
            if (!_patcherState.GeneralSettings.bHeadPartsExcludeCustomHeads)
            {
                return false;
            }

            var contextualGetters = npcInfo.NPC.FormKey.ToLinkGetter<INpcGetter>().ResolveAll(_environmentProvider.LinkCache);
            if (contextualGetters != null && contextualGetters.Count() > 1)
            {
                var winningContextRecord = contextualGetters.First();
                var baseContextRecord = contextualGetters.Last();

                // check face morph
                if (winningContextRecord.FaceMorph == null)
                {
                    if (baseContextRecord.FaceMorph != null)
                    {
                        return true;
                    }
                }
                else if (!winningContextRecord.FaceMorph.Equals(baseContextRecord.FaceMorph))
                {
                    return true;
                }

                // check face parts
                if (winningContextRecord.FaceParts == null)
                {
                    if (baseContextRecord.FaceParts != null)
                    {
                        return true;
                    }
                }
                else if (!winningContextRecord.FaceParts.Equals(baseContextRecord.FaceParts))
                {
                    return true;
                }

                // check head parts
                if (winningContextRecord.HeadParts == null)
                {
                    if (baseContextRecord.HeadParts != null)
                    {
                        return true;
                    }
                }
                else if (!winningContextRecord.HeadParts.Equals(baseContextRecord.HeadParts))
                {
                    return true;
                }
            }
            return false;
        }

        private void MakeRaceCompatible(IHeadPartGetter selectedHeadPartGetter, FormKey currentNpcRaceFK, ISkyrimMod outputMod)
        {
            if (selectedHeadPartGetter == null) { return; }

            HeadPart currentHeadPart;
            FormList raceFormList = null;
            if (selectedHeadPartGetter.ValidRaces == null || !selectedHeadPartGetter.ValidRaces.TryResolve(_environmentProvider.LinkCache, out var raceListGetter) || raceListGetter.Items == null)
            {
                raceFormList = GetRaceFormList(selectedHeadPartGetter, outputMod);
            }
            else if (!raceListGetter.Items.Contains(currentNpcRaceFK))
            {
                raceFormList = GetRaceFormList(selectedHeadPartGetter, outputMod); // seems like Mutagen new()s this automatically
                raceFormList.Items.AddRange(raceListGetter.Items);
            }
            
            if (raceFormList != null)
            {
                raceFormList.Items.Add(currentNpcRaceFK);
                currentHeadPart = outputMod.HeadParts.GetOrAddAsOverride(selectedHeadPartGetter);
                currentHeadPart.ValidRaces.SetTo(raceFormList);
            }
        }

        private FormList GetRaceFormList(IHeadPartGetter selectedHeadPartGetter, ISkyrimMod outputMod)
        {
            if (headPartFormLists.ContainsKey(selectedHeadPartGetter))
            {
                return headPartFormLists[selectedHeadPartGetter];
            }
            else
            {
                var raceFormList = outputMod.FormLists.AddNew();
                raceFormList.EditorID = "FL_HeadPartRaces_" + EditorIDHandler.GetEditorIDSafely(selectedHeadPartGetter);
                headPartFormLists.Add(selectedHeadPartGetter, raceFormList);
                return raceFormList;
            }
        }

        private Dictionary<IHeadPartGetter, FormList> headPartFormLists = new();
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

        public bool HasAssignment()
        {
            if (Hair != null && !Hair.Value.IsNull) { return true; }
            if (Face != null && !Face.Value.IsNull) { return true; }
            if (Eyes != null && !Eyes.Value.IsNull) { return true; }
            if (Beard != null && !Beard.Value.IsNull) { return true; }
            if (Scars != null && !Scars.Value.IsNull) { return true; }
            if (Brows != null && !Brows.Value.IsNull) { return true; }
            if (Misc != null && !Misc.Value.IsNull) { return true; }

            return false;
        }
    }
}
