using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>
/// Produces the texture-override map that <see cref="VM_CharacterViewer.ApplyTextureOverrides"/>
/// consumes, and resolves the effective race set for a subgroup by walking its
/// parent chain. Used by the TexMesh Render preview flow in <see cref="VM_AssetPresenter"/>.
/// </summary>
public class SubgroupTextureMapper
{
    /// <summary>
    /// Builds a (bodyPart, slot) → FilePathReplacement map for the given subgroup's
    /// own Paths. Only entries that the viewer knows how to render (body parts Head/
    /// Body/Hands/Feet and slots 0/1/2/3/7) are included. Delegates destination
    /// parsing to <see cref="VM_CharacterViewer.ParseBodyPart"/>/
    /// <see cref="VM_CharacterViewer.ParseTextureSlot"/> so both sides stay in sync.
    /// </summary>
    public Dictionary<(string bodyPart, int slot), FilePathReplacement> MapSubgroupTextures(VM_SubgroupPlaceHolder node)
    {
        var result = new Dictionary<(string, int), FilePathReplacement>();
        if (node?.AssociatedModel?.Paths == null) return result;

        foreach (var path in node.AssociatedModel.Paths)
        {
            if (string.IsNullOrWhiteSpace(path.Destination) || string.IsNullOrWhiteSpace(path.Source)) continue;
            string? bodyPart = VM_CharacterViewer.ParseBodyPart(path.Destination);
            int? slot = VM_CharacterViewer.ParseTextureSlot(path.Destination);
            if (bodyPart == null || slot == null) continue;
            result[(bodyPart, slot.Value)] = path;
        }
        return result;
    }

    /// <summary>
    /// Walks every subgroup in the given pack (deep, top-down, definition order)
    /// and records the FIRST FilePathReplacement seen for each (bodyPart, slot).
    /// Used by the "Select from Config File" action.
    /// </summary>
    public Dictionary<(string bodyPart, int slot), FilePathReplacement> MapAssetPackTextures(VM_AssetPack pack)
    {
        var result = new Dictionary<(string, int), FilePathReplacement>();
        if (pack?.Subgroups == null) return result;
        foreach (var top in pack.Subgroups)
        {
            MergeFirstSeen(top, result);
        }
        return result;
    }

    private void MergeFirstSeen(VM_SubgroupPlaceHolder node, Dictionary<(string, int), FilePathReplacement> sink)
    {
        if (node?.AssociatedModel?.Paths != null)
        {
            foreach (var path in node.AssociatedModel.Paths)
            {
                if (string.IsNullOrWhiteSpace(path.Destination) || string.IsNullOrWhiteSpace(path.Source)) continue;
                string? bodyPart = VM_CharacterViewer.ParseBodyPart(path.Destination);
                int? slot = VM_CharacterViewer.ParseTextureSlot(path.Destination);
                if (bodyPart == null || slot == null) continue;
                var key = (bodyPart, slot.Value);
                if (!sink.ContainsKey(key)) sink[key] = path;
            }
        }
        foreach (var child in node.Subgroups)
        {
            MergeFirstSeen(child, sink);
        }
    }

    /// <summary>
    /// Walks the subgroup's parent chain and computes the effective allowed race set.
    /// Starts with the subgroup's own Allowed* merged set; intersects with each
    /// ancestor's non-empty Allowed* set; subtracts every level's Disallowed* set.
    /// Returns an empty set when no constraints are present at any level (caller
    /// should fall back to the Default preview NPC pair).
    /// </summary>
    public HashSet<FormKey> ResolveEffectiveRaces(VM_SubgroupPlaceHolder node, List<RaceGrouping> groupings)
    {
        if (node == null) return new HashSet<FormKey>();

        var chain = new List<VM_SubgroupPlaceHolder> { node };
        chain.AddRange(node.GetParents());

        HashSet<FormKey>? allowed = null;
        var disallowed = new HashSet<FormKey>();

        foreach (var link in chain)
        {
            var m = link.AssociatedModel;
            var linkAllowed = RaceGrouping.MergeRaceAndGroupingList(m.AllowedRaceGroupings, groupings, m.AllowedRaces);
            var linkDisallowed = RaceGrouping.MergeRaceAndGroupingList(m.DisallowedRaceGroupings, groupings, m.DisallowedRaces);

            if (linkAllowed.Count > 0)
            {
                allowed = allowed == null ? new HashSet<FormKey>(linkAllowed) : new HashSet<FormKey>(allowed.Intersect(linkAllowed));
            }
            disallowed.UnionWith(linkDisallowed);
        }

        if (allowed == null) return new HashSet<FormKey>();
        allowed.ExceptWith(disallowed);
        return allowed;
    }
}
