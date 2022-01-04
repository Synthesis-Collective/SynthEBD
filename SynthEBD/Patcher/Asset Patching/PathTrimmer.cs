using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class PathTrimmer
    {
        public static void TrimFlattenedAssetPacks(HashSet<FlattenedAssetPack> assetPacks, HashSet<TrimPath> trimPaths)
        {
            foreach (var ap in assetPacks)
            {
                foreach (var subgroupsAtIndex in ap.Subgroups)
                {
                    foreach (var subgroup in subgroupsAtIndex)
                    {
                        foreach(var path in subgroup.Paths)
                        {
                            var matchedTrimPath = trimPaths.Where(x => path.Source.EndsWith(x.Extension, StringComparison.CurrentCultureIgnoreCase)).FirstOrDefault();
                            if (matchedTrimPath != null && path.Source.StartsWith(matchedTrimPath.PathToTrim, StringComparison.CurrentCultureIgnoreCase))
                            {
                                path.Source = path.Source.Remove(0, matchedTrimPath.PathToTrim.Length + 1); // +1 to account for subsequent \\
                            }
                        }
                    }
                }

                foreach (var replacer in ap.AssetReplacerGroups)
                {
                    foreach (var subgroupsAtIndex in replacer.Subgroups)
                    {
                        foreach (var subgroup in subgroupsAtIndex)
                        {
                            foreach (var path in subgroup.Paths)
                            {
                                var matchedTrimPath = trimPaths.Where(x => path.Source.EndsWith(x.Extension, StringComparison.CurrentCultureIgnoreCase)).FirstOrDefault();
                                if (matchedTrimPath != null && path.Source.StartsWith(matchedTrimPath.PathToTrim, StringComparison.CurrentCultureIgnoreCase))
                                {
                                    path.Source = path.Source.Remove(0, matchedTrimPath.PathToTrim.Length + 1); // +1 to account for subsequent \\
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
