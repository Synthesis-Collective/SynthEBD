using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;

namespace SynthEBD.Internal_Data_Classes
{
    class BodyGenConfig
    {
        public BodyGenConfig()
        {
            this.racialSettingsFemale = new HashSet<racialSettings>();
            this.racialSettingsMale = new HashSet<racialSettings>();
            this.templates = new HashSet<BodyGenTemplate>();
            this.templateGroups = new HashSet<string>();
            this.templateDescriptors = new HashSet<string>();
        }

        public HashSet<racialSettings> racialSettingsFemale { get; set; }
        public HashSet<racialSettings> racialSettingsMale { get; set; }
        public HashSet<BodyGenTemplate> templates { get; set; }
        public HashSet<string> templateGroups { get; set; }
        public HashSet<string> templateDescriptors { get; set; }
        public class racialSettings
        {
            public racialSettings()
            {
                this.EDID = "";
                this.combinations = new HashSet<BodyGenCombination>();
            }
            public string EDID { get; set; }
            public HashSet<BodyGenCombination> combinations { get; set; }

            public class BodyGenCombination
            {
                public BodyGenCombination()
                {
                    this.members = new HashSet<string>();
                    this.probabilityWeighting = 1;
                }
                public HashSet<string> members { get; set; }
                public int probabilityWeighting { get; set; }
            }
        }

        public class BodyGenTemplate
        {
            public BodyGenTemplate()
            {
                this.name = "";
                this.specs = "";
                this.gender = Gender.female;
                this.allowedRaces = new HashSet<FormKey>();
                this.disallowedRaces = new HashSet<FormKey>();
                this.allowedAttributes = new HashSet<string[]>();
                this.disallowedAttributes = new HashSet<string[]>();
                this.forceIfAttributes = new HashSet<string[]>();
                this.bAllowUnique = true;
                this.bAllowNonUnique = true;
                this.bAllowRandom = true;
                this.probabilityWeighting = 1;
                this.weightRange = new string[] { null, null };
            }

            public string name { get; set; }
            public string specs { get; set; } // will need special logic during I/O because in zEBD settings this is called "params" which is reserved in C#
            public Gender gender { get; set; } // might need to convert from string to enum depending on how json deserialization works
            public HashSet<FormKey> allowedRaces { get; set; }
            public HashSet<FormKey> disallowedRaces { get; set; }
            public HashSet<string[]> allowedAttributes { get; set; } // keeping as array to allow deserialization of original zEBD settings files
            public HashSet<string[]> disallowedAttributes { get; set; }
            public HashSet<string[]> forceIfAttributes { get; set; }
            public bool bAllowUnique { get; set; }
            public bool bAllowNonUnique { get; set; }
            public bool bAllowRandom { get; set; }
            public int probabilityWeighting { get; set; }
            public string[] weightRange { get; set; }
        }
    }
}
