using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>A named height-configuration file: a set of <see cref="HeightAssignment"/>s mapping races to height multipliers.</summary>
public class HeightConfig
{
    /// <summary>Display name of this height config.</summary>
    public string Label { get; set; } = "";
    public HashSet<HeightAssignment> HeightAssignments { get; set; } = new();

    /// <summary>On-disk path this config was loaded from (not serialized).</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string FilePath { get; set; }
}

/// <summary>Assigns male/female base height multipliers (each with a randomization range and distribution mode) to a set of races.</summary>
public class HeightAssignment
{
    /// <summary>Display name of this assignment.</summary>
    public string Label { get; set; } = "";
    /// <summary>Races this height assignment applies to.</summary>
    public HashSet<FormKey> Races { get; set; } = new();
    /// <summary>Base height multiplier applied to male NPCs.</summary>
    public float HeightMale {get; set;} = 1;
    /// <summary>Base height multiplier applied to female NPCs.</summary>
    public float HeightFemale { get; set; } = 1;
    /// <summary>± randomization range around <see cref="HeightMale"/>.</summary>
    public float HeightMaleRange { get; set; } = 0.02F;
    /// <summary>± randomization range around <see cref="HeightFemale"/>.</summary>
    public float HeightFemaleRange { get; set; } = 0.02F;
    /// <summary>How the randomized height is distributed across the range (e.g. uniform vs bell-curve).</summary>
    public DistMode DistributionMode { get; set; } = DistMode.uniform;

    /// <summary>Backwards-compatibility DTO mirroring the old zEBD height-assignment JSON (string-typed fields).</summary>
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