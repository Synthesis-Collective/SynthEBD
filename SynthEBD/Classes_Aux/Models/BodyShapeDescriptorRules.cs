using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

public class BodyShapeDescriptorRules
{
    public string DescriptorSignature { get; set; } = "";
    public HashSet<FormKey> AllowedRaces { get; set; } = new();
    public HashSet<FormKey> DisallowedRaces { get; set; } = new();
    public HashSet<string> AllowedRaceGroupings { get; set; } = new();
    public HashSet<string> DisallowedRaceGroupings { get; set; } = new();
    public HashSet<NPCAttribute> AllowedAttributes { get; set; } = new(); // keeping as array to allow deserialization of original zEBD settings files
    public HashSet<NPCAttribute> DisallowedAttributes { get; set; } = new();
    public bool AllowUnique { get; set; } = true;
    public bool AllowNonUnique { get; set; } = true;
    public bool AllowRandom { get; set; } = true;
    public double ProbabilityWeighting { get; set; } = 1;
    public NPCWeightRange WeightRange { get; set; } = new() { Lower = 0, Upper = 100 };
}

public interface IHasDescriptorRules
{
    public HashSet<BodyShapeDescriptorRules> DescriptorRules { get; set; }
}