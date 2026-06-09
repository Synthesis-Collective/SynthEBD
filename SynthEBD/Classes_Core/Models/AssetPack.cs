using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SynthEBD;

/// <summary>
/// The core asset configuration: one "config file" defining how a set of textures/meshes is distributed.
/// Holds the tree of <see cref="Subgroup"/>s (each a selectable option with its own race/attribute filters
/// and file paths), the asset-pack-wide <see cref="DistributionRules"/>, record-template assignments,
/// replacer groups, and the associated BodyGen config / BSA keys. <see cref="ConfigType"/> distinguishes a
/// primary pack from a mix-in.
/// </summary>
[DebuggerDisplay("{ShortName}: {GroupName}")]
public class AssetPack : IModelHasSubgroups
{
    public string GroupName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public AssetPackType ConfigType { get; set; } = AssetPackType.Primary;
    public Gender Gender { get; set; } = Gender.Male;
    public bool DisplayAlerts { get; set; } = true;
    public string UserAlert { get; set; } = "";
    public List<Subgroup> Subgroups { get; set; } = new(); // don't change to HashSet - need indexing for RequiredSubgroups
    public FormKey DefaultRecordTemplate { get; set; } = new();
    public HashSet<AdditionalRecordTemplate> AdditionalRecordTemplateAssignments { get; set; } = new();
    public string AssociatedBodyGenConfigName { get; set; } = "";
    public List<AssetReplacerGroup> ReplacerGroups { get; set; } = new();
    public HashSet<string> DefaultRecordTemplateAdditionalRacesPaths { get; set; } = new();
    public HashSet<AttributeGroup> AttributeGroups { get; set; } = new();
    public List<RaceGrouping> RaceGroupings { get; set; } = new();
    public ConfigDistributionRules DistributionRules { get; set; } = new();
    public List<ModKey> AssociatedBsaModKeys { get; set; } = new();
    public string InstallationToken { get; set; } = "";
    [Newtonsoft.Json.JsonIgnore]
    public string FilePath { get; set; }

    /// <summary>Assigns and returns a fresh installation token (group name + timestamp) used to detect re-installation of the pack.</summary>
    public string GenerateInstallationToken() // assings and returns the installation token string
    {
        InstallationToken = GroupName + "|" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss:fff");
        return InstallationToken;
    }

    /// <summary>The asset-pack-wide distribution rules: the allow/disallow race, attribute, and body-shape-descriptor filters (plus weighting and added keywords) inherited by every subgroup.</summary>
    public class ConfigDistributionRules : IProbabilityWeighted
    {
        public HashSet<FormKey> AllowedRaces { get; set; } = new();
        public HashSet<string> AllowedRaceGroupings { get; set; } = new();
        public HashSet<FormKey> DisallowedRaces { get; set; } = new();
        public HashSet<string> DisallowedRaceGroupings { get; set; } = new();
        public HashSet<NPCAttribute> AllowedAttributes { get; set; } = new();
        public HashSet<NPCAttribute> DisallowedAttributes { get; set; } = new();
        public bool DistributionEnabled { get; set; } = true;
        public bool AllowUnique { get; set; } = true;
        public bool AllowNonUnique { get; set; } = true;
        public HashSet<string> AddKeywords { get; set; } = new();
        public double ProbabilityWeighting { get; set; } = 1;
        public List<AttributeWeightModifier> ProbabilityWeightModifiers { get; set; } = new();
        /// <summary>Newtonsoft conditional-serialization hook: only serialize the weight modifiers when non-empty.</summary>
        public bool ShouldSerializeProbabilityWeightModifiers() => ProbabilityWeightModifiers.Count > 0;
        public HashSet<BodyShapeDescriptor.LabelSignature> AllowedBodyGenDescriptors { get; set; } = new();
        public DescriptorMatchMode AllowedBodyGenMatchMode { get; set; } = DescriptorMatchMode.All;
        public HashSet<BodyShapeDescriptor.LabelSignature> DisallowedBodyGenDescriptors { get; set; } = new();
        public DescriptorMatchMode DisallowedBodyGenMatchMode { get; set; } = DescriptorMatchMode.Any;
        public HashSet<BodyShapeDescriptor.LabelSignature> AllowedBodySlideDescriptors { get; set; } = new();
        public DescriptorMatchMode AllowedBodySlideMatchMode { get; set; } = DescriptorMatchMode.All;
        public HashSet<BodyShapeDescriptor.LabelSignature> DisallowedBodySlideDescriptors { get; set; } = new();
        public DescriptorMatchMode DisallowedBodySlideMatchMode { get; set; } = DescriptorMatchMode.Any;
        public HashSet<BodyShapeDescriptor.PrioritizedLabelSignature> PrioritizedBodySlideDescriptors { get; set; } = new();
        public NPCWeightRange WeightRange { get; set; } = new();

        public static string SubgroupIDString = "ConfigDistributionRules";
        public static string SubgroupNameString = "Main Distribution Rules";

        /// <summary>Wraps the asset-pack-wide rules in a synthetic top-level <see cref="Subgroup"/> so the selection logic can treat them as the inherited parent of every real subgroup.</summary>
        public static AssetPack.Subgroup CreateInheritanceParent(ConfigDistributionRules rules)
        {
            AssetPack.Subgroup subgroup = new AssetPack.Subgroup();
            subgroup.ID = SubgroupIDString;
            subgroup.Name = SubgroupNameString;
            subgroup.AllowedRaces = new HashSet<FormKey>(rules.AllowedRaces ?? new());
            subgroup.AllowedRaceGroupings = new HashSet<string>(rules.AllowedRaceGroupings ?? new());
            subgroup.DisallowedRaces = new HashSet<FormKey>(rules.DisallowedRaces ?? new());
            subgroup.DisallowedRaceGroupings = new HashSet<string>(rules.DisallowedRaceGroupings ?? new());
            subgroup.AllowedAttributes = new HashSet<NPCAttribute>(rules.AllowedAttributes ?? new());
            subgroup.DisallowedAttributes = new HashSet<NPCAttribute>(rules.DisallowedAttributes ?? new());
            subgroup.DistributionEnabled = rules.DistributionEnabled;
            subgroup.AllowUnique = rules.AllowUnique;
            subgroup.AllowNonUnique = rules.AllowNonUnique;
            subgroup.AddKeywords = new HashSet<string>(rules.AddKeywords ?? new());
            subgroup.ProbabilityWeighting = rules.ProbabilityWeighting;
            subgroup.ProbabilityWeightModifiers = (rules.ProbabilityWeightModifiers ?? new()).Select(AttributeWeightModifier.CloneAsNew).ToList();
            subgroup.AllowedBodyGenDescriptors = new HashSet<BodyShapeDescriptor.LabelSignature>(rules.AllowedBodyGenDescriptors ?? new());
            subgroup.AllowedBodyGenMatchMode = rules.AllowedBodyGenMatchMode;
            subgroup.DisallowedBodyGenDescriptors = new HashSet<BodyShapeDescriptor.LabelSignature>(rules.DisallowedBodyGenDescriptors ?? new());
            subgroup.DisallowedBodyGenMatchMode = rules.DisallowedBodyGenMatchMode;
            subgroup.AllowedBodySlideDescriptors = new HashSet<BodyShapeDescriptor.LabelSignature>(rules.AllowedBodySlideDescriptors ?? new());
            subgroup.AllowedBodySlideMatchMode = rules.AllowedBodySlideMatchMode;
            subgroup.DisallowedBodySlideDescriptors = new HashSet<BodyShapeDescriptor.LabelSignature>(rules.DisallowedBodySlideDescriptors ?? new());
            subgroup.DisallowedBodySlideMatchMode = rules.DisallowedBodySlideMatchMode;
            subgroup.WeightRange = rules.WeightRange.Clone();
            return subgroup;
        }
    }

    /// <summary>
    /// One selectable option within an asset pack (a node in the subgroup tree): its file-path replacements
    /// plus the race/attribute/body-shape-descriptor filters, required/excluded-subgroup constraints,
    /// weighting, and added keywords that govern when it may be selected. Subgroups nest via
    /// <see cref="Subgroups"/>.
    /// </summary>
    [DebuggerDisplay("{ID}: {Name}")]
    public class Subgroup : IProbabilityWeighted, IModelHasSubgroups
    {
        public string ID { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public bool DistributionEnabled { get; set; } = true;
        public string Notes { get; set; } = string.Empty;
        public HashSet<FormKey> AllowedRaces { get; set; } = new();
        public HashSet<string> AllowedRaceGroupings { get; set; } = new();
        public HashSet<FormKey> DisallowedRaces { get; set; } = new();
        public HashSet<string> DisallowedRaceGroupings { get; set; } = new();
        public HashSet<NPCAttribute> AllowedAttributes { get; set; } = new();
        public HashSet<NPCAttribute> DisallowedAttributes { get; set; } = new();
        public bool AllowUnique { get; set; } = true;
        public bool AllowNonUnique { get; set; } = true;
        public List<string> RequiredSubgroups { get; set; } = new();
        public List<string> ExcludedSubgroups { get; set; } = new();
        public HashSet<string> AddKeywords { get; set; } = new();
        public double ProbabilityWeighting { get; set; } = 1;
        public List<AttributeWeightModifier> ProbabilityWeightModifiers { get; set; } = new();
        /// <summary>Newtonsoft conditional-serialization hook: only serialize the weight modifiers when non-empty.</summary>
        public bool ShouldSerializeProbabilityWeightModifiers() => ProbabilityWeightModifiers.Count > 0;
        public HashSet<FilePathReplacement> Paths { get; set; } = new();
        public HashSet<BodyShapeDescriptor.LabelSignature> AllowedBodyGenDescriptors { get; set; } = new();
        public DescriptorMatchMode AllowedBodyGenMatchMode { get; set; } = DescriptorMatchMode.All;
        public HashSet<BodyShapeDescriptor.LabelSignature> DisallowedBodyGenDescriptors { get; set; } = new();
        public DescriptorMatchMode DisallowedBodyGenMatchMode { get; set; } = DescriptorMatchMode.Any;
        public HashSet<BodyShapeDescriptor.LabelSignature> AllowedBodySlideDescriptors { get; set; } = new();
        public DescriptorMatchMode AllowedBodySlideMatchMode { get; set; } = DescriptorMatchMode.All;
        public HashSet<BodyShapeDescriptor.LabelSignature> DisallowedBodySlideDescriptors { get; set; } = new();
        public DescriptorMatchMode DisallowedBodySlideMatchMode { get; set; } = DescriptorMatchMode.Any;
        public HashSet<BodyShapeDescriptor.PrioritizedLabelSignature> PrioritizedBodySlideDescriptors { get; set; } = new();
        public NPCWeightRange WeightRange { get; set; } = new();
        public List<Subgroup> Subgroups { get; set; } = new();
        public string TopLevelSubgroupID { get; set; } = "";

        /// <summary>Recursively collects the race-grouping labels referenced by this subgroup and its descendants into <paramref name="labels"/>.</summary>
        public void GetContainedRaceGroupingLabels(HashSet<string> labels)
        {
            foreach (var groupLabel in AllowedRaceGroupings)
            {
                if (!labels.Contains(groupLabel))
                {
                    labels.Add(groupLabel);
                }
            }
            foreach (var groupLabel in DisallowedRaceGroupings)
            {
                if (!labels.Contains(groupLabel))
                {
                    labels.Add(groupLabel);
                }
            }
            foreach (var subgroup in Subgroups)
            {
                subgroup.GetContainedRaceGroupingLabels(labels);
            }
        }
    }
}

/// <summary>Whether an asset pack distributes as a primary appearance config or as an additive mix-in.</summary>
public enum AssetPackType
{
    /// <summary>A primary asset pack (one is chosen per NPC).</summary>
    Primary,
    /// <summary>An additive mix-in pack (applied on top of the primary).</summary>
    MixIn
}
/// <summary>A named group of replacer subgroups that swap assets on an already-assigned NPC, relative to a template NPC.</summary>
public class AssetReplacerGroup : IModelHasSubgroups
{
    public string Label { get; set; } = "";
    public List<AssetPack.Subgroup> Subgroups { get; set; } = new();
    public FormKey TemplateNPCFormKey { get; set; } = new();
}

/// <summary>Identifies a record to be replaced: the source record paths, the destination FormKey, and the destination specifier.</summary>
public class RecordReplacerSpecifier
{
    public HashSet<string> Paths { get; set; } = new();
    public FormKey DestFormKeySpecifier { get; set; }
    public SubgroupCombination.DestinationSpecifier DestSpecifier { get; set; }
}

/// <summary>Common contract for models that own a list of <see cref="AssetPack.Subgroup"/>s (asset packs and replacer groups).</summary>
public interface IModelHasSubgroups
{
    public List<AssetPack.Subgroup> Subgroups { get; set; }
}

/* Will probably need to discard - can't edit tints
public class TintMaskSelector
{
    public TintMaskSelector()
    {

    }

    public string TexturePath { get; set; }
    public TintAssets.TintMaskType MaskType { get; set; }
    public TintMaskDistributionMode DistributionMode { get; set; }

}

public enum TintMaskDistributionMode
{
    Replace, // replace a given tint mask path with the given selector
    DistributeAndReplace, // distribute to any NPC if the attribute allows for it, but remove a given path if the NPC has it
    Distribute // distribute to any NPC without replacing any existing tint masks
}

public class TintColorSelector : IProbabilityWeighted
{

} */

// Backward compatibility classes for loading zEBD settings files and converting to synthEBD
/// <summary>Backwards-compatibility DTO for loading old zEBD asset-config JSON, converted via <see cref="ToSynthEBDAssetPack"/>.</summary>
class ZEBDAssetPack
{
    public string groupName { get; set; } = "";
    public Gender gender { get; set; } = Gender.Male;
    public bool displayAlerts { get; set; } = true;
    public string userAlert { get; set; } = "";
    public HashSet<ZEBDSubgroup> subgroups { get; set; } = new();

    /// <summary>Old zEBD subgroup DTO (string-typed; attributes as string arrays, paths as [source, destination] pairs).</summary>
    public class ZEBDSubgroup
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public bool enabled { get; set; } = true;
        public bool distributionEnabled { get; set; } = true;
        public List<string> allowedRaces { get; set; } = new();
        public List<RaceGrouping> allowedRaceGroupings { get; set; } = new();
        public List<RaceGrouping> disallowedRaceGroupings { get; set; } = new();
        public List<string> disallowedRaces { get; set; } = new();
        public List<string[]> allowedAttributes { get; set; } = new();
        public List<string[]> disallowedAttributes { get; set; } = new();
        public List<string[]> forceIfAttributes { get; set; } = new();
        public bool bAllowUnique { get; set; } = true;
        public bool bAllowNonUnique { get; set; } = true;
        public List<string> requiredSubgroups { get; set; } = new();
        public List<string> excludedSubgroups { get; set; } = new();
        public List<string> addKeywords { get; set; } = new();
        public double probabilityWeighting { get; set; } = 1;
        public List<string[]> paths { get; set; } = new();
        public List<string> allowedBodyGenDescriptors { get; set; } = new();
        public List<string> disallowedBodyGenDescriptors { get; set; } = new();
        public string[] weightRange { get; set; } = new string[] { null, null };
        public List<ZEBDSubgroup> subgroups { get; set; } = new();

        public string hashKey { get; set; }

        /// <summary>
        /// Recursively converts this legacy subgroup into a SynthEBD <see cref="AssetPack.Subgroup"/>: importing
        /// attributes, normalizing and converting the source→destination texture paths (via
        /// <see cref="zEBDTexturePathConversionDict"/>), resolving race EditorIDs/groupings, and parsing descriptors.
        /// Unrecognized destination paths are appended to <paramref name="conversionErrors"/>.
        /// </summary>
        public AssetPack.Subgroup ToSynthEBDSubgroup(List<RaceGrouping> raceGroupings, string topLevelSubgroupID, List<string> conversionErrors, IEnvironmentStateProvider environmentProvider, Logger logger, Converters converters)
        {
            AssetPack.Subgroup s = new AssetPack.Subgroup();

            s.ID = id;
            s.Name = name;
            s.Enabled = enabled;
            s.DistributionEnabled = distributionEnabled;
            s.AllowedAttributes = converters.zEBDStringArraysToAttributes(allowedAttributes);
            s.DisallowedAttributes = converters.zEBDStringArraysToAttributes(disallowedAttributes);
            Converters.ImportzEBDForceIfAttributes(s.AllowedAttributes, converters.zEBDStringArraysToAttributes(forceIfAttributes));
            s.AllowUnique = bAllowUnique;
            s.AllowNonUnique = bAllowNonUnique;
            s.RequiredSubgroups = new(requiredSubgroups);
            s.ExcludedSubgroups = new(excludedSubgroups);
            s.AddKeywords = new HashSet<string>(addKeywords);
            s.ProbabilityWeighting = probabilityWeighting;

            s.Paths = new HashSet<FilePathReplacement>();
            foreach (string[] pathPair in paths)
            {
                string newSource = pathPair[0];
                if (newSource.StartsWith('\\'))
                {
                    newSource = newSource.Remove(0, 1);
                }
                if (pathPair[0].StartsWith("Skyrim.esm", StringComparison.OrdinalIgnoreCase) && (pathPair[0].EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || pathPair[0].EndsWith(".nif", StringComparison.OrdinalIgnoreCase)))
                {
                    if (pathPair[0].EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                    {
                        newSource = newSource.Replace("Skyrim.esm", "Skyrim.esm\\Textures", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (pathPair[0].EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
                    {
                        newSource = newSource.Replace("Skyrim.esm", "Skyrim.esm\\Meshes", StringComparison.OrdinalIgnoreCase);
                    }
                }

                string newDest = "";
                if (zEBDTexturePathConversionDict.ContainsKey(pathPair[1].ToLower()))
                {
                    newDest = zEBDTexturePathConversionDict[pathPair[1].ToLower()];
                }
                else
                {
                    conversionErrors.Add("Subgroup: " + id + ": The destination path " + pathPair[1] + " was not recognized as a default path, so it could not be converted to SynthEBD format. Please upgrade it manually.");
                }
                s.Paths.Add(new FilePathReplacement { Source = newSource, Destination = newDest });
            }

            s.WeightRange = Converters.StringArrayToWeightRange(weightRange);

            foreach (string id in allowedRaces)
            {
                bool continueSearch = true;
                // first see if it belongs to a RaceGrouping
                foreach (var group in raceGroupings)
                {
                    if (group.Label == id)
                    {
                        s.AllowedRaceGroupings.Add(group.Label);
                        continueSearch = false;
                        break;
                    }
                    else if (zEBDtoSynthEBDRaceGroupingNames.ContainsKey(id))
                    {
                        s.AllowedRaceGroupings.Add(zEBDtoSynthEBDRaceGroupingNames[id]);
                        continueSearch = false;
                        break;
                    }
                }

                // if not, see if it is a race EditorID
                if (continueSearch == true)
                {
                    FormKey raceFormKey = Converters.RaceEDID2FormKey(id, environmentProvider);
                    if (raceFormKey.IsNull == false)
                    {
                        s.AllowedRaces.Add(raceFormKey);
                    }
                }
            }

            foreach (string id in disallowedRaces)
            {
                bool continueSearch = true;
                // first see if it belongs to a RaceGrouping
                foreach (var group in raceGroupings)
                {
                    if (group.Label == id)
                    {
                        s.DisallowedRaceGroupings.Add(group.Label);
                        continueSearch = false;
                        break;
                    }
                    else if (zEBDtoSynthEBDRaceGroupingNames.ContainsKey(id))
                    {
                        s.DisallowedRaceGroupings.Add(zEBDtoSynthEBDRaceGroupingNames[id]);
                        continueSearch = false;
                        break;
                    }
                }

                // if not, see if it is a race EditorID
                if (continueSearch == true)
                {
                    FormKey raceFormKey = Converters.RaceEDID2FormKey(id, environmentProvider);
                    if (raceFormKey.IsNull == false)
                    {
                        s.DisallowedRaces.Add(raceFormKey);
                    }
                }
            }

            foreach (string str in allowedBodyGenDescriptors)
            {
                if (BodyShapeDescriptor.LabelSignature.FromString(str, out BodyShapeDescriptor.LabelSignature allowedDescriptor, logger))
                {
                    s.AllowedBodyGenDescriptors.Add(allowedDescriptor);
                }
            }
            foreach (string str in disallowedBodyGenDescriptors)
            {
                if (BodyShapeDescriptor.LabelSignature.FromString(str, out BodyShapeDescriptor.LabelSignature disallowedDescriptor, logger))
                {
                    s.DisallowedBodyGenDescriptors.Add(disallowedDescriptor);
                }
            }

            if (topLevelSubgroupID == "")
            {
                s.TopLevelSubgroupID = s.ID; // this is the top level subgroup
            }
            else
            {
                s.TopLevelSubgroupID = topLevelSubgroupID;
            }

            foreach (var sg in subgroups)
            {
                s.Subgroups.Add(sg.ToSynthEBDSubgroup(raceGroupings, s.TopLevelSubgroupID, conversionErrors, environmentProvider, logger, converters));
            }

            return s;
        }

        /// <summary>Maps legacy zEBD built-in race-grouping keys to their SynthEBD race-grouping labels.</summary>
        public static Dictionary<string, string> zEBDtoSynthEBDRaceGroupingNames = new Dictionary<string, string>()
        {
            {"humanoid", "Humanoid" },
            {"humanoidStandard", "Humanoid Playable" },
            {"humanoid_NonVamp", "Humanoid Non-Vampire" },
            {"humanoidVampire", "Humanoid Vampire" },
            {"humanoidYoung", "Humanoid Young" },
            {"humanoidYoungNonVampire", "Humanoid Young Non-Vampire" },
            {"humanoidYoungVampire", "Humanoid Young Vampire" },
            {"elven", "Elven" },
            {"elvenNonVampire", "Elven Non-Vampire" },
            {"elvenVampire", "Elven Vampire" },
            {"elder", "Elder" },
            {"khajiit", "Khajiit" },
            {"argonian", "Argonian" }
        };
    }

    /// <summary>
    /// Converts this legacy zEBD asset pack into a SynthEBD <see cref="AssetPack"/>: converting every subgroup,
    /// wiring up the default/beast-race record templates, and — if any subgroup references BodyGen — prompting
    /// the user (via a dialog) to link an available BodyGen config. Conversion errors are written to a
    /// per-config log and surfaced in a message box.
    /// </summary>
    /// <returns>The converted asset pack.</returns>
    public AssetPack ToSynthEBDAssetPack(List<RaceGrouping> raceGroupings, List<SkyrimMod> recordTemplatePlugins, BodyGenConfigs availableBodyGenConfigs, IEnvironmentStateProvider environmentProvider, Converters converters, Logger logger, SynthEBDPaths paths)
    {
        List<string> conversionErrors = new List<string>();
        AssetPack s = new AssetPack();
        s.GroupName = groupName;
        s.Gender = gender;
        s.DisplayAlerts = displayAlerts;
        s.UserAlert = userAlert;
        foreach (ZEBDSubgroup sg in subgroups)
        {
            s.Subgroups.Add(sg.ToSynthEBDSubgroup(raceGroupings, "", conversionErrors, environmentProvider, logger, converters));
        }

        // Apply default record templates
        FormKey KhajiitRaceFK;
        FormKey KhajiitRaceVampireFK;
        FormKey ArgonianRaceFK;
        FormKey ArgonianRaceVampireFK;
        FormKey.TryFactory("013745:Skyrim.esm", out KhajiitRaceFK);
        FormKey.TryFactory("088845:Skyrim.esm", out KhajiitRaceVampireFK);
        FormKey.TryFactory("013740:Skyrim.esm", out ArgonianRaceFK);
        FormKey.TryFactory("08883A:Skyrim.esm", out ArgonianRaceVampireFK);

        foreach (var plugin in recordTemplatePlugins)
        {
            if (plugin.ModKey.Name == "Record Templates")
            {
                switch (s.Gender)
                {
                    case Gender.Female:
                        s.DefaultRecordTemplate = GetNPCByEDID(plugin, "DefaultFemale");
                        s.AdditionalRecordTemplateAssignments.Add(new AdditionalRecordTemplate { 
                            TemplateNPC = GetNPCByEDID(plugin, "KhajiitFemale"), 
                            Races = new HashSet<FormKey> { KhajiitRaceFK, KhajiitRaceVampireFK },
                            AdditionalRacesPaths = AdditionalRacesPathsBeastRaces
                        });
                        s.AdditionalRecordTemplateAssignments.Add(new AdditionalRecordTemplate { 
                            TemplateNPC = GetNPCByEDID(plugin, "ArgonianFemale"), 
                            Races = new HashSet<FormKey> { ArgonianRaceFK, ArgonianRaceVampireFK },
                            AdditionalRacesPaths = AdditionalRacesPathsBeastRaces
                        });
                        break;

                    case Gender.Male:
                        s.DefaultRecordTemplate = GetNPCByEDID(plugin, "DefaultMale");
                        s.AdditionalRecordTemplateAssignments.Add(new AdditionalRecordTemplate { 
                            TemplateNPC = GetNPCByEDID(plugin, "KhajiitMale"), 
                            Races = new HashSet<FormKey> { KhajiitRaceFK, KhajiitRaceVampireFK },
                            AdditionalRacesPaths = AdditionalRacesPathsBeastRaces
                        });
                        s.AdditionalRecordTemplateAssignments.Add(new AdditionalRecordTemplate { 
                            TemplateNPC = GetNPCByEDID(plugin, "ArgonianMale"), 
                            Races = new HashSet<FormKey> { ArgonianRaceFK, ArgonianRaceVampireFK },
                            AdditionalRacesPaths = AdditionalRacesPathsBeastRaces
                        });
                        break;
                }

            }
        }

        // add new AdditionalRacesPaths - zEBD accomplished this in a way that is impractical w/ Mutagen due to strong typing
        s.DefaultRecordTemplateAdditionalRacesPaths = new HashSet<string>()
        {
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].AdditionalRaces",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].AdditionalRaces",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].AdditionalRaces",
        };

        bool hasBodyGen = false;
        foreach (var subgroup in subgroups)
        {
            hasBodyGen = zEBDConfigReferencesBodyGen(subgroup);
            if (hasBodyGen) { break; }
        }
        if (hasBodyGen)
        {
            var linkBodyGenWindow = new Window_LinkZEBDAssetPackToBodyGen();
            var linkBodyGen = new VM_LinkZEBDAssetPackToBodyGen(availableBodyGenConfigs, s.Gender, s.GroupName, linkBodyGenWindow);
            linkBodyGenWindow.DataContext = linkBodyGen;
            linkBodyGenWindow.ShowDialog();
            if (linkBodyGen.SelectedConfig != null)
            {
                s.AssociatedBodyGenConfigName = linkBodyGen.SelectedConfig.Label;
            }
        }

        if (conversionErrors.Any())
        {
            string logFile = string.Join("_", groupName.Split(System.IO.Path.GetInvalidFileNameChars())) + ".txt";
            string logPath = System.IO.Path.Combine(paths.LogFolderPath, logFile);
            Task.Run(() => PatcherIO.WriteTextFile(logPath, conversionErrors, logger));
            logger.LogMessage(conversionErrors);
            MessageWindow.DisplayNotificationOK("Import Error", "Errors were encountered during upgrade of a zEBD Config File. Please see log at " + logPath + ".");
        }

        return s;
    }

    /// <summary>Record paths used to add beast-race "additional races" entries to converted record templates.</summary>
    private static HashSet<string> AdditionalRacesPathsBeastRaces = new HashSet<string>(){
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].AdditionalRaces",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].AdditionalRaces",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].AdditionalRaces",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].AdditionalRaces"
    };

    /// <summary>Finds the FormKey of the NPC with the given EditorID in a plugin (or a null FormKey if absent).</summary>
    private static FormKey GetNPCByEDID(SkyrimMod plugin, string edid)
    {
        foreach (var npc in plugin.Npcs)
        {
            if (npc.EditorID != null && npc.EditorID == edid)
            {
                return npc.FormKey;
            }
        }
        return new FormKey();
    }

    /// <summary>Returns true if the subgroup (or any descendant) references a BodyGen descriptor, indicating the legacy config used BodyGen.</summary>
    private static bool zEBDConfigReferencesBodyGen(ZEBDSubgroup subgroup)
    {
        if (subgroup.allowedBodyGenDescriptors.Count > 0) { return true; }
        if (subgroup.disallowedBodyGenDescriptors.Count > 0) { return true; }
        foreach (var subSubgroup in subgroup.subgroups)
        {
            if (zEBDConfigReferencesBodyGen(subSubgroup)) { return true; }
        }
        return false;
    }
        
    /// <summary>Maps legacy zEBD destination texture filenames to their SynthEBD record-path equivalents, used by <see cref="ZEBDSubgroup.ToSynthEBDSubgroup"/>.</summary>
    private static Dictionary<string, string> zEBDTexturePathConversionDict = new Dictionary<string, string>()
    {            
        // common
        {"blankdetailmap.dds", "HeadTexture.Height.GivenPath"},
        {"malebody_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.GivenPath"},
        {"malebody_1_tail_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.GivenPath"}, // common male tail sk
        {"malehands_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.GivenPath"},
        {"malebody_1_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.GivenPath"},
        {"femalebody_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.GivenPath"},
        {"femalehands_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.GivenPath"},
        {"femalebody_1_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.GivenPath"},
        // male humanoid
        {"malehead.dds", "HeadTexture.Diffuse.GivenPath" },
        {"malehead_msn.dds", "HeadTexture.NormalOrGloss.GivenPath"},
        {"malehead_sk.dds", "HeadTexture.GlowOrDetailMap.GivenPath"},
        {"malehead_s.dds", "HeadTexture.BacklightMaskOrSpecular.GivenPath"},
        {"malebody_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath"},
        {"malebody_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath"},
        {"malebody_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath"},
        {"malehands_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath"},
        {"malehands_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath"},
        {"malehands_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath"},
        {"malebody_1_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath"},
        {"malebody_1_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath"},
        {"malebody_1_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath"},
        // female humanoid
        {"femalehead.dds", "HeadTexture.Diffuse.GivenPath" },
        {"femalehead_msn.dds", "HeadTexture.NormalOrGloss.GivenPath"},
        {"femalehead_sk.dds", "HeadTexture.GlowOrDetailMap.GivenPath"},
        {"femalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular.GivenPath"},
        {"femalebody_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath"},
        {"femalebody_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.GivenPath"},
        {"femalebody_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.GivenPath"},
        {"femalehands_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath"},
        {"femalehands_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.GivenPath"},
        {"femalehands_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.GivenPath"},
        {"femalebody_1_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath"},
        {"femalebody_1_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.GivenPath"},
        {"femalebody_1_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.GivenPath"},
        // male argonian
        {"argonianmalehead.dds", "HeadTexture.Diffuse.GivenPath" },
        {"argonianmalehead_msn.dds", "HeadTexture.NormalOrGloss.GivenPath"},
        {"argonianmalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular.GivenPath"},
        {"argonianmalebody.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath"},
        {"argonianmalebody_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath"},
        {"argonianmalebody_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath"},
        {"argonianmalehands.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath"},
        {"argonianmalehands_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath"},
        {"argonianmalehands_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath"},
        {"argonianmalebody_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath"},
        {"argonianmalebody_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath"},
        {"argonianmalebody_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath"},
        {"argonianmalebody_tail.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath"},
        {"argonianmalebody_tail_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath"},
        {"argonianmalebody_tail_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath"},
        // female argonian
        {"argonianfemalehead.dds", "HeadTexture.Diffuse.GivenPath" },
        {"argonianfemalehead_msn.dds", "HeadTexture.NormalOrGloss.GivenPath"},
        {"argonianfemalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular.GivenPath"},
        {"argonianfemalebody.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath"},
        {"argonianfemalebody_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.GivenPath"},
        {"argonianfemalebody_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.GivenPath"},
        {"argonianfemalehands.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath"},
        {"argonianfemalehands_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.GivenPath"},
        {"argonianfemalehands_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.GivenPath"},
        {"argonianfemalebody_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath"},
        {"argonianfemalebody_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.GivenPath"},
        {"argonianfemalebody_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.GivenPath"},
        {"argonianfemalebody_tail.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath"},
        {"argonianfemalebody_tail_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.GivenPath"},
        {"argonianfemalebody_tail_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.GivenPath"},
        // male khajiit
        {"khajiitmalehead.dds", "HeadTexture.Diffuse.GivenPath" },
        {"khajiitmalehead_msn.dds", "HeadTexture.NormalOrGloss.GivenPath"},
        {"khajiitmalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular.GivenPath"},
        {"bodymale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath"},
        {"bodymale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath"},
        {"bodymale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath"},
        {"handsmale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath"},
        {"handsmale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath"},
        {"handsmale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath"},
        {"bodymale_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath"},
        {"bodymale_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath"},
        {"bodymale_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath"},
        {"bodymale_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.GivenPath"},
        {"bodymale_tail.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath"},
        {"bodymale_tail_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath"},
        {"bodymale_tail_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath"},
        {"bodymale_tail_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.GivenPath"},
        // female khajiit
        {"khajiitfemalehead.dds", "HeadTexture.Diffuse.GivenPath" },
        {"khajiitfemalehead_msn.dds", "HeadTexture.NormalOrGloss.GivenPath"},
        {"khajiitfemalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular.GivenPath"},
        {"bodyfemale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath"},
        {"bodyfemale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.GivenPath"},
        {"bodyfemale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.GivenPath"},
        {"bodyfemale_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.GivenPath"},
        {"handsfemale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath"},
        {"handsfemale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.GivenPath"},
        {"handsfemale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.GivenPath"},
        {"bodyfemale_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath"},
        {"bodyfemale_feet_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.GivenPath"},
        {"bodyfemale_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.GivenPath"},
        {"bodyfemale_tail.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath"},
        {"bodyfemale_tail_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.GivenPath"},
        {"bodyfemale_tail_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.GivenPath"},
        {"bodyfemale_tail_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.GivenPath"}
    };
}