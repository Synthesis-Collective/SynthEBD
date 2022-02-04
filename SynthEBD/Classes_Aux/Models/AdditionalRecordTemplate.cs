using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class AdditionalRecordTemplate
    {
        public AdditionalRecordTemplate()
        {
            this.Races = new HashSet<FormKey>();
            this.TemplateNPC = new FormKey();
            this.AdditionalRacesPaths = new HashSet<string>();
        }

        public HashSet<FormKey> Races { get; set; }
        public FormKey TemplateNPC { get; set; }
        public HashSet<string> AdditionalRacesPaths { get; set; }
    }
}
