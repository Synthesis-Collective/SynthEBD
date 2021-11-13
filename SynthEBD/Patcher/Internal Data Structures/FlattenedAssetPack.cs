using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class FlattenedAssetPack
    {
        public FlattenedAssetPack(AssetPack source)
        {
            this.GroupName = source.GroupName;
            this.Gender = source.Gender;
            this.Subgroups = new List<List<FlattenedSubgroup>>();
        }

        public FlattenedAssetPack(string groupName, Gender gender)
        {
            this.GroupName = groupName;
            this.Gender = gender;
            this.Subgroups = new List<List<FlattenedSubgroup>>();
        }

        public string GroupName { get; set; }
        public Gender Gender { get; set; }
        public List<List<FlattenedSubgroup>> Subgroups { get; set; }


        public static FlattenedAssetPack FlattenAssetPack(AssetPack source, List<RaceGrouping> raceGroupingList, bool includeBodyGen)
        {
            var output = new FlattenedAssetPack(source);

            for (int i = 0; i < source.Subgroups.Count; i++)
            {
                var flattenedSubgroups = new List<FlattenedSubgroup>();
                FlattenedSubgroup.FlattenSubgroups(source.Subgroups[i], null, flattenedSubgroups, raceGroupingList, output.GroupName, i, includeBodyGen, source.Subgroups, output);
                output.Subgroups.Add(flattenedSubgroups);
            }

            return output;
        }

        public FlattenedAssetPack ShallowCopy()
        {
            FlattenedAssetPack copy = new FlattenedAssetPack(this.GroupName, this.Gender);
            foreach (var subgroupList in this.Subgroups)
            {
                copy.Subgroups.Add(new List<FlattenedSubgroup>(subgroupList));
            }
            return copy;
        }
    }
}
