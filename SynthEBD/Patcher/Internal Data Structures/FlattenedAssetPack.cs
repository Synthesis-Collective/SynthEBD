using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

public class FlattenedAssetPack
{
    public readonly DictionaryMapper _dictionaryMapper;
    public FlattenedAssetPack(AssetPack source, AssetPackType type, DictionaryMapper dictionaryMapper)
    {
        _dictionaryMapper = dictionaryMapper;

        GroupName = source.GroupName;
        Gender = source.Gender;
        DefaultRecordTemplate = source.DefaultRecordTemplate;
        AdditionalRecordTemplateAssignments = source.AdditionalRecordTemplateAssignments;
        AssociatedBodyGenConfigName = source.AssociatedBodyGenConfigName;
        Source = source;
        Type = type;

        var configRulesSubgroup = AssetPack.ConfigDistributionRules.CreateInheritanceParent(source.DistributionRules);
        DistributionRules = new FlattenedSubgroup(configRulesSubgroup, source.RaceGroupings, new List<AssetPack.Subgroup>(), this, _dictionaryMapper);
    }

    public FlattenedAssetPack(string groupName, Gender gender, FormKey defaultRecordTemplate, HashSet<AdditionalRecordTemplate> additionalRecordTemplateAssignments, string associatedBodyGenConfigName, AssetPack source, AssetPackType type, DictionaryMapper dictionaryMapper)
    {
        _dictionaryMapper = dictionaryMapper;

        GroupName = groupName;
        Gender = gender;
        DefaultRecordTemplate = defaultRecordTemplate;
        AdditionalRecordTemplateAssignments = additionalRecordTemplateAssignments;
        AssociatedBodyGenConfigName = associatedBodyGenConfigName;
        Source = source;
        Type = type;

        var configRulesSubgroup = AssetPack.ConfigDistributionRules.CreateInheritanceParent(source.DistributionRules);
        DistributionRules = new FlattenedSubgroup(configRulesSubgroup, source.RaceGroupings, new List<AssetPack.Subgroup>(), this, _dictionaryMapper);
    }

    public FlattenedAssetPack(AssetPackType type, DictionaryMapper dictionaryMapper, List<RaceGrouping> sourceRaceGroupings)
    {
        _dictionaryMapper = dictionaryMapper;

        GroupName = "";
        Gender = Gender.Male;
        DefaultRecordTemplate = new FormKey();
        AdditionalRecordTemplateAssignments = new HashSet<AdditionalRecordTemplate>();
        AssociatedBodyGenConfigName = "";
        Source = new AssetPack();
        Type = type;
        DistributionRules = new FlattenedSubgroup(new AssetPack.Subgroup(), sourceRaceGroupings, new List<AssetPack.Subgroup>(), this, _dictionaryMapper);
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

    public static FlattenedAssetPack FlattenAssetPack(AssetPack source, DictionaryMapper dictionaryMapper)
    {
        FlattenedAssetPack output = null;
        if (source.ConfigType == SynthEBD.AssetPackType.MixIn)
        {
            output = new FlattenedAssetPack(source, AssetPackType.MixIn, dictionaryMapper);
        }
        else
        {
            output = new FlattenedAssetPack(source, AssetPackType.Primary, dictionaryMapper);
        }

        for (int i = 0; i < source.Subgroups.Count; i++)
        {
            var flattenedSubgroups = new List<FlattenedSubgroup>();
            FlattenedSubgroup.FlattenSubgroups(source.Subgroups[i], null, flattenedSubgroups, PatcherSettings.General.RaceGroupings, output.GroupName, i,  source.Subgroups, output, dictionaryMapper);
            output.Subgroups.Add(flattenedSubgroups);
        }


        for (int i = 0; i < source.ReplacerGroups.Count; i++)
        {
            output.AssetReplacerGroups.Add(FlattenedReplacerGroup.FlattenReplacerGroup(source.ReplacerGroups[i], PatcherSettings.General.RaceGroupings, output, dictionaryMapper));
        }

        return output;
    }

    public FlattenedAssetPack ShallowCopy()
    {
        FlattenedAssetPack copy = new FlattenedAssetPack(GroupName, Gender, DefaultRecordTemplate, AdditionalRecordTemplateAssignments, AssociatedBodyGenConfigName, Source, Type, _dictionaryMapper);
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

    public static FlattenedAssetPack CreateVirtualFromReplacerGroup(FlattenedReplacerGroup source, DictionaryMapper dictionaryMapper)
    {
        FlattenedAssetPack virtualFAP = new FlattenedAssetPack(AssetPackType.ReplacerVirtual, dictionaryMapper, source.Source.RaceGroupings);
        virtualFAP.GroupName = source.Source.GroupName;
        virtualFAP.ReplacerName = source.Name;
        foreach (var subgroupsAtPos in source.Subgroups)
        {
            virtualFAP.Subgroups.Add(subgroupsAtPos);
        }
        virtualFAP.Source = source.Source;
        return virtualFAP;
    }
}