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
    class NPCInfo
    {
        public NPCInfo(INpcGetter npc, Settings_General generalSettings)
        {
            this.Gender = GetGender(npc);

            AssetsRace = AliasHandler.GetAliasTexMesh(generalSettings, npc.Race.FormKey);
            BodyGenRace = AliasHandler.GetAliasBodyGen(generalSettings, npc.Race.FormKey);
            HeightRace = AliasHandler.GetAliasHeight(generalSettings, npc.Race.FormKey);
        }

        public Gender Gender { get; set; }
        public FormKey AssetsRace { get; set; }
        public FormKey BodyGenRace { get; set; }
        public FormKey HeightRace { get; set; }

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
