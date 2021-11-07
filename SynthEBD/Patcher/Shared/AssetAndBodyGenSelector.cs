using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class AssetAndBodyGenSelector
    {
        public static Tuple<SubgroupCombination, HashSet<string>> ChooseCombinationAndBodyGen(out bool bodyGenAssigned)
        {
            SubgroupCombination chosenCombination = new SubgroupCombination();
            HashSet<string> chosenMorphs = new HashSet<string>();
            bodyGenAssigned = false;

            return new Tuple<SubgroupCombination, HashSet<string>>(chosenCombination, chosenMorphs);
        }
    }
}
