using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class HeadPartSetting: IProbabilityWeighted
    {
        public FormKey HeadPartFormKey { get; set; }
        public string EditorID { get; set; }
        public bool bAllowMale { get; set; }
        public bool bAllowFemale { get; set; }
        public HashSet<FormKey> AllowedRaces { get; set; } = new();
        public HashSet<FormKey> DisallowedRaces { get; set; } = new();
        public HashSet<string> AllowedRaceGroupings { get; set; } = new();
        public HashSet<string> DisallowedRaceGroupings { get; set; } = new();
        public HashSet<NPCAttribute> AllowedAttributes { get; set; } = new();
        public HashSet<NPCAttribute> DisallowedAttributes { get; set; } = new();
        public bool bAllowUnique { get; set; } = true;
        public bool bAllowNonUnique { get; set; } = true;
        public bool bAllowRandom { get; set; } = true;
        public double ProbabilityWeighting { get; set; } = 1;
        public NPCWeightRange WeightRange { get; set; } = new();
        public HashSet<BodyShapeDescriptor.LabelSignature> AllowedBodySlideDescriptors { get; set; } = new();
        public HashSet<BodyShapeDescriptor.LabelSignature> DisallowedBodySlideDescriptors { get; set; } = new();

        // used during patching
        [Newtonsoft.Json.JsonIgnore]
        public int MatchedForceIfCount { get; set; } = 0;
        [Newtonsoft.Json.JsonIgnore]
        public IHeadPartGetter ResolvedHeadPart { get; set; } = null;
        [Newtonsoft.Json.JsonIgnore]
        public Dictionary<string, HashSet<string>> AllowedBodySlideDescriptorDictionary { get; set; }
        [Newtonsoft.Json.JsonIgnore]
        public Dictionary<string, HashSet<string>> DisallowedBodySlideDescriptorDictionary { get; set; }
    }
}
