using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class LinkedNPCGroupInfo
    {
        public LinkedNPCGroupInfo(LinkedNPCGroup sourceGroup)
        {
            this.NPCFormKeys = sourceGroup.NPCFormKeys;
            this.AssignedCombination = null;
            this.AssignedMorphs = null;
            this.AssignedHeight = -1;
        }

        public HashSet<FormKey> NPCFormKeys { get; set; }
        public SubgroupCombination AssignedCombination { get; set; }
        public List<string> AssignedMorphs { get; set; }
        public double AssignedHeight { get; set; }

        public static LinkedNPCGroupInfo GetInfoFromLinkedNPCGroup(HashSet<LinkedNPCGroup> definedGroups, HashSet<LinkedNPCGroupInfo> createdGroups, FormKey npcFormKey) // links the UI-defined LinkedNPCGroup (which only contains NPCs) to the corresponding generated LinkedNPCGroupInfo (which contains patcher assignments)
        {
            foreach (var group in definedGroups)
            {
                if (group.NPCFormKeys.Contains(npcFormKey))
                {
                    var associatedGroup = createdGroups.Where(x => x.NPCFormKeys.Contains(npcFormKey)).FirstOrDefault();
                    if (associatedGroup == null)
                    {
                        return new LinkedNPCGroupInfo(group);
                    }
                    else
                    {
                        return associatedGroup;
                    }
                }
            }
            return null;
        }
    }
}
