using Mutagen.Bethesda.Plugins;
using Newtonsoft.Json;
using System.Diagnostics;

namespace SynthEBD;

/// <summary>The loaded BodyGen configurations, partitioned by the sex they apply to.</summary>
public class BodyGenConfigs
{
    public HashSet<BodyGenConfig> Male { get; set; } = new();
    public HashSet<BodyGenConfig> Female { get; set; } = new();
}

/// <summary>
/// A single BodyGen configuration (one config file for one sex): the morph <see cref="Templates"/>, the
/// per-race template-group mapping, the template groups and shape descriptors, and the attribute groups /
/// race groupings used to evaluate them.
/// </summary>
[DebuggerDisplay("{Label}")]
public class BodyGenConfig
{
    public string Label { get; set; } = "";
    public Gender Gender { get; set; } = Gender.Female;
    public HashSet<RacialMapping> RacialTemplateGroupMap { get; set; } = new();
    public HashSet<BodyGenTemplate> Templates { get; set; } = new();
    public HashSet<string> TemplateGroups { get; set; } = new();
    [JsonConverter(typeof(BodyShapeDescriptorShellListConverter))]
    public List<BodyShapeDescriptorShell> TemplateDescriptors { get; set; } = new();
    public HashSet<AttributeGroup> AttributeGroups { get; set; } = new();
    public List<RaceGrouping> RaceGroupings { get; set; } = new();
    public DescriptorMatchMode AllowedDescriptorMatchMode { get; set; } = DescriptorMatchMode.All;
    public DescriptorMatchMode DisallowedDescriptorMatchMode { get; set; } = DescriptorMatchMode.Any;

    [Newtonsoft.Json.JsonIgnore]
    public string FilePath { get; set; }

    /// <summary>Maps a set of races (directly or via race groupings) to the weighted template-group combinations they may draw morphs from.</summary>
    public class RacialMapping
    {
        public string Label { get; set; } = "";
        public HashSet<FormKey> Races { get; set; } = new();
        public HashSet<string> RaceGroupings { get; set; } = new();
        public List<BodyGenCombination> Combinations { get; set; } = new();

        /// <summary>One weighted combination of template-group members eligible for the parent racial mapping.</summary>
        public class BodyGenCombination : IProbabilityWeighted
        {
            public List<string> Members { get; set; } = new();
            public double ProbabilityWeighting { get; set; } = 1;
            public List<AttributeWeightModifier> ProbabilityWeightModifiers { get; set; } = new(); // editing UI deferred; honored by the selector when authored
            /// <summary>Newtonsoft conditional-serialization hook: only serialize the modifiers list when non-empty.</summary>
            public bool ShouldSerializeProbabilityWeightModifiers() => ProbabilityWeightModifiers.Count > 0;
        }
    }

    /// <summary>A single BodyGen morph template: its morph spec string plus the allow/disallow race and attribute filters, weighting, weight range, and template-group membership that gate its selection.</summary>
    [DebuggerDisplay("{Label}")]
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
        public List<AttributeWeightModifier> ProbabilityWeightModifiers { get; set; } = new();
        /// <summary>Newtonsoft conditional-serialization hook: only serialize <see cref="ProbabilityWeightModifiers"/> when non-empty.</summary>
        public bool ShouldSerializeProbabilityWeightModifiers() => ProbabilityWeightModifiers.Count > 0;
        public HashSet<string> RequiredTemplates { get; set; } = new();
        public NPCWeightRange WeightRange { get; set; } = new();

        /// <summary>Runtime count of ForceIf attributes matched on the current NPC, used to rank candidates during selection (not serialized).</summary>
        [JsonIgnore]
        public int MatchedForceIfCount { get; set; } = 0;

        /// <summary>Back-reference to the owning config, set at load time (not serialized).</summary>
        [JsonIgnore]
        public BodyGenConfig ParentConfig { get; set; }
    }
}

/// <summary>Result of converting a legacy zEBD BodyGen config: separate male/female <see cref="BodyGenConfig"/>s plus flags for which sex sections were present.</summary>
public class zEBDSplitBodyGenConfig
{
    public BodyGenConfig Male { get; set; } = new();
    public bool bMaleInitialized { get; set; } = false;
    public BodyGenConfig Female { get; set; } = new();
    public bool bFemaleInitialized { get; set; } = false;
}
/// <summary>Backwards-compatibility loader for old (single-file, both-sexes) zEBD BodyGen configs; converts them into a <see cref="zEBDSplitBodyGenConfig"/>.</summary>
public class zEBDBodyGenConfig
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    private readonly Converters _converters;
    /// <summary>Captures the environment, logger, and zEBD converters used during conversion.</summary>
    public zEBDBodyGenConfig(IEnvironmentStateProvider environmentProvider, Logger logger, Converters converters)
    {
        _environmentProvider = environmentProvider;
        _logger = logger;
        _converters = converters;
    }
    public HashSet<racialSettings> racialSettingsFemale { get; set; } = new();
    public HashSet<racialSettings> racialSettingsMale { get; set; } = new();
    public HashSet<BodyGenTemplate> templates { get; set; } = new();
    public HashSet<string> templateGroups { get; set; } = new();
    public HashSet<string> templateDescriptors { get; set; } = new();

    /// <summary>Old zEBD per-race settings DTO (race EditorID plus its weighted template-group combinations).</summary>
    public class racialSettings
    {
        public string EDID { get; set; } = "";
        public List<BodyGenCombination> combinations { get; set; } = new();

        /// <summary>Old zEBD template-group combination DTO.</summary>
        public class BodyGenCombination
        {
            public List<string> members { get; set; } = new();
            public double probabilityWeighting { get; set; } = 1;
        }
    }

    /// <summary>Old zEBD BodyGen template DTO (string-typed; attributes stored as string arrays).</summary>
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

    /// <summary>Converts this legacy config into male/female SynthEBD <see cref="BodyGenConfig"/>s, collecting the descriptors and template groups actually referenced by each sex's templates.</summary>
    /// <param name="raceGroupings">Race groupings used to resolve template race filters.</param>
    /// <param name="filePath">Source path (currently unused by the body; see review notes).</param>
    /// <returns>The split, converted config with per-sex initialization flags.</returns>
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
            converted.Female.TemplateDescriptors = usedFemaleDescriptors
                .Select(x => new BodyShapeDescriptor() { ID = x })
                .ToShells();
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
            converted.Male.TemplateDescriptors = usedMaleDescriptors
                .Select(x => new BodyShapeDescriptor() { ID = x })
                .ToShells();
            converted.bMaleInitialized = true;
        }

        return converted;
    }

    /// <summary>Converts one legacy per-race settings entry into a SynthEBD <see cref="BodyGenConfig.RacialMapping"/>, accumulating referenced template-group names into <paramref name="usedGroups"/>.</summary>
    public BodyGenConfig.RacialMapping zEBDBodyGenRacialSettingsToSynthEBD(zEBDBodyGenConfig.racialSettings rs, HashSet<string> usedGroups)
    {
        BodyGenConfig.RacialMapping newRS = new BodyGenConfig.RacialMapping();
        newRS.Label = rs.EDID;
        newRS.Races = new HashSet<FormKey> { Converters.RaceEDID2FormKey(rs.EDID, _environmentProvider) };
        newRS.RaceGroupings = new HashSet<string>();
        newRS.Combinations = new();
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

    /// <summary>Converts one legacy BodyGen template into a SynthEBD <see cref="BodyGenConfig.BodyGenTemplate"/>: parsing descriptors, resolving race EditorIDs/groupings, and importing the zEBD allowed/disallowed/forceIf attribute arrays. Accumulates referenced descriptors into <paramref name="usedDescriptors"/>.</summary>
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
                FormKey raceFormKey = Converters.RaceEDID2FormKey(id, _environmentProvider);
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
                FormKey raceFormKey = Converters.RaceEDID2FormKey(id, _environmentProvider);
                if (raceFormKey.IsNull == false)
                {
                    newTemplate.DisallowedRaces.Add(raceFormKey);
                }
            }
        }

        newTemplate.AllowedAttributes = _converters.zEBDStringArraysToAttributes(zTemplate.allowedAttributes);
        newTemplate.DisallowedAttributes = _converters.zEBDStringArraysToAttributes(zTemplate.disallowedAttributes);
        Converters.ImportzEBDForceIfAttributes(newTemplate.AllowedAttributes, _converters.zEBDStringArraysToAttributes(zTemplate.forceIfAttributes));

        newTemplate.WeightRange = Converters.StringArrayToWeightRange(zTemplate.weightRange);

        newTemplate.AllowUnique = zTemplate.allowUnique;
        newTemplate.AllowNonUnique = zTemplate.allowNonUnique;
        newTemplate.AllowRandom = zTemplate.allowRandom;
        newTemplate.RequiredTemplates = zTemplate.requiredTemplates;
        newTemplate.ProbabilityWeighting = zTemplate.probabilityWeighting;

        return newTemplate;
    }
}