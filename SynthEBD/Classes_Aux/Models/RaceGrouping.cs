using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class RaceGrouping
    {
        public RaceGrouping()
        {
            this.Label = "";
            this.Races = new HashSet<FormKey>();
        }

        public string Label { get; set; }
        public HashSet<FormKey> Races { get; set; }

        public static HashSet<FormKey> MergeRaceAndGroupingList(HashSet<string> selectedGroupings, List<RaceGrouping> raceGroupingList, HashSet<FormKey> indivRaceList)
        {
            // this might need to work - might need to convert to string. Be sure to validate.
            var combinedRaceList = new HashSet<FormKey>(indivRaceList);
            var appliedGroupings = raceGroupingList.Where(x => selectedGroupings.Contains(x.Label)).Select(x => x.Races).ToHashSet();
            foreach (var appliedGrouping in appliedGroupings)
            {
                combinedRaceList = combinedRaceList.Union(appliedGrouping).ToHashSet();
            }

            return combinedRaceList;
        }
    }
}
