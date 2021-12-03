using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class NPCInfo
    {
        public NPCInfo(INpcGetter npc, HashSet<LinkedNPCGroup> definedLinkGroups, HashSet<LinkedNPCGroupInfo> createdLinkGroupInfos, HashSet<NPCAssignment> specificNPCAssignments, Dictionary<string, NPCAssignment> consistency)
        {
            this.NPC = npc;
            this.LogIDstring = Logger.GetNPCLogNameString(npc);
            this.Gender = GetGender(npc);
            AssetsRace = AliasHandler.GetAliasTexMesh(npc.Race.FormKey);
            BodyGenRace = AliasHandler.GetAliasBodyGen(npc.Race.FormKey);
            HeightRace = AliasHandler.GetAliasHeight(npc.Race.FormKey);
            AssociatedLinkGroup = LinkedNPCGroupInfo.GetInfoFromLinkedNPCGroup(definedLinkGroups, createdLinkGroupInfos, npc.FormKey);
            SpecificNPCAssignment = specificNPCAssignments.Where(x => x.NPCFormKey == npc.FormKey).FirstOrDefault();
            if (consistency.ContainsKey(this.NPC.FormKey.ToString()))
            {
                ConsistencyNPCAssignment = consistency[this.NPC.FormKey.ToString()];
            }
            else
            {
                ConsistencyNPCAssignment = new NPCAssignment();
                ConsistencyNPCAssignment.NPCFormKey = NPC.FormKey;
                ConsistencyNPCAssignment.DispName = LogIDstring;
                consistency.Add(this.NPC.FormKey.ToString(), ConsistencyNPCAssignment);
            }
        }

        public INpcGetter NPC { get; set; }
        public string LogIDstring { get; set; }
        public Gender Gender { get; set; }
        public FormKey AssetsRace { get; set; }
        public FormKey BodyGenRace { get; set; }
        public FormKey HeightRace { get; set; }
        public LinkedNPCGroupInfo AssociatedLinkGroup { get; set; }
        public NPCAssignment SpecificNPCAssignment { get; set; }
        public NPCAssignment ConsistencyNPCAssignment { get; set; }

        private static Gender GetGender(INpcGetter npc)
        {
            if (npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female))
            {
                return Gender.female;
            }

            return Gender.male;
        }
    }
}
