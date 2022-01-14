using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
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

        public static Dictionary<string, HashSet<string>> BodyShapeDescriptorsToDictionary(HashSet<BodyShapeDescriptor> BodyShapeDescriptors)
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
            var resultDict = dict1.Where(x => dict2.ContainsKey(x.Key)).ToDictionary(x => x.Key, x => x.Value.Intersect(dict2[x.Key]).ToHashSet()); //https://stackoverflow.com/questions/10685142/c-sharp-dictionaries-intersect
            return resultDict.Where(f => f.Value.Count > 0).ToDictionary(x => x.Key, x => x.Value); // https://stackoverflow.com/questions/16340818/remove-item-from-dictionary-where-value-is-empty-list
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
                int position = (getSubgroupTopLevelIndex(s, subgroupHierarchy));
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


        private static int getSubgroupTopLevelIndex(string subgroupID, List<AssetPack.Subgroup> subgroupHierarchy)
        {
            for (int i = 0; i < subgroupHierarchy.Count; i++)
            {
                if (currentSubgroupContainsID(subgroupID, subgroupHierarchy[i])) { return i; }
            }

            // Warn User
            return -1;
        }

        private static bool currentSubgroupContainsID(string subgroupID, AssetPack.Subgroup currentSubgroup)
        {
            if (currentSubgroup.id == subgroupID) { return true; }
            foreach (var sg in currentSubgroup.subgroups)
            {
                if (currentSubgroupContainsID(subgroupID, sg)) { return true; }
            }
            return false;
        }
    }
}
