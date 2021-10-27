using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class AllowedDisallowedCombiners
    {
        public static HashSet<FormKey> TrimDisallowedRacesFromAllowed(HashSet<FormKey> allowedRaces, HashSet<FormKey> disallowedRaces)
        {
            var disallowedFKstrings = disallowedRaces.Select(x => x.ToString());
            return allowedRaces.Where(x => disallowedFKstrings.Contains(x.ToString()) == false).ToHashSet();
        }
    }
}
