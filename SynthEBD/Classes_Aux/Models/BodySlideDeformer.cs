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

        int vertCount = positions.Length;
        int slidersApplied = 0;
        int vertsModified = 0;

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
                if (vertIndex >= vertCount)
                {
                    continue;
                }

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

        _logger.LogMessage("CharacterViewer: Applied preset '" + preset.Label +
            "' (" + slidersApplied + " sliders, " + vertsModified + " vertices modified, weight=" + weight + ")");
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
    /// </summary>
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

            foreach (var slider in osd.Sliders)
            {
                // Later OSD files override earlier ones for the same slider name
                map[slider.Name] = slider.VertexDeltas;
            }
        }

        return map;
    }
}
