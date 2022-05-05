using Mutagen.Bethesda.Plugins;

namespace SynthEBD
{
    public class BodyShapeDescriptorRules
    {
        public BodyShapeDescriptorRules()
        {
            DescriptorSignature = "";
            AllowedRaces = new HashSet<FormKey>();
            DisallowedRaces = new HashSet<FormKey>();
            AllowedRaceGroupings = new HashSet<string>();
            DisallowedRaceGroupings = new HashSet<string>();
            AllowedAttributes = new HashSet<NPCAttribute>();
            DisallowedAttributes = new HashSet<NPCAttribute>();
            AllowUnique = true;
            AllowNonUnique = true;
            AllowRandom = true;
            ProbabilityWeighting = 1;
            WeightRange = new NPCWeightRange() { Lower = 0, Upper = 100 };
        }

        public string DescriptorSignature { get; set; }
        public HashSet<FormKey> AllowedRaces { get; set; }
        public HashSet<FormKey> DisallowedRaces { get; set; }
        public HashSet<string> AllowedRaceGroupings { get; set; }
        public HashSet<string> DisallowedRaceGroupings { get; set; }
        public HashSet<NPCAttribute> AllowedAttributes { get; set; } // keeping as array to allow deserialization of original zEBD settings files
        public HashSet<NPCAttribute> DisallowedAttributes { get; set; }
        public bool AllowUnique { get; set; }
        public bool AllowNonUnique { get; set; }
        public bool AllowRandom { get; set; }
        public double ProbabilityWeighting { get; set; }
        public NPCWeightRange WeightRange { get; set; }
    }

    public interface IHasDescriptorRules
    {
        public HashSet<BodyShapeDescriptorRules> DescriptorRules { get; set; }
    }
}
