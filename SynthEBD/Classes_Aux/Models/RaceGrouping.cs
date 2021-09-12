using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class RaceGrouping
    {
        public RaceGrouping()
        {
            this.Label = "";
            this.Races = new HashSet<FormKey>();
        }

        public string Label { get; set; }
        public HashSet<FormKey> Races { get; set; }
    }
}
