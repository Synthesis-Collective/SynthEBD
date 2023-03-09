using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

public class AllowedDisallowedCombiners
{
    public static HashSet<FormKey> TrimDisallowedRacesFromAllowed(HashSet<FormKey> allowedRaces, HashSet<FormKey> disallowedRaces)
    {
        var disallowedFKstrings = disallowedRaces.Select(x => x.ToString()).ToArray();
        return allowedRaces.Where(x => disallowedFKstrings.Contains(x.ToString()) == false).ToHashSet();
    }

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