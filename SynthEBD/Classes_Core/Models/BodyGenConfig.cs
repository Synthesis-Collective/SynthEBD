using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;
using System.IO;
using Newtonsoft.Json;

namespace SynthEBD
{
    public class BodyGenConfigs
    {
        public BodyGenConfigs()
        {
            this.Male = new HashSet<BodyGenConfig>();
            this.Female = new HashSet<BodyGenConfig>();
        }
        public HashSet<BodyGenConfig> Male { get; set; }
        public HashSet<BodyGenConfig> Female { get; set; }
    }

    public class BodyGenConfig : IHasDescriptorRules
    {
        public BodyGenConfig()
        {
            this.Label = "";
            this.Gender = Gender.Female;
            this.RacialTemplateGroupMap = new HashSet<RacialMapping>();
            this.Templates = new HashSet<BodyGenTemplate>();
            this.TemplateGroups = new HashSet<string>();
            this.TemplateDescriptors = new HashSet<BodyShapeDescriptor>();
            this.AttributeGroups = new HashSet<AttributeGroup>();
            this.DescriptorRules = new HashSet<BodyShapeDescriptorRules>();
        }

        public string Label { get; set; }
        public Gender Gender { get; set; }
        public HashSet<RacialMapping> RacialTemplateGroupMap { get; set; }
        public HashSet<BodyGenTemplate> Templates { get; set; }
        public HashSet<string> TemplateGroups { get; set; }
        public HashSet<BodyShapeDescriptor> TemplateDescriptors { get; set; }
        public HashSet<AttributeGroup> AttributeGroups { get; set; }
        public HashSet<BodyShapeDescriptorRules> DescriptorRules { get; set; }
        [Newtonsoft.Json.JsonIgnore]
        public string FilePath { get; set; }

        public class RacialMapping
        {
            public RacialMapping()
            {
                this.Label = "";
                this.Races = new HashSet<FormKey>();
                this.RaceGroupings = new HashSet<string>();
                this.Combinations = new HashSet<BodyGenCombination>();
            }
            public string Label { get; set; }
            public HashSet<FormKey> Races { get; set; }
            public HashSet<string> RaceGroupings { get; set; }
            public HashSet<BodyGenCombination> Combinations { get; set; }

            public class BodyGenCombination : IProbabilityWeighted
            {
                public BodyGenCombination()
                {
                    this.Members = new HashSet<string>();
                    this.ProbabilityWeighting = 1;
                }
                public HashSet<string> Members { get; set; }
                public int ProbabilityWeighting { get; set; }
            }
        }

        public class BodyGenTemplate : IProbabilityWeighted
        {
            public BodyGenTemplate()
            {
                this.Label = "";
                this.Notes = "";
                this.Specs = "";
                this.MemberOfTemplateGroups = new HashSet<string>();
                this.BodyShapeDescriptors = new HashSet<BodyShapeDescriptor>();
                this.AllowedRaces = new HashSet<FormKey>();
                this.AllowedRaceGroupings = new HashSet<string>();
                this.DisallowedRaces = new HashSet<FormKey>();
                this.DisallowedRaceGroupings = new HashSet<string>();
                this.AllowedAttributes = new HashSet<NPCAttribute>();
                this.DisallowedAttributes = new HashSet<NPCAttribute>();
                this.AllowUnique = true;
                this.AllowNonUnique = true;
                this.AllowRandom = true;
                this.ProbabilityWeighting = 1;
                this.RequiredTemplates = new HashSet<string>();
                this.WeightRange = new NPCWeightRange();

                // used during patching, not written to settings file
                this.MatchedForceIfCount = 0;
            }

            public string Label { get; set; }
            public string Notes { get; set; }
            public string Specs { get; set; } // will need special logic during I/O because in zEBD settings this is called "params" which is reserved in C#
            public HashSet<string> MemberOfTemplateGroups { get; set; }
            public HashSet<BodyShapeDescriptor> BodyShapeDescriptors { get; set; }
            public HashSet<FormKey> AllowedRaces { get; set; }
            public HashSet<FormKey> DisallowedRaces { get; set; }
            public HashSet<string> AllowedRaceGroupings { get; set; }
            public HashSet<string> DisallowedRaceGroupings { get; set; }
            public HashSet<NPCAttribute> AllowedAttributes { get; set; } // keeping as array to allow deserialization of original zEBD settings files
            public HashSet<NPCAttribute> DisallowedAttributes { get; set; }
            public bool AllowUnique { get; set; }
            public bool AllowNonUnique { get; set; }
            public bool AllowRandom { get; set; }
            public int ProbabilityWeighting { get; set; }
            public HashSet<string> RequiredTemplates { get; set; }
            public NPCWeightRange WeightRange { get; set; }

            [JsonIgnore]
            public int MatchedForceIfCount { get; set; }
        }
    }

    public class zEBDSplitBodyGenConfig
    {
        public zEBDSplitBodyGenConfig()
        {
            this.Male = new BodyGenConfig();
            this.bMaleInitialized = false;
            this.Female = new BodyGenConfig();
            this.bFemaleInitialized = false;
        }
        public BodyGenConfig Male { get; set; }
        public bool bMaleInitialized { get; set; }
        public BodyGenConfig Female { get; set; }
        public bool bFemaleInitialized { get; set; }
    }
    public class zEBDBodyGenConfig
    {
        public zEBDBodyGenConfig()
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
                this.notes = "";
                this.specs = "";
                this.groups = new HashSet<string>();
                this.descriptors = new HashSet<string>();
                this.gender = "";
                this.allowedRaces = new HashSet<string>();
                this.disallowedRaces = new HashSet<string>();
                this.allowedAttributes = new List<string[]>();
                this.disallowedAttributes = new List<string[]>();
                this.forceIfAttributes = new List<string[]>();
                this.allowUnique = true;
                this.allowNonUnique = true;
                this.allowRandom = true;
                this.probabilityWeighting = 1;
                this.requiredTemplates = new HashSet<string>();
                this.weightRange = new string[] { null, null };
            }

            public string name { get; set; }
            public string notes { get; set; }
            public string specs { get; set; } // will need special logic during I/O because in zEBD settings this is called "params" which is reserved in C#
            public string gender { get; set; } // might need to convert from string to enum depending on how json deserialization works
            public HashSet<string> groups { get; set; }
            public HashSet<string> descriptors { get; set; }
            public HashSet<string> allowedRaces { get; set; }
            public HashSet<string> disallowedRaces { get; set; }
            public List<string[]> allowedAttributes { get; set; } // keeping as array to allow deserialization of original zEBD settings files
            public List<string[]> disallowedAttributes { get; set; }
            public List<string[]> forceIfAttributes { get; set; }
            public bool allowUnique { get; set; }
            public bool allowNonUnique { get; set; }
            public bool allowRandom { get; set; }
            public int probabilityWeighting { get; set; }
            public HashSet<string> requiredTemplates { get; set; }
            public string[] weightRange { get; set; }
        }

        public static zEBDSplitBodyGenConfig ToSynthEBDConfig(zEBDBodyGenConfig zConfig, List<RaceGrouping> raceGroupings, string filePath)
        {
            zEBDSplitBodyGenConfig converted = new zEBDSplitBodyGenConfig();

            List<BodyShapeDescriptor> usedMaleDescriptors = new List<BodyShapeDescriptor>();
            List<BodyShapeDescriptor> usedFemaleDescriptors = new List<BodyShapeDescriptor>();

            HashSet<string> usedMaleGroups = new HashSet<string>();
            HashSet<string> usedFemaleGroups = new HashSet<string>();

            // handle female section
            if (zConfig.racialSettingsFemale.Count > 0)
            {
                foreach (var rs in zConfig.racialSettingsFemale)
                {
                    converted.Female.RacialTemplateGroupMap.Add(zEBDBodyGenRacialSettingsToSynthEBD(rs, usedFemaleGroups));
                }

                foreach (var zTemplate in zConfig.templates)
                {
                    if (zTemplate.gender == "female")
                    {
                        converted.Female.Templates.Add(zEBDBodyGenRacialTemplateToSynthEBD(zTemplate, raceGroupings, usedFemaleDescriptors));
                    }
                }

                converted.Female.TemplateGroups = usedFemaleGroups;
                converted.Female.TemplateDescriptors = new HashSet<BodyShapeDescriptor>(usedFemaleDescriptors);
                converted.bFemaleInitialized = true;
            }

            // handle male section
            if (zConfig.racialSettingsMale.Count > 0)
            {
                foreach (var rs in zConfig.racialSettingsMale)
                {
                    converted.Male.RacialTemplateGroupMap.Add(zEBDBodyGenRacialSettingsToSynthEBD(rs, usedMaleGroups));
                }

                foreach (var zTemplate in zConfig.templates)
                {
                    if (zTemplate.gender == "male")
                    {
                        converted.Male.Templates.Add(zEBDBodyGenRacialTemplateToSynthEBD(zTemplate, raceGroupings, usedMaleDescriptors));
                    }
                }

                converted.Male.TemplateGroups = usedMaleGroups;
                converted.Male.TemplateDescriptors = new HashSet<BodyShapeDescriptor>(usedMaleDescriptors);
                converted.bMaleInitialized = true;
            }

            return converted;
        }

        public static BodyGenConfig.RacialMapping zEBDBodyGenRacialSettingsToSynthEBD(zEBDBodyGenConfig.racialSettings rs, HashSet<string> usedGroups)
        {
            BodyGenConfig.RacialMapping newRS = new BodyGenConfig.RacialMapping();
            newRS.Label = rs.EDID;
            newRS.Races = new HashSet<FormKey> { Converters.RaceEDID2FormKey(rs.EDID) };
            newRS.RaceGroupings = new HashSet<string>();
            newRS.Combinations = new HashSet<BodyGenConfig.RacialMapping.BodyGenCombination>();
            foreach (var combo in rs.combinations)
            {
                BodyGenConfig.RacialMapping.BodyGenCombination newCombo = new BodyGenConfig.RacialMapping.BodyGenCombination();
                newCombo.Members = combo.members;
                newCombo.ProbabilityWeighting = combo.probabilityWeighting;
                newRS.Combinations.Add(newCombo);

                foreach (var member in combo.members)
                {
                    if (usedGroups.Contains(member) == false)
                    {
                        usedGroups.Add(member);
                    }
                }
            }
            return newRS;
        }

        public static BodyGenConfig.BodyGenTemplate zEBDBodyGenRacialTemplateToSynthEBD(zEBDBodyGenConfig.BodyGenTemplate zTemplate, List<RaceGrouping> raceGroupings, List<BodyShapeDescriptor> usedDescriptors)
        {
            BodyGenConfig.BodyGenTemplate newTemplate = new BodyGenConfig.BodyGenTemplate();

            newTemplate.Label = zTemplate.name;
            newTemplate.Notes = zTemplate.notes;
            newTemplate.Specs = zTemplate.specs;
            newTemplate.MemberOfTemplateGroups = zTemplate.groups;
            foreach (string d in zTemplate.descriptors)
            {
                var convertedDescriptor = Converters.StringToBodyShapeDescriptor(d);
                if(!usedDescriptors.Any(n=> n.Signature == d))
                {
                    usedDescriptors.Add(convertedDescriptor);
                }
                newTemplate.BodyShapeDescriptors.Add(convertedDescriptor);
            }

            foreach (string id in zTemplate.allowedRaces)
            {
                bool continueSearch = true;
                // first see if it belongs to a RaceGrouping
                foreach (var group in raceGroupings)
                {
                    if (group.Label == id)
                    {
                        newTemplate.AllowedRaceGroupings.Add(group.Label);
                        continueSearch = false;
                        break;
                    }
                }

                // if not, see if it is a race EditorID
                if (continueSearch == true)
                {
                    FormKey raceFormKey = Converters.RaceEDID2FormKey(id);
                    if (raceFormKey.IsNull == false)
                    {
                        newTemplate.AllowedRaces.Add(raceFormKey);
                    }
                }
            }

            foreach (string id in zTemplate.disallowedRaces)
            {
                bool continueSearch = true;
                // first see if it belongs to a RaceGrouping
                foreach (var group in raceGroupings)
                {
                    if (group.Label == id)
                    {
                        newTemplate.DisallowedRaceGroupings.Add(group.Label);
                        continueSearch = false;
                        break;
                    }
                }

                // if not, see if it is a race EditorID
                if (continueSearch == true)
                {
                    FormKey raceFormKey = Converters.RaceEDID2FormKey(id);
                    if (raceFormKey.IsNull == false)
                    {
                        newTemplate.DisallowedRaces.Add(raceFormKey);
                    }
                }
            }

            newTemplate.AllowedAttributes = Converters.StringArraysToAttributes(zTemplate.allowedAttributes);
            newTemplate.DisallowedAttributes = Converters.StringArraysToAttributes(zTemplate.disallowedAttributes);
            Converters.zEBDForceIfAttributesToAllowed(newTemplate.AllowedAttributes, Converters.StringArraysToAttributes(zTemplate.forceIfAttributes));

            newTemplate.WeightRange = Converters.StringArrayToWeightRange(zTemplate.weightRange);

            newTemplate.AllowUnique = zTemplate.allowUnique;
            newTemplate.AllowNonUnique = zTemplate.allowNonUnique;
            newTemplate.AllowRandom = zTemplate.allowRandom;
            newTemplate.RequiredTemplates = zTemplate.requiredTemplates;
            newTemplate.ProbabilityWeighting = zTemplate.probabilityWeighting;

            return newTemplate;
        }
    }

}
