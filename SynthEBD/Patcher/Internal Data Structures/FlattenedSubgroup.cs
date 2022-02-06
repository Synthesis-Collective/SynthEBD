using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.AssetPack;

namespace SynthEBD
{
    public class FlattenedSubgroup : IProbabilityWeighted
    {
        public FlattenedSubgroup(AssetPack.Subgroup template, List<RaceGrouping> raceGroupingList, List<Subgroup> subgroupHierarchy, FlattenedAssetPack parent)
        {
            this.Id = template.ID;
            this.Name = template.Name;
            this.DistributionEnabled = template.DistributionEnabled;
            this.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.AllowedRaceGroupings, raceGroupingList, template.AllowedRaces);
            if (this.AllowedRaces.Count == 0) { this.AllowedRacesIsEmpty = true; }
            else { this.AllowedRacesIsEmpty = false; }
            this.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.DisallowedRaceGroupings, raceGroupingList, template.DisallowedRaces);
            this.AllowedRaces = AllowedDisallowedCombiners.TrimDisallowedRacesFromAllowed(this.AllowedRaces, this.DisallowedRaces);
            this.AllowedAttributes = new HashSet<NPCAttribute>(template.AllowedAttributes);
            this.DisallowedAttributes = new HashSet<NPCAttribute>(template.DisallowedAttributes);

            // replace Grouped attributes (if any) with their corresponding group members
            this.AllowedAttributes = NPCAttribute.SpreadGroupTypeAttributes(this.AllowedAttributes, parent.Source.AttributeGroups);
            this.DisallowedAttributes = NPCAttribute.SpreadGroupTypeAttributes(this.DisallowedAttributes, parent.Source.AttributeGroups);

            this.AllowUnique = template.AllowUnique;
            this.AllowNonUnique = template.AllowNonUnique;
            this.RequiredSubgroupIDs = DictionaryMapper.RequiredOrExcludedSubgroupsToDictionary(template.RequiredSubgroups, subgroupHierarchy);
            this.ExcludedSubgroupIDs = DictionaryMapper.RequiredOrExcludedSubgroupsToDictionary(template.ExcludedSubgroups, subgroupHierarchy);
            this.AddKeywords = new HashSet<string>(template.AddKeywords);
            this.ProbabilityWeighting = template.ProbabilityWeighting;
            this.Paths = new HashSet<FilePathReplacement>(template.Paths);
            this.AllowedBodyGenDescriptors = DictionaryMapper.BodyShapeDescriptorsToDictionary(template.AllowedBodyGenDescriptors);
            this.DisallowedBodyGenDescriptors = DictionaryMapper.BodyShapeDescriptorsToDictionary(template.DisallowedBodyGenDescriptors);
            this.AllowedBodySlideDescriptors = DictionaryMapper.BodyShapeDescriptorsToDictionary(template.AllowedBodySlideDescriptors);
            this.DisallowedBodySlideDescriptors = DictionaryMapper.BodyShapeDescriptorsToDictionary(template.AllowedBodySlideDescriptors);
            this.WeightRange = new NPCWeightRange { Lower = template.WeightRange.Lower, Upper = template.WeightRange.Upper };
            this.ContainedSubgroupIDs = new List<string> { this.Id };
            this.ContainedSubgroupNames = new List<string> { this.Name };
            this.ParentAssetPack = parent;
            this.ForceIfMatchCount = 0;
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
        public Dictionary<string, HashSet<string>> DisallowedBodyGenDescriptors { get; set; }
        public Dictionary<string, HashSet<string>> AllowedBodySlideDescriptors { get; set; }
        public Dictionary<string, HashSet<string>> DisallowedBodySlideDescriptors { get; set; }
        public NPCWeightRange WeightRange { get; set; }
        public int TopLevelSubgroupIndex { get; set; }
        public List<string> ContainedSubgroupIDs { get; set; }
        public List<string> ContainedSubgroupNames { get; set; }

        // used during combination generation
        public FlattenedAssetPack ParentAssetPack { get; set; }
        public int ForceIfMatchCount { get; set; }

        public static void FlattenSubgroups(Subgroup toFlatten, FlattenedSubgroup parent, List<FlattenedSubgroup> bottomLevelSubgroups, List<RaceGrouping> raceGroupingList, string parentAssetPackName, int topLevelIndex, List<Subgroup> subgroupHierarchy, FlattenedAssetPack parentAssetPack)
        {
            if (toFlatten.Enabled == false) { return; }

            FlattenedSubgroup flattened = new FlattenedSubgroup(toFlatten, raceGroupingList, subgroupHierarchy, parentAssetPack);
            flattened.TopLevelSubgroupIndex = topLevelIndex;

            if (parent != null)
            {
                // merge properties between current subgroup and parent
                if (parent.DistributionEnabled == false) { flattened.DistributionEnabled = false; }
                if (parent.AllowUnique == false) { flattened.AllowUnique = false; }
                if (parent.AllowNonUnique == false) { flattened.AllowNonUnique = false; }

                if (parent.Id != AssetPack.ConfigDistributionRules.SubgroupTitleString)
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

                // Paths
                flattened.Paths.UnionWith(parent.Paths);

                if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodyGen)
                {
                    flattened.AllowedBodyGenDescriptors = DictionaryMapper.GetMorphDictionaryIntersection(flattened.AllowedBodyGenDescriptors, parent.AllowedBodyGenDescriptors);
                    flattened.DisallowedBodyGenDescriptors = DictionaryMapper.MergeDictionaries(new List<Dictionary<string, HashSet<string>>> { flattened.DisallowedBodyGenDescriptors, parent.DisallowedBodyGenDescriptors });
                    flattened.AllowedBodyGenDescriptors = AllowedDisallowedCombiners.TrimDisallowedDescriptorsFromAllowed(flattened.AllowedBodyGenDescriptors, flattened.DisallowedBodyGenDescriptors, out bool BodyShapeDescriptorsValid);
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
                    FlattenSubgroups(subgroup, flattened, bottomLevelSubgroups, raceGroupingList, parentAssetPackName, topLevelIndex, subgroupHierarchy, parentAssetPack);
                }
            }
        }
    }
}
