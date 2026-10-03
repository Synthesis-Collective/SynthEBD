using System.Collections.Generic;
using FluentAssertions;
using OpenTK.Mathematics;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Covers <see cref="MeasurementKind.Angle"/>: the angle between the directions A->B and C->D, optionally
/// viewed along one axis (<see cref="MeasurementDefinition.AngleViewAxis"/>), through the pure
/// <see cref="MeasurementMath.AngleBetween"/> and end-to-end through <see cref="MeasurementMath.TryEvaluate"/>.
/// Also checks the cache fingerprint only changes for Angle rows.
/// </summary>
public class AngleMeasurementTests
{
    [Theory]
    [InlineData(1, 0, 0, 1, 0, 0, 0)]
    [InlineData(1, 0, 0, 0, 1, 0, 90)]
    [InlineData(1, 0, 0, -1, 0, 0, 180)]
    [InlineData(1, 0, 0, 1, 1, 0, 45)]
    public void AngleBetween_3D(float ux, float uy, float uz, float vx, float vy, float vz, float expected)
    {
        MeasurementMath.AngleBetween(new Vector3(ux, uy, uz), new Vector3(vx, vy, vz), null)!.Value
            .Should().BeApproximately(expected, 1e-3f);
    }

    [Fact]
    public void AngleBetween_SideView_IgnoresLeftRightOffsets()
    {
        // Same side-view directions (Y up, Z back), different X: the side view sees 45 degrees,
        // the full 3D angle does not.
        var u = new Vector3(0f, 1f, 0f);
        var v = new Vector3(5f, 1f, 1f);
        MeasurementMath.AngleBetween(u, v, MeasurementAxis.X)!.Value.Should().BeApproximately(45f, 1e-3f);
        MeasurementMath.AngleBetween(u, v, null)!.Value.Should().NotBeApproximately(45f, 1f);
    }

    [Fact]
    public void AngleBetween_DegenerateProjection_IsNull()
    {
        // A line pointing straight along the view axis collapses to a point in that view.
        MeasurementMath.AngleBetween(new Vector3(3f, 0f, 0f), new Vector3(0f, 1f, 0f), MeasurementAxis.X).Should().BeNull();
        MeasurementMath.AngleBetween(Vector3.Zero, new Vector3(0f, 1f, 0f), null).Should().BeNull();
    }

    [Fact]
    public void VertexRefCount_FourForRatioAndAngle()
    {
        MeasurementMath.VertexRefCount(MeasurementKind.Angle).Should().Be(4);
        MeasurementMath.VertexRefCount(MeasurementKind.RatioDistance).Should().Be(4);
        MeasurementMath.VertexRefCount(MeasurementKind.PointDistance).Should().Be(2);
    }

    private static NamedKeyVertex Kv(string name, int index) => new()
    {
        Name = name,
        ShapeName = "Body",
        VertexIndex = index,
        Strategy = KeyVertexStrategy.Explicit,
    };

    [Fact]
    public void TryEvaluate_KneeFoldGlute_TurnAtSharedPoint()
    {
        // Knee (0,40,5) -> fold (0,58,6) -> glute (3,66,11): with B = C the value is the turn at the fold.
        var positions = new[] { new Vector3(0f, 40f, 5f), new Vector3(0f, 58f, 6f), new Vector3(3f, 66f, 11f) };
        var kvs = new Dictionary<string, NamedKeyVertex> { ["Knee"] = Kv("Knee", 0), ["Fold"] = Kv("Fold", 1), ["Glute"] = Kv("Glute", 2) };
        MeasurementMath.VertexLookup lookup = (shape, idx) => idx >= 0 && idx < positions.Length ? positions[idx] : null;
        var def = new MeasurementDefinition
        {
            Name = "butt_angle",
            Kind = MeasurementKind.Angle,
            AngleViewAxis = MeasurementAxis.X,
            VertexRefNames = new List<string> { "Knee", "Fold", "Fold", "Glute" },
        };

        MeasurementMath.TryEvaluate(def, kvs, lookup, null, null, null, out float value).Should().BeTrue();
        // Side view: knee->fold (dy 18, dz 1) vs fold->glute (dy 8, dz 5).
        double expected = System.Math.Abs(System.Math.Atan2(1, 18) - System.Math.Atan2(5, 8)) * 180.0 / System.Math.PI;
        value.Should().BeApproximately((float)expected, 1e-3f);

        def.VertexRefNames = new List<string> { "Knee", "Fold", "Fold" };
        MeasurementMath.TryEvaluate(def, kvs, lookup, null, null, null, out _).Should().BeFalse("an Angle needs four refs");
    }

    [Fact]
    public void AngleMarker_ArcSpansFromABDirectionToCD_InTheViewPlane()
    {
        // Knee -> fold straight up, fold -> glute up and back with a left-right offset; side view.
        var knee = new Vector3(0f, 40f, 5f);
        var fold = new Vector3(0f, 58f, 5f);
        var glute = new Vector3(4f, 64f, 11f);
        var segs = new List<(Vector3 A, Vector3 B, Vector3 Color, string? Label)>();
        VM_BodyTypeProfile.AppendAngleMarker(knee, fold, fold, glute, MeasurementAxis.X, "m", segs);

        segs.Should().HaveCountGreaterThan(2);
        var guide = segs[0];
        guide.A.Should().Be(fold);
        (guide.B - fold).Normalized().Y.Should().BeApproximately(1f, 1e-4f, "the guide continues A-B's direction");

        var arcStart = segs[1].A - fold;
        var arcEnd = segs[^1].B - fold;
        arcStart.Normalized().Y.Should().BeApproximately(1f, 1e-4f);
        // The arc ends on C-D's side-view direction (dy 6, dz 6 = 45 degrees), not its 3D direction.
        arcEnd.X.Should().BeApproximately(0f, 1e-4f, "the arc lies in the side plane");
        (arcEnd.Z / arcEnd.Y).Should().BeApproximately(1f, 1e-3f);
        foreach (var s in segs) s.A.X.Should().BeApproximately(0f, 1e-4f);
    }

    [Fact]
    public void Fingerprint_ViewAxisCountsForAngleOnly()
    {
        var kvs = new Dictionary<string, NamedKeyVertex> { ["A"] = Kv("A", 0), ["B"] = Kv("B", 1) };
        var angle = new MeasurementDefinition { Name = "m", Kind = MeasurementKind.Angle, VertexRefNames = new() { "A", "B", "A", "B" } };
        string side = MeasurementCacheStore.ComputeMeasurementFingerprint(new MeasurementDefinition
        {
            Name = "m", Kind = MeasurementKind.Angle, AngleViewAxis = MeasurementAxis.X, VertexRefNames = new() { "A", "B", "A", "B" },
        }, kvs);
        MeasurementCacheStore.ComputeMeasurementFingerprint(angle, kvs).Should().NotBe(side);

        var point = new MeasurementDefinition { Name = "p", Kind = MeasurementKind.PointDistance, VertexRefNames = new() { "A", "B" } };
        MeasurementCacheStore.ComputeMeasurementFingerprint(point, kvs).Should().NotContain("AV=");
    }
}
