using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD.Internal_Data_Classes
{
    class HeightConfig
    {
        public string EDID { get; set; }
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
