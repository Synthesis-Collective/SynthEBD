using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

public class HeightConfig
{
    public string Label { get; set; } = "";
    public HashSet<HeightAssignment> HeightAssignments { get; set; } = new();

    [Newtonsoft.Json.JsonIgnore]
    public string FilePath { get; set; }
}

public class HeightAssignment
{
    public string Label { get; set; } = "";
    public HashSet<FormKey> Races { get; set; } = new();
    public float HeightMale {get; set;} = 1;
    public float HeightFemale { get; set; } = 1;
    public float HeightMaleRange { get; set; } = 0.02F;
    public float HeightFemaleRange { get; set; } = 0.02F;
    public DistMode DistributionMode { get; set; } = DistMode.uniform;

    public class zEBDHeightAssignment // for backwards compatibility
    {
        public string EDID { get; set; } = "";
        public string heightMale { get; set; } = "1.000000";
        public string heightFemale { get; set; } = "1.000000";
        public string heightMaleRange { get; set; } = "0.020000";
        public string heightFemaleRange { get; set; } = "0.020000";
        public string distMode { get; set; } = "uniform";
    }
}