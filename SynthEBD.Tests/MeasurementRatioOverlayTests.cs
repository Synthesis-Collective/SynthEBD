using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using OpenTK.Mathematics;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Covers <see cref="VM_BodyTypeProfile.AppendMeasurementOverlay"/>: a <see cref="MeasurementKind.MeasurementRatio"/>
/// is drawn as its operands, each as it would be drawn alone, with the numerator's geometry and region tint in
/// <see cref="VM_BodyTypeProfile.RatioNumeratorColor"/> and the denominator's in
/// <see cref="VM_BodyTypeProfile.RatioDenominatorColor"/>; non-ratio rows are unchanged.
/// </summary>
public class MeasurementRatioOverlayTests
{
    private static readonly Dictionary<string, Vector3> Points = new()
    {
        ["A"] = new Vector3(0, 0, 0),
        ["B"] = new Vector3(4, 0, 0),
        ["C"] = new Vector3(0, 0, 0),
        ["D"] = new Vector3(0, 3, 0),
    };

    private static VM_BodyTypeProfile.MeasurementLineSpec Point(string a, string b)
        => new(MeasurementKind.PointDistance, a, b, "", "", MeasurementAxis.X, null, null);

    private static VM_BodyTypeProfile.MeasurementLineSpec Volume(string region)
        => new(MeasurementKind.RegionVolume, "", "", "", "", MeasurementAxis.X, null, null, null, region);

    private static VM_BodyTypeProfile.MeasurementLineSpec Ratio(string num, string den)
        => new(MeasurementKind.MeasurementRatio, "", "", "", "", MeasurementAxis.X, null, null,
            MeasurementRefA: num, MeasurementRefB: den);

    private static (List<(Vector3 A, Vector3 B, Vector3 Color, string? Label)> Segs, List<(string Region, Vector3? Color)> Tints)
        Draw(string name, Dictionary<string, VM_BodyTypeProfile.MeasurementLineSpec> specs)
    {
        var segs = new List<(Vector3 A, Vector3 B, Vector3 Color, string? Label)>();
        var tints = new List<(string Region, Vector3? Color)>();
        VM_BodyTypeProfile.AppendMeasurementOverlay(specs[name], name, specs,
            n => Points.TryGetValue(n, out var p) ? p : null,
            (_, _, _) => "",
            null,
            s => s.Kind == MeasurementKind.RegionVolume ? new[] { s.RegionRefName } : Array.Empty<string>(),
            n => n == "width" ? 4f : null,
            name, segs, tints);
        return (segs, tints);
    }

    [Fact]
    public void RatioOfTwoDistances_DrawsNumeratorYellow_DenominatorCyan()
    {
        var specs = new Dictionary<string, VM_BodyTypeProfile.MeasurementLineSpec>
        {
            ["width"] = Point("A", "B"),
            ["height"] = Point("C", "D"),
            ["ratio"] = Ratio("width", "height"),
        };
        var (segs, tints) = Draw("ratio", specs);

        segs.Should().HaveCount(2);
        segs[0].B.Should().Be(Points["B"]);
        segs[0].Color.Should().Be(VM_BodyTypeProfile.RatioNumeratorColor);
        segs[0].Label.Should().Be("ratio (numerator: width = 4.0000)");
        segs[1].B.Should().Be(Points["D"]);
        segs[1].Color.Should().Be(VM_BodyTypeProfile.RatioDenominatorColor);
        segs[1].Label.Should().Be("ratio (denominator: height)");
        tints.Should().BeEmpty();
    }

    [Fact]
    public void RatioOfDistanceAndVolume_DrawsLineAndColoredRegionTint()
    {
        var specs = new Dictionary<string, VM_BodyTypeProfile.MeasurementLineSpec>
        {
            ["width"] = Point("A", "B"),
            ["bust_vol"] = Volume("Bust"),
            ["ratio"] = Ratio("bust_vol", "width"),
        };
        var (segs, tints) = Draw("ratio", specs);

        tints.Should().ContainSingle().Which.Should().Be(("Bust", (Vector3?)VM_BodyTypeProfile.RatioNumeratorColor));
        segs.Should().ContainSingle().Which.Color.Should().Be(VM_BodyTypeProfile.RatioDenominatorColor);
    }

    [Fact]
    public void NestedRatio_TakesOutermostRoleColor_AndCycleTerminates()
    {
        var specs = new Dictionary<string, VM_BodyTypeProfile.MeasurementLineSpec>
        {
            ["width"] = Point("A", "B"),
            ["height"] = Point("C", "D"),
            ["inner"] = Ratio("width", "height"),
            ["outer"] = Ratio("inner", "width"),
            ["loop1"] = Ratio("loop2", "width"),
            ["loop2"] = Ratio("loop1", "height"),
        };
        var (segs, _) = Draw("outer", specs);
        segs.Should().HaveCount(3);
        segs.Take(2).Should().OnlyContain(s => s.Color == VM_BodyTypeProfile.RatioNumeratorColor);
        segs[2].Color.Should().Be(VM_BodyTypeProfile.RatioDenominatorColor);

        var (loopSegs, _) = Draw("loop1", specs);
        loopSegs.Should().NotBeEmpty(); // finite: the repeat of loop1 is skipped
    }

    [Fact]
    public void NonRatio_KeepsOwnColorsAndDefaultTint()
    {
        var specs = new Dictionary<string, VM_BodyTypeProfile.MeasurementLineSpec>
        {
            ["width"] = Point("A", "B"),
            ["bust_vol"] = Volume("Bust"),
        };
        Draw("width", specs).Segs.Should().ContainSingle().Which.Color.Should().Be(VM_BodyTypeProfile.RatioNumeratorColor);
        Draw("bust_vol", specs).Tints.Should().ContainSingle().Which.Color.Should().BeNull();
    }
}
