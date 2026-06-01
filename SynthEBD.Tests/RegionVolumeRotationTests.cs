using System;
using System.Linq;
using FluentAssertions;
using SynthEBD;
using Xunit;
using Vector3 = OpenTK.Mathematics.Vector3;
using static SynthEBD.RegionVolumeEvaluator;

namespace SynthEBD.Tests;

/// <summary>
/// Tests for the rotatable region box. The whole feature works by transforming the mesh into the
/// box's local frame at resolve time and running the same axis-aligned clip there, so the two
/// invariants that matter are: (1) an identity rotation is byte-identical to the no-rotation path
/// (backward compatibility), and (2) rotating the box by R while counter-rotating the mesh by R
/// selects the same patch / volume (the transform is correct). A small grid mesh with known geometry
/// exercises both without the rendering stack.
/// </summary>
public class RegionVolumeRotationTests
{
    private const double Tol = 1e-3;

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

    private static Vector3 RotZ(Vector3 p, float deg)
    {
        float r = deg * (MathF.PI / 180f), c = MathF.Cos(r), s = MathF.Sin(r);
        return new Vector3(c * p.X - s * p.Y, s * p.X + c * p.Y, p.Z);
    }

    [Fact]
    public void IdentityRotation_IsIdentical_ToNoRotationPath()
    {
        var (pos, tris) = ClosedCube(2f);
        var box = new RegionAabb(new Vector3(-1, -1, -1), new Vector3(3, 3, 3));

        var noRot = ResolveRegion(pos, tris, box);                               // 4-arg overload
        var idRot = ResolveRegion(pos, tris, box, new BoxRotation(0, 0, 0));     // rotation overload, identity

        noRot.IsValid.Should().BeTrue(noRot.Diagnostic);
        idRot.IsValid.Should().BeTrue(idRot.Diagnostic);
        idRot.Vertices.Length.Should().Be(noRot.Vertices.Length);
        idRot.PatchTriangles.Length.Should().Be(noRot.PatchTriangles.Length);
        idRot.LoopCount.Should().Be(noRot.LoopCount);
        ComputeVolume(idRot, pos).Should().BeApproximately(ComputeVolume(noRot, pos), 1e-9);
    }

    [Fact]
    public void BoxRotation_Basis_IsOrthonormal()
    {
        var (ax, ay, az) = new BoxRotation(20, -35, 50).Basis();
        ax.Length.Should().BeApproximately(1f, 1e-5f);
        ay.Length.Should().BeApproximately(1f, 1e-5f);
        az.Length.Should().BeApproximately(1f, 1e-5f);
        Vector3.Dot(ax, ay).Should().BeApproximately(0f, 1e-5f);
        Vector3.Dot(ax, az).Should().BeApproximately(0f, 1e-5f);
        Vector3.Dot(ay, az).Should().BeApproximately(0f, 1e-5f);
    }

    [Fact]
    public void RotatedBox_OnRotatedMesh_RecoversSamePatchAndVolume()
    {
        // Resolve an axis-aligned box on the base cube. Then rotate BOTH the box and the mesh by the
        // same angle about the BOX's center (the pivot the box rotation uses): the box should select
        // the same anatomical patch and report the same volume. This proves the box-local transform is
        // correct. (Rotating the mesh about a different pivot than the box would NOT recover the same
        // patch — the box rotates about box.Center, so the mesh must too.)
        var (pos, tris) = ClosedCube(2f);
        // A box cutting the +X half only (so there's a real cut face, not the whole closed cube).
        var box = new RegionAabb(new Vector3(1, -1, -1), new Vector3(3, 3, 3));
        var pivot = box.Center; // == box rotation pivot

        var baseRegion = ResolveRegion(pos, tris, box);
        baseRegion.IsValid.Should().BeTrue(baseRegion.Diagnostic);
        double baseVol = ComputeVolume(baseRegion, pos, RegionCapMode.FlatPlane);

        // Rotate mesh about the box pivot by 30° around Z; rotate the box the same way.
        var rmesh = pos.Select(p => RotZ(p - pivot, 30) + pivot).ToArray();
        var rotRegion = ResolveRegion(rmesh, tris, box, new BoxRotation(0, 0, 30));
        rotRegion.IsValid.Should().BeTrue(rotRegion.Diagnostic);

        rotRegion.LoopCount.Should().Be(baseRegion.LoopCount);
        double rotVol = ComputeVolume(rotRegion, rmesh, RegionCapMode.FlatPlane);
        rotVol.Should().BeApproximately(baseVol, Tol);
    }

    [Fact]
    public void RotatedCut_CutNormalFollowsRotation()
    {
        // The cut face is the box's local -X face. On an unrotated box that normal is world X; rotating
        // the box 90° about Z should swing the detected cut normal toward world Y.
        var (pos, tris) = ClosedCube(2f);
        var box = new RegionAabb(new Vector3(1, -1, -1), new Vector3(3, 3, 3)); // cut on the x=1 face
        var pivot = box.Center;

        var baseRegion = ResolveRegion(pos, tris, box);
        // Base cut normal is along world X (the cut face is perpendicular to X).
        MathF.Abs(baseRegion.CutNormal.X).Should().BeApproximately(1f, 1e-3f);

        var rmesh = pos.Select(p => RotZ(p - pivot, 90) + pivot).ToArray();
        var rotRegion = ResolveRegion(rmesh, tris, box, new BoxRotation(0, 0, 90));
        rotRegion.IsValid.Should().BeTrue(rotRegion.Diagnostic);
        // After a 90° Z rotation the cut normal should be along world Y, not X.
        MathF.Abs(rotRegion.CutNormal.Y).Should().BeApproximately(1f, 1e-3f);
        MathF.Abs(rotRegion.CutNormal.X).Should().BeLessThan(1e-2f);
    }

    [Fact]
    public void ProjectToPlane_SlidesAlongNormalOntoPlane()
    {
        var n = new Vector3(0, 0, 1);
        var p = new Vector3(3, 4, 9);
        var q = ProjectToPlane(p, n, 2f); // plane z=2
        q.X.Should().Be(3f);
        q.Y.Should().Be(4f);
        q.Z.Should().BeApproximately(2f, 1e-5f);

        // Oblique normal: the projected point must satisfy dot(q,n) == planeD.
        var n2 = new Vector3(1, 1, 0).Normalized();
        var q2 = ProjectToPlane(new Vector3(5, 1, 7), n2, 0f);
        Vector3.Dot(q2, n2).Should().BeApproximately(0f, 1e-5f);
    }
}
