using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;

namespace SynthEBD
{
    class HeightConfig
    {
        public HeightConfig()
        {
            this.Race = new FormKey();
            this.heightMale = "1.000";
            this.heightFemale = "1.000";
            this.heightFemale = "0.020";
            this.heightFemalerange = "0.020";
            this.distMode = distMode.uniform;
        }

        public FormKey Race { get; set; }
        public string heightMale {get; set;}
        public string heightFemale { get; set; }
        public string heightMaleRange { get; set; }
        public string heightFemalerange { get; set; }
        public distMode distMode { get; set; }
    }

    public enum distMode
    {
        uniform,
        bellCurve
    }
}
