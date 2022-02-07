using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;

namespace SynthEBD
{
    public class RaceAlias
    {
        public RaceAlias()
        {
            this.Race = new FormKey();
            this.AliasRace = new FormKey();
            this.bMale = true;
            this.bFemale = true;
            this.bApplyToAssets = false;
            this.bApplyToBodyGen = false;
            this.bApplyToHeight = false;
        }

        public FormKey Race { get; set; } 
        public FormKey AliasRace { get; set; } 
        public bool bMale { get; set; }
        public bool bFemale { get; set; }

        public bool bApplyToAssets { get; set; }
        public bool bApplyToBodyGen { get; set; }
        public bool bApplyToHeight { get; set; }
    }
}
