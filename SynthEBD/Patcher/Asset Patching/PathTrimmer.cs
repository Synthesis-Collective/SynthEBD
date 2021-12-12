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
                            if (matchedTrimPath != null)
                            {
                                path.Source = path.Source.Replace(matchedTrimPath.PathToTrim + "\\", "", StringComparison.CurrentCultureIgnoreCase);
                            }
                        }
                    }
                }
            }
        }
    }
}
