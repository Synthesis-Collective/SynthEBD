using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SynthEBD;

/// <summary>
/// Applies BodySlide slider deformations to mesh vertex positions using OSD
/// data and preset slider values.
///
/// Algorithm (from BodySlide source — BodySlideApp::ComputeMorphedShapeData):
///   1. Start from base vertex positions
///   2. Apply slider diffs with Big values  → vertsHigh
///   3. Apply slider diffs with Small values → vertsLow
///   4. Interpolate: final = vertsHigh * (weight/100) + vertsLow * ((100-weight)/100)
///
/// OSD deltas are in NIF coordinate space (Z-up). Mesh positions in the viewer
/// are in HelixToolkit Y-up space (X stays, Y=Z_nif, Z=-Y_nif). The deltas
/// are converted to Y-up before application.
/// </summary>
public class BodySlideDeformer
{
    private readonly Logger _logger;

    public BodySlideDeformer(Logger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Applies BodySlide deformations to mesh positions in-place.
    /// </summary>
    /// <param name="positions">Vertex positions in Y-up (HelixToolkit) space. Modified in-place.</param>
    /// <param name="preset">The BodySlide preset containing slider names and Big/Small values.</param>
    /// <param name="weight">NPC weight (0–100) for Big/Small interpolation.</param>
    /// <param name="osdFiles">Parsed OSD files containing vertex deltas for this body type.</param>
    /// <param name="shapeName">The target shape name to match against OSD file ShapeNames. If null, applies all OSD files.</param>
    public void ApplyDeformation(
        Vector3[] positions,
        BodySlideSetting preset,
        int weight,
        List<OsdFile> osdFiles,
        string? shapeName = null)
    {
        if (positions == null || positions.Length == 0 || preset == null || osdFiles == null || osdFiles.Count == 0)
        {
            return;
        }

        weight = Math.Clamp(weight, 0, 100);

        // Build a combined lookup: slider data name → vertex deltas
        // from all OSD files matching the target shape
        var sliderDeltaMap = BuildSliderDeltaMap(osdFiles, shapeName);

        if (sliderDeltaMap.Count == 0)
        {
            _logger.LogMessage("CharacterViewer: No matching OSD slider data found for shape '" +
                (shapeName ?? "(any)") + "'");
            return;
        }

        ApplyFromSliderDeltaMap(positions, preset, weight, sliderDeltaMap, shapeName, sourceLabel: "OSD");
    }

    /// <summary>
    /// Applies BodySlide deformations using a body .tri file. Unlike the OSD
    /// path, .tri morph names are direct slider names (no LCP stripping needed)
    /// and the delta indices are authored against the sibling NIF's exact
    /// topology -- so a matching NIF should see zero out-of-range deltas.
    /// </summary>
    /// <param name="positions">Vertex positions in Y-up (HelixToolkit) space. Modified in-place.</param>
    /// <param name="preset">The BodySlide preset containing slider names and Big/Small values.</param>
    /// <param name="weight">NPC weight (0–100) for Big/Small interpolation.</param>
    /// <param name="triFile">Parsed body .tri for the NIF being deformed.</param>
    /// <param name="shapeName">The target shape to select inside the .tri. If null, uses the first shape.</param>
    public void ApplyDeformationFromTri(
        Vector3[] positions,
        BodySlideSetting preset,
        int weight,
        BodyTriFile triFile,
        string? shapeName = null)
    {
        if (positions == null || positions.Length == 0 || preset == null || triFile == null || triFile.Shapes.Count == 0)
        {
            return;
        }

        weight = Math.Clamp(weight, 0, 100);

        var sliderDeltaMap = BuildSliderDeltaMapFromTri(triFile, shapeName);
        if (sliderDeltaMap.Count == 0)
        {
            _logger.LogMessage("CharacterViewer: No matching .tri morph data found for shape '" +
                (shapeName ?? "(any)") + "' in '" + triFile.FilePath + "'");
            return;
        }

        ApplyFromSliderDeltaMap(positions, preset, weight, sliderDeltaMap, shapeName, sourceLabel: "TRI");
    }

    /// <summary>
    /// Shared core: given a slider-name → sparse-vertex-delta map, iterates the preset's
    /// Big/Small values and writes interpolated offsets into <paramref name="positions"/>.
    /// </summary>
    private void ApplyFromSliderDeltaMap(
        Vector3[] positions,
        BodySlideSetting preset,
        int weight,
        Dictionary<string, Dictionary<ushort, Vector3>> sliderDeltaMap,
        string? shapeName,
        string sourceLabel)
    {
        int vertCount = positions.Length;
        int slidersApplied = 0;
        int vertsModified = 0;

        // Topology-mismatch diagnostic. Counts in/out-of-range delta applications
        // and the OSD's highest referenced vertex index across all applied sliders.
        // If osdMaxIndex + 1 > vertCount (or deltasOutOfRange > 0) the target mesh
        // has fewer verts than the OSD was authored for — classic ref-mesh mismatch.
        int deltasInRange = 0;
        int deltasOutOfRange = 0;
        int osdMaxIndex = -1;

        // We need separate high/low accumulators for weight interpolation.
        // Start from a copy of the original positions, accumulate Big diffs → high,
        // Small diffs → low, then interpolate.
        var deltasHigh = new Vector3[vertCount]; // accumulated Big diffs (all start at zero)
        var deltasLow = new Vector3[vertCount];  // accumulated Small diffs

        // Track which vertices were touched
        var touchedVerts = new HashSet<int>();

        foreach (var kvp in preset.SliderValues)
        {
            string sliderName = kvp.Key;
            BodySlideSlider slider = kvp.Value;

            if (slider.Big == 0 && slider.Small == 0)
            {
                continue;
            }

            if (!sliderDeltaMap.TryGetValue(sliderName, out var deltas))
            {
                continue;
            }

            float bigPercent = slider.Big / 100f;
            float smallPercent = slider.Small / 100f;

            foreach (var delta in deltas)
            {
                int vertIndex = delta.Key;
                if (vertIndex > osdMaxIndex) osdMaxIndex = vertIndex;
                if (vertIndex >= vertCount)
                {
                    deltasOutOfRange++;
                    continue;
                }
                deltasInRange++;

                // Convert OSD delta from NIF Z-up to HelixToolkit Y-up:
                // X stays, Y = Z_nif, Z = -Y_nif
                Vector3 nifDelta = delta.Value;
                Vector3 yUpDelta = new Vector3(nifDelta.X, nifDelta.Z, -nifDelta.Y);

                if (slider.Big != 0)
                {
                    deltasHigh[vertIndex] += yUpDelta * bigPercent;
                }

                if (slider.Small != 0)
                {
                    deltasLow[vertIndex] += yUpDelta * smallPercent;
                }

                touchedVerts.Add(vertIndex);
            }

            slidersApplied++;
        }

        if (touchedVerts.Count == 0)
        {
            // Diagnostic: dump enough about both sides of the mismatch to see whether
            // the preset has zero non-zero sliders, has slider names that just don't
            // appear in the OSD, or something subtler (whitespace / casing / prefix).
            int presetSliderCount = preset.SliderValues?.Count ?? 0;
            int presetActiveCount = 0;
            var presetSampleNames = new List<string>();
            if (preset.SliderValues != null)
            {
                foreach (var kvp in preset.SliderValues)
                {
                    if (kvp.Value != null && (kvp.Value.Big != 0 || kvp.Value.Small != 0))
                    {
                        presetActiveCount++;
                        if (presetSampleNames.Count < 5) presetSampleNames.Add(kvp.Key);
                    }
                }
            }
            var osdSampleNames = sliderDeltaMap.Keys.Take(5).ToList();
            _logger.LogMessage("CharacterViewer: BodySlide preset '" + preset.Label +
                "' matched 0 vertices (no slider data overlap). " +
                "Preset sliders: " + presetSliderCount + " total, " + presetActiveCount + " with non-zero Big/Small. " +
                "OSD slider keys available: " + sliderDeltaMap.Count + ". " +
                "Preset sample: [" + string.Join(", ", presetSampleNames) + "]. " +
                "OSD sample: [" + string.Join(", ", osdSampleNames) + "]");
            return;
        }

        // Apply weight interpolation:
        // final = (basePos + deltasHigh) * (weight/100) + (basePos + deltasLow) * ((100-weight)/100)
        //       = basePos + deltasHigh * (weight/100) + deltasLow * ((100-weight)/100)
        float weightHigh = weight / 100f;
        float weightLow = (100 - weight) / 100f;

        foreach (int vi in touchedVerts)
        {
            Vector3 combinedDelta = deltasHigh[vi] * weightHigh + deltasLow[vi] * weightLow;
            Vector3 original = positions[vi];
            positions[vi] = original + combinedDelta;
        }

        vertsModified = touchedVerts.Count;

        // Topology-mismatch flag: the OSD refers to a vertex index the target mesh
        // doesn't have. This is the signature of "OSD authored for reference NIF X,
        // applied to different NIF Y" — the deltas that DO fit land on the wrong
        // verts (anatomically-similar but not identical), producing chopped bands.
        bool topologyMismatch = deltasOutOfRange > 0 || (osdMaxIndex >= 0 && osdMaxIndex + 1 != vertCount);

        _logger.LogMessage("CharacterViewer: Applied preset '" + preset.Label +
            "' to shape '" + (shapeName ?? "(any)") + "' via " + sourceLabel +
            " (" + slidersApplied + " sliders, " + vertsModified + " vertices modified, weight=" + weight + ")" +
            " | target verts=" + vertCount +
            ", " + sourceLabel + " max vertIndex=" + osdMaxIndex +
            " (implies " + (osdMaxIndex + 1) + "-vert ref mesh)" +
            ", deltas applied=" + deltasInRange +
            ", deltas skipped OOR=" + deltasOutOfRange +
            (topologyMismatch ? " [TOPOLOGY MISMATCH]" : " [topology OK]"));
    }

    /// <summary>
    /// Recalculates face normals after deformation. Computes area-weighted
    /// vertex normals from triangle face normals.
    /// </summary>
    public static void RecalculateNormals(Vector3[] positions, int[] indices, Vector3[] normals)
    {
        if (positions == null || indices == null || normals == null)
        {
            return;
        }

        int vertCount = positions.Length;

        // Zero out all normals
        for (int i = 0; i < vertCount; i++)
        {
            normals[i] = Vector3.Zero;
        }

        // Accumulate face normals (area-weighted via cross product magnitude)
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];

            if (i0 >= vertCount || i1 >= vertCount || i2 >= vertCount)
            {
                continue;
            }

            Vector3 v0 = positions[i0];
            Vector3 v1 = positions[i1];
            Vector3 v2 = positions[i2];

            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 faceNormal = Vector3.Cross(edge1, edge2);

            // The cross product magnitude is proportional to triangle area,
            // giving natural area-weighted normals
            normals[i0] += faceNormal;
            normals[i1] += faceNormal;
            normals[i2] += faceNormal;
        }

        // Normalize
        for (int i = 0; i < vertCount; i++)
        {
            Vector3 n = normals[i];
            float length = n.Length();
            normals[i] = length > 1e-8f ? n / length : Vector3.UnitY;
        }
    }

    /// <summary>
    /// Builds a combined lookup of slider data name → vertex deltas from all
    /// matching OSD files. OSD files are matched by ShapeName if specified.
    /// Uses case-insensitive key matching for slider names.
    ///
    /// BodySlide OSD files store slider entries as `&lt;shape&gt;&lt;slider&gt;` concatenations
    /// (e.g. `3BA RefAreolaSize`), but preset XMLs use canonical names (`AreolaSize`). We
    /// compute the longest common prefix across each file's slider names -- that's the shape
    /// tag -- and strip it before keying the dictionary, so preset lookups match.
    /// </summary>
    /// <summary>
    /// Selects one shape from the .tri and returns its morph-name → vertex-delta
    /// lookup. Matching is by case-insensitive substring (same rule as OSD). If
    /// <paramref name="shapeName"/> is null or nothing matches, falls back to the
    /// first shape in the file.
    /// </summary>
    private Dictionary<string, Dictionary<ushort, Vector3>> BuildSliderDeltaMapFromTri(
        BodyTriFile triFile, string? shapeName)
    {
        var map = new Dictionary<string, Dictionary<ushort, Vector3>>(StringComparer.OrdinalIgnoreCase);
        if (triFile.Shapes.Count == 0) return map;

        BodyTriShape selected = triFile.Shapes[0];
        if (!string.IsNullOrEmpty(shapeName))
        {
            foreach (var shape in triFile.Shapes)
            {
                if (shape.ShapeName.Contains(shapeName, StringComparison.OrdinalIgnoreCase) ||
                    shapeName.Contains(shape.ShapeName, StringComparison.OrdinalIgnoreCase))
                {
                    selected = shape;
                    break;
                }
            }
        }

        foreach (var morph in selected.Morphs)
        {
            if (string.IsNullOrWhiteSpace(morph.Name)) continue;
            map[morph.Name] = morph.VertexDeltas;
        }

        return map;
    }

    private Dictionary<string, Dictionary<ushort, Vector3>> BuildSliderDeltaMap(
        List<OsdFile> osdFiles, string? shapeName)
    {
        var map = new Dictionary<string, Dictionary<ushort, Vector3>>(StringComparer.OrdinalIgnoreCase);

        foreach (var osd in osdFiles)
        {
            // If a shape name filter is specified, check if this OSD file's shape matches
            if (shapeName != null &&
                !osd.ShapeName.Contains(shapeName, StringComparison.OrdinalIgnoreCase) &&
                !shapeName.Contains(osd.ShapeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rawNames = new List<string>(osd.Sliders.Count);
            foreach (var s in osd.Sliders)
            {
                if (!string.IsNullOrWhiteSpace(s?.Name)) rawNames.Add(s.Name);
            }
            string lcp = ComputeLongestCommonPrefix(rawNames);
            int minLen = int.MaxValue;
            foreach (var n in rawNames) if (n.Length < minLen) minLen = n.Length;
            if (lcp.Length >= minLen) lcp = "";

            foreach (var slider in osd.Sliders)
            {
                if (string.IsNullOrWhiteSpace(slider?.Name)) continue;
                var unprefixed = lcp.Length > 0 && slider.Name.StartsWith(lcp, StringComparison.Ordinal)
                    ? slider.Name.Substring(lcp.Length)
                    : slider.Name;
                if (string.IsNullOrWhiteSpace(unprefixed)) continue;
                // Later OSD files override earlier ones for the same slider name
                map[unprefixed] = slider.VertexDeltas;
            }
        }

        return map;
    }

    private static string ComputeLongestCommonPrefix(IList<string> names)
    {
        if (names == null || names.Count <= 1) return "";
        string prefix = names[0];
        for (int i = 1; i < names.Count; i++)
        {
            var name = names[i];
            int j = 0;
            int max = Math.Min(prefix.Length, name.Length);
            while (j < max && prefix[j] == name[j]) j++;
            prefix = prefix.Substring(0, j);
            if (prefix.Length == 0) break;
        }
        return prefix;
    }
}
