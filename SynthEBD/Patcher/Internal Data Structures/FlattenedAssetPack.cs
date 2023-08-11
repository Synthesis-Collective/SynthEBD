using Mutagen.Bethesda.Plugins;
using System.Diagnostics;

namespace SynthEBD;

[DebuggerDisplay("{GroupName}")]
public class FlattenedAssetPack
{
    public readonly DictionaryMapper _dictionaryMapper;
    private readonly PatcherState _patcherState;
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

    public string GroupName { get; set; }
    public Gender Gender { get; set; }
    public List<List<FlattenedSubgroup>> Subgroups { get; set; } = new();
    public FormKey DefaultRecordTemplate { get; set; }
    public HashSet<AdditionalRecordTemplate> AdditionalRecordTemplateAssignments { get; set; }
    public string AssociatedBodyGenConfigName { get; set; }
    public AssetPack Source { get; set; }
    public List<FlattenedReplacerGroup> AssetReplacerGroups { get; set; } = new();
    public AssetPackType Type { get; set; }
    public string ReplacerName { get; set; } = ""; // only used when Type == ReplacerVirtual
    public int MatchedWholeConfigForceIfs { get; set; } = 0;
    public FlattenedSubgroup DistributionRules { get; set; } // "virtual" subgroup

    public enum AssetPackType
    {
        Primary,
        MixIn,
        ReplacerVirtual
    }

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

        for (int i = 0; i < source.Subgroups.Count; i++)
        {
            var flattenedSubgroups = new List<FlattenedSubgroup>();
            FlattenedSubgroup.FlattenSubgroups(source.Subgroups[i], null, flattenedSubgroups, patcherState.GeneralSettings.RaceGroupings, output.GroupName, i,  source.Subgroups, output, dictionaryMapper, patcherState);
            output.Subgroups.Add(flattenedSubgroups);
        }


        for (int i = 0; i < source.ReplacerGroups.Count; i++)
        {
            output.AssetReplacerGroups.Add(FlattenedReplacerGroup.FlattenReplacerGroup(source.ReplacerGroups[i], patcherState.GeneralSettings.RaceGroupings, output, dictionaryMapper, patcherState));
        }

        return output;
    }

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
        return output;
    }
}