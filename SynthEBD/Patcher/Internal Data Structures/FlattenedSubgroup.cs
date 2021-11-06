using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.AssetPack;

namespace SynthEBD
{
    public class FlattenedSubgroup
    {
        public FlattenedSubgroup(AssetPack.Subgroup template, List<RaceGrouping> raceGroupingList, List<Subgroup> subgroupHierarchy)
        {
            this.Id = template.id;
            this.Name = template.name;
            this.DistributionEnabled = template.distributionEnabled;
            this.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.allowedRaceGroupings, raceGroupingList, template.allowedRaces);
            if (this.AllowedRaces.Count == 0) { this.AllowedRacesIsEmpty = true; }
            this.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.disallowedRaceGroupings, raceGroupingList, template.disallowedRaces);
            this.AllowedRaces = AllowedDisallowedCombiners.TrimDisallowedRacesFromAllowed(this.AllowedRaces, this.DisallowedRaces);
            this.AllowedAttributes = new HashSet<NPCAttribute>(template.allowedAttributes);
            this.DisallowedAttributes = new HashSet<NPCAttribute>(template.disallowedAttributes);
            this.ForceIfAttributes = new HashSet<NPCAttribute>(template.forceIfAttributes);
            this.AllowUnique = template.bAllowUnique;
            this.AllowNonUnique = template.bAllowNonUnique;
            this.RequiredSubgroupIDs = DictionaryMapper.RequiredOrExcludedSubgroupsToDictionary(template.requiredSubgroups, subgroupHierarchy);
            this.ExcludedSubgroupIDs = DictionaryMapper.RequiredOrExcludedSubgroupsToDictionary(template.excludedSubgroups, subgroupHierarchy);
            this.AddKeywords = new HashSet<string>(template.addKeywords);
            this.ProbabilityWeighting = template.probabilityWeighting;
            this.Paths = new HashSet<FilePathReplacement>(template.paths);
            this.AllowedBodyGenDescriptors = DictionaryMapper.MorphDescriptorsToDictionary(template.allowedBodyGenDescriptors);
            this.DisallowedBodyGenDescriptors = DictionaryMapper.MorphDescriptorsToDictionary(template.disallowedBodyGenDescriptors);
            this.WeightRange = new NPCWeightRange { Lower = template.weightRange.Lower, Upper = template.weightRange.Upper };
            this.ContainedSubgroupIDs = new List<string> { this.Id };
            this.ContainedSubgroupNames = new List<string> { this.Name };
        }
        public string Id { get; set; }
        public string Name { get; set; }
        public bool DistributionEnabled { get; set; }
        public HashSet<FormKey> AllowedRaces { get; set; }
        public HashSet<FormKey> DisallowedRaces { get; set; }
        public bool AllowedRacesIsEmpty { get; set; } // distinguishes between initially empty (All races valid) vs. empty after pruning of Disallowed Races (subgroup is invalid)
        public HashSet<NPCAttribute> AllowedAttributes { get; set; }
        public HashSet<NPCAttribute> DisallowedAttributes { get; set; }
        public HashSet<NPCAttribute> ForceIfAttributes { get; set; }
        public bool AllowUnique { get; set; }
        public bool AllowNonUnique { get; set; }
        public Dictionary<int, HashSet<string>> RequiredSubgroupIDs { get; set; }
        public Dictionary<int, HashSet<string>> ExcludedSubgroupIDs { get; set; }
        public HashSet<string> AddKeywords { get; set; }
        public int ProbabilityWeighting { get; set; }
        public HashSet<FilePathReplacement> Paths { get; set; }
        public Dictionary<string, HashSet<string>> AllowedBodyGenDescriptors { get; set; }
        public Dictionary<string, HashSet<string>> DisallowedBodyGenDescriptors { get; set; }
        public NPCWeightRange WeightRange { get; set; }
        public int TopLevelSubgroupIndex { get; set; }
        public string SourceAssetPack { get; set; }
        public List<string> ContainedSubgroupIDs { get; set; }
        public List<string> ContainedSubgroupNames { get; set; }

        public static void FlattenSubgroups(AssetPack.Subgroup toFlatten, FlattenedSubgroup parent, List<FlattenedSubgroup> bottomLevelSubgroups, List<RaceGrouping> raceGroupingList, string parentAssetPackName, int topLevelIndex, bool includeBodyGen, List<Subgroup> subgroupHierarchy)
        {
            if (toFlatten.enabled == false) { return; }

            FlattenedSubgroup flattened = new FlattenedSubgroup(toFlatten, raceGroupingList, subgroupHierarchy);

            if (parent != null)
            {
                flattened.TopLevelSubgroupIndex = topLevelIndex;
                flattened.SourceAssetPack = parentAssetPackName;

                // merge properties between current subgroup and parent
                if (parent.DistributionEnabled == false) { flattened.DistributionEnabled = false; }
                if (parent.AllowUnique == false) { flattened.AllowUnique = false; }
                if (parent.AllowNonUnique == false) { flattened.AllowNonUnique = false; }
                flattened.ProbabilityWeighting *= parent.ProbabilityWeighting;
                flattened.ContainedSubgroupIDs.InsertRange(0, parent.ContainedSubgroupIDs);
                flattened.ContainedSubgroupNames.InsertRange(0, parent.ContainedSubgroupNames);

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
                bool requiredSubgroupsValid = true;
                flattened.RequiredSubgroupIDs = AllowedDisallowedCombiners.TrimExcludedSubgroupsFromRequired(flattened.RequiredSubgroupIDs, flattened.ExcludedSubgroupIDs, out requiredSubgroupsValid);
                if (!requiredSubgroupsValid) { return; }

                // Attribute Merging
                flattened.AllowedAttributes = InheritParentAttributes(parent.AllowedAttributes, flattened.AllowedAttributes);
                flattened.DisallowedAttributes = InheritParentAttributes(parent.DisallowedAttributes, flattened.DisallowedAttributes);
                flattened.ForceIfAttributes = InheritParentAttributes(parent.ForceIfAttributes, flattened.ForceIfAttributes);

                // Weight Range
                if (parent.WeightRange.Lower > flattened.WeightRange.Lower) { flattened.WeightRange.Lower = parent.WeightRange.Lower; }
                if (parent.WeightRange.Upper < flattened.WeightRange.Upper) { flattened.WeightRange.Upper = parent.WeightRange.Upper; }

                // Paths
                flattened.Paths.UnionWith(parent.Paths);

                if (includeBodyGen)
                {
                    flattened.AllowedBodyGenDescriptors = DictionaryMapper.GetMorphDictionaryIntersection(flattened.AllowedBodyGenDescriptors, parent.AllowedBodyGenDescriptors);
                    flattened.DisallowedBodyGenDescriptors = DictionaryMapper.MergeDictionaries(new List<Dictionary<string, HashSet<string>>> { flattened.DisallowedBodyGenDescriptors, parent.DisallowedBodyGenDescriptors });
                    bool morphDescriptorsValid = true;
                    flattened.AllowedBodyGenDescriptors = AllowedDisallowedCombiners.TrimDisallowedDescriptorsFromAllowed(flattened.AllowedBodyGenDescriptors, flattened.DisallowedBodyGenDescriptors, out morphDescriptorsValid);
                    if (!morphDescriptorsValid) { return; }
                }
            }

            if (toFlatten.subgroups.Count == 0)
            {
                bottomLevelSubgroups.Add(flattened);
            }
            else
            {
                foreach (var subgroup in toFlatten.subgroups)
                {
                    FlattenSubgroups(subgroup, flattened, bottomLevelSubgroups, raceGroupingList, parentAssetPackName, topLevelIndex, includeBodyGen, subgroupHierarchy);
                }
            }
        }

        // Grouped Sub Attributes get merged together. E.g:
        // Parent has attributes (A && B) || (C && D)
        // Child has attributes (E && F) || (G && H)
        // After inheriting, child will have attributes (A && B && E && F) || (A && B && G && H) || (C && D && E && F) || (C && D && G && H)
        private static HashSet<NPCAttribute> InheritParentAttributes(HashSet<NPCAttribute> parentAttributes, HashSet<NPCAttribute> childAttributes)
        {
            var mergedAttributes = new HashSet<NPCAttribute>();

            if (parentAttributes.Count > 0 && childAttributes.Count == 0)
            {
                return parentAttributes;
            }
            else if (childAttributes.Count > 0 && parentAttributes.Count == 0)
            {
                return childAttributes;
            }
            else
            {
                foreach (var childAttribute in childAttributes)
                {
                    foreach (var parentAttribute in parentAttributes)
                    {
                        var combinedAttribute = new NPCAttribute();

                        foreach (var subParentAttribute in parentAttribute.GroupedSubAttributes)
                        {
                            combinedAttribute.GroupedSubAttributes.Add(subParentAttribute);
                        }

                        foreach (var subChildAttribute in childAttribute.GroupedSubAttributes)
                        {
                            combinedAttribute.GroupedSubAttributes.Add(subChildAttribute);
                        }

                        mergedAttributes.Add(combinedAttribute);
                    }
                }
            }

            return mergedAttributes;
        }
    }
}
