using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class LinkedNPCGroup
    {
        public LinkedNPCGroup()
        {
            this.GroupName = "";
            this.NPCFormKeys = new HashSet<FormKey>();
            this.Primary = new FormKey();
        }

        public string GroupName { get; set; }
        public HashSet<FormKey> NPCFormKeys { get; set; }
        public FormKey Primary { get; set; }
    }
}
