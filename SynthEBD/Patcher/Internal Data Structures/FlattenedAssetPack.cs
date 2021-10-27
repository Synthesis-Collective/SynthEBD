using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class FlattenedAssetPack
    {
        public FlattenedAssetPack(AssetPack source)
        {
            this.GroupName = source.groupName;
            this.Gender = source.gender;
            this.Subgroups = new HashSet<HashSet<FlattenedSubgroup>>();
        }

        public string GroupName { get; set; }
        public Gender Gender { get; set; }
        public HashSet<HashSet<FlattenedSubgroup>> Subgroups { get; set; }


        public static FlattenedAssetPack FlattenAssetPack(AssetPack source, List<RaceGrouping> raceGroupingList)
        {
            var output = new FlattenedAssetPack(source);

            foreach (var subgroup in source.subgroups)
            {
                var flattenedSubgroups = new HashSet<FlattenedSubgroup>();
                FlattenedSubgroup.FlattenSubgroups(subgroup, null, flattenedSubgroups, raceGroupingList);
                output.Subgroups.Add(flattenedSubgroups);
            }

            return output;
        }
    }
}
