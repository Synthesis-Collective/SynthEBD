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
        public static HeadPartSelection AssignHeadParts(NPCInfo npcInfo, Settings_Headparts settings, BlockedNPC blockedNPCentry, BlockedPlugin blockedPluginEntry)
        {
            Logger.OpenReportSubsection("HeadParts", npcInfo);
            Logger.LogReport("Selecting Head Parts for Current NPC", false, npcInfo);
            HeadPartSelection selectedHeadParts = new();

            foreach (var headPartType in settings.Types.Keys)
            {
                if (!blockedNPCentry.HeadPartTypes[headPartType])
                {
                    Logger.LogReport(headPartType + " assignment is blocked for current NPC.", false, npcInfo);
                    continue;
                }
                if (!blockedPluginEntry.HeadPartTypes[headPartType])
                {
                    Logger.LogReport(headPartType + " assignment is blocked for current NPC's plugin.", false, npcInfo);
                    continue;
                }
                AssignHeadPartType(settings.Types[headPartType], headPartType, npcInfo);
            }

            Logger.CloseReportSubsectionsToParentOf("HeadParts", npcInfo);
            return selectedHeadParts;
        }

        public static FormKey AssignHeadPartType(Settings_HeadPartType currentSettings, HeadPart.TypeEnum type, NPCInfo npcInfo)
        {
            if (!CanGetThisHeadPartType(currentSettings, type, npcInfo))
            {
                return new FormKey();
            }


            return new FormKey(); // temp
        }


        public static bool CanGetThisHeadPartType(Settings_HeadPartType currentSettings, HeadPart.TypeEnum type, NPCInfo npcInfo)
        {
            /*
            if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.BodyGenHead PartNames.Contains(candidateHeadPart.EditorID))
            {
                Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is valid because it is specifically assigned by user.", false, npcInfo);
                return true;
            }
            */

            if (!currentSettings.bAllowRandom && currentSettings.MatchedForceIfCount == 0) // don't need to check for specific assignment because it was evaluated just above
            {
                Logger.LogReport(type + " is invalid because it can only be assigned via ForceIf attributes or Specific NPC Assignments", false, npcInfo);
                return false;
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
            if (currentSettings.AllowedRaces.Any() && !currentSettings.AllowedRaces.Contains(npcInfo.BodyShapeRace))
            {
                Logger.LogReport(type + " is invalid because its allowed races do not include the current NPC's race", false, npcInfo);
                return false;
            }

            // Disallowed Races
            if (currentSettings.DisallowedRaces.Contains(npcInfo.BodyShapeRace))
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

            /*
            if (assignedAssetCombination != null)
            {
                foreach (var subgroup in assignedAssetCombination.ContainedSubgroups)
                {
                    if (subgroup.AllowedBodyGenDescriptors.Any())
                    {
                        if (!BodyShapeDescriptor.DescriptorsMatch(subgroup.AllowedBodyGenDescriptors, candidateHeadPart.BodyShapeDescriptors, out _))
                        {
                            Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because its descriptors do not match allowed descriptors from assigned subgroup " + Logger.GetSubgroupIDString(subgroup) + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(subgroup.AllowedBodyGenDescriptors), false, npcInfo);
                            return false;
                        }
                    }

                    if (BodyShapeDescriptor.DescriptorsMatch(subgroup.DisallowedBodyGenDescriptors, candidateHeadPart.BodyShapeDescriptors, out string matchedDescriptor))
                    {
                        Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because its descriptor [" + matchedDescriptor + "] is disallowed by assigned subgroup " + Logger.GetSubgroupIDString(subgroup), false, npcInfo);
                        return false;
                    }
                }
            }
            */

            // If the head part is still valid
            return true;
        }

        public static bool HeadPartIsValid(HeadPartSetting candidateHeadPart, NPCInfo npcInfo)
        {
            /*
            if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.BodyGenHead PartNames.Contains(candidateHeadPart.EditorID))
            {
                Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is valid because it is specifically assigned by user.", false, npcInfo);
                return true;
            }
            */

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
            if (candidateHeadPart.AllowedRaces.Any() && !candidateHeadPart.AllowedRaces.Contains(npcInfo.BodyShapeRace))
            {
                Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because its allowed races do not include the current NPC's race", false, npcInfo);
                return false;
            }

            // Disallowed Races
            if (candidateHeadPart.DisallowedRaces.Contains(npcInfo.BodyShapeRace))
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

            /*
            if (assignedAssetCombination != null)
            {
                foreach (var subgroup in assignedAssetCombination.ContainedSubgroups)
                {
                    if (subgroup.AllowedBodyGenDescriptors.Any())
                    {
                        if (!BodyShapeDescriptor.DescriptorsMatch(subgroup.AllowedBodyGenDescriptors, candidateHeadPart.BodyShapeDescriptors, out _))
                        {
                            Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because its descriptors do not match allowed descriptors from assigned subgroup " + Logger.GetSubgroupIDString(subgroup) + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(subgroup.AllowedBodyGenDescriptors), false, npcInfo);
                            return false;
                        }
                    }

                    if (BodyShapeDescriptor.DescriptorsMatch(subgroup.DisallowedBodyGenDescriptors, candidateHeadPart.BodyShapeDescriptors, out string matchedDescriptor))
                    {
                        Logger.LogReport("Head Part " + candidateHeadPart.EditorID + " is invalid because its descriptor [" + matchedDescriptor + "] is disallowed by assigned subgroup " + Logger.GetSubgroupIDString(subgroup), false, npcInfo);
                        return false;
                    }
                }
            }
            */

            // If the head part is still valid
            return true;
        }
    }

    public class HeadPartSelection
    {
        public FormKey Misc { get; set; }
        public FormKey Face { get; set; }
        public FormKey Eyes { get; set; }
        public FormKey Beard { get; set; }
        public FormKey Scars { get; set; }
        public FormKey Brows { get; set; }
        public FormKey Hair { get; set; }
    }
}
