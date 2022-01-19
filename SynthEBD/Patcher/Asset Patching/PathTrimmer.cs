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
                            if(PathStartsWithPlugin(path.Source, out string toRemove))
                            {
                                path.Source = path.Source.Remove(0, toRemove.Length);
                            }

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

        private static bool PathStartsWithPlugin(string path, out string toRemove)
        {
            toRemove = null;
            var split = path.Split(System.IO.Path.DirectorySeparatorChar);
            if (split.Length == 0)
            {
                return false;
            }
            else
            {
                var fileSplit = split[0].Split('.');
                if (fileSplit.Length != 2)
                {
                    return false;
                }
                else if (string.Equals(fileSplit[1], "esm", StringComparison.OrdinalIgnoreCase) || string.Equals(fileSplit[1], "esp", StringComparison.OrdinalIgnoreCase) || string.Equals(fileSplit[1], "esl", StringComparison.OrdinalIgnoreCase))
                {
                    toRemove = fileSplit[0] + "." + fileSplit[1] + System.IO.Path.DirectorySeparatorChar;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
    }
}
