using System;
using System.Collections.Generic;
using System.Numerics;

namespace CharacterViewer.Rendering;

/// <summary>
/// Deforms body shapes for measurement without the viewer: no GL, no normals, no render buffers, no scene
/// queues. Produces the same post-skinning positions <see cref="VM_CharacterViewer.ApplyMorphSet"/> writes
/// to a mesh's CPU positions, for batch work such as a body-type profile's measurement scan.
///
/// <para><b>Why it is fast and still exact.</b> The viewer computes, per shape,
/// <c>skin(lerp(base0, base1, t) + Σ_k c_k · D_k)</c>, where <c>D_k</c> is slider k's morph offset and
/// <c>c_k = Big_k/100 · t + Small_k/100 · (1 - t)</c>. Linear blend skinning is affine per vertex and the
/// weight lerp is an affine combination, so that equals
/// <c>lerp(skin(base0), skin(base1), t) + Σ_k c_k · skinOffset(D_k)</c>. Both skinned bases and every
/// slider's skinned offsets (<see cref="NifMeshBuilder.SkinOffset"/>) are computed once at construction;
/// a preset is then one pass over the base plus a sparse sum. Results match the viewer up to float rounding
/// (the order of operations differs), for any skeleton pose.</para>
///
/// <para>Delta-source rules mirror the viewer: a body .tri, when present, is used for every slider (no
/// per-slider OSD fallback); otherwise OSD files are used, registered per source key with
/// <see cref="PrepareOsdSource"/>. If any applied slider references a vertex past the shape's vertex count
/// (a topology mismatch) the shape is left at its base, as the viewer does.</para>
///
/// <para>Thread-safe after construction and preparation: <see cref="Deform"/> only reads.</para>
/// </summary>
public sealed class HeadlessBodyDeformer
{
    private sealed class SliderTable
    {
        public int[] Indices = Array.Empty<int>();
        public Vector3[] SkinnedOffsets = Array.Empty<Vector3>();
        public bool OutOfRange;
    }

    private sealed class ShapeTables
    {
        public string ShapeName = "";
        public int VertexCount;
        public Vector3[]? SkinnedW0, SkinnedW1;   // weight endpoints, when the shape has a _0/_1 pair
        public Vector3[] SkinnedBase = Array.Empty<Vector3>(); // otherwise the bind-pose base
        public NifMeshBuilder.SkinningInfo? Skinning;
        public Dictionary<string, SliderTable>? TriSliders;
        public readonly Dictionary<string, Dictionary<string, SliderTable>> OsdSliders = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<string, ShapeTables> _shapes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when a body .tri drives the deformation (OSD sources are then never consulted).</summary>
    public bool UsesTri { get; }

    /// <summary>The shapes this deformer can deform, by the name the viewer caches them under.</summary>
    public IReadOnlyCollection<string> ShapeNames => _shapes.Keys;

    internal HeadlessBodyDeformer(IEnumerable<(string ShapeName, NifMeshBuilder.BuiltMesh Mesh)> shapes, BodyTriFile? bodyTri)
    {
        UsesTri = bodyTri != null;
        foreach (var (name, mesh) in shapes)
        {
            if (mesh == null || string.IsNullOrEmpty(name) || _shapes.ContainsKey(name)) continue;
            var basePositions = mesh.BindPosePositions ?? mesh.Positions;
            if (basePositions == null || basePositions.Length == 0) continue;

            var t = new ShapeTables { ShapeName = name, VertexCount = basePositions.Length, Skinning = mesh.Skinning };
            var w0 = mesh.Weight0BindPosePositions;
            var w1 = mesh.Weight1BindPosePositions;
            if (w0 != null && w1 != null && w0.Length == basePositions.Length && w1.Length == basePositions.Length)
            {
                t.SkinnedW0 = Skin(w0, mesh.Skinning);
                t.SkinnedW1 = Skin(w1, mesh.Skinning);
            }
            else
            {
                t.SkinnedBase = Skin(basePositions, mesh.Skinning);
            }
            if (bodyTri != null && bodyTri.Shapes.Count > 0)
                t.TriSliders = BuildTables(BodySlideDeformer.BuildSliderDeltaMapFromTri(bodyTri, name), t);
            _shapes[name] = t;
        }
    }

    /// <summary>Registers the OSD delta source used for presets whose source key is <paramref name="sourceKey"/>
    /// (SynthEBD keys it by slider group). Ignored when <see cref="UsesTri"/>. Call before any concurrent
    /// <see cref="Deform"/>; a key registered twice keeps its first tables.</summary>
    public void PrepareOsdSource(string sourceKey, List<OsdFile>? osdFiles)
    {
        if (UsesTri || sourceKey == null || osdFiles == null || osdFiles.Count == 0) return;
        foreach (var t in _shapes.Values)
        {
            if (t.OsdSliders.ContainsKey(sourceKey)) continue;
            t.OsdSliders[sourceKey] = BuildTables(BodySlideDeformer.BuildSliderDeltaMap(osdFiles, t.ShapeName), t);
        }
    }

    /// <summary>
    /// Post-skinning positions of <paramref name="shapeName"/> with <paramref name="morphs"/> applied at NPC
    /// weight <paramref name="weight"/>, in the viewer's Y-up, pre-ModelScale local space. Null when the shape
    /// isn't one this deformer was built for. With no delta source for the preset (no .tri and no prepared OSD
    /// source for <paramref name="osdSourceKey"/>) the undeformed base is returned.
    /// </summary>
    public OpenTK.Mathematics.Vector3[]? Deform(string shapeName, MorphSet morphs, int weight, string? osdSourceKey = null)
    {
        if (string.IsNullOrEmpty(shapeName) || !_shapes.TryGetValue(shapeName, out var t)) return null;
        weight = Math.Clamp(weight, 0, 100);

        int n = t.VertexCount;
        var output = new OpenTK.Mathematics.Vector3[n];
        if (t.SkinnedW0 != null && t.SkinnedW1 != null)
        {
            float lt = weight / 100f;
            for (int i = 0; i < n; i++)
            {
                var p = Vector3.Lerp(t.SkinnedW0[i], t.SkinnedW1[i], lt);
                output[i] = new OpenTK.Mathematics.Vector3(p.X, p.Y, p.Z);
            }
        }
        else
        {
            for (int i = 0; i < n; i++)
            {
                var p = t.SkinnedBase[i];
                output[i] = new OpenTK.Mathematics.Vector3(p.X, p.Y, p.Z);
            }
        }

        Dictionary<string, SliderTable>? sliders = t.TriSliders;
        if (sliders == null && osdSourceKey != null) t.OsdSliders.TryGetValue(osdSourceKey, out sliders);
        if (sliders == null || morphs?.Sliders == null) return output;

        // Same selection as BodySlideDeformer.ApplyFromSliderDeltaMap: skip all-zero sliders and sliders the
        // source doesn't carry; any applied slider reaching past the mesh leaves the whole shape at its base.
        float weightHigh = weight / 100f;
        float weightLow = (100 - weight) / 100f;
        var applied = new List<(SliderTable Table, float Coefficient)>();
        foreach (var kvp in morphs.Sliders)
        {
            var slider = kvp.Value;
            if (slider.Big == 0 && slider.Small == 0) continue;
            if (!sliders.TryGetValue(kvp.Key, out var table)) continue;
            if (table.OutOfRange) return output;
            applied.Add((table, slider.Big / 100f * weightHigh + slider.Small / 100f * weightLow));
        }
        foreach (var (table, c) in applied)
        {
            var idx = table.Indices;
            var off = table.SkinnedOffsets;
            for (int i = 0; i < idx.Length; i++)
            {
                var d = off[i];
                ref var o = ref output[idx[i]];
                o.X += d.X * c; o.Y += d.Y * c; o.Z += d.Z * c;
            }
        }
        return output;
    }

    private static Vector3[] Skin(Vector3[] source, NifMeshBuilder.SkinningInfo? skinning)
    {
        var positions = new Vector3[source.Length];
        Array.Copy(source, positions, source.Length);
        if (skinning != null)
            NifMeshBuilder.ApplySkinning(positions, Array.Empty<Vector3>(), skinning, positions, Array.Empty<Vector3>());
        return positions;
    }

    /// <summary>Per-slider sparse tables of skinned Y-up offsets (OSD/.tri deltas are NIF Z-up: X stays,
    /// Y = Z_nif, Z = -Y_nif, as in the viewer).</summary>
    private static Dictionary<string, SliderTable> BuildTables(Dictionary<string, Dictionary<ushort, Vector3>> map, ShapeTables t)
    {
        var tables = new Dictionary<string, SliderTable>(StringComparer.OrdinalIgnoreCase);
        foreach (var (slider, deltas) in map)
        {
            var table = new SliderTable();
            var idx = new List<int>(deltas.Count);
            var off = new List<Vector3>(deltas.Count);
            foreach (var (vi, nif) in deltas)
            {
                if (vi >= t.VertexCount) { table.OutOfRange = true; continue; }
                var yUp = new Vector3(nif.X, nif.Z, -nif.Y);
                idx.Add(vi);
                off.Add(t.Skinning != null ? NifMeshBuilder.SkinOffset(t.Skinning, t.VertexCount, vi, yUp) : yUp);
            }
            table.Indices = idx.ToArray();
            table.SkinnedOffsets = off.ToArray();
            tables[slider] = table;
        }
        return tables;
    }
}
