using System;
using System.Collections.Generic;
using FluentAssertions;
using SynthEBD;
using Xunit;
using Vector3 = OpenTK.Mathematics.Vector3;

namespace SynthEBD.Tests;

/// <summary>
/// <see cref="TorsoTraceEvaluator"/>: the torso trace (the body cut by its own mid-depth surface), the stem window
/// and the lat flare. Uses stacks of elliptical rings (a torso, optionally two hanging "arm" tubes) whose half-width
/// at each height is known, plus loose "bust" vertices in front of the chest.
/// </summary>
public class TorsoTraceTests
{
    private const int RingVerts = 128;
    private const float Depth = 6f;           // torso half-depth (Z)
    private const float RingStep = 0.5f;

    /// <summary>Rings from Y 58 to 112 with half-width <paramref name="halfWidth"/>(y) along X and half-depth
    /// <see cref="Depth"/>, centred on (0, y, <paramref name="zCentre"/>). Angles are offset half a step so no vertex
    /// lies exactly on the cut. Optional arm tubes (radius 2) hang at X = +-25 over Y 75-100.</summary>
    internal static (List<Vector3> pos, List<int> tris) Body(Func<double, double> halfWidth, bool arms = false, float zCentre = 0f)
    {
        var pos = new List<Vector3>();
        var tris = new List<int>();
        AddTube(pos, tris, 58f, 112f, y => halfWidth(y), y => Depth, 0f, zCentre);
        if (arms)
        {
            AddTube(pos, tris, 75f, 100f, y => 2.0, y => 2.0, 25f, zCentre);
            AddTube(pos, tris, 75f, 100f, y => 2.0, y => 2.0, -25f, zCentre);
        }
        return (pos, tris);
    }

    private static void AddTube(List<Vector3> pos, List<int> tris, float y0, float y1,
        Func<double, double> rx, Func<double, double> rz, float cx, float cz)
    {
        int first = pos.Count;
        int rings = (int)Math.Round((y1 - y0) / RingStep) + 1;
        for (int k = 0; k < rings; k++)
        {
            float y = y0 + k * RingStep;
            for (int i = 0; i < RingVerts; i++)
            {
                double a = 2 * Math.PI * (i + 0.5) / RingVerts;
                pos.Add(new Vector3(cx + (float)(rx(y) * Math.Cos(a)), y, cz + (float)(rz(y) * Math.Sin(a))));
            }
        }
        for (int k = 0; k + 1 < rings; k++)
        {
            for (int i = 0; i < RingVerts; i++)
            {
                int a = first + k * RingVerts + i, b = first + k * RingVerts + (i + 1) % RingVerts;
                int c = a + RingVerts, d = b + RingVerts;
                tris.AddRange(new[] { a, b, c, b, d, c });
            }
        }
    }

    /// <summary>Half-width a regular polygon's crossing reads for an ellipse of half-width <paramref name="a"/>: the
    /// chord between the two vertices straddling the X axis.</summary>
    private static double Chord(double a) => a * Math.Cos(Math.PI / RingVerts);

    private static TorsoTraceEvaluator.Trace Trace(List<Vector3> pos, List<int> tris)
    {
        var t = TorsoTraceEvaluator.BuildTrace(pos.ToArray(), tris.ToArray());
        t.Should().NotBeNull();
        return t!;
    }

    [Fact]
    public void MidSurface_FollowsTheTorsoCentreOutsideTheBustHeights()
    {
        var (pos, _) = Body(y => 10, zCentre: 3f);
        TorsoTraceEvaluator.TryMidSurface(pos.ToArray(), out double firstY, out double[] z).Should().BeTrue();
        firstY.Should().Be(58);
        z[(int)(70 - firstY)].Should().BeApproximately(3.0, 1e-3);
        z[(int)(96 - firstY)].Should().BeApproximately(3.0, 1e-3, "the bust heights are interpolated from the torso either side");
    }

    [Fact]
    public void BuildTrace_NoTriangles_ReturnsNull()
    {
        var (pos, _) = Body(y => 10);
        TorsoTraceEvaluator.BuildTrace(pos.ToArray(), Array.Empty<int>()).Should().BeNull();
    }

    [Fact]
    public void InnerHalfWidth_ReadsTheTorsoSide_AndIgnoresTheArms()
    {
        var t = Trace(Body(y => 10, arms: true).pos, Body(y => 10, arms: true).tris);
        foreach (double y in new[] { 70.0, 85.0, 95.0 })
            t.InnerHalfWidth(y, 0.25).Should().BeApproximately(Chord(10), 1e-3);
        t.ExtentWidth(85, 0.5, 20).Should().BeApproximately(2 * Chord(10), 1e-3, "the arm loops at |X| ~ 25 are outside |X| < 20");
    }

    [Fact]
    public void LatFlare_IsWaistOverUnderArmpitWidth()
    {
        // waist half-width 8 over Y 79-92.5, 12 from Y 92.5 up (under the armpits), 10 below
        Func<double, double> w = y => y >= 92.5 ? 12 : y >= 79 ? 8 : 10;
        var (pos, tris) = Body(w, arms: true);
        TorsoTraceEvaluator.TryComputeLatFlare(Trace(pos, tris), out float lat).Should().BeTrue();
        lat.Should().BeApproximately((float)(8.0 / 12.0), 0.01f);
    }

    [Fact]
    public void LatFlare_StraightTorso_IsOne()
    {
        var (pos, tris) = Body(y => 10, arms: true);
        TorsoTraceEvaluator.TryComputeLatFlare(Trace(pos, tris), out float lat).Should().BeTrue();
        lat.Should().BeApproximately(1f, 1e-3f);
    }

    /// <summary>A straight stem of half-width 8 from Y 82 up, flaring out below (hips: +0.5 per unit).</summary>
    internal static double SpoonStem(double y) => y >= 82 ? 8 : 8 + (82 - y) * 0.5;

    internal static readonly Vector3[] Sternum = { new(0, 88, -Depth), new(0, 96, -Depth), new(0, 105, -Depth) };

    /// <summary>Loose bust vertices at X = <paramref name="x"/>, Z = -20 (well in front of the sternum), from Y
    /// <paramref name="bottom"/> to 100. Returns their indices.</summary>
    internal static List<int> AddBust(List<Vector3> pos, float x, float bottom)
    {
        var idx = new List<int>();
        for (float y = bottom; y <= 100f; y += 1f)
        {
            idx.Add(pos.Count);
            pos.Add(new Vector3(x, y, -20f));
        }
        return idx;
    }

    [Fact]
    public void StemWindow_RunsFromTheFlareStartToWhereTheBustStopsCoveringTheSide()
    {
        var (pos, tris) = Body(SpoonStem);
        var bust = AddBust(pos, 9.5f, 90f);  // reaches past the side (8): it hides the torso from Y 90 up
        var t = Trace(pos, tris);
        TorsoTraceEvaluator.TryComputeStemWindow(t, pos.ToArray(), bust, Sternum, out var w).Should().BeTrue();

        w.BustCovers.Should().BeTrue();
        w.TopY.Should().BeApproximately(90f, 1e-3f);
        // narrowest = 8 from Y 82 up; walking down, Y 81.75 (8.125) is within 3%, Y 81.5 (8.25) is not
        w.BottomY.Should().BeApproximately(81.75f, 1e-3f);
        w.Length.Should().BeApproximately(90f - 81.75f, 1e-3f);
        w.Flare.Should().BeApproximately(8.125f / 8f, 0.003f);
    }

    [Fact]
    public void StemWindow_BustInsideTheSide_HidesNothing_TopIsTheCap()
    {
        var (pos, tris) = Body(SpoonStem);
        var bust = AddBust(pos, 7f, 88f);    // in front of the sternum but inside the torso side
        TorsoTraceEvaluator.TryComputeStemWindow(Trace(pos, tris), pos.ToArray(), bust, Sternum, out var w).Should().BeTrue();
        w.BustCovers.Should().BeFalse();
        w.TopY.Should().Be((float)TorsoTraceEvaluator.StemTopCapY);
    }

    [Fact]
    public void StemWindow_VerticesBehindTheSternumLine_AreTorsoNotBust()
    {
        var (pos, tris) = Body(SpoonStem);
        var idx = new List<int>();
        for (float y = 88f; y <= 100f; y += 1f) { idx.Add(pos.Count); pos.Add(new Vector3(9.5f, y, -Depth + 0.5f)); }
        TorsoTraceEvaluator.TryComputeStemWindow(Trace(pos, tris), pos.ToArray(), idx, Sternum, out var w).Should().BeTrue();
        w.BustCovers.Should().BeFalse("they sit less than 1 unit in front of the sternum line");
    }

    [Fact]
    public void StemWindow_TaperedStem_ReadsLatFlare()
    {
        // the stem widens from 8 at Y 84 to 10 at Y 94: a taper in from the lats
        Func<double, double> w = y => y >= 94 ? 10 : y >= 84 ? 8 + (y - 84) * 0.2 : 8 + (84 - y) * 0.5;
        var (pos, tris) = Body(w);
        var bust = AddBust(pos, 12f, 94f);
        TorsoTraceEvaluator.TryComputeStemWindow(Trace(pos, tris), pos.ToArray(), bust, Sternum, out var sw).Should().BeTrue();
        sw.TopY.Should().BeApproximately(94f, 1e-3f);
        sw.Flare.Should().BeApproximately(10f / 8f, 0.01f);
    }

    /// <summary>A small triangle across the cut surface (Z = <paramref name="zCentre"/>) at height <paramref name="y"/>,
    /// spanning X <paramref name="x0"/>..<paramref name="x1"/>: two stray trace points, like the mid-depth surface
    /// grazing the skin of a very heavy body.</summary>
    private static void AddStray(List<Vector3> pos, List<int> tris, float y, float x0, float x1, float zCentre = 0f)
    {
        int a = pos.Count;
        pos.Add(new Vector3(x0, y, zCentre - 1f));
        pos.Add(new Vector3(x1, y, zCentre - 1f));
        pos.Add(new Vector3(0.5f * (x0 + x1), y + 0.1f, zCentre + 1f));
        tris.AddRange(new[] { a, a + 1, a + 2 });
    }

    [Fact]
    public void SparseTrace_PointsNearTheCentreLine_AreNotTorsoSides()
    {
        var (pos, tris) = Body(SpoonStem);
        var bust = AddBust(pos, 9.5f, 90f);
        TorsoTraceEvaluator.TryComputeStemWindow(Trace(pos, tris), pos.ToArray(), bust, Sternum, out var clean).Should().BeTrue();

        AddStray(pos, tris, 85f, -0.3f, 0.3f);   // straddles X = 0 at the waist
        var t = Trace(pos, tris);
        t.InnerHalfWidth(85, 0.25).Should().BeApproximately(Chord(8), 1e-3, "the stray points sit inside the centre-line gap");
        TorsoTraceEvaluator.TryComputeStemWindow(t, pos.ToArray(), bust, Sternum, out var w).Should().BeTrue();
        w.Should().Be(clean, "a near-zero narrowest width would blow the flare up");
    }

    [Fact]
    public void SparseTrace_LonePointsOnOneSide_GiveNoWidth()
    {
        // no torso surface between Y 84 and 86; only a stray triangle on the right side at Y 85
        var pos = new List<Vector3>();
        var tris = new List<int>();
        AddTube(pos, tris, 58f, 84f, y => 10, y => Depth, 0f, 0f);
        AddTube(pos, tris, 86f, 112f, y => 10, y => Depth, 0f, 0f);
        AddStray(pos, tris, 85f, 8f, 9f);
        var t = Trace(pos, tris);

        double.IsNaN(t.ExtentWidth(85, 0.5, 20)).Should().BeTrue("one side alone would read as a near-zero width");
        double.IsNaN(t.InnerWidth(85, 0.5)).Should().BeTrue();
        TorsoTraceEvaluator.TryComputeLatFlare(t, out float lat).Should().BeTrue();
        lat.Should().BeApproximately(1f, 1e-3f, "the waist heights with only one side are skipped");
    }

    [Fact]
    public void Interp_MatchesNumpy_ClampedAtTheEnds()
    {
        var xp = new List<double> { 1, 2, 4 };
        var fp = new List<double> { 10, 20, 0 };
        TorsoTraceEvaluator.Interp(0, xp, fp).Should().Be(10);
        TorsoTraceEvaluator.Interp(3, xp, fp).Should().Be(10);
        TorsoTraceEvaluator.Interp(5, xp, fp).Should().Be(0);
    }
}
