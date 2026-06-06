using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Filter criteria selecting which NPCs receive a detailed verbose report — by allowed/disallowed
    /// races and race groupings, allowed/disallowed attributes, unique/non-unique status, and weight range.
    /// </summary>
    public class DetailedReportNPCSelector
    {
        public HashSet<FormKey> AllowedRaces { get; set; } = new();
        public HashSet<string> AllowedRaceGroupings { get; set; } = new();
        public HashSet<FormKey> DisallowedRaces { get; set; } = new();
        public HashSet<string> DisallowedRaceGroupings { get; set; } = new();
        public HashSet<NPCAttribute> AllowedAttributes { get; set; } = new();
        public HashSet<NPCAttribute> DisallowedAttributes { get; set; } = new();
        public bool AllowUnique { get; set; } = true;
        public bool AllowNonUnique { get; set; } = true;
        public NPCWeightRange WeightRange { get; set; } = new();
    }
}
