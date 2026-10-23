using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json;
using SynthEBD;
using Xunit;
using Vector3 = OpenTK.Mathematics.Vector3;

namespace SynthEBD.Tests;

/// <summary>
/// <see cref="MeasurementDefinition.Enabled"/>: a disabled measurement is kept as a definition but is never given a
/// cache fingerprint (so never cached or saved) and never reaches the rules. It is not evaluated either, except when an
/// enabled MeasurementRatio reads it: then it is computed on the fly for the ratio and dropped again.
/// </summary>
public class MeasurementEnabledTests
{
    private const string Shape = "Body";

    private static MeasurementDefinition Axis(string name, string a, string b, bool enabled = true) =>
        new() { Name = name, Kind = MeasurementKind.AxisDistance, Axis = MeasurementAxis.X, VertexRefNames = new() { a, b }, Enabled = enabled };

    private static MeasurementDefinition Ratio(string name, string num, string den, bool enabled = true) =>
        new() { Name = name, Kind = MeasurementKind.MeasurementRatio, MeasurementRefNames = new() { num, den }, Enabled = enabled };

    [Fact]
    public void ActiveNames_ExcludeDisabled_ButKeepOperandsOfEnabledRatios()
    {
        var defs = new[]
        {
            Axis("on", "a", "b"),
            Axis("off", "a", "c", enabled: false),
            Axis("num", "a", "b", enabled: false),
            Axis("den", "a", "c", enabled: false),
            Ratio("ratio", "num", "den"),
            Ratio("offRatio", "on", "off", enabled: false),
        };

        MeasurementMath.ActiveMeasurementNames(defs).Should().BeEquivalentTo(new[] { "on", "num", "den", "ratio" });
    }

    [Fact]
    public void ActiveNames_FollowRatioChains()
    {
        var defs = new[]
        {
            Ratio("outer", "inner", "c"),
            Ratio("inner", "a", "b", enabled: false),
            Axis("a", "p", "q", enabled: false), Axis("b", "p", "r", enabled: false), Axis("c", "q", "r", enabled: false),
        };

        MeasurementMath.ActiveMeasurementNames(defs).Should().BeEquivalentTo(new[] { "outer", "inner", "a", "b", "c" });
    }

    [Fact]
    public void Fingerprints_SkipDisabled_AndEnabledIsNotPartOfTheFingerprint()
    {
        var on = Axis("on", "a", "b");
        var off = Axis("off", "a", "c", enabled: false);
        var fps = MeasurementCacheStore.ComputeAllMeasurementFingerprints(new[] { on, off }, null);

        fps.Keys.Should().BeEquivalentTo(new[] { "on" });

        // Re-enabling produces the same fingerprint the value would have had all along, so toggling never
        // invalidates anything else and a value cached before the disable would still be accepted.
        var reEnabled = MeasurementCacheStore.ComputeAllMeasurementFingerprints(new[] { on, Axis("off", "a", "c") }, null);
        reEnabled["on"].Should().Be(fps["on"]);
        reEnabled.Should().ContainKey("off");
    }

    [Fact]
    public void CachedNames_AreExactlyTheEnabledRows_EvenForOperandsOfEnabledRatios()
    {
        var defs = new[]
        {
            Axis("on", "a", "b"),
            Axis("num", "a", "b", enabled: false),
            Axis("den", "a", "c", enabled: false),
            Ratio("ratio", "num", "den"),
            Axis("on", "x", "y", enabled: false), // duplicate name: first row wins
        };

        MeasurementMath.CachedMeasurementNames(defs).Should().BeEquivalentTo(new[] { "on", "ratio" });
        MeasurementMath.ActiveMeasurementNames(defs).Should().BeEquivalentTo(new[] { "on", "num", "den", "ratio" });
    }

    [Fact]
    public void Fingerprints_OmitDisabledRatioOperands_ButTheRatioTracksTheirDefinitions()
    {
        var defs = new List<MeasurementDefinition> { Axis("num", "a", "b", enabled: false), Axis("den", "a", "c", enabled: false), Ratio("ratio", "num", "den") };
        var fps = MeasurementCacheStore.ComputeAllMeasurementFingerprints(defs, null);
        fps.Keys.Should().BeEquivalentTo(new[] { "ratio" });

        // Editing a disabled operand's definition still changes what the ratio measures.
        var edited = new List<MeasurementDefinition> { Axis("num", "a", "c", enabled: false), Axis("den", "a", "c", enabled: false), Ratio("ratio", "num", "den") };
        MeasurementCacheStore.ComputeAllMeasurementFingerprints(edited, null)["ratio"].Should().NotBe(fps["ratio"]);
    }

    [Fact]
    public void Derive_BuildsRatio_FromTermsOfUncachedOperands()
    {
        // The operands are disabled, so nothing caches them; but other cached rows read the same pairs.
        var defs = new List<MeasurementDefinition>
        {
            Axis("ab_cached", "a", "b"),
            Axis("ac_cached", "c", "a"), // reversed pair: same absolute term
            Axis("num", "a", "b", enabled: false),
            Axis("den", "a", "c", enabled: false),
            Ratio("ratio", "num", "den"),
            Ratio("outer", "ratio", "den"), // nested: reads an uncached ratio operand
        };
        var known = new Dictionary<string, float?> { ["ab_cached"] = 2f, ["ac_cached"] = 8f };

        var derived = MeasurementDerivation.Derive(defs, known, new[] { "ratio", "outer" });

        derived["ratio"].Should().Be(2f / 8f);
        derived["outer"].Should().Be((2f / 8f) / 8f);
        derived.Should().NotContainKey("num").And.NotContainKey("den"); // computed for the ratios only
    }

    [Fact]
    public void Derive_LeavesRatioUnresolved_WhenAnOperandHasNoKnownTerm()
    {
        var defs = new List<MeasurementDefinition>
        {
            Axis("ab_cached", "a", "b"),
            Axis("num", "a", "b", enabled: false),
            Axis("den", "a", "c", enabled: false),
            Ratio("ratio", "num", "den"),
        };
        var known = new Dictionary<string, float?> { ["ab_cached"] = 2f };

        // Not derivable: the scan falls back to a mesh partial fill for it.
        MeasurementDerivation.Derive(defs, known, new[] { "ratio" }).Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_SkipsDisabledMeasurements_ButComputesOperandsOfEnabledRatios()
    {
        var p = new[] { new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(8, 0, 0) };
        var profile = new BodyTypeProfile
        {
            KeyVertices = new List<NamedKeyVertex>
            {
                new() { Name = "a", ShapeName = Shape, VertexIndex = 0 },
                new() { Name = "b", ShapeName = Shape, VertexIndex = 1 },
                new() { Name = "c", ShapeName = Shape, VertexIndex = 2 },
            },
            Measurements = new List<MeasurementDefinition>
            {
                Axis("ab", "a", "b"),
                Axis("ac_off", "a", "c", enabled: false),
                Axis("num_off", "a", "b", enabled: false),
                Ratio("ratio", "num_off", "ac_off_operand"),
                Axis("ac_off_operand", "a", "c", enabled: false),
            },
        };
        var mesh = new MeasurementMeshSource
        {
            Vertex = (s, i) => s == Shape && i >= 0 && i < p.Length ? p[i] : null,
            Positions = s => s == Shape ? p : null,
            Bones = _ => (null, null),
            Zeroed = (s, _) => s == Shape ? p : null,
            Indices = _ => null,
            ShapeVertexCounts = () => new Dictionary<string, int> { [Shape] = p.Length },
        };

        var r = BodySlideMeasurementEvaluator.Evaluate(mesh, profile, skipRules: true);

        r.Measurements.Should().ContainKey("ab");
        r.Measurements.Should().NotContainKey("ac_off");
        r.FailedMeasurements.Should().NotContainKey("ac_off");
        r.Measurements["ratio"].Should().BeApproximately(0.25f, 1e-6f); // 2 / 8, operands evaluated though disabled
        // ...but only to feed the ratio: the result holds what the cache would, so rules can't read them.
        r.Measurements.Should().NotContainKey("num_off").And.NotContainKey("ac_off_operand");
        r.FailedMeasurements.Should().NotContainKey("num_off").And.NotContainKey("ac_off_operand");
    }

    [Fact]
    public void NewFields_DefaultForLegacyJson_AndRoundTrip()
    {
        var legacy = JsonConvert.DeserializeObject<MeasurementDefinition>("{\"Name\":\"x\"}")!;
        legacy.Enabled.Should().BeTrue();

        var rule = new MeasurementRule
        {
            Description = "whole rule",
            GroupsORlogic = new() { new AndGatedMeasurementGroup { Description = "branch" } },
        };
        var back = JsonConvert.DeserializeObject<MeasurementRule>(JsonConvert.SerializeObject(rule))!;
        back.Description.Should().Be("whole rule");
        back.GroupsORlogic.Single().Description.Should().Be("branch");

        var legacyRule = JsonConvert.DeserializeObject<MeasurementRule>("{\"GroupsORlogic\":[{}]}")!;
        legacyRule.Description.Should().Be("");
        legacyRule.GroupsORlogic.Single().Description.Should().Be("");
    }
}
