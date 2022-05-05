namespace SynthEBD
{
    public class FlattenedReplacerGroup
    {
        public FlattenedReplacerGroup(AssetReplacerGroup source)
        {
            this.Name = source.Label;
            this.Subgroups = new List<List<FlattenedSubgroup>>();
            this.Source = new AssetPack();
        }

        public FlattenedReplacerGroup(FlattenedReplacerGroup source)
        {
            this.Name = source.Name;
            this.Subgroups = new List<List<FlattenedSubgroup>>();
            this.Source = source.Source;
        }

        public string Name { get; set; }
        public List<List<FlattenedSubgroup>> Subgroups { get; set; }
        public AssetPack Source { get; set; }

        public static FlattenedReplacerGroup FlattenReplacerGroup(AssetReplacerGroup source, List<RaceGrouping> raceGroupingList, FlattenedAssetPack parentAssetPack)
        {
            var output = new FlattenedReplacerGroup(source);
            for (int i = 0; i < source.Subgroups.Count; i++)
            {
                var flattenedSubgroups = new List<FlattenedSubgroup>();
                FlattenedSubgroup.FlattenSubgroups(source.Subgroups[i], null, flattenedSubgroups, raceGroupingList, parentAssetPack.GroupName, i, source.Subgroups, parentAssetPack);
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
