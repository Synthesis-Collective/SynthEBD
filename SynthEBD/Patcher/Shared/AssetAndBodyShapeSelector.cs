using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class AssetAndBodyShapeSelector
    {
        public class AssetAndBodyShapeAssignment
        {
            public AssetAndBodyShapeAssignment()
            {
                this.AssignedCombination = null;
                this.AssignedBodyGenMorphs = new List<BodyGenConfig.BodyGenTemplate>();
                this.AssignedOBodyPreset = null;
            }
            public SubgroupCombination AssignedCombination { get; set; }
            public List<BodyGenConfig.BodyGenTemplate> AssignedBodyGenMorphs { get; set; }
            public BodySlideSetting AssignedOBodyPreset { get; set; }
        }

        public enum AssetPackAssignmentMode
        {
            Primary,
            MixIn,
            ReplacerVirtual
        }

        /// <summary>
        /// Assigns a SubgroupCombination to the given NPC
        /// If BodyGen integration is enabled, attempts to assign a morph that complies with the chosen combination's bodygen restrictions.
        /// </summary>
        /// <param name="bodyShapeAssigned">true if a BodyGen morph was able to be assigned. false if a morph could not be assigned and must be set independently of the SubgroupCombination</param>
        /// <param name="availableAssetPacks">Asset packs available to the current NPC</param>
        /// <param name="npcInfo">NPC info class</param>
        /// <returns></returns>
        public static AssetAndBodyShapeAssignment ChooseCombinationAndBodyShape(out bool assetsAssigned, out bool bodyShapeAssigned, HashSet<FlattenedAssetPack> availableAssetPacks, BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings, NPCInfo npcInfo, bool blockBodyShape, AssetPackAssignmentMode mode, AssetAndBodyShapeAssignment currentAssignments)
        {
            AssetAndBodyShapeAssignment assignment = new AssetAndBodyShapeAssignment();
            SubgroupCombination chosenCombination = new SubgroupCombination();
            assetsAssigned = false;
            bodyShapeAssigned = false;

            string subSectionLabel = string.Empty;
            string reportLine = string.Empty;
            if (mode == AssetPackAssignmentMode.Primary && !blockBodyShape)
            {
                subSectionLabel = "AssetsAndBody";
                reportLine = "Assigning Assets and Body Shape Combination";
            }
            else if (mode == AssetPackAssignmentMode.Primary)
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

            Logger.OpenReportSubsection(subSectionLabel, npcInfo);
            Logger.LogReport(reportLine, false, npcInfo);

            bool selectedFromLinkedNPC = false;
            if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary)
            {
                SubgroupCombination linkedCombination = null;
                switch (mode)
                {
                    case AssetPackAssignmentMode.Primary: linkedCombination = npcInfo.AssociatedLinkGroup.AssignedCombination; break;
                    case AssetPackAssignmentMode.MixIn: break; ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////// FILL OUT LATER 
                    case AssetPackAssignmentMode.ReplacerVirtual: var linkedAssignmentGroup = npcInfo.AssociatedLinkGroup.ReplacerAssignments.Where(x => x.ReplacerName == availableAssetPacks.First().GroupName).FirstOrDefault();
                        if (linkedAssignmentGroup != null) {  linkedCombination = linkedAssignmentGroup.AssignedReplacerCombination; }
                        break;
                }

                if (linkedCombination != null && AssetSelector.CombinationAllowedBySpecificNPCAssignment(npcInfo.SpecificNPCAssignment, linkedCombination, mode))
                {
                    Logger.LogReport("Selected combination from NPC link group", false, npcInfo);
                    chosenCombination = npcInfo.AssociatedLinkGroup.AssignedCombination;

                    if (!blockBodyShape && mode == AssetPackAssignmentMode.Primary)
                    {
                        switch (PatcherSettings.General.BodySelectionMode)
                        {
                            case BodyShapeSelectionMode.BodyGen:
                                assignment.AssignedBodyGenMorphs = npcInfo.AssociatedLinkGroup.AssignedMorphs;
                                bodyShapeAssigned = assignment.AssignedBodyGenMorphs.Any();
                                break;
                            case BodyShapeSelectionMode.BodySlide:
                                assignment.AssignedOBodyPreset = npcInfo.AssociatedLinkGroup.AssignedBodySlide;
                                bodyShapeAssigned = assignment.AssignedOBodyPreset != null;
                                break;
                            default: break;
                        }

                        if (bodyShapeAssigned)
                        {
                            Logger.LogReport("Selected body shape from NPC link group", false, npcInfo);
                        }
                    }
                    selectedFromLinkedNPC = true;
                }
                else if (linkedCombination != null)
                {
                    Logger.LogReport("The linked combination (" + linkedCombination.Signature + ") assigned to the primary Link Group member was incompatible with the Specific Assignments for this NPC. Consider making Specific Assignments only for the primary link group member.", true, npcInfo);
                }
            }
            else if (PatcherSettings.General.bLinkNPCsWithSameName && npcInfo.IsValidLinkedUnique)
            {
                SubgroupCombination linkedCombination = null;
                switch (mode)
                {
                    case AssetPackAssignmentMode.Primary: linkedCombination = UniqueNPCData.GetUniqueNPCTrackerData(npcInfo, AssignmentType.Assets); break;
                    case AssetPackAssignmentMode.MixIn: break; ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////// FILL OUT LATER 
                    case AssetPackAssignmentMode.ReplacerVirtual:
                        List<UniqueNPCData.UniqueNPCTracker.LinkedAssetReplacerAssignment> linkedReplacerCombinations = UniqueNPCData.GetUniqueNPCTrackerData(npcInfo, AssignmentType.ReplacerAssets);
                        if (linkedReplacerCombinations != null)
                        {
                            var linkedAssignmentGroup = linkedReplacerCombinations.Where(x => x.ReplacerName == availableAssetPacks.First().GroupName).FirstOrDefault();
                            if (linkedAssignmentGroup != null) { linkedCombination = linkedAssignmentGroup.AssignedReplacerCombination; }
                        }
                        break; 
                }

                if (linkedCombination != null && AssetSelector.CombinationAllowedBySpecificNPCAssignment(npcInfo.SpecificNPCAssignment, linkedCombination, mode))
                {
                    chosenCombination = Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].AssignedCombination;
                    Logger.LogReport("Another unique NPC with the same name was assigned a combination. Using that combination for current NPC.", false, npcInfo);

                    if (!blockBodyShape && mode == AssetPackAssignmentMode.Primary)
                    {
                        switch (PatcherSettings.General.BodySelectionMode)
                        {
                            case BodyShapeSelectionMode.BodyGen:
                                assignment.AssignedBodyGenMorphs = UniqueNPCData.GetUniqueNPCTrackerData(npcInfo, AssignmentType.BodyGen);
                                bodyShapeAssigned = assignment.AssignedBodyGenMorphs.Any();
                                break;
                            case BodyShapeSelectionMode.BodySlide:
                                assignment.AssignedOBodyPreset = UniqueNPCData.GetUniqueNPCTrackerData(npcInfo, AssignmentType.BodySlide);
                                bodyShapeAssigned = assignment.AssignedOBodyPreset != null;
                                break;
                            default: break;
                        }

                        if (bodyShapeAssigned)
                        {
                            Logger.LogReport("Another unique NPC with the same name was assigned a body shape. Using that body shape for current NPC.", false, npcInfo);
                        }
                    }
                    selectedFromLinkedNPC = true;
                }
            }
            
            if (!selectedFromLinkedNPC)
            {
                Logger.LogReport("Choosing Asset Combination and BodyGen for " + npcInfo.LogIDstring, false, npcInfo);
                chosenCombination = GenerateCombinationWithBodyShape(availableAssetPacks, bodyGenConfigs, oBodySettings, assignment, npcInfo, blockBodyShape, mode, currentAssignments); // chosenMorphs is populated by reference within ChooseRandomCombination

                switch (PatcherSettings.General.BodySelectionMode)
                {
                    case BodyShapeSelectionMode.BodyGen: bodyShapeAssigned = assignment.AssignedBodyGenMorphs.Any(); break;
                    case BodyShapeSelectionMode.BodySlide: bodyShapeAssigned = assignment.AssignedOBodyPreset != null; break;
                    case BodyShapeSelectionMode.None: break;
                }
            }

            if (chosenCombination != null && chosenCombination.AssetPackName != "")
            {
                assetsAssigned = true;
                assignment.AssignedCombination = chosenCombination;
            }
            if (bodyShapeAssigned)
            {
                switch(PatcherSettings.General.BodySelectionMode)
                {
                    case BodyShapeSelectionMode.BodyGen: npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames = assignment.AssignedBodyGenMorphs.Select(x => x.Label).ToList(); break;
                    case BodyShapeSelectionMode.BodySlide: npcInfo.ConsistencyNPCAssignment.BodySlidePreset = assignment.AssignedOBodyPreset.Label; break;
                }
            }

            Logger.CloseReportSubsection(npcInfo);
            return assignment;
        }

        public static SubgroupCombination GenerateCombinationWithBodyShape(HashSet<FlattenedAssetPack> availableAssetPacks, BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings, AssetAndBodyShapeAssignment assignment, NPCInfo npcInfo, bool blockBodyShape, AssetPackAssignmentMode mode, AssetAndBodyShapeAssignment currentAssignments)
        {
            List<BodyGenConfig.BodyGenTemplate> candidateMorphs = new List<BodyGenConfig.BodyGenTemplate>();
            BodySlideSetting candidatePreset = null;
            bool notifyOfPermutationMorphConflict = false;

            SubgroupCombination assignedCombination = new SubgroupCombination();
            HashSet<FlattenedAssetPack> filteredAssetPacks = new HashSet<FlattenedAssetPack>();
            AssignmentIteration iterationInfo = new AssignmentIteration();
            bool combinationIsValid = false;

            bool isFirstIteration = true;
            SubgroupCombination firstCombination = null;
            Tuple<SubgroupCombination, object> firstValidCombinationShapePair = new Tuple<SubgroupCombination, object>(new SubgroupCombination(), new List<string>()); // object can be List<string> (BodyGen) or BodySlideSetting (OBody)
            bool firstValidCombinationShapePairInitialized = false;

            Logger.OpenReportSubsection("CombinationGeneration", npcInfo);
            Logger.LogReport("Assigning an asset combination", false, npcInfo);

            // remove subgroups or entire asset packs whose distribution rules are incompatible with the current NPC
            filteredAssetPacks = AssetSelector.FilterValidConfigsForNPC(availableAssetPacks, npcInfo, false, out bool wasFilteredByConsistency, mode, currentAssignments);

            // initialize seeds
            iterationInfo.AvailableSeeds = AssetSelector.GetAllSubgroups(filteredAssetPacks).OrderByDescending(x => x.ForceIfMatchCount).ToList();

            while (!combinationIsValid)
            {
                if (!iterationInfo.AvailableSeeds.Any())
                {
                    if (wasFilteredByConsistency) // if no valid groups when filtering for consistency, try again without filtering for it
                    {
                        Logger.LogReport("Attempting to select a valid non-consistency Combination.", true, npcInfo);
                        filteredAssetPacks = AssetSelector.FilterValidConfigsForNPC(availableAssetPacks, npcInfo, true, out wasFilteredByConsistency, mode, currentAssignments);
                        iterationInfo.AvailableSeeds = AssetSelector.GetAllSubgroups(filteredAssetPacks);
                    }
                    else // no other filters can be relaxed
                    {
                        Logger.LogReport("No more asset packs remain to select assets from. Terminating combination selection.", true, npcInfo);
                        break;
                    }
                }

                // get an asset combination
                assignedCombination = AssetSelector.GenerateCombination(filteredAssetPacks, npcInfo, iterationInfo);

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
                if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.None || blockBodyShape || (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodyGen && !BodyGenSelector.BodyGenAvailableForGender(npcInfo.Gender, bodyGenConfigs)) || (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodySlide && !OBodySelector.CurrentNPCHasAvailablePresets(npcInfo, oBodySettings)))
                {
                    combinationIsValid = true;
                    Logger.LogReport("Current combination is accepted without body shape selection.", false, npcInfo);
                }
                else
                {
                    bool bodyShapeAssigned = false;
                    var bodyShapeStatusFlags = new BodyShapeSelectorStatusFlag();
                    switch (PatcherSettings.General.BodySelectionMode)
                    {
                        case BodyShapeSelectionMode.BodyGen: candidateMorphs = BodyGenSelector.SelectMorphs(npcInfo, out bodyShapeAssigned, bodyGenConfigs, assignedCombination, out bodyShapeStatusFlags); break;
                        case BodyShapeSelectionMode.BodySlide: candidatePreset = OBodySelector.SelectBodySlidePreset(npcInfo, out bodyShapeAssigned, oBodySettings, assignedCombination, out bodyShapeStatusFlags); break;
                    }

                    // Decision Tree

                    bool npcHasBodyShapeConsistency = false;
                    switch (PatcherSettings.General.BodySelectionMode)
                    {
                        case BodyShapeSelectionMode.BodyGen: npcHasBodyShapeConsistency = npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames != null && npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames.Any(); break;
                        case BodyShapeSelectionMode.BodySlide: npcHasBodyShapeConsistency = npcInfo.ConsistencyNPCAssignment.BodySlidePreset != null && !string.IsNullOrWhiteSpace(npcInfo.ConsistencyNPCAssignment.BodySlidePreset); break;
                    }

                    // Branch 1: No body shape could be assigned in conjuction with the current combination
                    if (!bodyShapeAssigned)
                    {
                        // check if any body shape would be valid for the given NPC without any restrictions from the asset combination
                        Logger.LogReport("Checking if any body shapes would be valid without the restrictions imposed by the current combination.", false, npcInfo);
                        bool bodyShapeAssignable = false;
                        switch (PatcherSettings.General.BodySelectionMode)
                        {
                            case BodyShapeSelectionMode.BodyGen: candidateMorphs = BodyGenSelector.SelectMorphs(npcInfo, out bool bodyGenAssignable, bodyGenConfigs, null, out bodyShapeStatusFlags); break;
                            case BodyShapeSelectionMode.BodySlide: candidatePreset = OBodySelector.SelectBodySlidePreset(npcInfo, out bodyShapeAssigned, oBodySettings, null, out bodyShapeStatusFlags); break;
                        }

                        // if not, then the curent combination is fine because no other combination would be compatible with any BodyGen morphs anyway
                        if (!bodyShapeAssignable)
                        {
                            Logger.LogReport("No body shapes would be assignable even without the restrictions imposed by the current combination. Keeping the current combination and the current body shape.", false, npcInfo);
                            switch (PatcherSettings.General.BodySelectionMode)
                            {
                                case BodyShapeSelectionMode.BodyGen: assignment.AssignedBodyGenMorphs.AddRange(candidateMorphs); break;
                                case BodyShapeSelectionMode.BodySlide: assignment.AssignedOBodyPreset = candidatePreset; break;
                            }
                            combinationIsValid = true;
                        }
                        else // 
                        {
                            Logger.LogReport("At least one other body shape would be assignable without the restrictions imposed by the currently selected combination. Attempting to find another combination whose restrictions would be compatible with a body shape.", false, npcInfo);
                            notifyOfPermutationMorphConflict = true; // Try picking another combination. if no other SubgroupCombination is compatible with any morphs, the user will be warned that there is an asset/morph ruleset conflict
                        }
                    }

                    // Branch 2: A valid morph was selected based on any of the following criteria:
                    // A) there is no consistency morph for this NPC
                    // B) there is a consistency morph for this NPC and the chosen morph is the consistency morph
                    // C) there is a consistency morph for this NPC but the consistency morph was INVALID for the given NPC irrespective of the chosen permutation's allowed/disallowed BodyGen rules, 
                    else if (!npcHasBodyShapeConsistency || bodyShapeStatusFlags.HasFlag(BodyShapeSelectorStatusFlag.MatchesConsistency) || bodyShapeStatusFlags.HasFlag(BodyShapeSelectorStatusFlag.ConsistencyMorphIsInvalid))
                    {
                        Logger.LogReport("Current combination is accepted along with the current body shape selection.", false, npcInfo);
                        switch (PatcherSettings.General.BodySelectionMode)
                        {
                            case BodyShapeSelectionMode.BodyGen: assignment.AssignedBodyGenMorphs.AddRange(candidateMorphs); break;
                            case BodyShapeSelectionMode.BodySlide: assignment.AssignedOBodyPreset = candidatePreset; break;
                        }
                        combinationIsValid = true;
                    }

                    //Branch 3: A consistency morph exists, but the chosen combination is only compatible with a morph that is NOT the consistency morph
                    else if (npcHasBodyShapeConsistency && !bodyShapeStatusFlags.HasFlag(BodyShapeSelectorStatusFlag.MatchesConsistency))
                    {
                        Logger.LogReport("Current combination is valid along with the current body shape selection, but only if the current body shape is not the consistency body shape. Attempting to find a different combination whose restrictions permit the consistency body shape.", false, npcInfo);

                        if (!firstValidCombinationShapePairInitialized)
                        {
                            switch (PatcherSettings.General.BodySelectionMode)
                            {
                                case BodyShapeSelectionMode.BodyGen: firstValidCombinationShapePair = new Tuple<SubgroupCombination, object>(assignedCombination, candidateMorphs); break;
                                case BodyShapeSelectionMode.BodySlide: firstValidCombinationShapePair = new Tuple<SubgroupCombination, object>(assignedCombination, candidatePreset); break;
                            }
                        }
                        firstValidCombinationShapePairInitialized = true;
                    }
                }
            }

            if (!combinationIsValid)
            {
                if (firstValidCombinationShapePairInitialized)
                {
                    Logger.LogMessage("Could not assign an asset combination to " + npcInfo.LogIDstring + " that is compatible with its consistency Body Shape. A valid combination was assigned, but Body Shape assignment will be re-randomized.");
                    Logger.LogReport("Could not assign an asset combination to " + npcInfo.LogIDstring + " that is compatible with its consistency Body Shape (" + String.Join(", ", npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames) + "). A valid combination was assigned, but Body Shape assignment will be re-randomized.", true, npcInfo);

                    assignedCombination = firstValidCombinationShapePair.Item1;
                    switch (PatcherSettings.General.BodySelectionMode)
                    {
                        case BodyShapeSelectionMode.BodyGen: assignment.AssignedBodyGenMorphs.AddRange((List<BodyGenConfig.BodyGenTemplate>)firstValidCombinationShapePair.Item2); break;
                        case BodyShapeSelectionMode.BodySlide: assignment.AssignedOBodyPreset = (BodySlideSetting)firstValidCombinationShapePair.Item2; break;
                    }
                }
                else if (notifyOfPermutationMorphConflict)
                {
                    Logger.LogMessage("Could not assign an asset combination to " + npcInfo.LogIDstring + " that was compatible with any valid Body Shapes. Assigning a valid asset combination. A Body Shape will be chosen without regard to this combination's constraints.");
                    Logger.LogReport("Could not assign an asset combination to " + npcInfo.LogIDstring + " that was compatible with any valid Body Shape. Assigning a valid asset combination. A Body Shape will be chosen without regard to this combination's constraints.", true, npcInfo);
                    assignedCombination = firstCombination;
                }
            }

            Logger.CloseReportSubsection(npcInfo);
            return assignedCombination;
        }

        [Flags]
        public enum BodyShapeSelectorStatusFlag
        {
            NoneValidForNPC = 1, // no morphs could be assigned irrespective of the rule set received from the assigned assetCombination
            MatchesConsistency = 2, // all selected morphs are present in consistency
            ConsistencyMorphIsInvalid = 4 // the consistency morph is no longer valid because its rule set no longer permits this NPC
        }

        public static void ClearStatusFlags(BodyShapeSelectorStatusFlag flags)
        {
            flags = ~BodyShapeSelectorStatusFlag.NoneValidForNPC;
            flags = ~BodyShapeSelectorStatusFlag.MatchesConsistency;
            flags = ~BodyShapeSelectorStatusFlag.ConsistencyMorphIsInvalid;
        }
    }
}
