using Mutagen.Bethesda.Plugins;
using Newtonsoft.Json;

namespace SynthEBD;

public class BodyGenConfigs
{
    public HashSet<BodyGenConfig> Male { get; set; } = new();
    public HashSet<BodyGenConfig> Female { get; set; } = new();
}

public class BodyGenConfig
{
    public string Label { get; set; } = "";
    public Gender Gender { get; set; } = Gender.Female;
    public HashSet<RacialMapping> RacialTemplateGroupMap { get; set; } = new();
    public HashSet<BodyGenTemplate> Templates { get; set; } = new();
    public HashSet<string> TemplateGroups { get; set; } = new();
    public HashSet<BodyShapeDescriptor> TemplateDescriptors { get; set; } = new();
    public HashSet<AttributeGroup> AttributeGroups { get; set; } = new();
    public List<RaceGrouping> RaceGroupings { get; set; } = new();

    [Newtonsoft.Json.JsonIgnore]
    public string FilePath { get; set; }

    public class RacialMapping
    {
        public string Label { get; set; } = "";
        public HashSet<FormKey> Races { get; set; } = new();
        public HashSet<string> RaceGroupings { get; set; } = new();
        public HashSet<BodyGenCombination> Combinations { get; set; } = new();

        public class BodyGenCombination : IProbabilityWeighted
        {
            public HashSet<string> Members { get; set; } = new();
            public double ProbabilityWeighting { get; set; } = 1;
        }
    }

    public class BodyGenTemplate : IProbabilityWeighted
    {
        public string Label { get; set; } = "";
        public string Notes { get; set; } = "";
        public string Specs { get; set; } = ""; // will need special logic during I/O because in zEBD settings this is called "params" which is reserved in C#
        public HashSet<string> MemberOfTemplateGroups { get; set; } = new();
        public HashSet<BodyShapeDescriptor.LabelSignature> BodyShapeDescriptors { get; set; } = new();
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
        public HashSet<string> RequiredTemplates { get; set; } = new();
        public NPCWeightRange WeightRange { get; set; } = new();

        [JsonIgnore]
        public int MatchedForceIfCount { get; set; } = 0;
    }
}

public class zEBDSplitBodyGenConfig
{
    public BodyGenConfig Male { get; set; } = new();
    public bool bMaleInitialized { get; set; } = false;
    public BodyGenConfig Female { get; set; } = new();
    public bool bFemaleInitialized { get; set; } = false;
}
public class zEBDBodyGenConfig
{
    private readonly IStateProvider _stateProvider;
    private readonly Logger _logger;
    private readonly Converters _converters;
    public zEBDBodyGenConfig(IStateProvider stateProvider, Logger logger, Converters converters)
    {
        _stateProvider = stateProvider;
        _logger = logger;
        _converters = converters;
    }
    public HashSet<racialSettings> racialSettingsFemale { get; set; } = new();
    public HashSet<racialSettings> racialSettingsMale { get; set; } = new();
    public HashSet<BodyGenTemplate> templates { get; set; } = new();
    public HashSet<string> templateGroups { get; set; } = new();
    public HashSet<string> templateDescriptors { get; set; } = new();

    public class racialSettings
    {
        public string EDID { get; set; } = "";
        public HashSet<BodyGenCombination> combinations { get; set; } = new();

        public class BodyGenCombination
        {
            public HashSet<string> members { get; set; } = new();
            public double probabilityWeighting { get; set; } = 1;
        }
    }

    public class BodyGenTemplate
    {
        public string name { get; set; } = "";
        public string notes { get; set; } = "";
        public string specs { get; set; } = ""; // will need special logic during I/O because in zEBD settings this is called "params" which is reserved in C#
        public string gender { get; set; } = ""; // might need to convert from string to enum depending on how json deserialization works
        public HashSet<string> groups { get; set; } = new();
        public HashSet<string> descriptors { get; set; } = new();
        public HashSet<string> allowedRaces { get; set; } = new();
        public HashSet<string> disallowedRaces { get; set; } = new();
        public List<string[]> allowedAttributes { get; set; } = new(); // keeping as array to allow deserialization of original zEBD settings files
        public List<string[]> disallowedAttributes { get; set; } = new();
        public List<string[]> forceIfAttributes { get; set; } = new();
        public bool allowUnique { get; set; } = true;
        public bool allowNonUnique { get; set; } = true;
        public bool allowRandom { get; set; } = true;
        public double probabilityWeighting { get; set; } = 1;
        public HashSet<string> requiredTemplates { get; set; } = new();
        public string[] weightRange { get; set; } = new string[] { null, null };
    }

    public zEBDSplitBodyGenConfig ToSynthEBDConfig(List<RaceGrouping> raceGroupings, string filePath)
    {
        zEBDSplitBodyGenConfig converted = new zEBDSplitBodyGenConfig();

        List<BodyShapeDescriptor.LabelSignature> usedMaleDescriptors = new();
        List<BodyShapeDescriptor.LabelSignature> usedFemaleDescriptors = new();

        HashSet<string> usedMaleGroups = new HashSet<string>();
        HashSet<string> usedFemaleGroups = new HashSet<string>();

        // handle female section
        if (racialSettingsFemale.Count > 0)
        {
            foreach (var rs in racialSettingsFemale)
            {
                converted.Female.RacialTemplateGroupMap.Add(zEBDBodyGenRacialSettingsToSynthEBD(rs, usedFemaleGroups));
            }

            foreach (var zTemplate in templates)
            {
                if (zTemplate.gender == "female")
                {
                    converted.Female.Templates.Add(ToSynthEBDTemplate(zTemplate, raceGroupings, usedFemaleDescriptors));
                }
            }

            converted.Female.TemplateGroups = usedFemaleGroups;
            converted.Female.TemplateDescriptors = usedFemaleDescriptors.Select(x => new BodyShapeDescriptor() { ID = x }).ToHashSet();
            converted.bFemaleInitialized = true;
        }

        // handle male section
        if (racialSettingsMale.Count > 0)
        {
            foreach (var rs in racialSettingsMale)
            {
                converted.Male.RacialTemplateGroupMap.Add(zEBDBodyGenRacialSettingsToSynthEBD(rs, usedMaleGroups));
            }

            foreach (var zTemplate in templates)
            {
                if (zTemplate.gender == "male")
                {
                    converted.Male.Templates.Add(ToSynthEBDTemplate(zTemplate, raceGroupings, usedMaleDescriptors));
                }
            }

            converted.Male.TemplateGroups = usedMaleGroups;
            converted.Male.TemplateDescriptors = usedMaleDescriptors.Select(x => new BodyShapeDescriptor() { ID = x }).ToHashSet();
            converted.bMaleInitialized = true;
        }

        return converted;
    }

    public BodyGenConfig.RacialMapping zEBDBodyGenRacialSettingsToSynthEBD(zEBDBodyGenConfig.racialSettings rs, HashSet<string> usedGroups)
    {
        BodyGenConfig.RacialMapping newRS = new BodyGenConfig.RacialMapping();
        newRS.Label = rs.EDID;
        newRS.Races = new HashSet<FormKey> { Converters.RaceEDID2FormKey(rs.EDID, _stateProvider) };
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

    public BodyGenConfig.BodyGenTemplate ToSynthEBDTemplate(BodyGenTemplate zTemplate, List<RaceGrouping> raceGroupings, List<BodyShapeDescriptor.LabelSignature> usedDescriptors)
    {
        BodyGenConfig.BodyGenTemplate newTemplate = new BodyGenConfig.BodyGenTemplate();

        newTemplate.Label = zTemplate.name;
        newTemplate.Notes = zTemplate.notes;
        newTemplate.Specs = zTemplate.specs;
        newTemplate.MemberOfTemplateGroups = zTemplate.groups;
        foreach (string d in zTemplate.descriptors)
        {
            if (BodyShapeDescriptor.LabelSignature.FromString(d, out BodyShapeDescriptor.LabelSignature descriptor, _logger))
            {
                if (!usedDescriptors.Any(n => n.Equals(descriptor)))
                {
                    usedDescriptors.Add(descriptor);
                }
                newTemplate.BodyShapeDescriptors.Add(descriptor);
            }
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
                FormKey raceFormKey = Converters.RaceEDID2FormKey(id, _stateProvider);
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
                FormKey raceFormKey = Converters.RaceEDID2FormKey(id, _stateProvider);
                if (raceFormKey.IsNull == false)
                {
                    newTemplate.DisallowedRaces.Add(raceFormKey);
                }
            }
        }

        newTemplate.AllowedAttributes = _converters.StringArraysToAttributes(zTemplate.allowedAttributes);
        newTemplate.DisallowedAttributes = _converters.StringArraysToAttributes(zTemplate.disallowedAttributes);
        Converters.ImportzEBDForceIfAttributes(newTemplate.AllowedAttributes, _converters.StringArraysToAttributes(zTemplate.forceIfAttributes));

        newTemplate.WeightRange = Converters.StringArrayToWeightRange(zTemplate.weightRange);

        newTemplate.AllowUnique = zTemplate.allowUnique;
        newTemplate.AllowNonUnique = zTemplate.allowNonUnique;
        newTemplate.AllowRandom = zTemplate.allowRandom;
        newTemplate.RequiredTemplates = zTemplate.requiredTemplates;
        newTemplate.ProbabilityWeighting = zTemplate.probabilityWeighting;

        return newTemplate;
    }
}