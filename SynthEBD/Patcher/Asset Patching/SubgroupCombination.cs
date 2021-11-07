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
        public string Signature { get; set; }
        public HashSet<FlattenedSubgroup> ContainedSubgroups { get; set; }
        public HashSet<Tuple<string, FormKey>> AssignedRecords { get; set; } // string is the location relative to the NPC.

        public static SubgroupCombination GenerateCombination()
        {
            var combination = new SubgroupCombination();


            return combination;
        }
    }
}
