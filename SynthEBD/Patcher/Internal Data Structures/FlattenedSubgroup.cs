using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.AssetPack;

namespace SynthEBD
{
    class FlattenedSubgroup
    {
        public FlattenedSubgroup(AssetPack.Subgroup template, List<RaceGrouping> raceGroupingList)
        {
            this.Id = template.id;
            this.Name = template.name;
            this.Enabled = template.enabled;
            this.DistributionEnabled = template.distributionEnabled;
            this.AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.allowedRaceGroupings, raceGroupingList, template.allowedRaces);
            this.DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.disallowedRaceGroupings, raceGroupingList, template.disallowedRaces);
            this.AllowedAttributes = new HashSet<NPCAttribute>(template.allowedAttributes);
            this.DisallowedAttributes = new HashSet<NPCAttribute>(template.disallowedAttributes);
            this.ForceIfAttributes = new HashSet<NPCAttribute>(template.forceIfAttributes);
            this.AllowUnique = template.bAllowUnique;
            this.AllowNonUnique = template.bAllowNonUnique;
            this.RequiredSubgroups = new HashSet<string>(template.requiredSubgroups);
            this.ExcludedSubgroups = new HashSet<string>(template.excludedSubgroups);
            this.AddKeywords = new HashSet<string>(template.addKeywords);
            this.ProbabilityWeighting = template.probabilityWeighting;
            this.Paths = new HashSet<FilePathReplacement>(template.paths);
            this.AllowedBodyGenDescriptors = new HashSet<string>(template.allowedBodyGenDescriptors);
            this.DisallowedBodyGenDescriptors = new HashSet<string>(template.disallowedBodyGenDescriptors);
            this.WeightRange = new NPCWeightRange { Lower = template.weightRange.Lower, Upper = template.weightRange.Upper };
        }
        public string Id { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public bool DistributionEnabled { get; set; }
        public HashSet<FormKey> AllowedRaces { get; set; }
        public HashSet<FormKey> DisallowedRaces { get; set; }

        public HashSet<NPCAttribute> AllowedAttributes { get; set; }
        public HashSet<NPCAttribute> DisallowedAttributes { get; set; }
        public HashSet<NPCAttribute> ForceIfAttributes { get; set; }
        public bool AllowUnique { get; set; }
        public bool AllowNonUnique { get; set; }
        public HashSet<string> RequiredSubgroups { get; set; }
        public HashSet<string> ExcludedSubgroups { get; set; }
        public HashSet<string> AddKeywords { get; set; }
        public int ProbabilityWeighting { get; set; }
        public HashSet<FilePathReplacement> Paths { get; set; }
        public HashSet<string> AllowedBodyGenDescriptors { get; set; }
        public HashSet<string> DisallowedBodyGenDescriptors { get; set; }
        public NPCWeightRange WeightRange { get; set; }
        public string TopLevelSubgroupID { get; set; }

        public static void FlattenSubgroups(AssetPack.Subgroup toFlatten, FlattenedSubgroup parent, HashSet<FlattenedSubgroup> bottomLevelSubgroups, List<RaceGrouping> raceGroupingList)
        {
            FlattenedSubgroup flattened = new FlattenedSubgroup(toFlatten, raceGroupingList);

            if (parent == null)
            {
                flattened.TopLevelSubgroupID = toFlatten.id;
            }
            else
            {
                flattened.TopLevelSubgroupID = parent.TopLevelSubgroupID;

                // merge properties between current subgroup and parent


            }

            if (toFlatten.subgroups.Count == 0)
            {
                bottomLevelSubgroups.Add(flattened);
            }
        }
    }
}
