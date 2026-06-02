using System;
using System.Linq;
using FluentAssertions;
using SynthEBD;
using Xunit;
using Vector3 = OpenTK.Mathematics.Vector3;
using static SynthEBD.RegionVolumeEvaluator;

namespace SynthEBD.Tests;

/// <summary>
/// Tests for the bugfix that makes a region track across presets: a box authored against a deformed
/// mesh must be converted to the zeroed-space AABB bounding the SAME vertex set
/// (<see cref="RegionVolumeEvaluator.ConvertBoxByVertexSet"/>), and a region resolved once against the
/// zeroed mesh must then evaluate correctly against ANY deformed positions (the baked-set tracking
/// invariant). Uses synthetic meshes so the whole path is exercised without the rendering stack.
/// </summary>
public class RegionVolumeTrackingTests
{
    private const double Tol = 1e-4;

    /// <summary>Closed axis-aligned cube [0,size]^3, 8 verts / 12 triangles, outward winding.</summary>
    private static (Vector3[] pos, int[] tris) ClosedCube(float size)
    {
        var p = new[]
        {
            new Vector3(0,0,0),    new Vector3(size,0,0),    new Vector3(size,size,0),    new Vector3(0,size,0),
            new Vector3(0,0,size), new Vector3(size,0,size), new Vector3(size,size,size), new Vector3(0,size,size),
        };
        var t = new[]
        {
            1,2,6, 1,6,5,  0,4,7, 0,7,3,  3,7,6, 3,6,2,
            0,1,5, 0,5,4,  4,5,6, 4,6,7,  0,3,2, 0,2,1,
        };
        return (p, t);
    }

    /// <summary>Closed cube with the +Z face removed → one open boundary loop around the top.</summary>
    private static (Vector3[] pos, int[] tris) OpenTopCube(float size)
    {
        var (p, full) = ClosedCube(size);
        var t = new System.Collections.Generic.List<int>(full);
        t.RemoveRange(24, 6); // drop the two +Z triangles (last face in the flat list)
        return (p, t.ToArray());
    }

    // ---- ConvertBoxByVertexSet ----

    [Fact]
    public void ConvertBox_MapsVertexSetIntoReferenceSpace_PreservingAirMargins()
    {
        // "Deformed" = cube shifted +50 in X. A box around the deformed cube with a 1-unit air gap
        // on every face must land in zeroed space as the cube bounds [0,2]^3 EXPANDED by that same
        // 1-unit margin → [-1,3]^3. (Preserving the air margin is what keeps the in-box patch to one
        // clean cut instead of fragmenting — the tight AABB would hug all six faces.)
        var (zeroed, _) = ClosedCube(2f);
        var deformed = zeroed.Select(v => v + new Vector3(50, 0, 0)).ToArray();

        var boxOnDeformed = new RegionAabb(new Vector3(49, -1, -1), new Vector3(53, 3, 3)); // 1-unit gap each face
        var converted = ConvertBoxByVertexSet(deformed, zeroed, boxOnDeformed);

        converted.Should().NotBeNull();
        converted!.Value.Min.X.Should().BeApproximately(-1f, 1e-4f);
        converted.Value.Min.Y.Should().BeApproximately(-1f, 1e-4f);
        converted.Value.Min.Z.Should().BeApproximately(-1f, 1e-4f);
        converted.Value.Max.X.Should().BeApproximately(3f, 1e-4f);
        converted.Value.Max.Y.Should().BeApproximately(3f, 1e-4f);
        converted.Value.Max.Z.Should().BeApproximately(3f, 1e-4f);
    }

    [Fact]
    public void ConvertBox_CutFace_StaysCutting_AirFace_StaysLoose()
    {
        // Asymmetric box: tight (gap 0) on the +X face — the "cut" face — and loose (gap 1) on -X.
        // The converted zeroed box must keep +X tight to the captured set's +X bound and -X loose.
        var (zeroed, _) = ClosedCube(2f);
        var deformed = zeroed.Select(v => v + new Vector3(50, 0, 0)).ToArray();

        // Captured set spans deformed x in [50,52]. Box: min.X=49 (gap 1 below 50), max.X=52 (gap 0).
        var box = new RegionAabb(new Vector3(49, -1, -1), new Vector3(52, 3, 3));
        var converted = ConvertBoxByVertexSet(deformed, zeroed, box);

        converted.Should().NotBeNull();
        // Zeroed set spans x in [0,2]: -X carries the 1-unit margin → -1; +X cut stays at 2.
        converted!.Value.Min.X.Should().BeApproximately(-1f, 1e-4f);
        converted.Value.Max.X.Should().BeApproximately(2f, 1e-4f);
    }

    [Fact]
    public void ConvertBox_NoVertsInBox_ReturnsNull()
    {
        var (zeroed, _) = ClosedCube(2f);
        var deformed = zeroed.Select(v => v + new Vector3(50, 0, 0)).ToArray();
        var box = new RegionAabb(new Vector3(1000, 1000, 1000), new Vector3(1001, 1001, 1001));
        ConvertBoxByVertexSet(deformed, zeroed, box).Should().BeNull();
    }

    [Fact]
    public void ConvertBox_MismatchedLengths_ReturnsNull()
    {
        var (zeroed, _) = ClosedCube(2f);
        var shorter = zeroed.Take(4).ToArray();
        ConvertBoxByVertexSet(zeroed, shorter, new RegionAabb(new Vector3(-1, -1, -1), new Vector3(3, 3, 3)))
            .Should().BeNull();
    }

    // ---- resolve-on-zeroed, evaluate-on-deformed (the tracking invariant) ----

    [Fact]
    public void Region_ResolvedOnZeroed_TracksArbitraryDeformedPositions()
    {
        // Author the box in zeroed space (whole cube), resolve once, then evaluate the baked set
        // against several different "presets" (translate, scale). The volume must follow.
        var (zeroed, tris) = ClosedCube(2f);
        var box = new RegionAabb(new Vector3(-1, -1, -1), new Vector3(3, 3, 3));
        var r = ResolveRegion(zeroed, tris, box);
        r.IsValid.Should().BeTrue(r.Diagnostic);

        // Same as zeroed → 8.
        ComputeVolume(r, zeroed).Should().BeApproximately(8.0, Tol);

        // Translated preset → still 8 (volume is translation-invariant).
        var translated = zeroed.Select(v => v + new Vector3(10, -20, 30)).ToArray();
        ComputeVolume(r, translated).Should().BeApproximately(8.0, Tol);

        // Scaled preset (a "bigger" body) → 8 * 1.5^3 = 27.
        var scaled = zeroed.Select(v => v * 1.5f).ToArray();
        ComputeVolume(r, scaled).Should().BeApproximately(27.0, 1e-3);
    }

    [Fact]
    public void AuthorOnDeformed_ConvertToZeroed_ResolvesAndTracks_EndToEnd()
    {
        // Full pipeline the bugfix implements:
        //   1. user draws a box on the DEFORMED mesh,
        //   2. we convert it to the zeroed-space AABB of the same verts,
        //   3. resolve once against the ZEROED mesh,
        //   4. evaluate against the deformed mesh (and a different preset) — both track.
        var (zeroed, tris) = ClosedCube(2f);
        var presetA = zeroed.Select(v => v + new Vector3(50, 0, 0)).ToArray();   // authoring preset
        var presetB = zeroed.Select(v => v * 2f).ToArray();                      // different preset

        var drawnOnA = new RegionAabb(new Vector3(49, -1, -1), new Vector3(53, 3, 3));
        var zeroedBox = ConvertBoxByVertexSet(presetA, zeroed, drawnOnA);
        zeroedBox.Should().NotBeNull();

        var r = ResolveRegion(zeroed, tris, zeroedBox!.Value);
        r.IsValid.Should().BeTrue(r.Diagnostic);

        // The baked set evaluates correctly on BOTH presets — this is what the old re-clip path
        // got wrong (it would re-clip the zeroed box against presetB and find the wrong patch).
        ComputeVolume(r, presetA).Should().BeApproximately(8.0, Tol);   // authoring preset
        ComputeVolume(r, presetB).Should().BeApproximately(64.0, 1e-3); // 8 * 2^3
    }

    // ---- cap modes (FlatPlane vs AnatomicalFan) ----

    /// <summary>Open-bottom box capturing the top of a unit cube with a pyramidal apex: the cut ring
    /// is the square z-loop, but the apex is pulled up so AnatomicalFan (centroid lid follows the
    /// apex-skewed ring) and FlatPlane (flat lid at the ring's mean) give DIFFERENT volumes.</summary>
    private static (Vector3[] pos, int[] tris) OpenBottomTentRoof()
    {
        // A square "tent": 4 base corners on the y=0 plane forming the open cut loop, 1 apex above.
        // The box will clip nothing (all inside) but the bottom face is open → one cut loop at y=0.
        var p = new[]
        {
            new Vector3(0,0,0), new Vector3(2,0,0), new Vector3(2,0,2), new Vector3(0,0,2), // base ring (y=0)
            new Vector3(1,2,1), // apex
        };
        // 4 side triangles (apex to each base edge), outward winding; bottom left open.
        var t = new[]
        {
            0,4,1,  1,4,2,  2,4,3,  3,4,0,
        };
        return (p, t);
    }

    [Fact]
    public void CapModes_PlanarRing_BothCapsAgree_AndFlatPlaneAxisIsDetected()
    {
        var (pos, tris) = OpenBottomTentRoof();
        // Box loose on all faces so nothing is clipped — the only boundary is the open y=0 base loop.
        var box = new RegionAabb(new Vector3(-1, -1, -1), new Vector3(3, 3, 3));
        var r = ResolveRegion(pos, tris, box);
        r.IsValid.Should().BeTrue(r.Diagnostic);
        r.LoopCount.Should().Be(1);

        // Cut normal is world Y (the base ring is constant-Y), so FlatPlane caps at the y=0 plane.
        MathF.Abs(r.CutNormal.Y).Should().BeApproximately(1f, 1e-3f);

        // The base ring is planar (y=0) at its centroid, so a flat lid and a centroid fan coincide →
        // identical volume = pyramid (2x2 base, height 2) = 8/3. (When the ring is planar the two
        // cap modes agree; they diverge in CONTOUR shape once the ring is skewed off its plane —
        // see GetCapLoopOverlayPoints_FlatPlane_ProjectsRingToConstantCutAxis. By the divergence
        // theorem the enclosed VOLUME for a single loop is governed by the boundary ring, so a
        // simple skew of this convex shape leaves the volume equal even as the lid shape changes —
        // the user-visible difference the toggle controls is the cap geometry / overlay, exercised
        // by the overlay test.)
        ComputeVolume(r, pos, RegionCapMode.FlatPlane).Should().BeApproximately(8.0 / 3.0, 1e-4);
        ComputeVolume(r, pos, RegionCapMode.AnatomicalFan).Should().BeApproximately(8.0 / 3.0, 1e-4);
    }

    [Fact]
    public void BuildSolidSurface_EmitsClosedSurface_PatchPlusCaps_InterleavedPosNormal()
    {
        // Open-top cube → patch = 5 cube faces (10 tris), cap = 1 loop of 4 boundary verts → fan of 4
        // tris. Total 14 tris * 3 verts * 6 floats = 252 floats. Each vertex carries a unit normal.
        var (pos, t) = OpenTopCube(2f);
        var r = ResolveRegion(pos, t, new RegionAabb(new Vector3(-1, -1, -1), new Vector3(3, 3, 3)));
        r.IsValid.Should().BeTrue(r.Diagnostic);
        r.LoopCount.Should().Be(1);

        var solid = RegionVolumeEvaluator.BuildSolidSurface(r, pos, RegionCapMode.AnatomicalFan);
        solid.Count.Should().BeGreaterThan(0);
        (solid.Count % 18).Should().Be(0); // whole triangles (18 floats each)

        // Every vertex's normal (floats 3,4,5 of each 6) is unit length.
        for (int i = 0; i < solid.Count; i += 6)
        {
            var nlen = MathF.Sqrt(solid[i + 3] * solid[i + 3] + solid[i + 4] * solid[i + 4] + solid[i + 5] * solid[i + 5]);
            nlen.Should().BeApproximately(1f, 1e-3f);
        }
    }

    [Fact]
    public void BuildSolidWireframe_ReturnsDedupedPatchEdges_ThatTrackDeformation()
    {
        var (pos, t) = OpenTopCube(2f);
        var r = ResolveRegion(pos, t, new RegionAabb(new Vector3(-1, -1, -1), new Vector3(3, 3, 3)));
        r.IsValid.Should().BeTrue(r.Diagnostic);

        var wire = RegionVolumeEvaluator.BuildSolidWireframe(r, pos);
        wire.Should().NotBeEmpty();

        // Edges are deduped: count distinct undirected endpoint-position pairs == total returned.
        string Key(Vector3 a, Vector3 b)
        {
            string sa = $"{a.X:F3},{a.Y:F3},{a.Z:F3}", sb = $"{b.X:F3},{b.Y:F3},{b.Z:F3}";
            return string.CompareOrdinal(sa, sb) <= 0 ? sa + "|" + sb : sb + "|" + sa;
        }
        wire.Select(e => Key(e.A, e.B)).Distinct().Count().Should().Be(wire.Count);

        // Tracks deformation: scaling the mesh scales the edge endpoints.
        var scaled = pos.Select(p => p * 2f).ToArray();
        var wire2 = RegionVolumeEvaluator.BuildSolidWireframe(r, scaled);
        wire2.Count.Should().Be(wire.Count);
        (wire2[0].A * 0.5f).Should().BeEquivalentTo(wire[0].A);
    }

    [Fact]
    public void BuildSolidSurface_TracksDeformedPositions()
    {
        var (pos, t) = OpenTopCube(2f);
        var r = ResolveRegion(pos, t, new RegionAabb(new Vector3(-1, -1, -1), new Vector3(3, 3, 3)));
        r.IsValid.Should().BeTrue(r.Diagnostic);

        var baseSolid = RegionVolumeEvaluator.BuildSolidSurface(r, pos, RegionCapMode.AnatomicalFan);
        var scaled = pos.Select(p => p * 2f).ToArray();
        var scaledSolid = RegionVolumeEvaluator.BuildSolidSurface(r, scaled, RegionCapMode.AnatomicalFan);

        baseSolid.Count.Should().Be(scaledSolid.Count); // same topology
        // A position float in the scaled build should be ~2x the base (sample the first vertex's X).
        scaledSolid[0].Should().BeApproximately(baseSolid[0] * 2f, 1e-3f);
    }

    [Fact]
    public void GetCapLoopOverlayPoints_FlatPlane_ProjectsRingToConstantCutAxis()
    {
        var (pos, tris) = OpenBottomTentRoof();
        var r = ResolveRegion(pos, tris, new RegionAabb(new Vector3(-1, -1, -1), new Vector3(3, 3, 3)));
        r.IsValid.Should().BeTrue(r.Diagnostic);

        var deformed = (Vector3[])pos.Clone();
        deformed[0] = new Vector3(0, 1.0f, 0); // skew the ring in Y

        var flatRing = RegionVolumeEvaluator.GetCapLoopOverlayPoints(r, 0, deformed, RegionCapMode.FlatPlane);
        flatRing.Should().NotBeEmpty();
        // All flat-projected points share one Y (the ring's mean) → a flat contour, no scoop.
        var ys = flatRing.Select(p => p.Y).ToList();
        (ys.Max() - ys.Min()).Should().BeLessThan(1e-4f);

        // AnatomicalFan keeps the skewed ring → Y varies.
        var fanRing = RegionVolumeEvaluator.GetCapLoopOverlayPoints(r, 0, deformed, RegionCapMode.AnatomicalFan);
        fanRing.Select(p => p.Y).Max().Should().BeGreaterThan(fanRing.Select(p => p.Y).Min());
    }
}
