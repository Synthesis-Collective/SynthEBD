using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class SubgroupCombination
    {
        public SubgroupCombination()
        {
            this.Signature = "";
            this.ContainedSubgroups = new List<FlattenedSubgroup>();
            this.AssignedRecords = new HashSet<Tuple<string, FormKey>>();
            this.AssetPackName = "";
            this.AssetPack = null;
        }
        public string Signature { get; set; }
        public List<FlattenedSubgroup> ContainedSubgroups { get; set; }
        public HashSet<Tuple<string, FormKey>> AssignedRecords { get; set; } // string is the location relative to the NPC.
        public string AssetPackName { get; set; }
        public FlattenedAssetPack AssetPack { get; set; }
    }
}
