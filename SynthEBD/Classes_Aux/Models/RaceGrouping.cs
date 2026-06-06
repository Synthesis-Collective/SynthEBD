using Mutagen.Bethesda.Plugins;
using System.Diagnostics;

namespace SynthEBD;

/// <summary>
/// A named set of races referenced as a unit throughout the settings (e.g. "Humanoid", "Beast"), so
/// rules can target many races at once.
/// </summary>
[DebuggerDisplay("{Label} ({Races.Count})")]
public class RaceGrouping : IHasLabel
{
    public string Label { get; set; } = "";
    public HashSet<FormKey> Races { get; set; } = new();

    /// <summary>Combines individually-selected races with the races of every selected grouping into one set.</summary>
    /// <param name="selectedGroupings">Labels of the groupings to include.</param>
    /// <param name="raceGroupingList">All available race groupings.</param>
    /// <param name="indivRaceList">Individually-selected race FormKeys.</param>
    /// <returns>The union of the individual races and all selected groupings' races.</returns>
    public static HashSet<FormKey> MergeRaceAndGroupingList(HashSet<string> selectedGroupings, List<RaceGrouping> raceGroupingList, HashSet<FormKey> indivRaceList)
    {
        // this might need to work - might need to convert to string. Be sure to validate.
        var combinedRaceList = new HashSet<FormKey>(indivRaceList);
        var appliedGroupings = raceGroupingList.Where(x => selectedGroupings.Contains(x.Label)).Select(x => x.Races).ToHashSet();
        foreach (var appliedGrouping in appliedGroupings)
        {
            combinedRaceList.UnionWith(appliedGrouping);
        }

        return combinedRaceList;
    }
}