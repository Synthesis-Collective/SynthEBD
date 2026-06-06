using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>
/// Builds and combines lookup dictionaries used by the patcher: race/gender-keyed asset pack
/// maps, body-shape descriptor groupings, morph-descriptor set algebra, and required/excluded
/// subgroup position maps derived from the asset pack subgroup hierarchy.
/// </summary>
public class DictionaryMapper
{
    private readonly Logger _logger;
    /// <summary>Initializes a new <see cref="DictionaryMapper"/> with the shared <see cref="Logger"/>.</summary>
    public DictionaryMapper(Logger logger)
    {
        _logger = logger;
    }
    /// <summary>
    /// Produces, for each patchable race and gender, the set of flattened asset packs compatible with that
    /// race after pruning race-incompatible subgroups. Each pack is shallow-copied before pruning so the
    /// originals are untouched; packs left with no subgroups at any position are dropped.
    /// </summary>
    /// <param name="flattenedAssetPacks">All flattened asset packs to evaluate.</param>
    /// <param name="patchableRaces">Races to build (race, gender) keys for.</param>
    /// <returns>Map from (race FormKey, gender) to the compatible pruned asset packs.</returns>
    public static Dictionary<Tuple<FormKey, Gender>, HashSet<FlattenedAssetPack>> GetAssetPacksByRaceGender(HashSet<FlattenedAssetPack> flattenedAssetPacks, List<FormKey> patchableRaces)
    {
        Dictionary<Tuple<FormKey, Gender>, HashSet<FlattenedAssetPack>> apDict = new Dictionary<Tuple<FormKey, Gender>, HashSet<FlattenedAssetPack>>();

        foreach (var raceFK in patchableRaces)
        {
            var mTuple = new Tuple<FormKey, Gender>(raceFK, Gender.Male);
            var fTuple = new Tuple<FormKey, Gender>(raceFK, Gender.Female);

            HashSet<FlattenedAssetPack> prunedAssetPacksM = new HashSet<FlattenedAssetPack>();
            HashSet<FlattenedAssetPack> prunedAssetPacksF = new HashSet<FlattenedAssetPack>();

            foreach (var flattenedAP in flattenedAssetPacks)
            {
                var prunedAP = flattenedAP.ShallowCopy();
                if (PruneFlattenedAssetPackByRace(prunedAP, raceFK))
                {
                    switch (prunedAP.Gender)
                    {
                        case Gender.Male: prunedAssetPacksM.Add(prunedAP); break;
                        case Gender.Female: prunedAssetPacksF.Add(prunedAP); break;
                    }
                }
            }

            apDict.Add(mTuple, prunedAssetPacksM);
            apDict.Add(fTuple, prunedAssetPacksF);
        }

        return apDict;
    }

    /// <summary>
    /// Removes from <paramref name="fAP"/> every subgroup that disallows <paramref name="race"/> (or that has a
    /// non-empty allowed-races list not containing it). Mutates the pack in place.
    /// </summary>
    /// <returns><c>false</c> if any top-level position is emptied (pack incompatible with the race); otherwise <c>true</c>.</returns>
    private static bool PruneFlattenedAssetPackByRace(FlattenedAssetPack fAP, FormKey race)
    {
        foreach (var subgroupsAtPos in fAP.Subgroups)
        {
            for (int i = 0; i < subgroupsAtPos.Count; i++)
            {
                var currentSubgroup = subgroupsAtPos[i];

                if (currentSubgroup.DisallowedRaces.Contains(race) || (!currentSubgroup.AllowedRacesIsEmpty && !currentSubgroup.AllowedRaces.Contains(race)))
                {
                    subgroupsAtPos.RemoveAt(i);
                    i--;
                    continue;
                }
            }    

            if (subgroupsAtPos.Count == 0) // if there are no remaining subgroups within any top-level position, then the entire asset pack is incompatible with this race
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Groups a set of body-shape descriptor label signatures into a category-to-values dictionary
    /// (descriptor category =&gt; set of values within that category).
    /// </summary>
    public static Dictionary<string, HashSet<string>> BodyShapeDescriptorsToDictionary(HashSet<BodyShapeDescriptor.LabelSignature> BodyShapeDescriptors)
    {
        Dictionary<string, HashSet<string>> dict = new Dictionary<string, HashSet<string>>();
        foreach (var m in BodyShapeDescriptors)
        {
            if (!dict.ContainsKey(m.Category))
            {
                dict.Add(m.Category, new HashSet<string>());
            }
            if (!dict[m.Category].Contains(m.Value))
            {
                dict[m.Category].Add(m.Value);
            }
        }
        return dict;
    }

    /// <summary>
    /// Combines two morph-descriptor dictionaries per category: where both contain a category (and
    /// <paramref name="dict2"/>'s values are non-empty) the intersection of values is taken; otherwise the
    /// category's values are carried through unchanged. Categories present only in <paramref name="dict2"/>
    /// are appended.
    /// </summary>
    public static Dictionary<string, HashSet<string>> GetMorphDictionaryIntersection(Dictionary<string, HashSet<string>> dict1, Dictionary<string, HashSet<string>> dict2) 
    {
        Dictionary<string, HashSet<string>> output = new();
        foreach (var category in dict1.Keys)
        {
            if (dict2.ContainsKey(category) && dict2[category].Any())
            {
                output.Add(category, dict1[category].Intersect(dict2[category]).ToHashSet());
            }
            else
            {
                output.Add(category, dict1[category]);
            }
        }
        
        foreach (var category in dict2.Keys)
        {
            if (!output.ContainsKey(category))
            {
                output.Add(category, dict2[category]);
            }
        }

        return output;
    }

    /// <summary>
    /// Merges two morph-descriptor dictionaries by key, keeping the first occurrence's value set on key
    /// collision (no value-level union is performed).
    /// </summary>
    public static Dictionary<string, HashSet<string>> GetMorphDictionaryUnion(Dictionary<string, HashSet<string>> dict1, Dictionary<string, HashSet<string>> dict2)
    {
        return dict1.Union(dict2).GroupBy(g => g.Key).ToDictionary(pair => pair.Key, pair => pair.First().Value); 
    }

    /// <summary>
    /// Merges a sequence of dictionaries into one, keeping the first value seen for any duplicate key.
    /// </summary>
    public static Dictionary<K, V> MergeDictionaries<K, V>(IEnumerable<Dictionary<K, V>> dictionaries) where K: notnull // https://www.techiedelight.com/merge-dictionaries-csharp/
    {
        Dictionary<K, V> result = new Dictionary<K, V>();

        foreach (Dictionary<K, V> dict in dictionaries)
        {
            result = result.Union(dict)
                .GroupBy(g => g.Key)
                .ToDictionary(pair => pair.Key, pair => pair.First().Value);
        }

        return result;
    }

    /// <summary>
    /// Maps a list of subgroup IDs to their top-level position index in the asset pack hierarchy,
    /// producing a position =&gt; set-of-subgroup-IDs dictionary. IDs whose top-level index cannot be
    /// resolved are skipped.
    /// </summary>
    public Dictionary<int, HashSet<string>> RequiredOrExcludedSubgroupsToDictionary(List<string> sgList, List<AssetPack.Subgroup> subgroupHierarchy)
    {
        Dictionary<int, HashSet<string>> dict = new Dictionary<int, HashSet<string>>();

        foreach (string s in sgList)
        {
            int position = (GetSubgroupTopLevelIndex(s, subgroupHierarchy));
            if (position == -1)
            {
                continue;
            }
            if (position >= 0)
            {
                if(!dict.ContainsKey(position))
                {
                    dict.Add(position, new HashSet<string>());
                }
            }
            if (!dict[position].Contains(s))
            {
                dict[position].Add(s);
            }
        }

        return dict;
    }


    /// <summary>
    /// Returns the index of the top-level subgroup whose hierarchy contains <paramref name="subgroupID"/>,
    /// or -1 (with a logged error) if no top-level subgroup contains it.
    /// </summary>
    private int GetSubgroupTopLevelIndex(string subgroupID, List<AssetPack.Subgroup> subgroupHierarchy)
    {
        for (int i = 0; i < subgroupHierarchy.Count; i++)
        {
            if (CurrentSubgroupContainsID(subgroupID, subgroupHierarchy[i])) { return i; }
        }

        _logger.LogError("Error: DictionaryMapper.GetSubgroupTopLevelIndex() could not find the top-level subgroup of the subgroup with ID " + subgroupID + ". Please report this issue.");
        return -1;
    }

    /// <summary>
    /// Recursively tests whether <paramref name="currentSubgroup"/> or any of its descendants has the given
    /// <paramref name="subgroupID"/>.
    /// </summary>
    private static bool CurrentSubgroupContainsID(string subgroupID, AssetPack.Subgroup currentSubgroup)
    {
        if (currentSubgroup.ID == subgroupID) { return true; }
        foreach (var sg in currentSubgroup.Subgroups)
        {
            if (CurrentSubgroupContainsID(subgroupID, sg)) { return true; }
        }
        return false;
    }
}