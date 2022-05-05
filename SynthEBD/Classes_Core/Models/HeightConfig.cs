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
        [Newtonsoft.Json.JsonIgnore]
        public string FilePath { get; set; }
    }

    public class HeightAssignment
    {
        public HeightAssignment()
        {
            this.Label = "";
            this.Races = new HashSet<FormKey>();
            this.HeightMale = 1;
            this.HeightFemale = 1;
            this.HeightMaleRange = 0.02F;
            this.HeightFemaleRange = 0.02F;
            this.DistributionMode = DistMode.uniform;
        }

        public string Label { get; set; }
        public HashSet<FormKey> Races { get; set; }
        public float HeightMale {get; set;}
        public float HeightFemale { get; set; }
        public float HeightMaleRange { get; set; }
        public float HeightFemaleRange { get; set; }
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
}
