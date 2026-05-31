using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using SynthEBD;
using Xunit;
using Vector3 = OpenTK.Mathematics.Vector3;
using static SynthEBD.RegionVolumeEvaluator;

namespace SynthEBD.Tests;

/// <summary>
/// Geometry-core tests for <see cref="RegionVolumeEvaluator"/>. Each uses a synthetic mesh whose
/// volume is known analytically, so the clip / weld / boundary-loop / cap / signed-tetra pipeline
/// can be validated end-to-end without the rendering stack.
/// </summary>
public class RegionVolumeEvaluatorTests
{
    private const double Tol = 1e-4;

    // ---- synthetic meshes ----

    /// <summary>Closed axis-aligned cube [0,size]^3, 8 verts / 12 triangles, outward winding.</summary>
    private static (Vector3[] pos, int[] tris) ClosedCube(float size)
    {
        var p = new[]
        {
            new Vector3(0,0,0),       new Vector3(size,0,0),       new Vector3(size,size,0),       new Vector3(0,size,0),
            new Vector3(0,0,size),    new Vector3(size,0,size),    new Vector3(size,size,size),    new Vector3(0,size,size),
        };
        var t = new[]
        {
            1,2,6, 1,6,5,   // +X
            0,4,7, 0,7,3,   // -X
            3,7,6, 3,6,2,   // +Y
            0,1,5, 0,5,4,   // -Y
            4,5,6, 4,6,7,   // +Z
            0,3,2, 0,2,1,   // -Z
        };
        return (p, t);
    }

    /// <summary>Cube with the +Z face removed: one open boundary loop around the top.</summary>
    private static (Vector3[] pos, int[] tris) OpenTopCube(float size)
    {
        var (p, full) = ClosedCube(size);
        // Drop the two +Z triangles (indices 24..29 in the flat list above).
        var t = new List<int>(full);
        t.RemoveRange(24, 6);
        return (p, t.ToArray());
    }

    /// <summary>Open square tube of footprint side <paramref name="side"/> along Y in [y0,y1]; 8 verts / 8 triangles, no end caps.</summary>
    private static (Vector3[] pos, int[] tris) SquareTube(float side, float y0, float y1)
    {
        var p = new[]
        {
            new Vector3(0,y0,0),    new Vector3(side,y0,0),    new Vector3(side,y0,side),    new Vector3(0,y0,side),  // bottom rim 0..3
            new Vector3(0,y1,0),    new Vector3(side,y1,0),    new Vector3(side,y1,side),    new Vector3(0,y1,side),  // top rim 4..7
        };
        var t = new List<int>();
        for (int k = 0; k < 4; k++)
        {
            int b0 = k, b1 = (k + 1) % 4;
            int t0 = k + 4, t1 = (k + 1) % 4 + 4;
            t.AddRange(new[] { b0, b1, t1, b0, t1, t0 });
        }
        return (p, t.ToArray());
    }

    private static RegionAabb Box(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        => new RegionAabb(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));

    // ---- tests ----

    [Fact]
    public void ClosedMeshVolume_Cube_EqualsSideCubed()
    {
        var (p, t) = ClosedCube(2f);
        ClosedMeshVolume(p, t).Should().BeApproximately(8.0, Tol);
    }

    [Fact]
    public void SignedTetraVolume_IsTranslationSensitiveButClosedSumIsNot()
    {
        // A closed mesh's volume is translation invariant even though per-tetra terms are not.
        var (p, t) = ClosedCube(3f);
        var moved = p.Select(v => v + new Vector3(100, -50, 25)).ToArray();
        ClosedMeshVolume(moved, t).Should().BeApproximately(27.0, Tol);
    }

    [Fact]
    public void ResolveRegion_FullyContainedClosedCube_NoLoops_VolumeExact()
    {
        var (p, t) = ClosedCube(2f);
        var r = ResolveRegion(p, t, Box(-1, -1, -1, 3, 3, 3));

        r.IsValid.Should().BeTrue(r.Diagnostic);
        r.LoopCount.Should().Be(0);
        ComputeVolume(r, p).Should().BeApproximately(8.0, Tol);
    }

    [Fact]
    public void ResolveRegion_OpenTopCube_OneLoop_CappedVolumeExact()
    {
        // Box contains the whole cube, so no clipping happens; the only boundary is the open top,
        // which the cap fan must close consistently to recover the full cube volume.
        var (p, t) = OpenTopCube(2f);
        var r = ResolveRegion(p, t, Box(-1, -1, -1, 3, 3, 3));

        r.IsValid.Should().BeTrue(r.Diagnostic);
        r.LoopCount.Should().Be(1);
        ComputeVolume(r, p).Should().BeApproximately(8.0, Tol);
    }

    [Fact]
    public void ComputeVolume_TracksTranslationAndScaleOfDeformedPositions()
    {
        // Resolve once on the zeroed mesh, then evaluate on transformed "deformed" positions.
        var (p, t) = OpenTopCube(2f);
        var r = ResolveRegion(p, t, Box(-1, -1, -1, 3, 3, 3));
        r.IsValid.Should().BeTrue(r.Diagnostic);

        var translated = p.Select(v => v + new Vector3(10, 20, 30)).ToArray();
        ComputeVolume(r, translated).Should().BeApproximately(8.0, Tol);

        var scaled = p.Select(v => v * 2f).ToArray();
        ComputeVolume(r, scaled).Should().BeApproximately(64.0, 1e-3); // volume scales with factor^3
    }

    [Fact]
    public void ResolveRegion_ClipsTube_TwoLoops_CappedBandVolumeExact()
    {
        // Box slices the tube on its Y faces only (X/Z faces sit outside the footprint), leaving a
        // middle band with two open rims. Capped volume = footprint area * band height = 4 * 2 = 8.
        var (p, t) = SquareTube(2f, 0f, 4f);
        var r = ResolveRegion(p, t, Box(-1, 1, -1, 3, 3, 3));

        r.IsValid.Should().BeTrue(r.Diagnostic);
        r.LoopCount.Should().Be(2);
        ComputeVolume(r, p).Should().BeApproximately(8.0, 1e-3);
    }

    [Fact]
    public void ResolveRegion_BoxOutsideMesh_IsInvalid()
    {
        var (p, t) = ClosedCube(2f);
        var r = ResolveRegion(p, t, Box(10, 10, 10, 12, 12, 12));

        r.IsValid.Should().BeFalse();
        r.Diagnostic.Should().Contain("does not intersect");
    }

    [Fact]
    public void ResolveRegion_ExpectedCapCountMismatch_IsRejected()
    {
        var (p, t) = OpenTopCube(2f);
        var opts = new RegionResolveOptions { ExpectedCapCount = 2 };
        var r = ResolveRegion(p, t, Box(-1, -1, -1, 3, 3, 3), opts);

        r.IsValid.Should().BeFalse();
        r.Diagnostic.Should().Contain("expected 2");
    }

    [Fact]
    public void ResolveRegion_TwoDisconnectedCubes_RejectedAsMultipleComponents()
    {
        // Two separate closed cubes inside one box → two components → reject.
        var (p0, t0) = ClosedCube(1f);
        var (p1, t1) = ClosedCube(1f);
        var shifted = p1.Select(v => v + new Vector3(5, 0, 0)).ToArray();

        var pos = p0.Concat(shifted).ToArray();
        var tris = t0.Concat(t1.Select(i => i + p0.Length)).ToArray();

        var r = ResolveRegion(pos, tris, Box(-1, -1, -1, 7, 2, 2));

        r.IsValid.Should().BeFalse();
        r.Diagnostic.Should().Contain("disconnected");
    }
}
