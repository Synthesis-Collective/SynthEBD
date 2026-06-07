using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using SynthEBD;

namespace SynthEBD.Tests.Integration;

/// <summary>
/// Fluent builder for small, in-memory <see cref="AssetPack"/>s used to exercise the asset distribution
/// engine. Each scenario pack is a Primary config with a single top-level position whose leaf subgroups
/// carry exactly the rules under test (allowed/disallowed races, groupings, attributes, weighting, weight
/// range, distribution flags), plus a unique texture <c>Source</c> path per leaf so assignments can be
/// traced. Optional parent-position rules let tests verify rule inheritance from a parent subgroup down to
/// its leaves.
///
/// <para>Building model objects directly (rather than JSON on disk) is faithful because
/// <c>RunPatcher</c> deep-clones every asset pack via <c>JSONhandler&lt;AssetPack&gt;</c> and flattens it
/// before use — the same path a disk-loaded pack takes.</para>
/// </summary>
public static class AssetScenario
{
    /// <summary>Default record template for a female CBBE skin (a template NPC in Record Templates.esp).</summary>
    public static readonly FormKey FemaleRecordTemplate = FormKey.Factory("000801:Record Templates.esp");
    /// <summary>Default record template for a male skin (The New Gentleman template NPC).</summary>
    public static readonly FormKey MaleRecordTemplate = FormKey.Factory("000800:Record Templates - The New Gentleman.esp");

    /// <summary>A female-body skin diffuse destination at the body armature (record-based in every face mode).</summary>
    public const string FemaleBodyDiffuseDestination =
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath";
    /// <summary>A male-body skin diffuse destination at the body armature.</summary>
    public const string MaleBodyDiffuseDestination =
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath";
    /// <summary>The head-texture (FaceGen) diffuse destination, written to the NPC HeadTexture record in NifEdit mode.</summary>
    public const string HeadDiffuseDestination = "HeadTexture.Diffuse.GivenPath";

    /// <summary>Describes one leaf subgroup and the rules under test.</summary>
    public sealed class Leaf
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        /// <summary>Unique texture source path, so the assignment can be traced to a record.</summary>
        public string Source { get; init; } = "";
        public string Destination { get; init; } = HeadDiffuseDestination;
        public HashSet<FormKey> AllowedRaces { get; init; } = new();
        public HashSet<FormKey> DisallowedRaces { get; init; } = new();
        public HashSet<string> AllowedRaceGroupings { get; init; } = new();
        public HashSet<string> DisallowedRaceGroupings { get; init; } = new();
        public HashSet<NPCAttribute> AllowedAttributes { get; init; } = new();
        public HashSet<NPCAttribute> DisallowedAttributes { get; init; } = new();
        public double ProbabilityWeighting { get; init; } = 1;
        public List<AttributeWeightModifier> ProbabilityWeightModifiers { get; init; } = new();
        public NPCWeightRange? WeightRange { get; init; }
        public bool DistributionEnabled { get; init; } = true;
        public bool AllowUnique { get; init; } = true;
        public bool AllowNonUnique { get; init; } = true;
    }

    /// <summary>
    /// Builds a one-position Primary asset pack from the given leaves. When <paramref name="parentRules"/>
    /// is supplied it is applied to the top-level position subgroup (whose own paths are empty) so the
    /// leaves inherit those rules — used by the rule-inheritance tests.
    /// </summary>
    public static AssetPack BuildPack(string groupName, Gender gender, IEnumerable<Leaf> leaves,
        Leaf? parentRules = null, string positionId = "P")
    {
        var leafSubgroups = leaves.Select(ToSubgroup).ToList();

        var position = parentRules != null ? ToSubgroup(parentRules) : new AssetPack.Subgroup();
        position.ID = positionId;
        position.Name = positionId;
        position.Paths = new HashSet<FilePathReplacement>(); // a position holds no paths of its own
        position.Subgroups = leafSubgroups;

        return new AssetPack
        {
            GroupName = groupName,
            ShortName = groupName,
            ConfigType = AssetPackType.Primary,
            Gender = gender,
            DisplayAlerts = false,
            DefaultRecordTemplate = gender == Gender.Female ? FemaleRecordTemplate : MaleRecordTemplate,
            Subgroups = new List<AssetPack.Subgroup> { position },
        };
    }

    /// <summary>
    /// Builds the same set of leaves as both a Male and a Female pack, so a scenario covers NPCs of both
    /// genders in one run. Leaf ids are identical across the two packs; combination-log queries match by id
    /// across all configs, so assignments aggregate naturally.
    /// </summary>
    public static IReadOnlyList<AssetPack> BuildBothGenders(string baseName, IEnumerable<Leaf> leaves,
        Leaf? parentRules = null)
    {
        var leafList = leaves.ToList();
        return new[]
        {
            BuildPack(baseName + " (F)", Gender.Female, leafList, parentRules),
            BuildPack(baseName + " (M)", Gender.Male, leafList, parentRules),
        };
    }

    private static AssetPack.Subgroup ToSubgroup(Leaf leaf)
    {
        var sg = new AssetPack.Subgroup
        {
            ID = leaf.Id,
            Name = string.IsNullOrEmpty(leaf.Name) ? leaf.Id : leaf.Name,
            Enabled = true,
            DistributionEnabled = leaf.DistributionEnabled,
            AllowUnique = leaf.AllowUnique,
            AllowNonUnique = leaf.AllowNonUnique,
            ProbabilityWeighting = leaf.ProbabilityWeighting,
            ProbabilityWeightModifiers = leaf.ProbabilityWeightModifiers,
            AllowedRaces = leaf.AllowedRaces,
            DisallowedRaces = leaf.DisallowedRaces,
            AllowedRaceGroupings = leaf.AllowedRaceGroupings,
            DisallowedRaceGroupings = leaf.DisallowedRaceGroupings,
            AllowedAttributes = leaf.AllowedAttributes,
            DisallowedAttributes = leaf.DisallowedAttributes,
            Paths = string.IsNullOrEmpty(leaf.Source)
                ? new HashSet<FilePathReplacement>()
                : new HashSet<FilePathReplacement> { new() { Source = leaf.Source, Destination = leaf.Destination } },
        };
        if (leaf.WeightRange != null) { sg.WeightRange = leaf.WeightRange; }
        return sg;
    }
}
