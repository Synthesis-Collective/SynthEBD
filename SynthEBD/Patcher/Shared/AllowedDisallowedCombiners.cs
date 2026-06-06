using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>
/// Reconciles paired allow/disallow rule sets when subgroups are combined: removes disallowed races
/// from an allowed-race set, and strips excluded morph descriptors / excluded subgroups from their
/// required counterparts, reporting whether a usable result remains.
/// </summary>
public class AllowedDisallowedCombiners
{
    /// <summary>
    /// Returns the allowed races with any race also present in <paramref name="disallowedRaces"/> removed.
    /// </summary>
    public static HashSet<FormKey> TrimDisallowedRacesFromAllowed(HashSet<FormKey> allowedRaces, HashSet<FormKey> disallowedRaces)
    {
        var disallowedFKstrings = disallowedRaces.Select(x => x.ToString()).ToArray();
        return allowedRaces.Where(x => disallowedFKstrings.Contains(x.ToString()) == false).ToHashSet();
    }

    /// <summary>
    /// Removes, per category, any disallowed descriptor values from the allowed-descriptor dictionary and
    /// drops categories left empty. <paramref name="resultValid"/> is set false when a non-empty input is
    /// trimmed to nothing (the combined subgroup product is invalid).
    /// </summary>
    public static Dictionary<string, HashSet<string>> TrimDisallowedDescriptorsFromAllowed(Dictionary<string, HashSet<string>> allowed, Dictionary<string, HashSet<string>> disallowed, out bool resultValid)
    {
        foreach (var entry in allowed)
        {
            if (disallowed.ContainsKey(entry.Key))
            {
                var disallowedList = disallowed[entry.Key];
                foreach (string x in disallowedList)
                {
                    if (entry.Value.Contains(x)) { entry.Value.Remove(x); }
                }
            }
        }

        var trimmedDict = allowed.Where(f => f.Value.Count > 0).ToDictionary(x => x.Key, x => x.Value); // remove empty keys
        if (allowed.Count > 0 && trimmedDict.Count == 0)
        {
            resultValid = false; // if no more morph descriptors remain after merging two subgroups, then the product is invalid
        }
        else
        {
            resultValid = true;
        }
        return trimmedDict;
    }
        
    /// <summary>
    /// Removes, per position, any excluded subgroup IDs from the required-subgroups dictionary and drops
    /// positions left empty. <paramref name="resultValid"/> is set false when a non-empty input is trimmed to
    /// nothing (the combined subgroup product is invalid).
    /// </summary>
    public static Dictionary<int, HashSet<string>> TrimExcludedSubgroupsFromRequired(Dictionary<int, HashSet<string>> required, Dictionary<int, HashSet<string>> excluded, out bool resultValid) // this function is currently structured exactly like TrimDisallowedDescriptorsFromAllowed but choosing to keep separate for clarity instead of merging into generic
    {
        foreach (var entry in required)
        {
            if (excluded.ContainsKey(entry.Key))
            {
                var disallowedList = excluded[entry.Key];
                foreach (string x in disallowedList)
                {
                    if (entry.Value.Contains(x)) { entry.Value.Remove(x); }
                }
            }
        }

        var trimmedDict = required.Where(f => f.Value.Count > 0).ToDictionary(x => x.Key, x => x.Value); // remove empty keys
        if (required.Count > 0 && trimmedDict.Count == 0)
        {
            resultValid = false; // if no more required subgroups remain after merging two subgroups, then the product is invalid
        }
        else
        {
            resultValid = true;
        }
        return trimmedDict;
    }
}