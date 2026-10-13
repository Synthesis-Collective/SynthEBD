using System;
using System.Collections.Generic;
using FluentAssertions;
using SynthEBD;
using Xunit;
using Vector3 = OpenTK.Mathematics.Vector3;
using static SynthEBD.RegionVolumeEvaluator;

namespace SynthEBD.Tests;

/// <summary>
/// A region may opt into more than two cap loops through <see cref="NamedRegion.ExpectedCapCount"/> (e.g. a hip band
/// cut at the waist and through both legs). Uses an open-top 5 x 2 x 3 box whose floor has two separate holes: three
/// boundary loops, enclosed volume exactly 30 once every loop is capped.
/// </summary>
public class RegionMultiLoopTests
{
    /// <summary>Open-top box [0,5]x[0,2]x[0,3] on a unit grid (shared vertices, outward winding) with floor
    /// cells (1,1) and (3,1) removed.</summary>
    private static (Vector3[] pos, int[] tris) OpenTopBoxWithTwoFloorHoles()
    {
        var pos = new List<Vector3>();
        var index = new Dictionary<(float, float, float), int>();
        var tris = new List<int>();
        int V(float x, float y, float z)
        {
            if (!index.TryGetValue((x, y, z), out int i))
            {
                i = pos.Count; pos.Add(new Vector3(x, y, z)); index[(x, y, z)] = i;
            }
            return i;
        }
        void Quad(int a, int b, int c, int d) => tris.AddRange(new[] { a, b, c, a, c, d });

        for (int x = 0; x < 5; x++)
            for (int z = 0; z < 3; z++)
                if (!(z == 1 && (x == 1 || x == 3)))
                    Quad(V(x, 0, z), V(x + 1, 0, z), V(x + 1, 0, z + 1), V(x, 0, z + 1)); // floor, -Y
        for (int x = 0; x < 5; x++)
        {
            Quad(V(x, 0, 0), V(x, 2, 0), V(x + 1, 2, 0), V(x + 1, 0, 0)); // front, -Z
            Quad(V(x, 0, 3), V(x + 1, 0, 3), V(x + 1, 2, 3), V(x, 2, 3)); // back, +Z
        }
        for (int z = 0; z < 3; z++)
        {
            Quad(V(0, 0, z), V(0, 0, z + 1), V(0, 2, z + 1), V(0, 2, z)); // left, -X
            Quad(V(5, 0, z), V(5, 2, z), V(5, 2, z + 1), V(5, 0, z + 1)); // right, +X
        }
        return (pos.ToArray(), tris.ToArray());
    }

    private static NamedRegion Region(int? expectedCaps) => new()
    {
        Name = "band", ShapeName = "s",
        BoxMinX = -1, BoxMinY = -1, BoxMinZ = -1, BoxMaxX = 6, BoxMaxY = 3, BoxMaxZ = 4,
        ExpectedCapCount = expectedCaps, CapMode = RegionCapMode.FlatPlane,
    };

    private static ResolvedRegion Resolve(int? expectedCaps)
    {
        var (p, t) = OpenTopBoxWithTwoFloorHoles();
        return ResolveRegions(new[] { Region(expectedCaps) }, _ => p, _ => t)["band"];
    }

    [Fact]
    public void ThreeLoops_OptedIn_ResolvesAndCapsEveryLoop()
    {
        var r = Resolve(3);

        r.IsValid.Should().BeTrue(r.Diagnostic);
        r.LoopCount.Should().Be(3);
        var (p, _) = OpenTopBoxWithTwoFloorHoles();
        ComputeVolume(r, p, RegionCapMode.FlatPlane).Should().BeApproximately(30.0, 1e-4);
    }

    [Fact]
    public void ThreeLoops_WithoutOptIn_StillRejected()
    {
        var r = Resolve(null);

        r.IsValid.Should().BeFalse();
        r.Diagnostic.Should().Contain("max 2");
    }
}
