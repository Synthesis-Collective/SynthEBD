using Mutagen.Bethesda.Plugins;
using System.Diagnostics;
using static SynthEBD.AssetPack;

namespace SynthEBD;

[DebuggerDisplay("Name = {Id}: {Name}")]
public class FlattenedSubgroup : IProbabilityWeighted
{
    private readonly DictionaryMapper _dictionaryMapper;
    public FlattenedSubgroup(Subgroup template, List<RaceGrouping> raceGroupingList, List<Subgroup> subgroupHierarchy, FlattenedAssetPack parent, DictionaryMapper dictionaryMapper)
    {
        _dictionaryMapper = dictionaryMapper;

        Id = template.ID;
        Name = template.Name;
        DistributionEnabled = template.DistributionEnabled;
        AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.AllowedRaceGroupings, raceGroupingList, template.AllowedRaces);
        if (AllowedRaces.Count == 0) { AllowedRacesIsEmpty = true; }
        else { AllowedRacesIsEmpty = false; }
        DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.DisallowedRaceGroupings, raceGroupingList, template.DisallowedRaces);
        AllowedRaces = AllowedDisallowedCombiners.TrimDisallowedRacesFromAllowed(AllowedRaces, DisallowedRaces);
        AllowedAttributes = new HashSet<NPCAttribute>(template.AllowedAttributes);
        DisallowedAttributes = new HashSet<NPCAttribute>(template.DisallowedAttributes);
        AllowUnique = template.AllowUnique;
        AllowNonUnique = template.AllowNonUnique;
        RequiredSubgroupIDs = _dictionaryMapper.RequiredOrExcludedSubgroupsToDictionary(template.RequiredSubgroups, subgroupHierarchy);
        ExcludedSubgroupIDs = _dictionaryMapper.RequiredOrExcludedSubgroupsToDictionary(template.ExcludedSubgroups, subgroupHierarchy);
        AddKeywords = new HashSet<string>(template.AddKeywords);
        ProbabilityWeighting = template.ProbabilityWeighting;
        Paths = new HashSet<FilePathReplacement>(template.Paths);
        AllowedBodyGenDescriptors = DictionaryMapper.BodyShapeDescriptorsToDictionary(template.AllowedBodyGenDescriptors);
        AllowedBodyGenMatchMode = template.AllowedBodyGenMatchMode;
        DisallowedBodyGenDescriptors = DictionaryMapper.BodyShapeDescriptorsToDictionary(template.DisallowedBodyGenDescriptors);
        DisallowedBodyGenMatchMode = template.DisallowedBodyGenMatchMode;
        AllowedBodySlideDescriptors = DictionaryMapper.BodyShapeDescriptorsToDictionary(template.AllowedBodySlideDescriptors);
        AllowedBodySlideMatchMode = template.AllowedBodySlideMatchMode;
        DisallowedBodySlideDescriptors = DictionaryMapper.BodyShapeDescriptorsToDictionary(template.DisallowedBodySlideDescriptors);
        DisallowedBodySlideMatchMode = template.DisallowedBodySlideMatchMode;
        WeightRange = template.WeightRange.Clone();
        ContainedSubgroupIDs = new List<string> { Id };
        ContainedSubgroupNames = new List<string> { Name };
        ParentAssetPack = parent;
    }
    public string Id { get; set; }
    public string Name { get; set; }
    public bool DistributionEnabled { get; set; }
    public HashSet<FormKey> AllowedRaces { get; set; }
    public HashSet<FormKey> DisallowedRaces { get; set; }
    public bool AllowedRacesIsEmpty { get; set; } // distinguishes between initially empty (All races valid) vs. empty after pruning of Disallowed Races (subgroup is invalid)
    public HashSet<NPCAttribute> AllowedAttributes { get; set; }
    public HashSet<NPCAttribute> DisallowedAttributes { get; set; }
    public bool AllowUnique { get; set; }
    public bool AllowNonUnique { get; set; }
    public Dictionary<int, HashSet<string>> RequiredSubgroupIDs { get; set; }
    public Dictionary<int, HashSet<string>> ExcludedSubgroupIDs { get; set; }
    public HashSet<string> AddKeywords { get; set; }
    public double ProbabilityWeighting { get; set; }
    public HashSet<FilePathReplacement> Paths { get; set; }
    public Dictionary<string, HashSet<string>> AllowedBodyGenDescriptors { get; set; }
    public DescriptorMatchMode AllowedBodyGenMatchMode { get; set; } = DescriptorMatchMode.All;
    public Dictionary<string, HashSet<string>> DisallowedBodyGenDescriptors { get; set; }
    public DescriptorMatchMode DisallowedBodyGenMatchMode { get; set; } = DescriptorMatchMode.Any;
    public Dictionary<string, HashSet<string>> AllowedBodySlideDescriptors { get; set; }
    public DescriptorMatchMode AllowedBodySlideMatchMode { get; set; } = DescriptorMatchMode.All;
    public Dictionary<string, HashSet<string>> DisallowedBodySlideDescriptors { get; set; }
    public DescriptorMatchMode DisallowedBodySlideMatchMode { get; set; } = DescriptorMatchMode.Any;
    public NPCWeightRange WeightRange { get; set; }
    public int TopLevelSubgroupIndex { get; set; }
    public List<string> ContainedSubgroupIDs { get; set; }
    public List<string> ContainedSubgroupNames { get; set; }

    // used during combination generation
    public FlattenedAssetPack ParentAssetPack { get; set; }
    public int ForceIfMatchCount { get; set; } = 0;

    public static void FlattenSubgroups(Subgroup toFlatten, FlattenedSubgroup parent, List<FlattenedSubgroup> bottomLevelSubgroups, List<RaceGrouping> raceGroupingList, string parentAssetPackName, int topLevelIndex, List<Subgroup> subgroupHierarchy, FlattenedAssetPack parentAssetPack, DictionaryMapper dictionaryMapper, PatcherState patcherState)
    {
        if (toFlatten.Enabled == false) { return; }

        FlattenedSubgroup flattened = new FlattenedSubgroup(toFlatten, raceGroupingList, subgroupHierarchy, parentAssetPack, dictionaryMapper);
        flattened.TopLevelSubgroupIndex = topLevelIndex;

        if (parent != null)
        {
            // merge properties between current subgroup and parent
            if (parent.DistributionEnabled == false) { flattened.DistributionEnabled = false; }
            if (parent.AllowUnique == false) { flattened.AllowUnique = false; }
            if (parent.AllowNonUnique == false) { flattened.AllowNonUnique = false; }

            if (parent.Id != AssetPack.ConfigDistributionRules.SubgroupIDString)
            {
                flattened.ProbabilityWeighting *= parent.ProbabilityWeighting; // handled by calling function for the "fake" Distribution Rules subgroup
                flattened.ContainedSubgroupIDs.InsertRange(0, parent.ContainedSubgroupIDs);
                flattened.ContainedSubgroupNames.InsertRange(0, parent.ContainedSubgroupNames);
            }

            //handle DisallowedRaces first
            flattened.DisallowedRaces.UnionWith(parent.DisallowedRaces);

            // if both flattened and parent AllowedRaces are empty, do nothing
            // else if parent AllowedRaces is empty and flatted AllowedRaces is not empty, do nothing
            // else if parent AllowedRaces is not empty and flattened AllowedRaces is empty:
            if (parent.AllowedRaces.Count > 0 && flattened.AllowedRacesIsEmpty)
            {
                flattened.AllowedRaces = parent.AllowedRaces;
                flattened.AllowedRacesIsEmpty = false;
            }
            // else if both parent AllowedRaces and flattened AllowedRaces are not empty, get their intersection
            else if (parent.AllowedRaces.Count > 0 && flattened.AllowedRaces.Count > 0)
            {
                flattened.AllowedRaces.IntersectWith(parent.AllowedRaces);
            }
            // now trim disallowedRaces from allowed
            flattened.AllowedRaces = AllowedDisallowedCombiners.TrimDisallowedRacesFromAllowed(flattened.AllowedRaces, flattened.DisallowedRaces);
            // if there are now no more AllowedRaces, the current subgroup is incompatible with parent and should be ignored along with all children
            if (flattened.AllowedRaces.Count == 0 && flattened.AllowedRacesIsEmpty == false) { return; }

            //Required / Excluded Subgroups
            flattened.RequiredSubgroupIDs = DictionaryMapper.MergeDictionaries(new List<Dictionary<int, HashSet<string>>> { flattened.RequiredSubgroupIDs, parent.RequiredSubgroupIDs});
            flattened.ExcludedSubgroupIDs = DictionaryMapper.MergeDictionaries(new List<Dictionary<int, HashSet<string>>> { flattened.ExcludedSubgroupIDs, parent.ExcludedSubgroupIDs });
            flattened.RequiredSubgroupIDs = AllowedDisallowedCombiners.TrimExcludedSubgroupsFromRequired(flattened.RequiredSubgroupIDs, flattened.ExcludedSubgroupIDs, out bool requiredSubgroupsValid);
            if (!requiredSubgroupsValid) { return; }

            // Attribute Merging
            flattened.AllowedAttributes = NPCAttribute.InheritAttributes(parent.AllowedAttributes, flattened.AllowedAttributes);
            flattened.DisallowedAttributes = NPCAttribute.InheritAttributes(parent.DisallowedAttributes, flattened.DisallowedAttributes);

            // Weight Range
            if (parent.WeightRange.Lower > flattened.WeightRange.Lower) { flattened.WeightRange.Lower = parent.WeightRange.Lower; }
            if (parent.WeightRange.Upper < flattened.WeightRange.Upper) { flattened.WeightRange.Upper = parent.WeightRange.Upper; }

            // Keywords
            flattened.AddKeywords.UnionWith(parent.AddKeywords);

            // Paths
            flattened.Paths.UnionWith(parent.Paths);

            if (patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodyGen)
            {
                flattened.AllowedBodyGenDescriptors = DictionaryMapper.GetMorphDictionaryIntersection(flattened.AllowedBodyGenDescriptors, parent.AllowedBodyGenDescriptors);
                flattened.DisallowedBodyGenDescriptors = DictionaryMapper.MergeDictionaries(new List<Dictionary<string, HashSet<string>>> { flattened.DisallowedBodyGenDescriptors, parent.DisallowedBodyGenDescriptors });
                flattened.AllowedBodyGenDescriptors = AllowedDisallowedCombiners.TrimDisallowedDescriptorsFromAllowed(flattened.AllowedBodyGenDescriptors, flattened.DisallowedBodyGenDescriptors, out bool BodyShapeDescriptorsValid);
                if (!BodyShapeDescriptorsValid) { return; }
            }
            else if (patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodySlide)
            {
                flattened.AllowedBodySlideDescriptors = DictionaryMapper.GetMorphDictionaryIntersection(flattened.AllowedBodySlideDescriptors, parent.AllowedBodySlideDescriptors);
                flattened.DisallowedBodySlideDescriptors = DictionaryMapper.MergeDictionaries(new List<Dictionary<string, HashSet<string>>> { flattened.DisallowedBodySlideDescriptors, parent.DisallowedBodySlideDescriptors });
                flattened.AllowedBodySlideDescriptors = AllowedDisallowedCombiners.TrimDisallowedDescriptorsFromAllowed(flattened.AllowedBodySlideDescriptors, flattened.DisallowedBodySlideDescriptors, out bool BodyShapeDescriptorsValid);
                if (!BodyShapeDescriptorsValid) { return; }
            }
        }

        if (toFlatten.Subgroups.Count == 0)
        {
            bottomLevelSubgroups.Add(flattened);
        }
        else
        {
            foreach (var subgroup in toFlatten.Subgroups)
            {
                FlattenSubgroups(subgroup, flattened, bottomLevelSubgroups, raceGroupingList, parentAssetPackName, topLevelIndex, subgroupHierarchy, parentAssetPack, dictionaryMapper, patcherState);
            }
        }
    }

    public string GetReportString()
    {
        return "Subgroup " + Id + " (" + Name + ") ";
    }
}