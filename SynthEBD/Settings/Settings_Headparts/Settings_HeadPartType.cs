using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>Per-head-part-category settings POCO (one per <see cref="HeadPart.TypeEnum"/>),
    /// persisted as JSON within <see cref="Settings_Headparts.Types"/>. Holds the list of
    /// available head parts plus the whole-category eligibility rules (gender, race, attribute,
    /// uniqueness, weight, and body-shape descriptor filters). Several dictionaries are runtime-only
    /// caches rebuilt during patching and excluded from serialization.</summary>
    public class Settings_HeadPartType
    {
        /// <summary>The individual head-part entries available in this category.</summary>
        public List<HeadPartSetting> HeadParts { get; set; } = new();

        // whole-category rules
        public bool bAllowFemale { get; set; } = true;
        public bool bAllowMale { get; set; } = true;
        /// <summary>Only assign to NPCs that already have a head part of this type.</summary>
        public bool bRestrictToNPCsWithThisType { get; set; } = true;
        public HashSet<FormKey> AllowedRaces { get; set; } = new();
        public HashSet<FormKey> DisallowedRaces { get; set; } = new();
        /// <summary>Labels of race groupings whose members are eligible for this category.</summary>
        public HashSet<string> AllowedRaceGroupings { get; set; } = new();
        /// <summary>Labels of race groupings whose members are excluded from this category.</summary>
        public HashSet<string> DisallowedRaceGroupings { get; set; } = new();
        public HashSet<NPCAttribute> AllowedAttributes { get; set; } = new();
        public HashSet<NPCAttribute> DisallowedAttributes { get; set; } = new();
        public bool bAllowUnique { get; set; } = true;
        public bool bAllowNonUnique { get; set; } = true;
        /// <summary>Allow assigning a random head part of this type (vs. only explicit rules).</summary>
        public bool bAllowRandom { get; set; } = true;
        /// <summary>NPC body-weight window in which this category applies.</summary>
        public NPCWeightRange WeightRange { get; set; } = new();
        /// <summary>Probability (0-100) that an eligible NPC actually receives a head part of this type.</summary>
        public double RandomizationPercentage { get; set; } = 50;
        /// <summary>BodySlide descriptors an NPC's assigned preset must match for this category to apply.</summary>
        public HashSet<BodyShapeDescriptor.LabelSignature> AllowedBodySlideDescriptors { get; set; } = new();
        /// <summary>Whether allowed-BodySlide-descriptor matching requires All or Any descriptors.</summary>
        public DescriptorMatchMode AllowedBodySlideMatchMode { get; set; } = DescriptorMatchMode.All;
        public HashSet<BodyShapeDescriptor.LabelSignature> DisallowedBodySlideDescriptors { get; set; } = new();
        /// <summary>Whether disallowed-BodySlide-descriptor matching requires All or Any descriptors.</summary>
        public DescriptorMatchMode DisallowedBodySlideMatchMode { get; set; } = DescriptorMatchMode.Any;
        /// <summary>BodyGen descriptors a male NPC's assigned morphs must match for this category to apply.</summary>
        public HashSet<BodyShapeDescriptor.LabelSignature> AllowedBodyGenDescriptorsMale { get; set; } = new();
        public DescriptorMatchMode AllowedBodyGenDescriptorMatchModeMale { get; set; } = DescriptorMatchMode.All;
        public HashSet<BodyShapeDescriptor.LabelSignature> DisallowedBodyGenDescriptorsMale { get; set; } = new();
        public DescriptorMatchMode DisallowedBodyGenDescriptorMatchModeMale { get; set; } = DescriptorMatchMode.Any;
        /// <summary>BodyGen descriptors a female NPC's assigned morphs must match for this category to apply.</summary>
        public HashSet<BodyShapeDescriptor.LabelSignature> AllowedBodyGenDescriptorsFemale { get; set; } = new();
        public DescriptorMatchMode AllowedBodyGenDescriptorMatchModeFemale { get; set; } = DescriptorMatchMode.All;
        public HashSet<BodyShapeDescriptor.LabelSignature> DisallowedBodyGenDescriptorsFemale { get; set; } = new();
        public DescriptorMatchMode DisallowedBodyGenDescriptorMatchModeFemale { get; set; } = DescriptorMatchMode.Any;

        // populated during patching
        /// <summary>Runtime count of matched ForceIf attributes, used for rule prioritization.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public int MatchedForceIfCount { get; set; } = 0;

        /// <summary>Runtime cache: allowed BodySlide descriptors reshaped as category → values for fast lookup.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public Dictionary<string, HashSet<string>> AllowedBodySlideDescriptorDictionary { get; set; } = new();

        /// <summary>Runtime cache: disallowed BodySlide descriptors reshaped as category → values for fast lookup.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public Dictionary<string, HashSet<string>> DisallowedBodySlideDescriptorDictionary { get; set; } = new();

        /// <summary>Runtime cache: allowed male BodyGen descriptors reshaped as category → values for fast lookup.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public Dictionary<string, HashSet<string>> AllowedBodyGenDescriptorDictionaryMale { get; set; } = new();

        /// <summary>Runtime cache: disallowed male BodyGen descriptors reshaped as category → values for fast lookup.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public Dictionary<string, HashSet<string>> DisallowedBodyGenDescriptorDictionaryMale { get; set; } = new();

        /// <summary>Runtime cache: allowed female BodyGen descriptors reshaped as category → values for fast lookup.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public Dictionary<string, HashSet<string>> AllowedBodyGenDescriptorDictionaryFemale { get; set; } = new();

        /// <summary>Runtime cache: disallowed female BodyGen descriptors reshaped as category → values for fast lookup.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public Dictionary<string, HashSet<string>> DisallowedBodyGenDescriptorDictionaryFemale { get; set; } = new();

        /// <summary>Runtime cache: head parts split by gender, populated during patching.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public Dictionary<Gender, HashSet<HeadPartSetting>> HeadPartsGendered { get; set; } = new()
        {
            {Gender.Male, new HashSet<HeadPartSetting>() },
            {Gender.Female, new HashSet<HeadPartSetting>() }
        };
    }
}
