using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VerboseLoggingNPCSelector
    {
        private readonly PatcherState _patcherState;
        private readonly AttributeMatcher _attributeMatcher;
        public VerboseLoggingNPCSelector(PatcherState patcherState, AttributeMatcher attributeMatcher)
        {
            _patcherState = patcherState;
            _attributeMatcher = attributeMatcher;
        }

        public bool VerboseLoggingForCurrentNPC(NPCInfo npcInfo)
        {
            if (!_patcherState.GeneralSettings.bUseDetailedReportSelection)
            {
                return false;
            }

            var loggingRules = _patcherState.GeneralSettings.DetailedReportSelector;

            // Allow unique NPCs
            if (!loggingRules.AllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                return false;
            }

            // Allow non-unique NPCs
            if (!loggingRules.AllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
            {
                return false;
            }

            // Allowed Races
            if (loggingRules.AllowedRaces.Any() && !loggingRules.AllowedRaces.Contains(npcInfo.NPC.Race.FormKey))
            {
                return false;
            }

            // Disallowed Races
            if (loggingRules.DisallowedRaces.Contains(npcInfo.NPC.Race.FormKey))
            {
                return false;
            }

            // Weight Range
            if (npcInfo.NPC.Weight < loggingRules.WeightRange.Lower || npcInfo.NPC.Weight > loggingRules.WeightRange.Upper)
            {
                return false;
            }

            // Allowed Attributes
            _attributeMatcher.MatchNPCtoAttributeList(loggingRules.AllowedAttributes, npcInfo.NPC, _patcherState.GeneralSettings.AttributeGroups, false, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int _, out string _, out string _, out string _, null);
            if (hasAttributeRestrictions && !matchesAttributeRestrictions)
            {
                return false;
            }

            // Disallowed Attributes
            _attributeMatcher.MatchNPCtoAttributeList(loggingRules.DisallowedAttributes, npcInfo.NPC, _patcherState.OBodySettings.AttributeGroups, false, out hasAttributeRestrictions, out matchesAttributeRestrictions, out int _, out string _, out string _, out string _, null);
            if (hasAttributeRestrictions && matchesAttributeRestrictions)
            {
                return false;
            }
            return true;
        }
    }
}
