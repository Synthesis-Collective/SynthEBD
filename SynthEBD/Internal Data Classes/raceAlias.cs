using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD.Internal_Data_Classes
{
    public class raceAlias
    {
        public raceAlias()
        {
            this.race = "";
            this.aliasRace = "";
            this.bMale = true;
            this.bFemale = true;
            this.bApplyToAssets = false;
            this.bApplyToBodyGen = false;
            this.bApplyToHeight = false;
        }

        public string race { get; set; } // change later to mutagen race
        public string aliasRace { get; set; } // change later to mutagen race
        public bool bMale { get; set; }
        public bool bFemale { get; set; }

        public bool bApplyToAssets { get; set; }
        public bool bApplyToBodyGen { get; set; }
        public bool bApplyToHeight { get; set; }
    }
}
