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
/// <see cref="MeasurementKind.Girth"/>: the tape-measure circumference of a region band, measured as the convex-hull
/// perimeter of horizontal slices selected on the zeroed mesh (<see cref="RegionGirthEvaluator"/>). Uses a stack of
/// 64-vertex rings, radius 2 except a bulge (radius 3) at Y 4.5 and a pinch (radius 1) at Y 7.5.
/// </summary>
public class GirthMeasurementTests
{
    private const int RingVerts = 64;

    /// <summary>Perimeter of a regular <see cref="RingVerts"/>-gon of circumradius <paramref name="r"/>.</summary>
    private static double Polygon(double r) => 2 * RingVerts * r * Math.Sin(Math.PI / RingVerts);

    private static double RadiusAt(float y) => Math.Abs(y - 4.5f) < 1e-3 ? 3.0 : Math.Abs(y - 7.5f) < 1e-3 ? 1.0 : 2.0;

    /// <summary>Rings every 0.5 units from Y 0 to 10, so each 1.0-step slice (centred on 0.5, 1.5, ...) holds exactly
    /// one ring. The region is deliberately invalid: a girth reads only the member set.</summary>
    private static (ResolvedRegion region, Vector3[] zeroed) RingStack()
    {
        var pos = new List<Vector3>();
        for (int k = 0; k <= 20; k++)
        {
            float y = k * 0.5f;
            double r = RadiusAt(y);
            for (int i = 0; i < RingVerts; i++)
            {
                double a = 2 * Math.PI * i / RingVerts;
                pos.Add(new Vector3((float)(r * Math.Cos(a)), y, (float)(r * Math.Sin(a))));
            }
        }
        var zeroed = pos.ToArray();
        var region = new ResolvedRegion
        {
            ShapeName = "Body",
            IsValid = false,
            Diagnostic = "too many loops",
            MemberVertexIndices = Enumerable.Range(0, zeroed.Length).ToArray(),
            MemberZeroedY = zeroed.Select(p => p.Y).ToArray(),
        };
        return (region, zeroed);
    }

    [Fact]
    public void ConvexHullPerimeter_UnitSquareWithInteriorPoint_IsFour()
    {
        var pts = new List<(double, double)> { (0, 0), (1, 0), (1, 1), (0, 1), (0.5, 0.5), (0.2, 0.7) };
        RegionGirthEvaluator.ConvexHullPerimeter(pts).Should().BeApproximately(4.0, 1e-9);
    }

    [Fact]
    public void ConvexHullPerimeter_BridgesConcavity()
    {
        // Two separate unit squares 2 apart (two thighs): the tape spans the gap, so the hull is a 1 x 5 rectangle.
        var pts = new List<(double, double)> { (0, 0), (1, 0), (1, 1), (0, 1), (3, 0), (4, 0), (4, 1), (3, 1) };
        RegionGirthEvaluator.ConvexHullPerimeter(pts).Should().BeApproximately(2 * (4 + 1), 1e-9);
    }

    [Fact]
    public void ConvexHullPerimeter_DegenerateInput_IsZero()
    {
        RegionGirthEvaluator.ConvexHullPerimeter(new List<(double, double)> { (0, 0), (1, 1) }).Should().Be(0);
        RegionGirthEvaluator.ConvexHullPerimeter(new List<(double, double)> { (0, 0), (1, 1), (2, 2) }).Should().Be(0);
    }

    [Theory]
    [InlineData(GirthAggregate.Max, 3.0)]
    [InlineData(GirthAggregate.Min, 1.0)]
    public void Aggregate_PicksFullestOrNarrowestSlice(GirthAggregate aggregate, double radius)
    {
        var (region, zeroed) = RingStack();
        RegionGirthEvaluator.TryComputeGirth(region, zeroed, aggregate, 1.0f, out float g).Should().BeTrue();
        g.Should().BeApproximately((float)Polygon(radius), 1e-3f);
    }

    [Fact]
    public void Mean_AveragesEverySlice()
    {
        var (region, zeroed) = RingStack();
        // Slices at 0.5 .. 9.5: ten slices, one bulge (r 3), one pinch (r 1), eight at r 2.
        double expected = (Polygon(3) + Polygon(1) + 8 * Polygon(2)) / 10;
        RegionGirthEvaluator.TryComputeGirth(region, zeroed, GirthAggregate.Mean, 1.0f, out float g).Should().BeTrue();
        g.Should().BeApproximately((float)expected, 1e-3f);
    }

    [Fact]
    public void Slices_FollowZeroedSpace_NotTheDeformedHeight()
    {
        // The preset doubles every girth and lifts the body by 5: slices still select by zeroed height, so the
        // bulge is still found and measures twice its zeroed girth.
        var (region, zeroed) = RingStack();
        var deformed = zeroed.Select(p => new Vector3(p.X * 2, p.Y + 5, p.Z * 2)).ToArray();
        RegionGirthEvaluator.TryComputeGirth(region, deformed, GirthAggregate.Max, 1.0f, out float g).Should().BeTrue();
        g.Should().BeApproximately((float)(2 * Polygon(3)), 1e-3f);
    }

    [Fact]
    public void NoMembers_OrOutOfRangeIndex_DoesNotResolve()
    {
        var empty = new ResolvedRegion();
        RegionGirthEvaluator.TryComputeGirth(empty, new Vector3[10], GirthAggregate.Max, 1f, out _).Should().BeFalse();

        var (region, zeroed) = RingStack();
        RegionGirthEvaluator.TryComputeGirth(region, zeroed.Take(10).ToArray(), GirthAggregate.Max, 1f, out _).Should().BeFalse();
    }

    [Fact]
    public void Evaluator_MeasuresGirthOnAnInvalidRegion()
    {
        var (region, zeroed) = RingStack();
        var regions = new Dictionary<string, ResolvedRegion> { ["Band"] = region };
        var def = new MeasurementDefinition { Name = "bust_girth", Kind = MeasurementKind.Girth, RegionRefName = "Band" };

        BodySlideMeasurementEvaluator.TryEvaluateRegionMeasurement(def, regions, _ => zeroed, out float g, out _)
            .Should().BeTrue();
        g.Should().BeApproximately((float)Polygon(3), 1e-3f);

        // The same invalid region cannot give a volume.
        var vol = new MeasurementDefinition { Name = "v", Kind = MeasurementKind.RegionVolume, RegionRefName = "Band" };
        BodySlideMeasurementEvaluator.TryEvaluateRegionMeasurement(vol, regions, _ => zeroed, out _, out var reason)
            .Should().BeFalse();
        reason.Should().Be(BodySlideMeasurementEvaluator.MeasurementFailureReason.RegionNotResolved);
    }

    [Fact]
    public void Evaluator_MissingRegion_ReportsReason()
    {
        var def = new MeasurementDefinition { Name = "g", Kind = MeasurementKind.Girth, RegionRefName = "Nope" };
        BodySlideMeasurementEvaluator.TryEvaluateRegionMeasurement(def, new Dictionary<string, ResolvedRegion>(), _ => null, out _, out var reason)
            .Should().BeFalse();
        reason.Should().Be(BodySlideMeasurementEvaluator.MeasurementFailureReason.MissingRegion);
    }

    [Fact]
    public void TryEvaluate_RejectsGirthWithoutThrowing()
    {
        var def = new MeasurementDefinition { Name = "g", Kind = MeasurementKind.Girth, RegionRefName = "Band" };
        MeasurementMath.TryEvaluate(def, new Dictionary<string, NamedKeyVertex>(), (_, _) => null, null, null, null, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void ResolveRegion_RecordsMemberZeroedHeights()
    {
        // A flat 3 x 3 vertex grid in the XY plane; a box over its middle row selects three vertices at Y 1.
        var pos = new List<Vector3>();
        for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) pos.Add(new Vector3(x, y, 0));
        var tris = new List<int>();
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
            {
                int a = y * 3 + x, b = a + 1, c = a + 4, d = a + 3;
                tris.AddRange(new[] { a, b, c, a, c, d });
            }
        var box = new RegionAabb(new Vector3(-0.5f, 0.5f, -1), new Vector3(2.5f, 1.5f, 1));
        var resolved = ResolveRegion(pos.ToArray(), tris.ToArray(), box, new BoxRotation(0, 0, 0), new RegionResolveOptions());

        resolved.MemberVertexIndices.Should().BeEquivalentTo(new[] { 3, 4, 5 });
        resolved.MemberZeroedY.Should().HaveCount(3).And.OnlyContain(y => y == 1f);
    }

    [Fact]
    public void Fingerprint_CoversAggregateAndStep_AndLeavesVolumeUnchanged()
    {
        var regions = new Dictionary<string, NamedRegion> { ["Band"] = new NamedRegion { Name = "Band", ShapeName = "Body" } };
        string Fp(MeasurementDefinition d) => MeasurementCacheStore.ComputeMeasurementFingerprint(d, null, regions);

        var max = new MeasurementDefinition { Name = "g", Kind = MeasurementKind.Girth, RegionRefName = "Band" };
        var min = new MeasurementDefinition { Name = "g", Kind = MeasurementKind.Girth, RegionRefName = "Band", GirthAggregate = GirthAggregate.Min };
        var fine = new MeasurementDefinition { Name = "g", Kind = MeasurementKind.Girth, RegionRefName = "Band", GirthStep = 0.5f };
        Fp(max).Should().NotBe(Fp(min));
        Fp(max).Should().NotBe(Fp(fine));

        // A volume over the same region ignores the girth fields entirely.
        var vol = new MeasurementDefinition { Name = "g", Kind = MeasurementKind.RegionVolume, RegionRefName = "Band" };
        var volEdited = new MeasurementDefinition { Name = "g", Kind = MeasurementKind.RegionVolume, RegionRefName = "Band", GirthAggregate = GirthAggregate.Min, GirthStep = 3f };
        Fp(vol).Should().Be(Fp(volEdited));
        Fp(vol).Should().NotBe(Fp(max));
    }
}
