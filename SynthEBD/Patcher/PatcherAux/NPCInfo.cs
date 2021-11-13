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
        public NPCInfo(INpcGetter npc, Settings_General generalSettings, HashSet<LinkedNPCGroup> definedLinkGroups, HashSet<LinkedNPCGroupInfo> createdLinkGroupInfos, HashSet<NPCAssignment> specificNPCAssignments)
        {
            this.NPC = npc;
            this.LogIDstring = npc.Name?.String + " | " + npc.EditorID + " | " + npc.FormKey.ToString();
            this.Gender = GetGender(npc);
            AssetsRace = AliasHandler.GetAliasTexMesh(generalSettings, npc.Race.FormKey);
            BodyGenRace = AliasHandler.GetAliasBodyGen(generalSettings, npc.Race.FormKey);
            HeightRace = AliasHandler.GetAliasHeight(generalSettings, npc.Race.FormKey);
            AssociatedLinkGroup = LinkedNPCGroupInfo.GetInfoFromLinkedNPCGroup(definedLinkGroups, createdLinkGroupInfos, npc.FormKey);
            SpecificNPCAssignment = specificNPCAssignments.Where(x => x.NPCFormKey == npc.FormKey).FirstOrDefault();
            //TEMP
            ConsistencyNPCAssignment = new NPCAssignment();
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
