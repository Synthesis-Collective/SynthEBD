using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CharacterViewer.Rendering;

/// <summary>
/// The key-vertex, region, measurement and slider-heatmap overlay channels of the body-shape
/// authoring tools, drawn through the renderer's overlay primitives. All positions are in the
/// renderer's pre-ModelScale mesh-local space (the space of <see cref="GlMesh.CpuPositions"/>), so
/// the overlays track the model through ModelScale changes. Every channel is owned by the host
/// tool that fills it and cleared by it on deselect.
/// </summary>
public sealed class BodyShapeOverlay : IRenderOverlay
{
    /// <summary>Thick line width (px) for <see cref="MeasurementLines"/> and
    /// <see cref="RegionOverlayLines"/>. Reads at typical zoom levels against busy textures while
    /// staying under the typical driver-clamped maximum (5–10 for aliased lines).</summary>
    public const float ThickLineWidth = 4.5f;

    /// <summary>Key-vertex marker spheres (the key-vertex picking workflow).</summary>
    public List<Vector3> KeyVertexMarkers { get; } = new();

    /// <summary>RGB color for the key-vertex marker spheres.</summary>
    public Vector3 KeyVertexMarkerColor { get; set; } = new Vector3(1.0f, 0.38f, 0.15f);

    /// <summary>Indices into <see cref="KeyVertexMarkers"/> drawn in
    /// <see cref="KeyVertexMarkerSelectedColor"/>, so the user can tell which marker matches the
    /// selected pick row.</summary>
    public HashSet<int> SelectedKeyVertexMarkerIndices { get; } = new();

    /// <summary>RGB color for selected key-vertex marker spheres.</summary>
    public Vector3 KeyVertexMarkerSelectedColor { get; set; } = new Vector3(0.25f, 1.0f, 0.35f);

    /// <summary>Bounding-box-resolved markers, in a colour distinct from the static picks.</summary>
    public List<Vector3> BoxResolvedMarkers { get; } = new();

    /// <summary>RGB color for bounding-box-resolved markers. Yellow matches the Pick Box UI accent.</summary>
    public Vector3 BoxResolvedMarkerColor { get; set; } = new Vector3(1.0f, 0.80f, 0.25f);

    /// <summary>Transient preview marker for the key-vertex row selected in the editor.</summary>
    public List<Vector3> PreviewKeyVertexMarkers { get; } = new();

    /// <summary>RGB color for the preview marker (the same green as the selected-pick color).</summary>
    public Vector3 PreviewKeyVertexMarkerColor { get; set; } = new Vector3(0.25f, 1.0f, 0.35f);

    /// <summary>Pending-box pick preview: the vertices the current pending box and criterion
    /// would resolve to if confirmed now.</summary>
    public List<Vector3> PreviewPickMarkers { get; } = new();

    /// <summary>RGB color for the pending-box pick preview spheres (purple).</summary>
    public Vector3 PreviewPickMarkerColor { get; set; } = new Vector3(0.78f, 0.30f, 1.0f);

    /// <summary>Boundary-loop vertex markers of the selected region; the matching loop edges are
    /// in <see cref="RegionOverlayLines"/>.</summary>
    public List<Vector3> RegionCapMarkers { get; } = new();

    /// <summary>RGB color for the region cap-loop markers (cyan).</summary>
    public Vector3 RegionCapMarkerColor { get; set; } = new Vector3(0.20f, 0.90f, 1.0f);

    /// <summary>The selected region's closed surface as a 6-float (position + normal) triangle
    /// list, drawn lit and always on top so it reads as a solid seen through the model.</summary>
    public List<float> RegionSolidTriangles { get; } = new();

    /// <summary>RGB color for the region solid (magenta).</summary>
    public Vector3 RegionSolidColor { get; set; } = new Vector3(1.0f, 0.10f, 0.85f);

    /// <summary>Surface patches of the regions the displayed measurements use, as a 6-float
    /// triangle list drawn translucent and depth-tested, so it tints only the visible surface.</summary>
    public List<float> MeasurementRegionTriangles { get; } = new();

    /// <summary>RGB color of the measurement-region tint when
    /// <see cref="MeasurementRegionColorRanges"/> is empty.</summary>
    public Vector3 MeasurementRegionColor { get; set; } = new Vector3(0.20f, 0.90f, 1.0f);

    /// <summary>Optional per-range colours for <see cref="MeasurementRegionTriangles"/>, in
    /// vertices. When non-empty, only the listed ranges are drawn.</summary>
    public List<(int FirstVertex, int VertexCount, Vector3 Color)> MeasurementRegionColorRanges { get; } = new();

    /// <summary>Opacity of the measurement-region tint (0..1).</summary>
    public float MeasurementRegionAlpha { get; set; } = 0.45f;

    /// <summary>Slider-morph heatmap: the vertices a slider moves, as a 9-float
    /// (position + normal + colour) triangle list coloured per vertex by morph magnitude, drawn
    /// unlit and always on top.</summary>
    public List<float> SliderHeatmapTriangles { get; } = new();

    /// <summary>Radius of each marker sphere before ModelScale. Small enough not to obscure
    /// neighbouring vertices on a dense mesh, while still readable at typical zooms.</summary>
    public float KeyVertexMarkerRadius { get; set; } = 0.275f;

    /// <summary>Line segments visualising the selected measurement.</summary>
    public List<OverlayLineSegment> MeasurementLines { get; } = new();

    /// <summary>Boundary-loop edges of the selected region, drawn at the thick width.</summary>
    public List<OverlayLineSegment> RegionOverlayLines { get; } = new();

    /// <summary>The region solid's triangle edges, drawn thin so they read as a wireframe.</summary>
    public List<OverlayLineSegment> RegionWireLines { get; } = new();

    /// <summary>Line width (px) for <see cref="RegionWireLines"/>.</summary>
    public float RegionWireWidth { get; set; } = 1.25f;

    public void Draw(GlRenderer renderer, in OverlayContext context)
    {
        // Marker pass: everything here is depth-test-off except the measurement-region tint.
        if ((KeyVertexMarkers.Count != 0
             || BoxResolvedMarkers.Count != 0
             || PreviewKeyVertexMarkers.Count != 0
             || PreviewPickMarkers.Count != 0
             || RegionCapMarkers.Count != 0
             || RegionSolidTriangles.Count != 0
             || MeasurementRegionTriangles.Count != 0
             || SliderHeatmapTriangles.Count != 0)
            && renderer.BeginOverlaySurfacePass(context, out var surface))
        {
            // Measurement-region tint first: depth-tested, so it only shades visible surface, and
            // everything that follows layers over it.
            renderer.DrawOverlayTranslucentTint(MeasurementRegionTriangles, MeasurementRegionColor,
                MeasurementRegionAlpha, MeasurementRegionColorRanges);

            // Region solid next, so the cap markers/edges layer on top of it.
            renderer.DrawOverlaySolidTriangles(RegionSolidTriangles, RegionSolidColor);

            renderer.DrawOverlayMarkers(KeyVertexMarkers, KeyVertexMarkerColor, KeyVertexMarkerRadius,
                SelectedKeyVertexMarkerIndices, KeyVertexMarkerSelectedColor);
            renderer.DrawOverlayMarkers(BoxResolvedMarkers, BoxResolvedMarkerColor, KeyVertexMarkerRadius);
            renderer.DrawOverlayMarkers(PreviewKeyVertexMarkers, PreviewKeyVertexMarkerColor, KeyVertexMarkerRadius);
            renderer.DrawOverlayMarkers(PreviewPickMarkers, PreviewPickMarkerColor, KeyVertexMarkerRadius);
            renderer.DrawOverlayMarkers(RegionCapMarkers, RegionCapMarkerColor, KeyVertexMarkerRadius);

            renderer.SetOverlayShaded(false);

            // Slider heatmap last: it switches to its own shader and VAO.
            renderer.DrawOverlayVertexColorTriangles(SliderHeatmapTriangles, context);

            renderer.EndOverlaySurfacePass(surface);
        }

        // Line pass, after the markers so connections between endpoint markers read clearly.
        if ((MeasurementLines.Count != 0 || RegionOverlayLines.Count != 0 || RegionWireLines.Count != 0)
            && renderer.BeginOverlayLinePass(context, out var lines))
        {
            renderer.DrawOverlayLines(MeasurementLines, ThickLineWidth, context);
            renderer.DrawOverlayLines(RegionOverlayLines, ThickLineWidth, context);
            renderer.DrawOverlayLines(RegionWireLines, RegionWireWidth, context);
            renderer.EndOverlayLinePass(lines);
        }
    }
}
