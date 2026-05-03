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
    /// view so the character stays inside the framing band.</para>
    /// <para><paramref name="log"/> is an optional diagnostic sink. When
    /// non-null, the fitter emits per-shape bboxes, the unioned bbox, the
    /// camera-relative screen extents, and the resulting Distance/Target so
    /// hosts can diagnose unexpected framing (e.g. tall hair pulling the
    /// bbox center upward and the face into the lower half of the frame).
    /// Messages use the library's "CharacterViewer: " prefix so adapter
    /// loggers route them through the same path as other verbose lines.</para></summary>
    public static void ApplyTo(VM_CharacterViewer vm,
        CameraFraming.MeshAware framing,
        int viewportWidth, int viewportHeight,
        bool preserveCameraOrientation = false,
        Action<string>? log = null)
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
        // GlMesh.CpuPositions is in pre-ModelScale (NIF-original) units, but the
        // renderer multiplies every position by Matrix4.CreateScale(ModelScale)
        // at draw time. Without scaling the bbox, the camera targets the wrong
        // Y (the unscaled centerY) while the rendered head is at scaled Y, and
        // for sub-1 NPC heights the face slides into the lower half of the
        // frame with only the forehead visible. Multiplying every vertex
        // observation by ModelScale rebases the bbox into the same render-space
        // the camera operates in. Padding is treated as render-space too so a
        // configured "X units of headroom above the hair" stays visually
        // consistent across short / tall NPCs.
        float modelScale = vm.Renderer.ModelScale;
        if (!float.IsFinite(modelScale) || modelScale <= 0f) modelScale = 1f;
        log?.Invoke("CharacterViewer: [Framing] Begin: yaw=" + framing.Yaw.ToString("F1")
            + ", pitch=" + framing.Pitch.ToString("F1")
            + ", topFrac=" + framing.FrameTopFraction.ToString("F2")
            + ", bottomFrac=" + framing.FrameBottomFraction.ToString("F2")
            + ", viewport=" + viewportWidth + "x" + viewportHeight
            + ", preserveOrient=" + preserveCameraOrientation
            + ", loadedMeshes=" + allMeshes.Count
            + ", modelScale=" + modelScale.ToString("F3"));
        if (allMeshes.Count == 0)
        {
            log?.Invoke("CharacterViewer: [Framing] No loaded meshes — leaving camera as-is.");
            return;
        }

        // Collect per-FramingShape bboxes, then union.
        var unionMin = new NumericsVec3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var unionMax = new NumericsVec3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        bool anyContribution = false;

        for (int si = 0; si < framing.Shapes.Count; si++)
        {
            var shape = framing.Shapes[si];
            var matched = MatchShapes(allMeshes, shape.Selector);
            log?.Invoke("CharacterViewer: [Framing] Shape[" + si + "] selector="
                + DescribeSelector(shape.Selector)
                + ", filter=" + DescribeFilter(shape.Filter)
                + ", padding=" + shape.Padding.ToString("F2")
                + " → matched " + matched.Count + " mesh(es)");
            if (matched.Count == 0) continue;

            // Resolve the filter's reference Y bound, if any. ResolveFilterMinY
            // returns the min in CpuPositions space; rebase to render-space so
            // it can be compared directly against the scaled vertex Y below.
            float? minYBound = ResolveFilterMinY(shape.Filter, allMeshes);
            if (minYBound.HasValue) minYBound = minYBound.Value * modelScale;
            if (shape.Filter != null)
            {
                log?.Invoke("CharacterViewer: [Framing]   filter resolved minYBound="
                    + (minYBound.HasValue ? minYBound.Value.ToString("F2") : "null")
                    + " (render-space)");
            }

            foreach (var mesh in matched)
            {
                var verts = mesh.CpuPositions;
                if (verts == null || verts.Length == 0)
                {
                    log?.Invoke("CharacterViewer: [Framing]   '" + mesh.ShapeName
                        + "' (BodyPart=" + mesh.BodyPart + ", PrimaryHead=" + mesh.IsPrimaryHeadShape
                        + ") — no CpuPositions, skipped");
                    continue;
                }

                NumericsVec3 mn = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                NumericsVec3 mx = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                bool meshHadVerts = false;
                int includedVerts = 0;

                for (int i = 0; i < verts.Length; i++)
                {
                    var v = verts[i] * modelScale;
                    if (minYBound.HasValue && v.Y < minYBound.Value) continue;
                    mn = NumericsVec3.Min(mn, v);
                    mx = NumericsVec3.Max(mx, v);
                    meshHadVerts = true;
                    includedVerts++;
                }
                if (!meshHadVerts)
                {
                    log?.Invoke("CharacterViewer: [Framing]   '" + mesh.ShapeName
                        + "' — all " + verts.Length + " verts filtered out, skipped");
                    continue;
                }

                NumericsVec3 mnRaw = mn, mxRaw = mx;
                if (shape.Padding > 0f)
                {
                    var pad = new NumericsVec3(shape.Padding, shape.Padding, shape.Padding);
                    mn -= pad;
                    mx += pad;
                }

                log?.Invoke("CharacterViewer: [Framing]   '" + mesh.ShapeName
                    + "' (BodyPart=" + mesh.BodyPart + ", PrimaryHead=" + mesh.IsPrimaryHeadShape + ")"
                    + " verts=" + includedVerts + "/" + verts.Length
                    + " bbox(render) X[" + mnRaw.X.ToString("F2") + ".." + mxRaw.X.ToString("F2") + "]"
                    + " Y[" + mnRaw.Y.ToString("F2") + ".." + mxRaw.Y.ToString("F2") + "]"
                    + " Z[" + mnRaw.Z.ToString("F2") + ".." + mxRaw.Z.ToString("F2") + "]"
                    + (shape.Padding > 0f
                        ? " (padded to Y[" + mn.Y.ToString("F2") + ".." + mx.Y.ToString("F2") + "])"
                        : ""));

                unionMin = NumericsVec3.Min(unionMin, mn);
                unionMax = NumericsVec3.Max(unionMax, mx);
                anyContribution = true;
            }
        }

        if (!anyContribution)
        {
            log?.Invoke("CharacterViewer: [Framing] No shape contributed verts — leaving camera as-is.");
            return;
        }
        log?.Invoke("CharacterViewer: [Framing] Union bbox"
            + " X[" + unionMin.X.ToString("F2") + ".." + unionMax.X.ToString("F2") + "]"
            + " Y[" + unionMin.Y.ToString("F2") + ".." + unionMax.Y.ToString("F2") + "]"
            + " Z[" + unionMin.Z.ToString("F2") + ".." + unionMax.Z.ToString("F2") + "]");

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

        if (log != null)
        {
            log.Invoke("CharacterViewer: [Framing] centerY=" + centerY.ToString("F2")
                + ", target=(" + camera.Target.X.ToString("F2")
                + ", " + camera.Target.Y.ToString("F2")
                + ", " + camera.Target.Z.ToString("F2") + ")");
            log.Invoke("CharacterViewer: [Framing] basis: az=" + camera.Azimuth.ToString("F1")
                + ", el=" + camera.Elevation.ToString("F1")
                + ", xaxis=(" + xaxis.X.ToString("F3") + "," + xaxis.Y.ToString("F3") + "," + xaxis.Z.ToString("F3") + ")"
                + ", yaxis=(" + yaxis.X.ToString("F3") + "," + yaxis.Y.ToString("F3") + "," + yaxis.Z.ToString("F3") + ")"
                + ", zaxis=(" + zaxis.X.ToString("F3") + "," + zaxis.Y.ToString("F3") + "," + zaxis.Z.ToString("F3") + ")");
            log.Invoke("CharacterViewer: [Framing] screenSpace W=" + bboxScreenW.ToString("F2")
                + " (px in [" + minPx.ToString("F2") + ".." + maxPx.ToString("F2") + "])"
                + ", H=" + bboxScreenH.ToString("F2")
                + " (py in [" + minPy.ToString("F2") + ".." + maxPy.ToString("F2") + "])");
            log.Invoke("CharacterViewer: [Framing] band=" + band.ToString("F3")
                + ", fov=" + camera.FieldOfView.ToString("F1")
                + ", aspect=" + aspect.ToString("F3")
                + ", distForHeight=" + distanceForHeight.ToString("F2")
                + ", distForWidth=" + distanceForWidth.ToString("F2")
                + " → final distance=" + camera.Distance.ToString("F2")
                + " (clamped MinDistance=" + camera.MinDistance.ToString("F2") + ")");
        }
    }

    private static string DescribeSelector(FramingShapeSelector selector) => selector switch
    {
        FramingShapeSelector.AllLoaded => "AllLoaded",
        FramingShapeSelector.PrimaryHead => "PrimaryHead",
        FramingShapeSelector.HeadAccessories => "HeadAccessories",
        FramingShapeSelector.BodyPart bp => "BodyPart(" + bp.Name + ")",
        FramingShapeSelector.ShapeNameContains snc => "ShapeNameContains('" + snc.Substring + "'"
            + (snc.InBodyPart != null ? ", in=" + snc.InBodyPart : "") + ")",
        _ => selector.GetType().Name,
    };

    private static string DescribeFilter(FramingShapeFilter? filter) => filter switch
    {
        null => "(none)",
        FramingShapeFilter.AboveLowerYOfPrimaryHead => "AboveLowerYOfPrimaryHead",
        FramingShapeFilter.AboveLowerYOfBodyPart bp => "AboveLowerYOfBodyPart(" + bp.BodyPart + ")",
        FramingShapeFilter.AboveWorldY abs => "AboveWorldY(" + abs.Y.ToString("F2") + ")",
        _ => filter.GetType().Name,
    };

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
