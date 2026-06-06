namespace SynthEBD;

/// <summary>
/// Static helper that normalizes the source asset paths inside flattened asset packs before patching,
/// stripping any leading plugin-name folder (e.g. "MyMod.esp\") and any configured <see cref="TrimPath"/>
/// prefix so paths line up with the game's loose-file layout.
/// </summary>
public class PathTrimmer
{
    /// <summary>
    /// Trims path prefixes from every source path in the given flattened asset packs (both the main
    /// subgroups and the asset replacer groups). Removes a leading plugin folder via
    /// <see cref="PathStartsWithPlugin"/>, then removes any matching <paramref name="trimPaths"/> prefix
    /// whose extension matches the path. Mutates the <c>Source</c> strings in place.
    /// </summary>
    /// <param name="assetPacks">Flattened asset packs whose subgroup paths are normalized in place.</param>
    /// <param name="trimPaths">Extension-scoped prefixes to strip from matching source paths.</param>
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

    /// <summary>
    /// Determines whether the first segment of <paramref name="path"/> is a plugin file name
    /// (ends in .esm/.esp/.esl). If so, outputs that segment plus its trailing separator via
    /// <paramref name="toRemove"/> so it can be stripped.
    /// </summary>
    /// <param name="path">The source path to inspect.</param>
    /// <param name="toRemove">The leading "plugin.ext\" prefix to remove, or null if none.</param>
    /// <returns>True if the path begins with a plugin folder.</returns>
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