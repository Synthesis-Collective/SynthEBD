using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mutagen.Bethesda;
using OpenTK.Mathematics;

namespace SynthEBD;

/// <summary>
/// The Match Presets scan's headless path: measures (preset, weight) rows without driving the viewer.
///
/// The live path applies every preset to the viewer (all body shapes deformed, normals, GPU upload, a UI yield,
/// and the editor's Live-column refresh on every apply) and then reads the result back. This path captures the
/// scene once (<see cref="HeadlessBodyDeformer"/>: pre-skinned base meshes and per-slider offset tables for only
/// the shapes the profile measures) and evaluates rows in parallel on worker threads, each with its own profile
/// copy because key-vertex resolution writes <see cref="NamedKeyVertex.VertexIndex"/>. Values match the live
/// path up to float rounding.
///
/// Shapes the profile references but the viewer doesn't deform (static meshes) are snapshotted once: a morph
/// never moves them, so the snapshot is what the live path would read too.
/// </summary>
public sealed class HeadlessMeasurementScan
{
    /// <summary>One (preset, weight) to measure. <see cref="NamesAllowlist"/> null = every measurement.</summary>
    public readonly record struct Row(BodySlideSetting Preset, Gender Gender, int Weight, HashSet<string>? NamesAllowlist);

    private readonly HeadlessBodyDeformer _deformer;
    private readonly Dictionary<string, Vector3[]> _staticPositions;
    private readonly Dictionary<string, (int[]? BoneIndices, float[]? BoneWeights)> _bones;
    private readonly Dictionary<string, int[]?> _indices;
    private readonly Dictionary<string, Vector3[]> _zeroed0;
    private readonly Dictionary<string, int> _vertexCounts;
    private readonly BodyTypeProfile _profile;

    private static readonly MorphSet NoMorphs = new();

    private HeadlessMeasurementScan(HeadlessBodyDeformer deformer, BodyTypeProfile profile,
        Dictionary<string, Vector3[]> staticPositions, Dictionary<string, (int[]?, float[]?)> bones,
        Dictionary<string, int[]?> indices, Dictionary<string, Vector3[]> zeroed0, Dictionary<string, int> vertexCounts)
    {
        _deformer = deformer;
        _profile = profile;
        _staticPositions = staticPositions;
        _bones = bones;
        _indices = indices;
        _zeroed0 = zeroed0;
        _vertexCounts = vertexCounts;
    }

    /// <summary>The shapes a profile reads: every key vertex's shape and every region's shape.</summary>
    public static HashSet<string> ReferencedShapes(BodyTypeProfile profile)
    {
        var shapes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in profile.KeyVertices ?? new())
            if (!string.IsNullOrEmpty(kv?.ShapeName)) shapes.Add(kv.ShapeName);
        foreach (var r in profile.Regions ?? new())
            if (!string.IsNullOrEmpty(r?.ShapeName)) shapes.Add(r.ShapeName);
        return shapes;
    }

    /// <summary>Captures what the scan needs from <paramref name="viewer"/>'s current scene. Must run on the
    /// viewer's thread. Null when the viewer can't supply a deformer for any referenced shape (no scene, scene
    /// mid-rebuild, BodySlide disabled) -- the caller then falls back to the live path.</summary>
    public static HeadlessMeasurementScan? TryCreate(VM_CharacterViewer viewer, BodyTypeProfile profile, IEnumerable<BodySlideSetting> presets)
    {
        if (viewer == null || profile == null) return null;
        var shapes = ReferencedShapes(profile);
        if (shapes.Count == 0) return null;
        var deformer = viewer.CreateHeadlessDeformer(shapes, presets);
        if (deformer == null) return null;

        var deformable = new HashSet<string>(deformer.ShapeNames, StringComparer.OrdinalIgnoreCase);
        var staticPositions = new Dictionary<string, Vector3[]>(StringComparer.OrdinalIgnoreCase);
        var bones = new Dictionary<string, (int[]?, float[]?)>(StringComparer.OrdinalIgnoreCase);
        var indices = new Dictionary<string, int[]?>(StringComparer.OrdinalIgnoreCase);
        var zeroed0 = new Dictionary<string, Vector3[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var shape in shapes)
        {
            bones[shape] = viewer.GetShapeBoneInfo(shape);
            indices[shape] = viewer.GetShapeIndices(shape);
            if (viewer.BodyShape().GetZeroedShapePositions(shape, 0) is { } z) zeroed0[shape] = z;
            if (!deformable.Contains(shape) && viewer.GetShapePositions(shape) is { } p) staticPositions[shape] = p;
        }
        return new HeadlessMeasurementScan(deformer, profile, staticPositions, bones, indices, zeroed0,
            viewer.GetCurrentShapeVertexCounts());
    }

    /// <summary>
    /// Measures <paramref name="rows"/> in parallel. Returns one result per row, in order; a row is null when it
    /// was skipped by cancellation or threw (the first error is reported through <paramref name="firstError"/>).
    /// Rules are not evaluated (the scan re-derives descriptors from the cache afterwards).
    /// <paramref name="progress"/> receives the completed-row count every few rows.
    /// </summary>
    public BodySlideMeasurementEvaluator.EvaluationResult?[] Run(
        IReadOnlyList<Row> rows,
        IReadOnlyDictionary<int, Dictionary<string, RegionVolumeEvaluator.ResolvedRegion>>? regionsByWeight,
        CancellationToken ct,
        IProgress<int>? progress,
        out Exception? firstError)
    {
        var results = new BodySlideMeasurementEvaluator.EvaluationResult?[rows.Count];
        int completed = 0;
        Exception? error = null;
        Parallel.For(0, rows.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
            () => JSONhandler<BodyTypeProfile>.CloneViaJSON(_profile),
            (i, state, profile) =>
            {
                if (ct.IsCancellationRequested) { state.Stop(); return profile; }
                try
                {
                    var row = rows[i];
                    Dictionary<string, RegionVolumeEvaluator.ResolvedRegion>? regions = null;
                    regionsByWeight?.TryGetValue(row.Weight, out regions);
                    results[i] = BodySlideMeasurementEvaluator.Evaluate(
                        BuildSource(row), profile,
                        includeDrafts: true,
                        evaluationGender: row.Gender,
                        measurementNamesAllowlist: row.NamesAllowlist,
                        skipRules: true,
                        resolvedRegions: regions);
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref error, ex, null);
                }
                int done = Interlocked.Increment(ref completed);
                if (done % 25 == 0) progress?.Report(done);
                return profile;
            },
            _ => { });
        progress?.Report(completed);
        firstError = error;
        return results;
    }

    /// <summary>The mesh state for one row: each deformable shape deformed once, on demand, then shared by every
    /// lookup in the row.</summary>
    private MeasurementMeshSource BuildSource(Row row)
    {
        var morphs = SynthEbdViewerHostState.ToMorphSet(row.Preset);
        var osdKey = SynthEbdViewerHostState.HeadlessOsdKey(row.Preset?.SliderGroup);
        var deformed = new Dictionary<string, Vector3[]?>(StringComparer.OrdinalIgnoreCase);
        Vector3[]? Positions(string shape)
        {
            if (string.IsNullOrEmpty(shape)) return null;
            if (!deformed.TryGetValue(shape, out var p))
            {
                p = _deformer.Deform(shape, morphs, row.Weight, osdKey);
                if (p == null) _staticPositions.TryGetValue(shape, out p);
                deformed[shape] = p;
            }
            return p;
        }
        return new MeasurementMeshSource
        {
            Vertex = (shape, idx) => Positions(shape) is { } p && idx >= 0 && idx < p.Length ? p[idx] : null,
            Positions = Positions,
            Bones = shape => shape != null && _bones.TryGetValue(shape, out var b) ? b : (null, null),
            Zeroed = (shape, weight) => weight == 0 && shape != null && _zeroed0.TryGetValue(shape, out var z)
                ? z
                : _deformer.Deform(shape, NoMorphs, weight),
            Indices = shape => shape != null && _indices.TryGetValue(shape, out var ix) ? ix : null,
            ShapeVertexCounts = () => _vertexCounts,
            NpcWeight = row.Weight,
        };
    }
}
