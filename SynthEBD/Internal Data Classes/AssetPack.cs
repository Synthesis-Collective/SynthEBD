using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;

namespace SynthEBD
{
    class AssetPack
    {
        public AssetPack()
        {
            this.groupName = "";
            this.gender = Gender.male;
            this.displayAlerts = true;
            this.userAlert = "";
            this.subgroups = new HashSet<Subgroup>();
        }

        public string groupName { get; set; }
        public Gender gender { get; set; }
        public bool displayAlerts { get; set; }
        public string userAlert { get; set; }
        public HashSet<Subgroup> subgroups { get; set; }

        public class Subgroup
        {
            public Subgroup()
            {
                this.id = "";
                this.name = "";
                this.enabled = true;
                this.distributionEnabled = true;
                this.allowedRaces = new HashSet<FormKey>();
                this.disallowedRaces = new HashSet<FormKey>();
                this.allowedAttributes = new HashSet<string[]>();
                this.disallowedAttributes = new HashSet<string[]>();
                this.forceIfAttributes = new HashSet<string[]>();
                this.bAllowUnique = true;
                this.bAllowNonUnique = true;
                this.requiredSubgroups = new HashSet<string>();
                this.excludedSubgroups = new HashSet<string>();
                this.addKeywords = new HashSet<string>();
                this.probabilityWeighting = 1;
                this.paths = new string[] { "", "" };
                this.allowedBodyGenDescriptors = new HashSet<string>();
                this.disallowedBodyGenDescriptors = new HashSet<string>();
                this.weightRange = new string[] { null, null };
                this.subgroups = new HashSet<Subgroup>();
            }
            
            public string id { get; set; }
            public string name { get; set; }
            public bool enabled { get; set; }
            public bool distributionEnabled { get; set; }
            public HashSet<FormKey> allowedRaces { get; set; } // consider converting to generic to allow for both Mutagen races and defined collections
            public HashSet<FormKey> disallowedRaces { get; set; } // same as above
            public HashSet<string[]> allowedAttributes { get; set; } // keeping as array to allow deserialization of original zEBD settings files
            public HashSet<string[]> disallowedAttributes { get; set; } 
            public HashSet<string[]> forceIfAttributes { get; set; }
            public bool bAllowUnique { get; set; }
            public bool bAllowNonUnique { get; set; }
            public HashSet<string> requiredSubgroups { get; set; }
            public HashSet<string> excludedSubgroups { get; set; }
            public HashSet<string> addKeywords { get; set; }
            public int probabilityWeighting { get; set; }
            public string[] paths { get; set; }
            public HashSet<string> allowedBodyGenDescriptors { get; set; }
            public HashSet<string> disallowedBodyGenDescriptors { get; set; }
            public string[] weightRange { get; set; }
            public HashSet<Subgroup> subgroups { get; set; }
        }
    }
}
