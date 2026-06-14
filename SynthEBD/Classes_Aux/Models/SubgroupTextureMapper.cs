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
    /// Builds the (bodyPart, slot) → FilePathReplacement map for a fully-resolved
    /// <see cref="SubgroupCombination"/> (one valid distribution roll). Walks every
    /// contained <see cref="FlattenedSubgroup"/>'s already-inherited <c>Paths</c>,
    /// keeping the last-seen replacement per slot. This reflects only the subgroups the
    /// distribution simulator actually selected for the preview NPC — unlike a naive
    /// whole-pack dump, which would ignore the config's distribution rules and mix
    /// mutually-exclusive subgroups together. Drives the "Select from Config File" action.
    /// </summary>
    public Dictionary<(string bodyPart, int slot), FilePathReplacement> MapCombinationTextures(SubgroupCombination combination)
    {
        var result = new Dictionary<(string, int), FilePathReplacement>();
        if (combination?.ContainedSubgroups == null) return result;

        foreach (var subgroup in combination.ContainedSubgroups)
        {
            if (subgroup?.Paths == null) continue;
            foreach (var path in subgroup.Paths)
            {
                if (string.IsNullOrWhiteSpace(path.Destination) || string.IsNullOrWhiteSpace(path.Source)) continue;
                string? bodyPart = VM_CharacterViewer.ParseBodyPart(path.Destination);
                int? slot = VM_CharacterViewer.ParseTextureSlot(path.Destination);
                if (bodyPart == null || slot == null) continue;
                result[(bodyPart, slot.Value)] = path;
            }
        }
        return result;
    }

    /// <summary>
    /// Builds the neutral <see cref="MeshOverride"/> list for a selected subgroup:
    /// one per non-base biped armature it (or an ancestor) defines a
    /// <c>WorldModel.&lt;gender&gt;.File</c> for. The driving case is an
    /// auxiliary armature on a non-base slot (e.g. slot 52,
    /// <c>(BipedObjectFlag)4194304</c>) that some mods add to the actor only at
    /// runtime by script, so it never appears in the resolver's static WornArmor
    /// walk and must be synthesized from the config selection.
    ///
    /// <para>Base armatures (Body/Hands/Feet/Hair/Tail/Head) are intentionally
    /// excluded - those are already resolved onto the rendered NPC, and the
    /// texture-only channel handles their skin overrides. Only generic
    /// "Slot{n}" armatures (auxiliary / modded extra slots) become mesh
    /// overrides, so the base body is never double-rendered. Same-slot
    /// <c>SkinTexture.&lt;gender&gt;.*</c> paths in the selection are bundled
    /// onto the override; absent that, the renderer falls back to the NIF's own
    /// <c>BSShaderTextureSet</c>.</para>
    /// </summary>
    public List<MeshOverride> MapSubgroupMeshOverrides(VM_SubgroupPlaceHolder node, Gender gender)
    {
        if (node == null) return new List<MeshOverride>();

        // The mesh template may sit on the selected leaf or on an ancestor;
        // walk the chain leaf-first so the most-specific definition wins.
        var chain = new List<VM_SubgroupPlaceHolder> { node };
        chain.AddRange(node.GetParents());

        var paths = chain
            .Where(link => link?.AssociatedModel?.Paths != null)
            .SelectMany(link => link.AssociatedModel.Paths);
        return BuildMeshOverrides(paths, gender);
    }

    /// <summary>
    /// Mesh-override variant for a fully-resolved <see cref="SubgroupCombination"/>
    /// (one valid distribution roll). Each contained <see cref="FlattenedSubgroup"/>
    /// already carries its inherited <c>Paths</c>, so the union of every position's
    /// paths is the complete asset set the NPC would receive — the same input the
    /// real patcher acts on. Used by the "Select from Config File" action once the
    /// distribution simulator has produced a compatible combination.
    /// </summary>
    public List<MeshOverride> MapCombinationMeshOverrides(SubgroupCombination combination, Gender gender)
    {
        if (combination?.ContainedSubgroups == null) return new List<MeshOverride>();
        var paths = combination.ContainedSubgroups
            .Where(sg => sg?.Paths != null)
            .SelectMany(sg => sg.Paths);
        return BuildMeshOverrides(paths, gender);
    }

    /// <summary>
    /// Builds the neutral auxiliary-armature <see cref="MeshOverride"/> list from a flat
    /// set of <see cref="FilePathReplacement"/>s (gathered from a selected subgroup's
    /// parent chain, or from a rolled <see cref="SubgroupCombination"/>). Records the
    /// first-seen mesh source per non-base biped slot and bundles same-slot
    /// <c>SkinTexture.&lt;gender&gt;.*</c> paths onto it. See
    /// <see cref="MapSubgroupMeshOverrides"/> for the rationale on excluding base
    /// armatures and the gender gate.
    /// </summary>
    private List<MeshOverride> BuildMeshOverrides(IEnumerable<FilePathReplacement> paths, Gender gender)
    {
        var result = new List<MeshOverride>();
        string genderStr = gender == Gender.Female ? "Female" : "Male";

        // biped flag -> first-seen mesh source for that slot
        var meshBySlot = new Dictionary<int, string>();
        // biped flag -> (tex slot -> source), first-seen per (slot, texslot)
        var texBySlot = new Dictionary<int, Dictionary<int, string>>();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path.Destination) || string.IsNullOrWhiteSpace(path.Source)) continue;
            // Gender gate: only this preview NPC's gendered WorldModel/SkinTexture.
            if (!path.Destination.Contains("." + genderStr + ".", StringComparison.OrdinalIgnoreCase)) continue;

            int? flag = ParseBipedFlag(path.Destination);
            if (flag == null) continue;

            bool isMesh = path.Destination.Contains("WorldModel", StringComparison.OrdinalIgnoreCase)
                          && path.Destination.Contains("File", StringComparison.OrdinalIgnoreCase);
            bool isTex = path.Destination.Contains("SkinTexture", StringComparison.OrdinalIgnoreCase);

            if (isMesh)
            {
                if (!meshBySlot.ContainsKey(flag.Value)) meshBySlot[flag.Value] = path.Source;
            }
            else if (isTex)
            {
                int? texSlot = VM_CharacterViewer.ParseTextureSlot(path.Destination);
                if (texSlot == null) continue;
                if (!texBySlot.TryGetValue(flag.Value, out var d)) { d = new(); texBySlot[flag.Value] = d; }
                if (!d.ContainsKey(texSlot.Value)) d[texSlot.Value] = path.Source;
            }
        }

        foreach (var (flag, meshSource) in meshBySlot)
        {
            string? key = VM_CharacterViewer.BipedFlagToBodyPart(flag);
            // Only synthesize for non-base armatures (generic "Slot{n}"); base
            // body parts are already on the rendered NPC.
            if (key == null || !key.StartsWith("Slot", StringComparison.Ordinal)) continue;

            Dictionary<int, string>? textures = null;
            if (texBySlot.TryGetValue(flag, out var tex) && tex.Count > 0) textures = tex;

            result.Add(new MeshOverride
            {
                Key = key,
                MeshPath = meshSource,
                BipedSlots = flag,
                Kind = MeshOverrideKind.Skin,
                Textures = textures,
            });
        }

        return result;
    }

    /// <summary>Extracts the BipedObjectFlag bit a destination targets, handling
    /// both the named (<c>BipedObjectFlag.Body</c>) and the raw-cast
    /// (<c>(BipedObjectFlag)4194304</c>) forms the asset-pack DSL uses. Returns
    /// null when the destination encodes no biped flag.</summary>
    private static int? ParseBipedFlag(string destination)
    {
        if (destination.Contains("BipedObjectFlag.Body", StringComparison.OrdinalIgnoreCase)) return 4;
        if (destination.Contains("BipedObjectFlag.Hands", StringComparison.OrdinalIgnoreCase)) return 8;
        if (destination.Contains("BipedObjectFlag.Feet", StringComparison.OrdinalIgnoreCase)) return 128;
        if (destination.Contains("BipedObjectFlag.Hair", StringComparison.OrdinalIgnoreCase)) return 2;
        if (destination.Contains("BipedObjectFlag.Tail", StringComparison.OrdinalIgnoreCase)) return 1024;

        const string marker = "(BipedObjectFlag)";
        int mi = destination.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (mi >= 0)
        {
            int p = mi + marker.Length, start = p;
            while (p < destination.Length && char.IsDigit(destination[p])) p++;
            if (p > start && int.TryParse(destination.Substring(start, p - start), out int f)) return f;
        }
        return null;
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
