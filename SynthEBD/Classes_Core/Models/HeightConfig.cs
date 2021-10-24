using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;

namespace SynthEBD
{
    public class HeightConfig
    {
        public HeightConfig()
        {
            this.Label = "";
            this.HeightAssignments = new HashSet<HeightAssignment>();
        }

        public string Label { get; set; }
        public HashSet<HeightAssignment> HeightAssignments { get; set; }
    }

    public class HeightAssignment
    {
        public HeightAssignment()
        {
            this.Label = "";
            this.Races = new HashSet<FormKey>();
            this.HeightMale = "1.000000";
            this.HeightFemale = "1.000000";
            this.HeightMaleRange = "0.020000";
            this.HeightFemaleRange = "0.020000";
            this.DistributionMode = DistMode.uniform;
        }

        public string Label { get; set; }
        public HashSet<FormKey> Races { get; set; }
        public string HeightMale {get; set;}
        public string HeightFemale { get; set; }
        public string HeightMaleRange { get; set; }
        public string HeightFemaleRange { get; set; }
        public DistMode DistributionMode { get; set; }

        public class zEBDHeightAssignment // for backwards compatibility
        {
            public zEBDHeightAssignment()
            {
                this.EDID = "";
                this.heightMale = "1.000000";
                this.heightFemale = "1.000000";
                this.heightMaleRange = "0.020000";
                this.heightFemaleRange = "0.020000";
                this.distMode = "uniform";
            }
            public string EDID { get; set; }
            public string heightMale { get; set; }
            public string heightFemale { get; set; }
            public string heightMaleRange { get; set; }
            public string heightFemaleRange { get; set; }
            public string distMode { get; set; }
        }
    }

    public enum DistMode
    {
        uniform,
        bellCurve
    }
}
