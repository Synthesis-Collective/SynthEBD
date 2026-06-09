using Mutagen.Bethesda.Plugins;
using System.Diagnostics;

namespace SynthEBD;

/// <summary>
/// Runtime (flattened) counterpart of the <see cref="AssetPack"/> settings model that the asset selector walks
/// during patching. Flattening collapses each subgroup tree into lists of bottom-level <see cref="FlattenedSubgroup"/>s
/// (inheriting parent rules and resolving race groupings to form keys), flattens the replacer groups, and lifts the
/// pack-wide distribution rules into a virtual <see cref="DistributionRules"/> subgroup.
/// </summary>
[DebuggerDisplay("{GroupName}")]
public class FlattenedAssetPack
{
    /// <summary>Mapper used to translate subgroup references and body-shape descriptors into id/label dictionaries.</summary>
    public readonly DictionaryMapper _dictionaryMapper;
    private readonly PatcherState _patcherState;
    /// <summary>Creates a flattened pack from a source <see cref="AssetPack"/>, copying its metadata and building the virtual distribution-rules subgroup.</summary>
    public FlattenedAssetPack(AssetPack source, AssetPackType type, DictionaryMapper dictionaryMapper, PatcherState patcherState)
    {
        _dictionaryMapper = dictionaryMapper;
        _patcherState = patcherState;

        GroupName = source.GroupName;
        Gender = source.Gender;
        DefaultRecordTemplate = source.DefaultRecordTemplate;
        AdditionalRecordTemplateAssignments = source.AdditionalRecordTemplateAssignments;
        AssociatedBodyGenConfigName = source.AssociatedBodyGenConfigName;
        Source = source;
        Type = type;

        var configRulesSubgroup = AssetPack.ConfigDistributionRules.CreateInheritanceParent(source.DistributionRules);
        DistributionRules = new FlattenedSubgroup(configRulesSubgroup, GetRaceGroupings(), new List<AssetPack.Subgroup>(), this, _dictionaryMapper);
    }

    /// <summary>Creates a flattened pack from explicit field values (used by <see cref="ShallowCopy"/>) and builds the virtual distribution-rules subgroup.</summary>
    public FlattenedAssetPack(string groupName, Gender gender, FormKey defaultRecordTemplate, HashSet<AdditionalRecordTemplate> additionalRecordTemplateAssignments, string associatedBodyGenConfigName, AssetPack source, AssetPackType type, DictionaryMapper dictionaryMapper, PatcherState patcherState)
    {
        _dictionaryMapper = dictionaryMapper;
        _patcherState = patcherState;

        GroupName = groupName;
        Gender = gender;
        DefaultRecordTemplate = defaultRecordTemplate;
        AdditionalRecordTemplateAssignments = additionalRecordTemplateAssignments;
        AssociatedBodyGenConfigName = associatedBodyGenConfigName;
        Source = source;
        Type = type;

        var configRulesSubgroup = AssetPack.ConfigDistributionRules.CreateInheritanceParent(source.DistributionRules);
        DistributionRules = new FlattenedSubgroup(configRulesSubgroup, GetRaceGroupings(), new List<AssetPack.Subgroup>(), this, _dictionaryMapper);
    }

    /// <summary>Creates an empty flattened pack of the given type with default/blank metadata (used when building a virtual pack from a replacer group).</summary>
    public FlattenedAssetPack(AssetPackType type, DictionaryMapper dictionaryMapper, PatcherState patcherState)
    {
        _dictionaryMapper = dictionaryMapper;
        _patcherState = patcherState;

        GroupName = "";
        Gender = Gender.Male;
        DefaultRecordTemplate = new FormKey();
        AdditionalRecordTemplateAssignments = new HashSet<AdditionalRecordTemplate>();
        AssociatedBodyGenConfigName = "";
        Source = new AssetPack();
        Type = type;
        DistributionRules = new FlattenedSubgroup(new AssetPack.Subgroup(), GetRaceGroupings(), new List<AssetPack.Subgroup>(), this, _dictionaryMapper);
    }

    /// <summary>The asset pack's name.</summary>
    public string GroupName { get; set; }
    /// <summary>The gender this pack applies to.</summary>
    public Gender Gender { get; set; }
    /// <summary>Per top-level position, the flattened bottom-level subgroups available for selection.</summary>
    public List<List<FlattenedSubgroup>> Subgroups { get; set; } = new();
    /// <summary>Form key of the default record template used to seed generated records.</summary>
    public FormKey DefaultRecordTemplate { get; set; }
    /// <summary>Additional per-condition record template overrides.</summary>
    public HashSet<AdditionalRecordTemplate> AdditionalRecordTemplateAssignments { get; set; }
    /// <summary>Name of the BodyGen config associated with this pack, if any.</summary>
    public string AssociatedBodyGenConfigName { get; set; }
    /// <summary>The settings-model asset pack this flattened pack was built from.</summary>
    public AssetPack Source { get; set; }
    /// <summary>The pack's flattened replacer groups.</summary>
    public List<FlattenedReplacerGroup> AssetReplacerGroups { get; set; } = new();
    /// <summary>Whether this pack is a primary, mix-in, or replacer-virtual pack.</summary>
    public AssetPackType Type { get; set; }
    /// <summary>Replacer name; only used when <see cref="Type"/> is <see cref="AssetPackType.ReplacerVirtual"/>.</summary>
    public string ReplacerName { get; set; } = ""; // only used when Type == ReplacerVirtual
    /// <summary>Count of whole-config ForceIf attributes matched for the current NPC (used in selection scoring).</summary>
    public int MatchedWholeConfigForceIfs { get; set; } = 0;
    /// <summary>Virtual subgroup carrying the pack-wide distribution rules, inherited by every real subgroup during flattening.</summary>
    public FlattenedSubgroup DistributionRules { get; set; } // "virtual" subgroup
    /// <summary>Running count of how many times this pack has been assigned (for logging).</summary>
    public int AssignmentCount { get; set; } = 0; // for logging

    /// <summary>Categorizes a flattened asset pack by its role in the patcher.</summary>
    public enum AssetPackType
    {
        /// <summary>A standard primary asset pack.</summary>
        Primary,
        /// <summary>A mix-in pack layered on top of a primary assignment.</summary>
        MixIn,
        /// <summary>A synthetic pack wrapping a replacer group (see <see cref="CreateVirtualFromReplacerGroup"/>).</summary>
        ReplacerVirtual
    }

    /// <summary>
    /// Flattens a settings-model <see cref="AssetPack"/> into its runtime form: chooses Primary vs MixIn type,
    /// flattens each top-level subgroup tree into bottom-level <see cref="FlattenedSubgroup"/>s, and flattens the replacer groups.
    /// </summary>
    /// <param name="source">The asset pack to flatten.</param>
    /// <param name="dictionaryMapper">Mapper for subgroup/descriptor id resolution.</param>
    /// <param name="patcherState">Current patcher state (general settings, race groupings).</param>
    public static FlattenedAssetPack FlattenAssetPack(AssetPack source, DictionaryMapper dictionaryMapper, PatcherState patcherState)
    {
        FlattenedAssetPack output = null;
        if (source.ConfigType == SynthEBD.AssetPackType.MixIn)
        {
            output = new FlattenedAssetPack(source, AssetPackType.MixIn, dictionaryMapper, patcherState);
        }
        else
        {
            output = new FlattenedAssetPack(source, AssetPackType.Primary, dictionaryMapper, patcherState);
        }

        // B48: flatten subgroups/replacers against the pack's full effective race groupings (General union the config's
        // local RaceGroupings, toggle-resolved) -- the SAME set the whole-config DistributionRules use -- so a subgroup or
        // replacer rule referencing a grouping the user lacks in General still falls back to the config's local definition
        // (matching the config-level path). Identical to GeneralSettings.RaceGroupings when the config has no local groupings.
        var effectiveRaceGroupings = output.GetRaceGroupings();
        for (int i = 0; i < source.Subgroups.Count; i++)
        {
            var flattenedSubgroups = new List<FlattenedSubgroup>();
            FlattenedSubgroup.FlattenSubgroups(source.Subgroups[i], null, flattenedSubgroups, effectiveRaceGroupings, output.GroupName, i,  source.Subgroups, output, dictionaryMapper, patcherState);
            output.Subgroups.Add(flattenedSubgroups);
        }


        for (int i = 0; i < source.ReplacerGroups.Count; i++)
        {
            output.AssetReplacerGroups.Add(FlattenedReplacerGroup.FlattenReplacerGroup(source.ReplacerGroups[i], effectiveRaceGroupings, output, dictionaryMapper, patcherState));
        }

        return output;
    }

    /// <summary>Returns a shallow copy: a new pack with copied metadata and fresh subgroup/replacer lists holding the same (shallow-copied) child references.</summary>
    public FlattenedAssetPack ShallowCopy()
    {
        FlattenedAssetPack copy = new FlattenedAssetPack(GroupName, Gender, DefaultRecordTemplate, AdditionalRecordTemplateAssignments, AssociatedBodyGenConfigName, Source, Type, _dictionaryMapper, _patcherState);
        foreach (var subgroupList in Subgroups)
        {
            copy.Subgroups.Add(new List<FlattenedSubgroup>(subgroupList));
        }
        foreach (var replacer in AssetReplacerGroups)
        {
            copy.AssetReplacerGroups.Add(replacer.ShallowCopy());
        }
        return copy;
    }

    /// <summary>
    /// Wraps a <see cref="FlattenedReplacerGroup"/> in a synthetic <see cref="AssetPackType.ReplacerVirtual"/> pack so the
    /// asset selector can treat replacer subgroups uniformly with normal asset packs.
    /// </summary>
    /// <param name="source">The flattened replacer group to wrap.</param>
    /// <param name="dictionaryMapper">Mapper for subgroup/descriptor id resolution.</param>
    /// <param name="patcherState">Current patcher state.</param>
    public static FlattenedAssetPack CreateVirtualFromReplacerGroup(FlattenedReplacerGroup source, DictionaryMapper dictionaryMapper, PatcherState patcherState)
    {
        FlattenedAssetPack virtualFAP = new FlattenedAssetPack(AssetPackType.ReplacerVirtual, dictionaryMapper, patcherState);
        virtualFAP.GroupName = source.Source.GroupName;
        virtualFAP.ReplacerName = source.Name;
        foreach (var subgroupsAtPos in source.Subgroups)
        {
            virtualFAP.Subgroups.Add(subgroupsAtPos);
        }
        virtualFAP.Source = source.Source;
        return virtualFAP;
    }

    /// <summary>
    /// Resolves the effective race groupings for this pack: when <c>OverwritePluginRaceGroups</c> is set, the
    /// pack's groupings whose labels match the main settings are replaced by the main-settings groupings; all
    /// remaining pack groupings are then appended unless their label is already present.
    /// </summary>
    private List<RaceGrouping> GetRaceGroupings()
    {
        var output = new List<RaceGrouping>();

        var mainGroupingLabels = _patcherState.GeneralSettings.RaceGroupings.Select(x => x.Label).ToArray();
        if(_patcherState.GeneralSettings.OverwritePluginRaceGroups)
        {
            var toOverwrite = new List<RaceGrouping>();
            foreach (var grouping in Source.RaceGroupings.Where(x => mainGroupingLabels.Contains(x.Label)).ToArray())
            {
                var overwriteGrouping = _patcherState.GeneralSettings.RaceGroupings.Where(x => x.Label == grouping.Label).First();
                output.Add(new RaceGrouping() { Label = overwriteGrouping.Label, Races = new(overwriteGrouping.Races) });
            }
        }

        foreach (var grouping in Source.RaceGroupings)
        {
            if (!output.Select(x => x.Label).Contains(grouping.Label))
            {
                output.Add(grouping);
            }
        }

        // Guard: a race grouping referenced by the whole-config distribution rules but absent from the config's own
        // RaceGroupings still resolves against the user's General settings (otherwise the config-level rule silently
        // matches nothing). This is realistically almost never reached -- configs auto-import the General groupings their
        // subgroups/replacers reference into the local set (VM_AssetPack.AddFallBackRaceGroupings), so the Source fallback
        // above normally already covers referenced groupings; this only catches a config-level-only label that is absent
        // locally yet present in the recipient's General settings (e.g. a config published referencing a stray grouping,
        // shared with a user who happens to define it). With this loop GetRaceGroupings returns the full effective set
        // (General union Source, with OverwritePluginRaceGroups deciding precedence on shared labels).
        foreach (var grouping in _patcherState.GeneralSettings.RaceGroupings)
        {
            if (!output.Select(x => x.Label).Contains(grouping.Label))
            {
                output.Add(grouping);
            }
        }
        return output;
    }

    /// <summary>
    /// Builds a display label for the top-level subgroup at the given position, formatted as "ID: Name"
    /// (optionally wrapped in parentheses), or an empty string if the position is out of range.
    /// </summary>
    /// <param name="index">Top-level subgroup position.</param>
    /// <param name="includeFormatting">When true, wraps the result in " (" and ")".</param>
    public string GetSubgroupPositionString(int index, bool includeFormatting = true)
    {
        return Source is null ? string.Empty : FormatSubgroupPosition(Source.Subgroups, index, includeFormatting);
    }

    /// <summary>
    /// Formats the subgroup at <paramref name="index"/> in <paramref name="subgroups"/> as "ID: Name"
    /// (optionally wrapped in " (" and ")"), or an empty string when <paramref name="index"/> is out of range
    /// or the entry is null. Pure helper extracted from <see cref="GetSubgroupPositionString"/> for testability.
    /// </summary>
    /// <param name="subgroups">Top-level subgroup list.</param>
    /// <param name="index">Position within <paramref name="subgroups"/>; the valid range is 0..Count-1.</param>
    /// <param name="includeFormatting">When true, wraps the result in " (" and ")".</param>
    public static string FormatSubgroupPosition(IReadOnlyList<AssetPack.Subgroup> subgroups, int index, bool includeFormatting = true)
    {
        if (subgroups is null || index < 0 || index >= subgroups.Count || subgroups[index] is null)
        {
            return string.Empty;
        }

        var sg = subgroups[index];
        var inner = sg.ID + ": " + sg.Name;
        return includeFormatting ? " (" + inner + ")" : inner;
    }
}