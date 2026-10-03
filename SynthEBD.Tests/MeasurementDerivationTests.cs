using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Coverage for <see cref="MeasurementDerivation"/>: filling a newly-added measurement from values
/// already cached for the same slice, so the scan needs no mesh work for it.
/// </summary>
public class MeasurementDerivationTests
{
    private static MeasurementDefinition Axis(string name, string a, string b, MeasurementAxis axis = MeasurementAxis.X) =>
        new() { Name = name, Kind = MeasurementKind.AxisDistance, Axis = axis, VertexRefNames = new() { a, b } };

    private static MeasurementDefinition Ratio(string name, string a, string b, string c, string d,
        MeasurementAxis? num = MeasurementAxis.X, MeasurementAxis? den = MeasurementAxis.X) =>
        new() { Name = name, Kind = MeasurementKind.RatioDistance, NumeratorAxis = num, DenominatorAxis = den, VertexRefNames = new() { a, b, c, d } };

    private static readonly MeasurementDefinition WaistWidth = Axis("waist_width", "L_Waist", "R_Waist");
    private static readonly MeasurementDefinition HipWidth = Axis("hip_width", "L_Hip", "R_Hip");
    private static readonly MeasurementDefinition ArmpitToHip = Ratio("armpit_to_hip", "L_Armpit", "R_Armpit", "L_Hip", "R_Hip");
    private static readonly MeasurementDefinition WaistToArmpit = Ratio("waist_to_armpit", "L_Waist", "R_Waist", "L_Armpit", "R_Armpit");

    [Fact]
    public void NewRatio_OfTwoCachedDistances_Derives()
    {
        var defs = new[] { WaistWidth, HipWidth, Ratio("waist_to_hip", "L_Waist", "R_Waist", "L_Hip", "R_Hip") };
        var known = new Dictionary<string, float?> { ["waist_width"] = 16f, ["hip_width"] = 32f };

        MeasurementDerivation.Derive(defs, known, new[] { "waist_to_hip" })
            .Should().ContainKey("waist_to_hip").WhoseValue.Should().BeApproximately(0.5f, 1e-6f);
    }

    [Fact]
    public void DistanceRecoveredThroughACachedRatio_FeedsTheNewRatio()
    {
        // armpit width isn't cached on its own, but armpit_to_hip * hip_width gives it:
        // armpit = 0.9 * 32 = 28.8, so waist_to_armpit = 16 / 28.8.
        var defs = new[] { WaistWidth, HipWidth, ArmpitToHip, WaistToArmpit };
        var known = new Dictionary<string, float?> { ["waist_width"] = 16f, ["hip_width"] = 32f, ["armpit_to_hip"] = 0.9f };

        MeasurementDerivation.Derive(defs, known, new[] { "waist_to_armpit" })
            .Should().ContainKey("waist_to_armpit").WhoseValue.Should().BeApproximately(16f / (0.9f * 32f), 1e-5f);
    }

    [Fact]
    public void VertexOrder_DoesNotMatter_ForAbsoluteTerms()
    {
        var defs = new[] { Axis("w", "L_Waist", "R_Waist"), Axis("w_reversed", "R_Waist", "L_Waist") };
        var known = new Dictionary<string, float?> { ["w"] = 16f };

        MeasurementDerivation.Derive(defs, known, new[] { "w_reversed" })["w_reversed"].Should().Be(16f);
    }

    [Fact]
    public void DifferentAxis_OrLengthVsAxis_IsNotTheSameTerm()
    {
        var defs = new[]
        {
            WaistWidth,
            Axis("waist_depth", "L_Waist", "R_Waist", MeasurementAxis.Z),
            new MeasurementDefinition { Name = "waist_span", Kind = MeasurementKind.PointDistance, VertexRefNames = new() { "L_Waist", "R_Waist" } },
            Ratio("len_ratio", "L_Waist", "R_Waist", "L_Hip", "R_Hip", num: null, den: MeasurementAxis.X),
            HipWidth,
        };
        var known = new Dictionary<string, float?> { ["waist_width"] = 16f, ["hip_width"] = 32f };

        MeasurementDerivation.Derive(defs, known, new[] { "waist_depth", "waist_span", "len_ratio" })
            .Should().BeEmpty("an X separation says nothing about the Z separation or the 3D length");
    }

    [Fact]
    public void SignedAxis_ReversedPair_IsNegated()
    {
        var defs = new[]
        {
            new MeasurementDefinition { Name = "s", Kind = MeasurementKind.SignedAxisDistance, Axis = MeasurementAxis.Z, VertexRefNames = new() { "Navel", "Spine" } },
            new MeasurementDefinition { Name = "s_rev", Kind = MeasurementKind.SignedAxisDistance, Axis = MeasurementAxis.Z, VertexRefNames = new() { "Spine", "Navel" } },
        };
        var known = new Dictionary<string, float?> { ["s"] = -7.5f };

        MeasurementDerivation.Derive(defs, known, new[] { "s_rev" })["s_rev"].Should().Be(7.5f);
    }

    [Fact]
    public void NullValues_NearZeroDenominators_AndRegionVolumes_AreNotDerived()
    {
        var defs = new[]
        {
            WaistWidth, HipWidth,
            Ratio("waist_to_hip", "L_Waist", "R_Waist", "L_Hip", "R_Hip"),
            new MeasurementDefinition { Name = "belly_volume", Kind = MeasurementKind.RegionVolume, RegionRefName = "Belly" },
        };

        MeasurementDerivation.Derive(defs, new Dictionary<string, float?> { ["waist_width"] = 16f, ["hip_width"] = null },
                new[] { "waist_to_hip" })
            .Should().BeEmpty("a null cached value states nothing");

        MeasurementDerivation.Derive(defs, new Dictionary<string, float?> { ["waist_width"] = 16f, ["hip_width"] = 0f },
                new[] { "waist_to_hip" })
            .Should().BeEmpty("the geometry evaluator refuses a denominator under 1e-6, so derivation does too");

        MeasurementDerivation.Derive(defs, new Dictionary<string, float?> { ["waist_width"] = 16f, ["hip_width"] = 32f },
                new[] { "belly_volume" })
            .Should().BeEmpty("a region volume reads geometry, not key-vertex distances");
    }

    [Fact]
    public void UnknownTerm_LeavesTheMeasurementForTheGeometryScan()
    {
        var defs = new[] { WaistWidth, WaistToArmpit };
        var known = new Dictionary<string, float?> { ["waist_width"] = 16f }; // nothing pins the armpit width

        MeasurementDerivation.Derive(defs, known, new[] { "waist_to_armpit" }).Should().BeEmpty();
    }
}
