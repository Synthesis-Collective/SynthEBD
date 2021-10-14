using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class Settings_BodyGen
    {
        public Settings_BodyGen()
        {
            this.CurrentMaleConfig = null;
            this.CurrentFemaleConfig = null;
        }

        public string CurrentMaleConfig { get; set; }
        public string CurrentFemaleConfig { get; set; }

    }
}
