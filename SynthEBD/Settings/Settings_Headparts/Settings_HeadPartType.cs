using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class Settings_HeadPartType
    {
        public List<HeadPartSetting> HeadParts { get; set; } = new();

        // whole-category rules
        public bool bAllowFemale { get; set; } = true;
        public bool bAllowMale { get; set; } = true;
        public bool bRestrictToNPCsWithThisType { get; set; } = true;
        public HashSet<FormKey> AllowedRaces { get; set; } = new();
        public HashSet<FormKey> DisallowedRaces { get; set; } = new();
        public HashSet<string> AllowedRaceGroupings { get; set; } = new();
        public HashSet<string> DisallowedRaceGroupings { get; set; } = new();
        public HashSet<NPCAttribute> AllowedAttributes { get; set; } = new();
        public HashSet<NPCAttribute> DisallowedAttributes { get; set; } = new();
        public bool bAllowUnique { get; set; } = true;
        public bool bAllowNonUnique { get; set; } = true;
        public bool bAllowRandom { get; set; } = true;
        public NPCWeightRange WeightRange { get; set; } = new();
        public double RandomizationPercentage { get; set; } = 50;
        public HashSet<BodyShapeDescriptor> AllowedBodySlideDescriptors { get; set; } = new();
        public HashSet<BodyShapeDescriptor> DisallowedBodySlideDescriptors { get; set; } = new();
        public HashSet<BodyShapeDescriptor> AllowedBodyGenDescriptors { get; set; } = new();
        public HashSet<BodyShapeDescriptor> DisallowedBodyGenDescriptors { get; set; } = new();

        [Newtonsoft.Json.JsonIgnore]
        public int MatchedForceIfCount { get; set; } = 0;
    }
}
