using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class UniqueNPCData
    {
        public static HashSet<string> UniqueNameExclusions { get; set; }
        public class UniqueNPCTracker
        {
            public UniqueNPCTracker()
            {
                AssignedCombination = null;
                AssignedMorphs = new List<string>();
                AssignedHeight = -1;
            }
            public SubgroupCombination AssignedCombination;
            public List<string> AssignedMorphs;
            public float AssignedHeight;
        }

        /// <summary>
        /// Determines if a given NPC should be treated as a linked unique NPC
        /// </summary>
        /// <param name="npc"></param>
        /// <param name="npcName"></param>
        /// <returns></returns>
        public static bool IsValidUnique(INpcGetter npc, out string npcName)
        {
            if (npc.Name == null)
            {
                npcName = "";
                return false;
            }
            else
            {
                npcName = npc.Name.ToString();
            }

            if (UniqueNameExclusions.Contains(npcName, StringComparer.CurrentCultureIgnoreCase))
            {
                return false;
            }

            if (npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
