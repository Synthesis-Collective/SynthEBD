using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class BodyGenSelector
    {
        public static HashSet<string> SelectMorphs(NPCInfo npcInfo, out bool success, SubgroupCombination assetCombination, BodyGenSelectorStatusFlag statusFlags)
        {
            //TEMPORARY
            success = true;
            //

            ClearStatusFlags(statusFlags);
            HashSet<string> chosenMorphs = new HashSet<string>();

            if (npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames != null && !npcInfo.ConsistencyNPCAssignment.BodyGenMorphNames.Except(chosenMorphs).Any()) // https://stackoverflow.com/questions/407729/determine-if-a-sequence-contains-all-elements-of-another-sequence-using-linq
            {
                statusFlags |= BodyGenSelectorStatusFlag.MatchesConsistency;
            }

            return chosenMorphs;
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
    }
}
