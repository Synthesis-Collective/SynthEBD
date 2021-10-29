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
            this.Subgroups = new List<HashSet<FlattenedSubgroup>>();
        }

        public string GroupName { get; set; }
        public Gender Gender { get; set; }
        public List<HashSet<FlattenedSubgroup>> Subgroups { get; set; }


        public static FlattenedAssetPack FlattenAssetPack(AssetPack source, List<RaceGrouping> raceGroupingList, bool includeBodyGen)
        {
            var output = new FlattenedAssetPack(source);

            for (int i = 0; i < source.subgroups.Count; i++)
            {
                var flattenedSubgroups = new HashSet<FlattenedSubgroup>();
                FlattenedSubgroup.FlattenSubgroups(source.subgroups[i], null, flattenedSubgroups, raceGroupingList, output.GroupName, i, includeBodyGen, source.subgroups);
                output.Subgroups.Add(flattenedSubgroups);
            }

            return output;
        }
    }
}
