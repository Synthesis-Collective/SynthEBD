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
    /// (the offscreen FBO size, or the live preview's pixel size).
    /// <para><paramref name="preserveCameraOrientation"/> = false (default):
    /// camera Az/El are set from <see cref="CameraFraming.MeshAware.Yaw"/> /
    /// <see cref="CameraFraming.MeshAware.Pitch"/> as before. = true: leaves
    /// camera Az/El alone and only updates <see cref="OrbitCamera.Target"/>
    /// and <see cref="OrbitCamera.Distance"/>. Hosts use this for live
    /// drag-to-rotate-in-Auto-mode workflows: the user's mouse drag mutates
    /// camera angles directly, then this call re-fits distance for the new
    /// view so the character stays inside the framing band.</para></summary>
    public static void ApplyTo(VM_CharacterViewer vm,
        CameraFraming.MeshAware framing,
        int viewportWidth, int viewportHeight,
        bool preserveCameraOrientation = false)
    {
        if (vm == null) throw new ArgumentNullException(nameof(vm));
        if (framing == null) throw new ArgumentNullException(nameof(framing));

        var camera = vm.Camera;
        if (!preserveCameraOrientation)
        {
            camera.Azimuth = framing.Yaw;
            camera.Elevation = framing.Pitch;
        }

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

        // Camera target. Frontal fits keep X/Z at 0 so the orbit axis stays
        // aligned with world Y (Skyrim NPCs stand at the world origin facing
        // -Z; the existing offscreen pipeline depends on this for stable,
        // reproducible mugshot output). When the host has rotated the camera
        // off-axis (preserveCameraOrientation=true, e.g. drag-to-rotate in
        // the live preview), the bbox's actual X/Z midpoint is used so the
        // orbit revolves around the visual center even for asymmetric
        // characters (Khajiit/Argonian tail, off-center hair pieces).
        float centerY = (unionMin.Y + unionMax.Y) * 0.5f;
        camera.Target = preserveCameraOrientation
            ? new Vector3((unionMin.X + unionMax.X) * 0.5f, centerY, (unionMin.Z + unionMax.Z) * 0.5f)
            : new Vector3(0f, centerY, 0f);

        // Project the AABB into camera screen-space (X = right, Y = up) by
        // dotting each of the 8 corners' offset-from-center against the
        // camera's right and up basis vectors. The screen-space extent —
        // not the world-space Y/X extent — is what controls how much
        // framebuffer the model occupies, so this is the only fit that
        // remains correct when the camera has been rotated away from the
        // frontal default.
        //
        // For frontal Az=180/El=0 (the default mugshot pose) the right
        // basis aligns with world -X and the up basis with world +Y, so
        // the projected extents reduce to the previous bboxWidth/bboxHeight
        // and there's no behavior change for the offscreen renderer.
        float azRad = MathHelper.DegreesToRadians(camera.Azimuth);
        float elRad = MathHelper.DegreesToRadians(camera.Elevation);
        // zaxis = from target toward eye (matches OpenTK LookAt's zaxis).
        var zaxis = new Vector3(
            MathF.Cos(elRad) * MathF.Sin(azRad),
            MathF.Sin(elRad),
            MathF.Cos(elRad) * MathF.Cos(azRad));
        var xaxis = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, zaxis));
        var yaxis = Vector3.Cross(zaxis, xaxis);

        var centerWorld = new Vector3(
            (unionMin.X + unionMax.X) * 0.5f,
            centerY,
            (unionMin.Z + unionMax.Z) * 0.5f);

        float minPx = float.PositiveInfinity, maxPx = float.NegativeInfinity;
        float minPy = float.PositiveInfinity, maxPy = float.NegativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? unionMin.X : unionMax.X,
                (i & 2) == 0 ? unionMin.Y : unionMax.Y,
                (i & 4) == 0 ? unionMin.Z : unionMax.Z);
            var d = corner - centerWorld;
            float px = Vector3.Dot(d, xaxis);
            float py = Vector3.Dot(d, yaxis);
            if (px < minPx) minPx = px;
            if (px > maxPx) maxPx = px;
            if (py < minPy) minPy = py;
            if (py > maxPy) maxPy = py;
        }
        float bboxScreenW = maxPx - minPx;
        float bboxScreenH = maxPy - minPy;

        // Fit the projected extent into the framing band (top-bottom
        // fractions of the framebuffer). distance scales inversely with
        // band size.
        float band = MathF.Max(0.05f, framing.FrameTopFraction - framing.FrameBottomFraction);
        float halfFovRad = MathHelper.DegreesToRadians(camera.FieldOfView * 0.5f);
        float tanHalfFov = MathF.Tan(halfFovRad);
        float distanceForHeight = (bboxScreenH / band) / (2f * tanHalfFov);

        // Horizontal fit at this aspect: bbox screen width must fit in
        // 2 * D * tan(fov/2) * aspect. If horizontal would clip, push back.
        float aspect = (viewportHeight > 0) ? (float)viewportWidth / viewportHeight : 1f;
        float distanceForWidth = bboxScreenW / (2f * tanHalfFov * aspect);

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
