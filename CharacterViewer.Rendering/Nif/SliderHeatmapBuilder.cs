using System.Collections.Generic;
using System.Numerics;

namespace CharacterViewer.Rendering;

/// <summary>
/// Pure geometry/color logic for the Label-by-Sliders vertex highlight: turns a slider's sparse
/// per-vertex morph deltas into an interleaved triangle list (9 floats/vert: position.xyz +
/// normal.xyz + color.rgb) that <see cref="Gl.GlRenderer.SliderHeatmapTriangles"/> draws unlit.
/// Kept free of OpenGL / OpenTK types so it is unit-testable
/// (see SynthEBD.Tests.SliderHeatmapBuilderTests).
///
/// <para>Which vertices count as "moved": a slider's delta dictionary is already sparse (only moved
/// vertices are stored), but authored data can carry near-zero deltas, so a vertex is treated as
/// affected only when |delta| exceeds <see cref="DefaultMinDelta"/>. A triangle is emitted when
/// <b>any</b> of its three corners is affected, so the patch is a continuous surface with a cold
/// falloff at its boundary rather than a set of disconnected slivers. Color grades |delta| against
/// the slider's own maximum |delta| through <see cref="Ramp"/> (cold = barely moved, hot = the
/// vertex this slider moves most). The set and grade are direction-agnostic: Small vs Big only
/// scales this one geometric morph, so the moved-vertex set is identical at any weight.</para>
/// </summary>
public static class SliderHeatmapBuilder
{
    /// <summary>Magnitude (in mesh units) below which an authored delta is treated as "not moved",
    /// filtering near-zero authored noise. Stated in the OBody.AnnotatorShowMovedVertices tooltip.</summary>
    public const float DefaultMinDelta = 1e-4f;

    /// <summary>
    /// Appends the heatmap triangles for one shape to <paramref name="output"/> and returns the number
    /// of triangles emitted. <paramref name="positions"/> are the shape's current (deformed) vertex
    /// positions; <paramref name="indices"/> are flat triangle triplets local to that array;
    /// <paramref name="deltas"/> maps vertexIndex → morph offset for the designated slider. Emits
    /// nothing (returns 0) when no vertex is affected above <paramref name="minDelta"/>.
    /// </summary>
    public static int Build(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<int> indices,
        IReadOnlyDictionary<ushort, Vector3> deltas,
        List<float> output,
        float minDelta = DefaultMinDelta)
    {
        if (positions == null || indices == null || deltas == null || output == null) return 0;
        int n = positions.Count;
        if (n == 0 || indices.Count < 3 || deltas.Count == 0) return 0;

        // Per-vertex magnitude of this slider's morph, and the max over affected vertices (the ramp
        // normalizer). Vertices below the threshold keep magnitude 0 and read as "not moved".
        var mag = new float[n];
        float maxMag = 0f;
        foreach (var kv in deltas)
        {
            int idx = kv.Key;
            if (idx < 0 || idx >= n) continue;
            float m = kv.Value.Length();
            if (m <= minDelta) continue;
            mag[idx] = m;
            if (m > maxMag) maxMag = m;
        }
        if (maxMag <= 0f) return 0; // nothing this slider actually moves on this shape

        float invMax = 1f / maxMag;
        int emitted = 0;
        for (int t = 0; t + 2 < indices.Count; t += 3)
        {
            int a = indices[t], b = indices[t + 1], c = indices[t + 2];
            if ((uint)a >= (uint)n || (uint)b >= (uint)n || (uint)c >= (uint)n) continue;
            if (mag[a] <= 0f && mag[b] <= 0f && mag[c] <= 0f) continue; // no affected corner

            EmitVertex(output, positions[a], mag[a] * invMax, a, b, c, positions);
            EmitVertex(output, positions[b], mag[b] * invMax, b, c, a, positions);
            EmitVertex(output, positions[c], mag[c] * invMax, c, a, b, positions);
            emitted++;
        }
        return emitted;
    }

    /// <summary>Emits one 9-float vertex: position, the triangle's flat normal (carried for VAO layout
    /// parity even though the heatmap shader is unlit), and the ramp color for <paramref name="t"/>.
    /// The flat normal is derived from the vertex and its two triangle-mates so every corner of a
    /// triangle writes the same normal.</summary>
    private static void EmitVertex(List<float> output, Vector3 p, float t, int self, int m1, int m2,
        IReadOnlyList<Vector3> positions)
    {
        Vector3 nrm = Vector3.Cross(positions[m1] - positions[self], positions[m2] - positions[self]);
        float len = nrm.Length();
        nrm = len > 1e-12f ? nrm / len : new Vector3(0f, 1f, 0f);

        Vector3 col = Ramp(t);
        output.Add(p.X); output.Add(p.Y); output.Add(p.Z);
        output.Add(nrm.X); output.Add(nrm.Y); output.Add(nrm.Z);
        output.Add(col.X); output.Add(col.Y); output.Add(col.Z);
    }

    /// <summary>Maps a normalized magnitude <paramref name="t"/> ∈ [0,1] to a cold→hot RGB ramp
    /// (blue → cyan → green → yellow → red). t is clamped. Public for unit tests.</summary>
    public static Vector3 Ramp(float t)
    {
        if (t <= 0f) return new Vector3(0.10f, 0.20f, 0.90f); // cold blue
        if (t >= 1f) return new Vector3(0.95f, 0.10f, 0.10f); // hot red

        // Four equal segments across the classic jet-style ramp.
        // 0.00 blue -> 0.25 cyan -> 0.50 green -> 0.75 yellow -> 1.00 red
        if (t < 0.25f) return Lerp(new Vector3(0.10f, 0.20f, 0.90f), new Vector3(0.10f, 0.85f, 0.90f), t / 0.25f);
        if (t < 0.50f) return Lerp(new Vector3(0.10f, 0.85f, 0.90f), new Vector3(0.15f, 0.85f, 0.20f), (t - 0.25f) / 0.25f);
        if (t < 0.75f) return Lerp(new Vector3(0.15f, 0.85f, 0.20f), new Vector3(0.95f, 0.90f, 0.10f), (t - 0.50f) / 0.25f);
        return Lerp(new Vector3(0.95f, 0.90f, 0.10f), new Vector3(0.95f, 0.10f, 0.10f), (t - 0.75f) / 0.25f);
    }

    private static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
}
