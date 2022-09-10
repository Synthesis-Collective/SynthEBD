namespace SynthEBD;

public class OBodySelector
{
    public static BodySlideSetting SelectBodySlidePreset(NPCInfo npcInfo, out bool selectionMade, Settings_OBody oBodySettings, SubgroupCombination assignedAssetCombination, out AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag statusFlags)
    {
        selectionMade = false;

        Logger.OpenReportSubsection("OBodySelection", npcInfo);
        Logger.LogReport("Selecting a BodySlide preset for the current NPC", false, npcInfo);
        List<BodySlideSetting> availablePresets = null;
        statusFlags = new AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag();
        switch (npcInfo.Gender)
        {
            case Gender.Male: availablePresets = new List<BodySlideSetting>(oBodySettings.BodySlidesMale); break; // shallow copy to allow pruning
            case Gender.Female: availablePresets = new List<BodySlideSetting>(oBodySettings.BodySlidesFemale); break;
        }

        if (!availablePresets.Any())
        {
            selectionMade = false;
            Logger.LogReport("No BodySlide presets are available for NPCs of the current gender.", false, npcInfo);
            Logger.CloseReportSubsection(npcInfo);
            return null;
        }

        AssetAndBodyShapeSelector.ClearStatusFlags(statusFlags);

        BodySlideSetting selectedPreset = null;

        #region Specific NPC Assignments
        if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.BodySlidePreset != "")
        {
            selectedPreset = availablePresets.Where(x => x.Label == npcInfo.SpecificNPCAssignment.BodySlidePreset).FirstOrDefault();
            if (selectedPreset != null)
            {
                Logger.LogReport("Assigned forced BodySlide preset " + selectedPreset.Label, false, npcInfo);
            }
            else
            {
                Logger.LogReport("Could not find the forced BodySlide preset \"" + npcInfo.SpecificNPCAssignment.BodySlidePreset + "\" within the available presets. Attempting to assign another preset.", true, npcInfo);
            }
        }
        #endregion

        #region Linked NPC Group
        if (selectedPreset == null && npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary) // check for selectedPreset == null to avoid overwriting Specific Assignment
        {
            selectedPreset = npcInfo.AssociatedLinkGroup.AssignedBodySlide;
            if (selectedPreset != null)
            {
                Logger.LogReport("Assigned linked BodySlide preset " + selectedPreset.Label + " from primary NPC " + npcInfo.AssociatedLinkGroup.PrimaryNPCFormKey.ToString(), false, npcInfo);
            }
            else
            {
                Logger.LogReport("Could not find the linked BodySlide preset \"" + npcInfo.AssociatedLinkGroup.AssignedBodySlide + "\" from primary NPC " + npcInfo.AssociatedLinkGroup.PrimaryNPCFormKey.ToString() + " within the available presets. Attempting to assign another preset.", true, npcInfo);
            }
        }
        #endregion

        #region Unique NPC replicates
        else if (selectedPreset == null && UniqueNPCData.IsValidUnique(npcInfo.NPC, out var npcName)) // check for selectedPreset == null to avoid overwriting Specific Assignment
        {
            selectedPreset = UniqueNPCData.GetUniqueNPCTrackerData(npcInfo, AssignmentType.BodySlide);
            if (selectedPreset != null)
            {
                Logger.LogReport("Assigned BodySlide preset " + selectedPreset.Label + " from unique NPC with same name: " + npcName, false, npcInfo);
            }
        }
        #endregion

        #region Random Selection

        if (selectedPreset == null)
        {
            var filteredPresets = new List<BodySlideSetting>(); // fall back if ForceIfs fail
            var forceIfPresets = new List<BodySlideSetting>();

            foreach (var preset in availablePresets)
            {
                if (PresetIsValid(preset, npcInfo, assignedAssetCombination, oBodySettings))
                {
                    filteredPresets.Add(preset);
                    if (preset.MatchedForceIfCount > 0)
                    {
                        forceIfPresets.Add(preset);
                    }
                }
            }

            if (forceIfPresets.Any())
            {
                #region Consistency (With ForceIf)
                if (PatcherSettings.General.bEnableConsistency && npcInfo.ConsistencyNPCAssignment != null && npcInfo.ConsistencyNPCAssignment.BodySlidePreset != "" && forceIfPresets.Select(x => x.Label).Contains(npcInfo.ConsistencyNPCAssignment.BodySlidePreset))
                {
                    selectedPreset = forceIfPresets.Where(x => x.Label == npcInfo.ConsistencyNPCAssignment.BodySlidePreset).FirstOrDefault();
                    if (selectedPreset is not null)
                    {
                        Logger.LogReport("Found consistency BodySlide: " + selectedPreset.Label, false, npcInfo);
                    }
                    else
                    {
                        Logger.LogReport("Consistency BodySlide " + npcInfo.ConsistencyNPCAssignment.BodySlidePreset + " is no longer available.", false, npcInfo);
                    }
                }
                #endregion
                else
                {
                    selectedPreset = (BodySlideSetting)ProbabilityWeighting.SelectByProbability(forceIfPresets);
                }
            }
            else
            {
                #region Consistency (Without ForceIf)
                if (PatcherSettings.General.bEnableConsistency && npcInfo.ConsistencyNPCAssignment != null && npcInfo.ConsistencyNPCAssignment.BodySlidePreset != "" && filteredPresets.Select(x => x.Label).Contains(npcInfo.ConsistencyNPCAssignment.BodySlidePreset))
                {
                    selectedPreset = filteredPresets.Where(x => x.Label == npcInfo.ConsistencyNPCAssignment.BodySlidePreset).FirstOrDefault();
                    if (selectedPreset is not null)
                    {
                        Logger.LogReport("Found consistency BodySlide: " + selectedPreset.Label, false, npcInfo);
                    }
                    else
                    {
                        Logger.LogReport("Consistency BodySlide " + npcInfo.ConsistencyNPCAssignment.BodySlidePreset + " is no longer available.", false, npcInfo);
                    }
                }
                #endregion
                else
                {
                    selectedPreset = (BodySlideSetting)ProbabilityWeighting.SelectByProbability(filteredPresets);
                }
            }
        }
        #endregion

        if (selectedPreset == null)
        {
            Logger.LogReport("Could not choose any valid BodySlide presets for NPC " + npcInfo.LogIDstring, true, npcInfo);
            Logger.CloseReportSubsection(npcInfo);
            selectionMade = false;
            return null;
        }
        else
        {
            Logger.LogReport("Chose BodySlide Preset: " + selectedPreset.Label, false, npcInfo);
            selectionMade = true;

            if (PatcherSettings.General.bEnableConsistency && selectedPreset.Label != npcInfo.ConsistencyNPCAssignment.BodySlidePreset && availablePresets.Select(x => x.Label).Contains(npcInfo.ConsistencyNPCAssignment.BodySlidePreset))
            {
                Logger.LogReport("The consisteny BodySlide preset " + npcInfo.ConsistencyNPCAssignment.BodySlidePreset + " could not be chosen because it no longer complied with the current distribution rules so a new BodySlide was selected.", true, npcInfo);
            }
        }

        //store selected morphs
        if (npcInfo.ConsistencyNPCAssignment != null && npcInfo.ConsistencyNPCAssignment.BodySlidePreset != null && npcInfo.ConsistencyNPCAssignment.BodySlidePreset != "" && npcInfo.ConsistencyNPCAssignment.BodySlidePreset == selectedPreset.Label)
        {
            statusFlags |= AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag.MatchesConsistency;
        }

        Logger.CloseReportSubsection(npcInfo);

        return selectedPreset;
    }

    public static bool PresetIsValid(BodySlideSetting candidatePreset, NPCInfo npcInfo, SubgroupCombination assignedAssetCombination, Settings_OBody oBodySettings)
    {
        if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.BodyGenMorphNames.Contains(candidatePreset.Label))
        {
            Logger.LogReport("Preset " + candidatePreset.Label + " is valid because it is specifically assigned by user.", false, npcInfo);
            return true;
        }

        // Allow unique NPCs
        if (!candidatePreset.AllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because it is disallowed for unique NPCs", false, npcInfo);
            return false;
        }

        // Allow non-unique NPCs
        if (!candidatePreset.AllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because it is disallowed for non-unique NPCs", false, npcInfo);
            return false;
        }

        // Allowed Races
        if (candidatePreset.AllowedRaces.Any() && !candidatePreset.AllowedRaces.Contains(npcInfo.BodyShapeRace))
        {
            Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because its allowed races do not include the current NPC's race", false, npcInfo);
            return false;
        }

        // Disallowed Races
        if (candidatePreset.DisallowedRaces.Contains(npcInfo.BodyShapeRace))
        {
            Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because its disallowed races include the current NPC's race", false, npcInfo);
            return false;
        }

        // Weight Range
        if (npcInfo.NPC.Weight < candidatePreset.WeightRange.Lower || npcInfo.NPC.Weight > candidatePreset.WeightRange.Upper)
        {
            Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because the current NPC's weight falls outside of the it's allowed weight range", false, npcInfo);
            return false;
        }

        // Allowed and Forced Attributes
        candidatePreset.MatchedForceIfCount = 0;
        AttributeMatcher.MatchNPCtoAttributeList(candidatePreset.AllowedAttributes, npcInfo.NPC, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfWeightedCount, out string _, out string unmatchedLog, out string forceIfLog);
        if (hasAttributeRestrictions && !matchesAttributeRestrictions)
        {
            Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because the NPC does not match any of its allowed attributes: " + unmatchedLog, false, npcInfo);
            return false;
        }
        else
        {
            candidatePreset.MatchedForceIfCount = matchedForceIfWeightedCount;
        }

        if (candidatePreset.MatchedForceIfCount > 0)
        {
            Logger.LogReport("Preset " + candidatePreset.Label + " Current NPC matches the following forced attributes: " + forceIfLog, false, npcInfo);
        }

        // Disallowed Attributes
        AttributeMatcher.MatchNPCtoAttributeList(candidatePreset.DisallowedAttributes, npcInfo.NPC, out hasAttributeRestrictions, out matchesAttributeRestrictions, out int dummy, out string matchLog, out string _, out string _);
        if (hasAttributeRestrictions && matchesAttributeRestrictions)
        {
            Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because the NPC matches one of its disallowed attributes: " + matchLog, false, npcInfo);
            return false;
        }

        // Repeat the above checks for the preset's descriptor rules
        foreach (var descriptorLabel in candidatePreset.BodyShapeDescriptors)
        {
            var associatedDescriptor = oBodySettings.TemplateDescriptors.Where(x => x.Signature.Equals(descriptorLabel)).FirstOrDefault();
            if (associatedDescriptor is not null)
            {
                if (associatedDescriptor.PermitNPC(npcInfo, out string reportStr))
                {
                    if (associatedDescriptor.AssociatedRules.MatchedForceIfCount > 0)
                    {
                        candidatePreset.MatchedForceIfCount += associatedDescriptor.AssociatedRules.MatchedForceIfCount;
                        Logger.LogReport(reportStr, false, npcInfo);
                    }
                }
                else
                {
                    Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because the rules for its descriptor " + reportStr, false, npcInfo);
                }
            }
        }

        if (assignedAssetCombination != null)
        {
            // check whole config rules
            if (assignedAssetCombination.AssetPack.DistributionRules.AllowedBodyGenDescriptors.Any())
            {
                if (!BodyShapeDescriptor.DescriptorsMatch(assignedAssetCombination.AssetPack.DistributionRules.AllowedBodySlideDescriptors, candidatePreset.BodyShapeDescriptors, out _))
                {
                    Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because its descriptors do not match allowed descriptors from assigned Asset Pack " + assignedAssetCombination.AssetPackName + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(assignedAssetCombination.AssetPack.DistributionRules.AllowedBodySlideDescriptors), false, npcInfo);
                    return false;
                }
            }

            if (BodyShapeDescriptor.DescriptorsMatch(assignedAssetCombination.AssetPack.DistributionRules.DisallowedBodySlideDescriptors, candidatePreset.BodyShapeDescriptors, out string matchedDescriptor))
            {
                Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because its descriptor [" + matchedDescriptor + "] is disallowed by assigned Asset Pack " + assignedAssetCombination.AssetPackName, false, npcInfo);
                return false;
            }

            // check subgroups
            foreach (var subgroup in assignedAssetCombination.ContainedSubgroups)
            {
                if (subgroup.AllowedBodySlideDescriptors.Any())
                {
                    if (!BodyShapeDescriptor.DescriptorsMatch(subgroup.AllowedBodySlideDescriptors, candidatePreset.BodyShapeDescriptors, out _))
                    {
                        Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because its descriptors do not match allowed descriptors from assigned subgroup " + Logger.GetSubgroupIDString(subgroup) + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(subgroup.AllowedBodySlideDescriptors), false, npcInfo);
                        return false;
                    }
                }

                if (BodyShapeDescriptor.DescriptorsMatch(subgroup.DisallowedBodySlideDescriptors, candidatePreset.BodyShapeDescriptors, out matchedDescriptor))
                {
                    Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because its descriptor [" + matchedDescriptor + "] is disallowed by assigned subgroup " + Logger.GetSubgroupIDString(subgroup), false, npcInfo);
                    return false;
                }
            }
        }

        // if the current Preset's forceIf attributes match the current NPC, skip the checks for Distribution Enabled

        if (!candidatePreset.AllowRandom && candidatePreset.MatchedForceIfCount == 0) // don't need to check for specific assignment because it was evaluated just above
        {
            Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because it can only be assigned via ForceIf attributes or Specific NPC Assignments", false, npcInfo);
            return false;
        }

        // If the candidateMorph is still valid
        return true;
    }

    public static bool CurrentNPCHasAvailablePresets(NPCInfo npcInfo, Settings_OBody oBodySettings)
    {
        List<BodySlideSetting> currentBodySlides = new List<BodySlideSetting>();
        switch (npcInfo.Gender)
        {
            case Gender.Male: currentBodySlides = oBodySettings.BodySlidesMale; break;
            case Gender.Female: currentBodySlides = oBodySettings.BodySlidesFemale; break;
        }

        if (!currentBodySlides.Any())
        {
            return false;
        }

        else
        {
            foreach (var slide in currentBodySlides)
            {
                if ((!slide.AllowedRaces.Any() || slide.AllowedRaces.Contains(npcInfo.BodyShapeRace)) && !slide.DisallowedRaces.Contains(npcInfo.BodyShapeRace))
                {
                    return true;
                }
            }
        }

        Logger.LogReport("No BodySlide presets are available for this NPC.", false, npcInfo);

        return false;
    }
    public static void RecordBodySlideConsistencyAndLinkedNPCs(BodySlideSetting assignedBodySlide, NPCInfo npcInfo)
    {
        npcInfo.ConsistencyNPCAssignment.BodySlidePreset = assignedBodySlide.Label;

        // assign to linked group if necessary 
        if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Primary)
        {
            npcInfo.AssociatedLinkGroup.AssignedBodySlide = assignedBodySlide;
        }
        // assign to unique NPC list if necessary
        if (PatcherSettings.General.bLinkNPCsWithSameName && npcInfo.IsValidLinkedUnique && Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].AssignedBodySlidePreset == null)
        {
            Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].AssignedBodySlidePreset = assignedBodySlide;
        }
    }
}