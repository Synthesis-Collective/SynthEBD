using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class FlattenedReplacerGroup
    {
        public FlattenedReplacerGroup(AssetReplacerGroup source)
        {
            this.GroupName = source.Label;
            this.Subgroups = new List<List<FlattenedSubgroup>>();
            this.Source = new AssetPack();
        }

        public FlattenedReplacerGroup(FlattenedReplacerGroup source)
        {
            this.GroupName = source.GroupName;
            this.Subgroups = new List<List<FlattenedSubgroup>>();
            this.Source = source.Source;
        }

        public string GroupName { get; set; }
        public List<List<FlattenedSubgroup>> Subgroups { get; set; }
        public AssetPack Source { get; set; }

        public static FlattenedReplacerGroup FlattenReplacerGroup(AssetReplacerGroup source, List<RaceGrouping> raceGroupingList, FlattenedAssetPack parentAssetPack, bool includeBodyGen)
        {
            var output = new FlattenedReplacerGroup(source);
            for (int i = 0; i < source.Subgroups.Count; i++)
            {
                var flattenedSubgroups = new List<FlattenedSubgroup>();
                FlattenedSubgroup.FlattenSubgroups(source.Subgroups[i], null, flattenedSubgroups, raceGroupingList, parentAssetPack.GroupName, i, includeBodyGen, source.Subgroups, parentAssetPack);
                output.Subgroups.Add(flattenedSubgroups);
            }
            output.Source = parentAssetPack.Source;

            return output;
        }
        
        public FlattenedReplacerGroup ShallowCopy()
        {
            var copy = new FlattenedReplacerGroup(this);
            foreach (var subgroupList in this.Subgroups)
            {
                copy.Subgroups.Add(new List<FlattenedSubgroup>(subgroupList));
            }
            return copy;
        }    
    }
}
