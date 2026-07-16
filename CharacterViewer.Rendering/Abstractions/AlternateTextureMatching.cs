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

    /// <summary>Resolves the effective per-shape texture-slot map for one shape:
    /// every entry naming the shape applies (in list order, later wins per slot);
    /// when none does, entries from <paramref name="danglingNameEntries"/> whose
    /// 3D Index equals <paramref name="shapeOrdinal"/> apply instead. Returns
    /// null when no entry targets the shape. Applied entries are added to
    /// <paramref name="consumed"/> (so a caller can report never-applied entries)
    /// and index-fallback applications also to
    /// <paramref name="appliedByIndexFallback"/> (so a caller can log them).</summary>
    public static Dictionary<int, string>? MatchForShape(
        IReadOnlyList<AlternateTextureSpec> specs,
        IReadOnlyList<AlternateTextureSpec>? danglingNameEntries,
        string shapeName, int shapeOrdinal,
        ISet<AlternateTextureSpec>? consumed = null,
        ICollection<AlternateTextureSpec>? appliedByIndexFallback = null)
    {
        Dictionary<int, string>? merged = null;
        foreach (var spec in specs)
        {
            if (spec.ShapeName.Length == 0
                || !string.Equals(spec.ShapeName, shapeName, StringComparison.OrdinalIgnoreCase))
                continue;
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
