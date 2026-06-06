namespace SynthEBD;

/// <summary>
/// Runtime (flattened) counterpart of the <see cref="AssetReplacerGroup"/> settings model. Holds, per top-level
/// subgroup position, the list of bottom-level <see cref="FlattenedSubgroup"/>s the asset selector walks when
/// applying a replacer.
/// </summary>
public class FlattenedReplacerGroup
{
    /// <summary>Creates an empty flattened group named after the source <see cref="AssetReplacerGroup"/>; subgroups are populated later by <see cref="FlattenReplacerGroup"/>.</summary>
    public FlattenedReplacerGroup(AssetReplacerGroup source)
    {
        Name = source.Label;
        Source = new AssetPack();
    }

    /// <summary>Copy constructor that copies the name and source reference (used by <see cref="ShallowCopy"/>).</summary>
    public FlattenedReplacerGroup(FlattenedReplacerGroup source)
    {
        Name = source.Name;
        Source = source.Source;
    }

    /// <summary>The replacer group's name (from the source <see cref="AssetReplacerGroup.Label"/>).</summary>
    public string Name { get; set; }
    /// <summary>Per top-level position, the flattened bottom-level subgroups available for selection.</summary>
    public List<List<FlattenedSubgroup>> Subgroups { get; set; } = new();
    /// <summary>The owning asset pack this replacer group belongs to.</summary>
    public AssetPack Source { get; set; }

    /// <summary>
    /// Flattens an <see cref="AssetReplacerGroup"/> into its runtime form by flattening each top-level subgroup
    /// tree into a list of bottom-level <see cref="FlattenedSubgroup"/>s, and wires the result back to the parent asset pack.
    /// </summary>
    /// <param name="source">The settings-model replacer group to flatten.</param>
    /// <param name="raceGroupingList">Race groupings used to resolve allowed/disallowed race lists.</param>
    /// <param name="parentAssetPack">The flattened asset pack that owns this replacer group.</param>
    /// <param name="dictionaryMapper">Mapper used to translate subgroup references into id dictionaries.</param>
    /// <param name="patcherState">Current patcher state (general settings, race groupings, etc.).</param>
    public static FlattenedReplacerGroup FlattenReplacerGroup(AssetReplacerGroup source, List<RaceGrouping> raceGroupingList, FlattenedAssetPack parentAssetPack, DictionaryMapper dictionaryMapper, PatcherState patcherState)
    {
        var output = new FlattenedReplacerGroup(source);
        for (int i = 0; i < source.Subgroups.Count; i++)
        {
            var flattenedSubgroups = new List<FlattenedSubgroup>();
            FlattenedSubgroup.FlattenSubgroups(source.Subgroups[i], null, flattenedSubgroups, raceGroupingList, parentAssetPack.GroupName, i, source.Subgroups, parentAssetPack, dictionaryMapper, patcherState);
            output.Subgroups.Add(flattenedSubgroups);
        }
        output.Source = parentAssetPack.Source;

        return output;
    }
        
    /// <summary>Returns a shallow copy: a new group with copied name/source and fresh per-position subgroup lists holding the same <see cref="FlattenedSubgroup"/> references.</summary>
    public FlattenedReplacerGroup ShallowCopy()
    {
        var copy = new FlattenedReplacerGroup(this);
        foreach (var subgroupList in Subgroups)
        {
            copy.Subgroups.Add(new List<FlattenedSubgroup>(subgroupList));
        }
        return copy;
    }    
}