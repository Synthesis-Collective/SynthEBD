using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// <see cref="MeasurementKind.MeasurementRatio"/>: one measurement's value divided by another's, read by name after the
/// geometry measurements. Covers evaluation, cache derivation, the operand-aware fingerprint and partial-fill operand
/// expansion.
/// </summary>
public class MeasurementRatioTests
{
    private static MeasurementDefinition Vol(string name, string region) =>
        new() { Name = name, Kind = MeasurementKind.RegionVolume, RegionRefName = region };

    private static MeasurementDefinition Ratio(string name, string num, string den) =>
        new() { Name = name, Kind = MeasurementKind.MeasurementRatio, MeasurementRefNames = new() { num, den } };

    private static MeasurementDefinition Axis(string name, string a, string b) =>
        new() { Name = name, Kind = MeasurementKind.AxisDistance, Axis = MeasurementAxis.X, VertexRefNames = new() { a, b } };

    // ---- evaluation ----

    [Fact]
    public void Ratio_DividesNamedValues()
    {
        var defs = new[] { Vol("bust", "B"), Vol("hip", "H"), Ratio("b2h", "bust", "hip") };
        var values = new Dictionary<string, float> { ["bust"] = 300f, ["hip"] = 400f };

        MeasurementMath.EvaluateMeasurementRatios(defs, values).Should().BeEmpty();
        values["b2h"].Should().BeApproximately(0.75f, 1e-6f);
    }

    [Fact]
    public void RatioOfRatio_ResolvesRegardlessOfRowOrder()
    {
        // The outer ratio is listed first, so it can only resolve on a later pass.
        var defs = new[] { Ratio("outer", "inner", "c"), Ratio("inner", "a", "b"), Vol("a", "A"), Vol("b", "B"), Vol("c", "C") };
        var values = new Dictionary<string, float> { ["a"] = 6f, ["b"] = 3f, ["c"] = 4f };

        MeasurementMath.EvaluateMeasurementRatios(defs, values).Should().BeEmpty();
        values["inner"].Should().Be(2f);
        values["outer"].Should().Be(0.5f);
    }

    [Fact]
    public void MissingOperand_Cycle_SelfReference_AndZeroDenominator_AllStayUnresolved()
    {
        var defs = new[]
        {
            Ratio("missing", "a", "nope"),
            Ratio("x", "y", "a"), Ratio("y", "x", "a"),   // cycle
            Ratio("self", "self", "a"),
            Ratio("zero", "a", "z"),
            Vol("a", "A"), Vol("z", "Z"),
        };
        var values = new Dictionary<string, float> { ["a"] = 1f, ["z"] = 0f };

        MeasurementMath.EvaluateMeasurementRatios(defs, values)
            .Should().BeEquivalentTo(new[] { "missing", "x", "y", "self", "zero" });
        values.Keys.Should().BeEquivalentTo(new[] { "a", "z" });
    }

    [Fact]
    public void NegativeDenominator_IsAllowed_AndMatchesDirectDivision()
    {
        MeasurementMath.TryDivideMeasurements(3f, -2f, out var v).Should().BeTrue();
        v.Should().Be(-1.5f);
    }

    [Fact]
    public void DuplicateNames_FirstRowWins()
    {
        // A geometry row named "r" listed first shadows the later ratio of the same name.
        var defs = new[] { Axis("r", "A", "B"), Ratio("r", "a", "b"), Vol("a", "A"), Vol("b", "B") };
        var values = new Dictionary<string, float> { ["a"] = 1f, ["b"] = 2f };

        MeasurementMath.EvaluateMeasurementRatios(defs, values).Should().BeEmpty();
        values.Should().NotContainKey("r");
    }

    [Fact]
    public void VertexRefCount_IsZero_ForMeasurementRatio() =>
        MeasurementMath.VertexRefCount(MeasurementKind.MeasurementRatio).Should().Be(0);

    // ---- partial fill ----

    [Fact]
    public void WithRatioOperands_AddsOperandsTransitively_AndLeavesNullAlone()
    {
        var defs = new[] { Ratio("outer", "inner", "c"), Ratio("inner", "a", "b"), Vol("a", "A"), Vol("b", "B"), Vol("c", "C"), Vol("d", "D") };

        BodySlideMeasurementEvaluator.WithRatioOperands(new HashSet<string> { "outer" }, defs)
            .Should().BeEquivalentTo(new[] { "outer", "inner", "a", "b", "c" });
        BodySlideMeasurementEvaluator.WithRatioOperands(null, defs).Should().BeNull();
    }

    // ---- cache derivation ----

    [Fact]
    public void Derive_RatioFromCachedOperands_NeedsNoGeometry()
    {
        var defs = new[] { Vol("bust", "B"), Vol("hip", "H"), Ratio("b2h", "bust", "hip") };
        var known = new Dictionary<string, float?> { ["bust"] = 300f, ["hip"] = 400f };

        MeasurementDerivation.Derive(defs, known, new[] { "b2h" })["b2h"].Should().BeApproximately(0.75f, 1e-6f);
    }

    [Fact]
    public void Derive_RatioWhoseOperandIsItselfDerived()
    {
        // waist_to_hip isn't cached, but waist_width and hip_width are, so it derives first and feeds the ratio.
        var defs = new MeasurementDefinition[]
        {
            Axis("waist_width", "L_Waist", "R_Waist"), Axis("hip_width", "L_Hip", "R_Hip"),
            new() { Name = "waist_to_hip", Kind = MeasurementKind.RatioDistance, NumeratorAxis = MeasurementAxis.X,
                    DenominatorAxis = MeasurementAxis.X, VertexRefNames = new() { "L_Waist", "R_Waist", "L_Hip", "R_Hip" } },
            Vol("bust", "B"),
            Ratio("w2h_per_bust", "waist_to_hip", "bust"),
        };
        var known = new Dictionary<string, float?> { ["waist_width"] = 16f, ["hip_width"] = 32f, ["bust"] = 2f };

        var d = MeasurementDerivation.Derive(defs, known, new[] { "waist_to_hip", "w2h_per_bust" });
        d["w2h_per_bust"].Should().BeApproximately(0.25f, 1e-6f);
    }

    [Fact]
    public void Derive_NullOperand_FallsBackToGeometry()
    {
        var defs = new[] { Vol("bust", "B"), Vol("hip", "H"), Ratio("b2h", "bust", "hip") };
        var known = new Dictionary<string, float?> { ["bust"] = 300f, ["hip"] = null };

        MeasurementDerivation.Derive(defs, known, new[] { "b2h" }).Should().BeEmpty();
    }

    // ---- fingerprint ----

    private static Dictionary<string, NamedRegion> Regions(float hipMaxY = 75.5f) => new()
    {
        ["B"] = new NamedRegion { Name = "B", ShapeName = "3BA", BoxMaxY = 103.5f },
        ["H"] = new NamedRegion { Name = "H", ShapeName = "3BA", BoxMaxY = hipMaxY },
    };

    private static string Fp(MeasurementDefinition def, IEnumerable<MeasurementDefinition> all, Dictionary<string, NamedRegion> regions) =>
        MeasurementCacheStore.ComputeMeasurementFingerprint(def, new Dictionary<string, NamedKeyVertex>(), regions,
            MeasurementCacheStore.MeasurementsByName(all));

    [Fact]
    public void Fingerprint_OfOtherKinds_IsUnchangedByTheMeasurementMap()
    {
        var vol = Vol("bust", "B");
        var legacy = MeasurementCacheStore.ComputeMeasurementFingerprint(vol, new Dictionary<string, NamedKeyVertex>(), Regions());
        Fp(vol, new[] { vol }, Regions()).Should().Be(legacy);
    }

    [Fact]
    public void Fingerprint_ChangesWhenAnOperandsDefinitionChanges()
    {
        var all = new[] { Vol("bust", "B"), Vol("hip", "H"), Ratio("b2h", "bust", "hip") };
        Fp(all[2], all, Regions(75.5f)).Should().NotBe(Fp(all[2], all, Regions(80f)));
    }

    [Fact]
    public void Fingerprint_SurvivesAnOperandRename()
    {
        var before = new[] { Vol("bust", "B"), Vol("hip", "H"), Ratio("b2h", "bust", "hip") };
        var after = new[] { Vol("bust", "B"), Vol("hip_band_vol", "H"), Ratio("b2h", "bust", "hip_band_vol") };
        Fp(after[2], after, Regions()).Should().Be(Fp(before[2], before, Regions()));
    }

    [Fact]
    public void Fingerprint_OperandOrderMatters_AndMissingOrCyclicOperandsDontThrow()
    {
        var all = new[] { Vol("bust", "B"), Vol("hip", "H"), Ratio("b2h", "bust", "hip"), Ratio("h2b", "hip", "bust"),
                          Ratio("x", "y", "bust"), Ratio("y", "x", "bust"), Ratio("gone", "nope", "hip") };
        Fp(all[2], all, Regions()).Should().NotBe(Fp(all[3], all, Regions()));
        Fp(all[4], all, Regions()).Should().NotBeNullOrEmpty();
        Fp(all[6], all, Regions()).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ComputeAll_FoldsOperandsIn()
    {
        var all = new List<MeasurementDefinition> { Vol("bust", "B"), Vol("hip", "H"), Ratio("b2h", "bust", "hip") };
        var a = MeasurementCacheStore.ComputeAllMeasurementFingerprints(all, new List<NamedKeyVertex>(), Regions(75.5f).Values.ToList());
        var b = MeasurementCacheStore.ComputeAllMeasurementFingerprints(all, new List<NamedKeyVertex>(), Regions(80f).Values.ToList());
        a["b2h"].Should().NotBe(b["b2h"]);
        a["bust"].Should().Be(b["bust"]);
    }

    // ---- serialization ----

    [Fact]
    public void LegacyJson_WithoutMeasurementRefNames_DeserializesToEmptyList()
    {
        var def = JsonConvert.DeserializeObject<MeasurementDefinition>(
            "{\"Name\":\"w\",\"Kind\":2,\"VertexRefNames\":[\"A\",\"B\"],\"Axis\":0}");
        def!.MeasurementRefNames.Should().NotBeNull().And.BeEmpty();
    }
}
