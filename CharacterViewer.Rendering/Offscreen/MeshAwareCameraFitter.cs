using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Mathematics;
using NumericsVec3 = System.Numerics.Vector3;

namespace CharacterViewer.Rendering.Offscreen;

/// <summary>
/// Computes orbit-camera framing from a <see cref="CameraFraming.MeshAware"/>
/// spec against a loaded <see cref="VM_CharacterViewer"/>'s scene. Walks the
/// renderer's loaded shapes, applies the per-shape selectors / vertex
/// filters / paddings, unions the resulting bboxes, and writes
/// <c>vm.Camera.Distance / Target / Azimuth / Elevation</c> so the union
/// fits inside the requested framing band at the given viewport aspect.
///
/// <para>The offscreen renderer applies this internally on each render. The
/// live preview UC (NPC Plugin Chooser 2's mugshot preview, SynthEBD's
/// CharacterViewer toolbar in interactive mode, etc.) calls this directly
/// to keep the on-screen camera in lockstep with what the saved PNG would
/// produce — so a yellow crop overlay's framing matches the eventual
/// output bit-for-bit.</para>
///
/// <para>Empty match (no shapes survived the selectors, or no loaded mesh
/// has CPU vertices yet) leaves the camera at its current state. Hosts
/// that want to detect "no contribution" can compare camera state before
/// and after, but in practice this only happens when called before
/// <c>VM_CharacterViewer.ProcessPendingSceneToCompletion</c> has run for
/// the first time.</para>
/// </summary>
public static class MeshAwareCameraFitter
{
    /// <summary>Applies <paramref name="framing"/> to <paramref name="vm"/>'s
    /// camera. <paramref name="viewportWidth"/> / <paramref name="viewportHeight"/>
    /// determine the aspect ratio used for horizontal-fit calculation —
    /// pass the dimensions of whatever target you're about to render into
    /// (the offscreen FBO size, or the live preview's pixel size).</summary>
    public static void ApplyTo(VM_CharacterViewer vm,
        CameraFraming.MeshAware framing,
        int viewportWidth, int viewportHeight)
    {
        if (vm == null) throw new ArgumentNullException(nameof(vm));
        if (framing == null) throw new ArgumentNullException(nameof(framing));

        var camera = vm.Camera;
        camera.Azimuth = framing.Yaw;
        camera.Elevation = framing.Pitch;

        var allMeshes = vm.Renderer.Meshes;
        if (allMeshes.Count == 0) return;

        // Collect per-FramingShape bboxes, then union.
        var unionMin = new NumericsVec3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var unionMax = new NumericsVec3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        bool anyContribution = false;

        foreach (var shape in framing.Shapes)
        {
            var matched = MatchShapes(allMeshes, shape.Selector);
            if (matched.Count == 0) continue;

            // Resolve the filter's reference Y bound, if any.
            float? minYBound = ResolveFilterMinY(shape.Filter, allMeshes);

            foreach (var mesh in matched)
            {
                var verts = mesh.CpuPositions;
                if (verts == null || verts.Length == 0) continue;

                NumericsVec3 mn = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                NumericsVec3 mx = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                bool meshHadVerts = false;

                for (int i = 0; i < verts.Length; i++)
                {
                    var v = verts[i];
                    if (minYBound.HasValue && v.Y < minYBound.Value) continue;
                    mn = NumericsVec3.Min(mn, v);
                    mx = NumericsVec3.Max(mx, v);
                    meshHadVerts = true;
                }
                if (!meshHadVerts) continue;

                if (shape.Padding > 0f)
                {
                    var pad = new NumericsVec3(shape.Padding, shape.Padding, shape.Padding);
                    mn -= pad;
                    mx += pad;
                }

                unionMin = NumericsVec3.Min(unionMin, mn);
                unionMax = NumericsVec3.Max(unionMax, mx);
                anyContribution = true;
            }
        }

        if (!anyContribution) return;

        // Camera target = bbox center on Y; X/Z stay 0 so the orbit revolves
        // around the character's vertical axis instead of an off-axis point.
        float centerY = (unionMin.Y + unionMax.Y) * 0.5f;
        camera.Target = new Vector3(0f, centerY, 0f);

        // Fit the bbox vertical extent into the framing band (top-bottom
        // fractions of the framebuffer). The band defines what fraction of
        // the FBO the bbox should occupy vertically; distance scales
        // inversely with band size.
        float bboxHeight = unionMax.Y - unionMin.Y;
        float bboxWidth = unionMax.X - unionMin.X;
        float band = MathF.Max(0.05f, framing.FrameTopFraction - framing.FrameBottomFraction);

        // Vertical fit: bboxHeight / band must equal 2 * D * tan(fov/2).
        float halfFovRad = MathHelper.DegreesToRadians(camera.FieldOfView * 0.5f);
        float tanHalfFov = MathF.Tan(halfFovRad);
        float distanceForHeight = (bboxHeight / band) / (2f * tanHalfFov);

        // Horizontal fit at this aspect: bboxWidth must fit in
        // 2 * D * tan(fov/2) * aspect. If horizontal would clip, push back.
        float aspect = (viewportHeight > 0) ? (float)viewportWidth / viewportHeight : 1f;
        float distanceForWidth = bboxWidth / (2f * tanHalfFov * aspect);

        float distance = MathF.Max(distanceForHeight, distanceForWidth);
        camera.Distance = MathF.Max(camera.MinDistance, distance);
    }

    private static IReadOnlyList<GlMesh> MatchShapes(IReadOnlyList<GlMesh> all, FramingShapeSelector selector)
    {
        return selector switch
        {
            FramingShapeSelector.AllLoaded => all,

            FramingShapeSelector.PrimaryHead =>
                all.Where(m => m.IsPrimaryHeadShape).ToList(),

            FramingShapeSelector.HeadAccessories =>
                all.Where(m => string.Equals(m.BodyPart, "Head", StringComparison.OrdinalIgnoreCase)
                               && !m.IsPrimaryHeadShape).ToList(),

            FramingShapeSelector.BodyPart bp =>
                all.Where(m => string.Equals(m.BodyPart, bp.Name, StringComparison.OrdinalIgnoreCase)).ToList(),

            FramingShapeSelector.ShapeNameContains snc =>
                all.Where(m => m.ShapeName.Contains(snc.Substring, StringComparison.OrdinalIgnoreCase)
                               && (snc.InBodyPart == null
                                   || string.Equals(m.BodyPart, snc.InBodyPart, StringComparison.OrdinalIgnoreCase)))
                   .ToList(),

            _ => Array.Empty<GlMesh>(),
        };
    }

    private static float? ResolveFilterMinY(FramingShapeFilter? filter, IReadOnlyList<GlMesh> all)
    {
        switch (filter)
        {
            case null:
                return null;

            case FramingShapeFilter.AboveWorldY abs:
                return abs.Y;

            case FramingShapeFilter.AboveLowerYOfPrimaryHead:
            {
                var primary = all.FirstOrDefault(m => m.IsPrimaryHeadShape);
                return MinYOf(primary);
            }

            case FramingShapeFilter.AboveLowerYOfBodyPart bp:
            {
                float minY = float.PositiveInfinity;
                bool found = false;
                foreach (var m in all)
                {
                    if (!string.Equals(m.BodyPart, bp.BodyPart, StringComparison.OrdinalIgnoreCase)) continue;
                    var y = MinYOf(m);
                    if (y.HasValue && y.Value < minY) { minY = y.Value; found = true; }
                }
                return found ? minY : null;
            }

            default:
                return null;
        }
    }

    private static float? MinYOf(GlMesh? mesh)
    {
        if (mesh?.CpuPositions == null || mesh.CpuPositions.Length == 0) return null;
        float min = float.PositiveInfinity;
        foreach (var v in mesh.CpuPositions)
            if (v.Y < min) min = v.Y;
        return float.IsFinite(min) ? min : null;
    }
}
