using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace SynthEBD;

/// <summary>
/// The mesh state a <see cref="BodySlideMeasurementEvaluator"/> evaluation reads: current (deformed) vertex
/// positions, bone info, zeroed (sliders-0) reference positions, triangle indices and per-shape vertex counts.
/// <see cref="FromViewer"/> reads the live <see cref="VM_CharacterViewer"/>; the Match Presets scan's headless
/// path builds one per (preset, weight) from <see cref="HeadlessBodyDeformer"/> output instead, so the evaluator
/// is identical for both and only the source of positions differs.
/// </summary>
public sealed class MeasurementMeshSource
{
    /// <summary>Deformed position of one vertex, or null when the shape or index isn't available.</summary>
    public required MeasurementMath.VertexLookup Vertex { get; init; }

    /// <summary>All deformed positions of a shape. Callers treat the array as read-only.</summary>
    public required MeasurementMath.ShapePositionsLookup Positions { get; init; }

    /// <summary>Per-vertex bone indices and weights of a shape (bone-transition criteria only).</summary>
    public required MeasurementMath.ShapeBoneInfoLookup Bones { get; init; }

    /// <summary>A shape's sliders-0 positions at an NPC weight (region resolution, Coordinate key vertices).</summary>
    public required Func<string, int, Vector3[]?> Zeroed { get; init; }

    /// <summary>A shape's triangle index buffer (region resolution).</summary>
    public required Func<string, int[]?> Indices { get; init; }

    /// <summary>Vertex count per loaded shape (topology fingerprint check).</summary>
    public required Func<Dictionary<string, int>> ShapeVertexCounts { get; init; }

    /// <summary>The NPC weight being measured (regions are resolved at it when the caller passes none).</summary>
    public int NpcWeight { get; init; }

    /// <summary>A source reading the viewer's current state, exactly as evaluations always have.</summary>
    public static MeasurementMeshSource FromViewer(VM_CharacterViewer viewer) => new()
    {
        Vertex = (shape, idx) => viewer.TryGetCurrentVertex(shape, idx, out var p) ? p : null,
        Positions = shape => viewer.GetShapePositions(shape),
        Bones = shape => viewer.GetShapeBoneInfo(shape),
        Zeroed = (shape, weight) => viewer.GetZeroedShapePositions(shape, weight),
        Indices = shape => viewer.GetShapeIndices(shape),
        ShapeVertexCounts = () => viewer.GetCurrentShapeVertexCounts(),
        NpcWeight = viewer.NpcWeight,
    };
}
