using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
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

            #region Consistency
            if (selectedPreset == null && npcInfo.ConsistencyNPCAssignment != null && npcInfo.ConsistencyNPCAssignment.BodySlidePreset != "")
            {
                selectedPreset = availablePresets.Where(x => x.Label == npcInfo.ConsistencyNPCAssignment.BodySlidePreset).FirstOrDefault();
                if (selectedPreset != null)
                {
                    Logger.LogReport("Assigned BodySlide preset " + selectedPreset.Label + " from consistency ", false, npcInfo);
                }
                else
                {
                    Logger.LogReport("Could not find the consistency BodySlide preset \"" + npcInfo.ConsistencyNPCAssignment.BodySlidePreset + "\" within the available presets. Attempting to assign another preset.", true, npcInfo);
                }
            }
            #endregion

            #region Random Selection
            if (selectedPreset == null)
            {
                while (availablePresets.Any())
                {
                    var candidatePreset = (BodySlideSetting)ProbabilityWeighting.SelectByProbability(availablePresets);
                    Logger.LogReport("Drew random BodySlide preset " + candidatePreset.Label, false, npcInfo);
                    if (PresetIsValid(candidatePreset, npcInfo, assignedAssetCombination))
                    {
                        selectedPreset = candidatePreset;
                        Logger.LogReport("Selected preset is valid. Choosing this BodySlide preset.", false, npcInfo);
                        break;
                    }
                    else
                    {
                        availablePresets.Remove(candidatePreset);
                    }
                }
            }
            #endregion

            if (selectedPreset == null)
            {
                Logger.LogReport("Could not choose any valid BodySlide presets for NPC " + npcInfo.LogIDstring, false, npcInfo);
                Logger.CloseReportSubsection(npcInfo);
                selectionMade = false;
                return null;
            }
            else
            {
                Logger.LogReport("Chose BodySlide Preset: " + selectedPreset.Label, false, npcInfo);
                selectionMade = true;
            }

            //store selected morphs
            if (npcInfo.ConsistencyNPCAssignment != null && npcInfo.ConsistencyNPCAssignment.BodySlidePreset != null && npcInfo.ConsistencyNPCAssignment.BodySlidePreset != "" && npcInfo.ConsistencyNPCAssignment.BodySlidePreset == selectedPreset.Label)
            {
                statusFlags |= AssetAndBodyShapeSelector.BodyShapeSelectorStatusFlag.MatchesConsistency;
            }

            Logger.CloseReportSubsection(npcInfo);

            return selectedPreset;
        }

        public static bool PresetIsValid(BodySlideSetting candidatePreset, NPCInfo npcInfo, SubgroupCombination assignedAssetCombination)
        {
            if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.BodyGenMorphNames.Contains(candidatePreset.Label))
            {
                Logger.LogReport("Preset " + candidatePreset.Label + " is valid because it is specifically assigned by user.", false, npcInfo);
                return true;
            }

            if (!candidatePreset.AllowRandom && candidatePreset.MatchedForceIfCount == 0) // don't need to check for specific assignment because it was evaluated just above
            {
                Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because it can only be assigned via ForceIf attribtues or Specific NPC Assignments", false, npcInfo);
                return false;
            }

            // Allow unique NPCs
            if (!candidatePreset.AllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because the current morph is disallowed for unique NPCs", false, npcInfo);
                return false;
            }

            // Allow non-unique NPCs
            if (!candidatePreset.AllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because the current morph is disallowed for non-unique NPCs", false, npcInfo);
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
                Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because the current NPC's weight falls outside of the morph's allowed weight range", false, npcInfo);
                return false;
            }

            // Allowed Attributes
            if (candidatePreset.AllowedAttributes.Any() && !AttributeMatcher.HasMatchedAttributes(candidatePreset.AllowedAttributes, npcInfo.NPC))
            {
                Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because the NPC does not match any of the morph's allowed attributes", false, npcInfo);
                return false;
            }

            // Disallowed Attributes
            if (AttributeMatcher.HasMatchedAttributes(candidatePreset.DisallowedAttributes, npcInfo.NPC))
            {
                Logger.LogReport("Preset " + candidatePreset.Label + " is invalid because the NPC matches one of the morph's disallowed attributes", false, npcInfo);
                return false;
            }

            if (assignedAssetCombination != null)
            {
                foreach (var subgroup in assignedAssetCombination.ContainedSubgroups)
                {
                    if (subgroup.AllowedBodyGenDescriptors.Any())
                    {
                        if (!BodyShapeDescriptor.DescriptorsMatch(subgroup.AllowedBodySlideDescriptors, candidatePreset.BodyShapeDescriptors))
                        {
                            Logger.LogReport("Morph is invalid because its descriptors does not match any of the assigned asset combination's allowed descriptors", false, npcInfo);
                            return false;
                        }
                    }

                    if (BodyShapeDescriptor.DescriptorsMatch(subgroup.DisallowedBodySlideDescriptors, candidatePreset.BodyShapeDescriptors))
                    {
                        Logger.LogReport("Morph is invalid because one of its descriptors matches one of the assigned asset combination's disallowed descriptors", false, npcInfo);
                        return false;
                    }
                }
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

    }
}
