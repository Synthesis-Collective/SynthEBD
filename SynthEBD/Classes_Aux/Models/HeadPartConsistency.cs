using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class HeadPartConsistency
    {
        public FormKey FormKey { get; set; }
        public string EditorID { get; set; }
        public bool RandomizedToNone { get; set; } = false;
    }
}
