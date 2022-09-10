using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

public class DictionaryMapper
{
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

    public static Dictionary<string, HashSet<string>> GetMorphDictionaryUnion(Dictionary<string, HashSet<string>> dict1, Dictionary<string, HashSet<string>> dict2)
    {
        return dict1.Union(dict2).GroupBy(g => g.Key).ToDictionary(pair => pair.Key, pair => pair.First().Value); 
    }

    public static Dictionary<K, V> MergeDictionaries<K, V>(IEnumerable<Dictionary<K, V>> dictionaries) // https://www.techiedelight.com/merge-dictionaries-csharp/
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

    public static Dictionary<int, HashSet<string>> RequiredOrExcludedSubgroupsToDictionary(HashSet<string> sgList, List<AssetPack.Subgroup> subgroupHierarchy)
    {
        Dictionary<int, HashSet<string>> dict = new Dictionary<int, HashSet<string>>();

        foreach (string s in sgList)
        {
            int position = (GetSubgroupTopLevelIndex(s, subgroupHierarchy));
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


    private static int GetSubgroupTopLevelIndex(string subgroupID, List<AssetPack.Subgroup> subgroupHierarchy)
    {
        for (int i = 0; i < subgroupHierarchy.Count; i++)
        {
            if (CurrentSubgroupContainsID(subgroupID, subgroupHierarchy[i])) { return i; }
        }

        Logger.LogError("Error: DictionaryMapper.GetSubgroupTopLevelIndex() could not find the top-level subgroup of the subgroup with ID " + subgroupID + ". Please report this issue.");
        return -1;
    }

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