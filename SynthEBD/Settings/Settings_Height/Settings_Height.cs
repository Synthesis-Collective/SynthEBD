using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class Settings_Height
    {
        public Settings_Height()
        {
            this.bChangeNPCHeight = true;
            this.bChangeRaceHeight = true;
            this.bOverwriteNonDefaultNPCHeights = true;
            this.SelectedHeightConfig = "";
        }

        public bool bChangeNPCHeight { get; set; }
        public bool bChangeRaceHeight { get; set; }
        public bool bOverwriteNonDefaultNPCHeights { get; set; }
        public string SelectedHeightConfig { get; set; }
    }
}
