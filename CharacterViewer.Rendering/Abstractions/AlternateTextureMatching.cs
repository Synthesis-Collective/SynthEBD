using System;
using System.Collections.Generic;
using System.Linq;

namespace CharacterViewer.Rendering;

/// <summary>
/// Matching rules for binding a record's AlternateTextures (MODS) entries to a
/// mesh's shapes, kept pure/GL-free so the exact semantics the renderer uses
/// (<c>VM_CharacterViewer.ApplyOneMeshOverride</c>) can be unit-tested.
///
/// <para>An entry carries two identity fields for its target shape (see
/// <see cref="AlternateTextureSpec"/>): the 3D Name and the 3D Index. Neither
/// alone is reliable in wild data, and they fail in opposite directions:</para>
/// <list type="bullet">
///   <item><b>Name goes stale on rebuild.</b> BodySlide/Outfit Studio output
///   renames shapes (project shape names differ from the shipped mesh's) while
///   preserving their order. The engine still applies the entry — it keys on
///   the index — so name-only matching renders such variants in game and the
///   CK but not in the preview (untextured/black outfit pieces).</item>
///   <item><b>Index desyncs on block re-sort.</b> A shape's ordinal here is its
///   file block order (nifly <c>GetShapes()</c>), which matches the record's
///   index for Outfit Studio-written files but can be desynced by block-sorting
///   tools — observed in the field as an <c>[Index == 1]</c> entry whose named
///   shape is the third geometry in the file. Those tools reorder without
///   renaming, so the name stays good exactly when the index goes bad.</item>
/// </list>
/// <para>Hence: match by name first, and only entries whose name matches NO
/// shape of the mesh fall back to index matching. An entry whose name binds
/// elsewhere never index-hijacks a second shape.</para>
///
/// <para><b>Duplicate shape names.</b> A name match is one-to-many when a mesh
/// has two shapes with the same name (BodySlide output can produce them), and
/// applying the entry to both is wrong: the engine gives it to exactly one. So
/// when the name is ambiguous AND the entry's 3D Index picks out one of the
/// same-named shapes, the index breaks the tie
/// (<see cref="BuildShapeOrdinalsByName"/>). If the index matches none of them
/// — the block-re-sort desync above — every same-named shape still gets it,
/// because over-applying beats an entry that binds to nothing.</para>
/// </summary>
public static class AlternateTextureMatching
{
    /// <summary>Entries whose 3D Name matches none of <paramref name="shapeNames"/>
    /// (including unnamed entries) — the pool eligible for the 3D-index fallback.
    /// Compute once per mesh and pass to every <see cref="MatchForShape"/> call.</summary>
    public static List<AlternateTextureSpec> DanglingNameEntries(
        IReadOnlyList<AlternateTextureSpec> specs, IEnumerable<string> shapeNames)
    {
        var names = new HashSet<string>(shapeNames, StringComparer.OrdinalIgnoreCase);
        return specs.Where(s => s.ShapeName.Length == 0 || !names.Contains(s.ShapeName)).ToList();
    }

    /// <summary>
    /// Ordinals per shape name, but ONLY for names borne by more than one shape — and null when
    /// the mesh has no duplicates at all, which is the overwhelming majority. Compute once per mesh
    /// and pass to every <see cref="MatchForShape"/> call; passing null keeps pure name matching.
    ///
    /// <para>Returning null rather than an empty map is deliberate: it makes "this mesh has no
    /// ambiguity" a single reference check per shape instead of a dictionary probe, and it means the
    /// disambiguation code below provably cannot alter the result for a normal mesh.</para>
    ///
    /// <para><paramref name="shapeNames"/> must be enumerated in shape-ordinal order — the same
    /// order the ordinals passed to <see cref="MatchForShape"/> come from.</para>
    /// </summary>
    public static Dictionary<string, List<int>>? BuildShapeOrdinalsByName(IEnumerable<string> shapeNames)
    {
        Dictionary<string, List<int>>? byName = null;
        int ordinal = 0;
        foreach (var name in shapeNames)
        {
            if (!string.IsNullOrEmpty(name))
            {
                byName ??= new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                if (!byName.TryGetValue(name, out var ordinals))
                {
                    byName[name] = ordinals = new List<int>(1);
                }
                ordinals.Add(ordinal);
            }
            ordinal++;
        }

        if (byName == null) return null;

        // Drop the unambiguous names; if nothing is left, say so with null.
        foreach (var name in byName.Keys.Where(k => byName[k].Count < 2).ToList())
        {
            byName.Remove(name);
        }
        return byName.Count > 0 ? byName : null;
    }

    /// <summary>Resolves the effective per-shape texture-slot map for one shape:
    /// every entry naming the shape applies (in list order, later wins per slot);
    /// when none does, entries from <paramref name="danglingNameEntries"/> whose
    /// 3D Index equals <paramref name="shapeOrdinal"/> apply instead. Returns
    /// null when no entry targets the shape. Applied entries are added to
    /// <paramref name="consumed"/> (so a caller can report never-applied entries)
    /// and index-fallback applications also to
    /// <paramref name="appliedByIndexFallback"/> (so a caller can log them).</summary>
    /// <param name="shapeOrdinalsByName">
    /// From <see cref="BuildShapeOrdinalsByName"/> — the duplicate-name index only. Null (or a mesh
    /// with no duplicated names) leaves name matching exactly as it was.
    /// </param>
    /// <param name="skippedByNameAmbiguity">
    /// Receives entries this shape declined because their 3D Index named a DIFFERENT shape of the
    /// same name. Purely for logging; without it a shape silently losing a TXST it used to get looks
    /// like a regression.
    /// </param>
    public static Dictionary<int, string>? MatchForShape(
        IReadOnlyList<AlternateTextureSpec> specs,
        IReadOnlyList<AlternateTextureSpec>? danglingNameEntries,
        string shapeName, int shapeOrdinal,
        ISet<AlternateTextureSpec>? consumed = null,
        ICollection<AlternateTextureSpec>? appliedByIndexFallback = null,
        IReadOnlyDictionary<string, List<int>>? shapeOrdinalsByName = null,
        ICollection<AlternateTextureSpec>? skippedByNameAmbiguity = null)
    {
        // Only non-null for a mesh that actually has same-named shapes, and only for those names.
        List<int>? sameNamedOrdinals = null;
        shapeOrdinalsByName?.TryGetValue(shapeName, out sameNamedOrdinals);

        Dictionary<int, string>? merged = null;
        foreach (var spec in specs)
        {
            if (spec.ShapeName.Length == 0
                || !string.Equals(spec.ShapeName, shapeName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Ambiguous name: the engine binds this entry to ONE shape, by index. Only step aside
            // when the index actually picks out one of the same-named shapes and it is not this one
            // — if it matches none of them (block-re-sort desync) every one of them keeps the entry,
            // because an entry bound to nothing is worse than an entry bound twice.
            if (sameNamedOrdinals != null
                && spec.ShapeIndex >= 0
                && spec.ShapeIndex != shapeOrdinal
                && sameNamedOrdinals.Contains(spec.ShapeIndex))
            {
                skippedByNameAmbiguity?.Add(spec);
                continue;
            }

            merged ??= new Dictionary<int, string>();
            foreach (var kv in spec.Textures) merged[kv.Key] = kv.Value;
            consumed?.Add(spec);
        }

        if (merged == null && danglingNameEntries is { Count: > 0 } && shapeOrdinal >= 0)
        {
            foreach (var spec in danglingNameEntries)
            {
                if (spec.ShapeIndex < 0 || spec.ShapeIndex != shapeOrdinal) continue;
                merged ??= new Dictionary<int, string>();
                foreach (var kv in spec.Textures) merged[kv.Key] = kv.Value;
                consumed?.Add(spec);
                appliedByIndexFallback?.Add(spec);
            }
        }

        return merged;
    }
}
