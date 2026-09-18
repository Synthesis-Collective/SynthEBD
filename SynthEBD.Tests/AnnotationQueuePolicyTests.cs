using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Coverage for <see cref="AnnotationQueueOrdering"/>, the pure sampling policy behind the
/// Label-then-Suggest annotation queue. Everything here runs without a view model, a profile, or
/// the viewer -- the whole reason the ordering lives in its own static class.
/// <para>The properties under test are the ones the sampling policy exists to guarantee:
/// <c>Spread</c> covers the measurement's range early, <c>Uncertainty</c> serves boundary-adjacent
/// slices first, <c>Random</c> reproduces from a seed, and a non-zero <c>RandomFraction</c>
/// genuinely interleaves random draws rather than nominally offering to. That last one is the
/// honesty mechanism behind decision <c>D25</c> (boundary-only sampling scored 66/70 in-sample and
/// 8/14 on a random draw), so it is asserted on an exact count, not a tendency.</para>
/// </summary>
public class AnnotationQueuePolicyTests
{
    // ---------- helpers ----------

    /// <summary>Candidate scores 0, 1, 2, ... n-1 -- a uniform ramp, so a quantile bin is just a
    /// contiguous slice of the value range and "which decile did this land in" is arithmetic.</summary>
    private static List<double?> Ramp(int n) => Enumerable.Range(0, n).Select(i => (double?)i).ToList();

    private static MeasurementRule RuleWith(string category, string value, params MeasurementCondition[] conditions)
        => new()
        {
            Descriptor = new BodyShapeDescriptor.LabelSignature { Category = category, Value = value },
            GroupsORlogic = new List<AndGatedMeasurementGroup>
            {
                new() { ConditionsANDlogic = conditions.ToList() },
            },
        };

    private static MeasurementCondition Meas(string name, float threshold) => new()
    {
        Kind = MeasurementConditionKind.Measurement,
        MeasurementName = name,
        Comparator = MeasurementComparator.GreaterThanOrEqual,
        Value = threshold,
    };

    // ---------- Spread ----------

    [Fact]
    public void Spread_FirstPassVisitsEveryQuantileBin()
    {
        // 80 candidates over 8 bins: the first 8 served must be one per decile-ish bin, which is
        // what "start extreme, cover the whole range" means operationally.
        var order = AnnotationQueueOrdering.OrderSpread(Ramp(80), binCount: 8, seed: 1234);

        var firstPassBins = order.Take(8).Select(i => i / 10).ToList();
        firstPassBins.Should().OnlyHaveUniqueItems("the first pass round-robins the bins");
        firstPassBins.Should().BeEquivalentTo(Enumerable.Range(0, 8));
    }

    [Fact]
    public void Spread_CoversRangeFarSoonerThanSortedOrder()
    {
        var order = AnnotationQueueOrdering.OrderSpread(Ramp(200), binCount: 10, seed: 7);

        // After 10 served slices the spread of values seen should already be most of the corpus
        // range; a naive ascending walk would have covered 5% of it.
        var firstTen = order.Take(10).Select(i => (double)i).ToList();
        (firstTen.Max() - firstTen.Min()).Should().BeGreaterThan(150.0);
    }

    [Fact]
    public void Spread_IsAPermutationAndParksUnrankableSlicesLast()
    {
        // Slices the evaluator could not measure have no quantile, but they are still slices the
        // user may want to label -- they go last rather than being dropped.
        var values = new List<double?> { 5.0, null, 1.0, null, 9.0, 3.0 };
        var order = AnnotationQueueOrdering.OrderSpread(values, binCount: 3, seed: 42);

        order.Should().BeEquivalentTo(new[] { 0, 1, 2, 3, 4, 5 }, "nothing may be dropped");
        order.TakeLast(2).Should().BeEquivalentTo(new[] { 1, 3 }, "null-valued slices sort last");
    }

    [Fact]
    public void Spread_SameSeedReproducesWithinBinOrder()
    {
        var a = AnnotationQueueOrdering.OrderSpread(Ramp(50), binCount: 5, seed: 99);
        var b = AnnotationQueueOrdering.OrderSpread(Ramp(50), binCount: 5, seed: 99);
        a.Should().Equal(b);
    }

    // ---------- Uncertainty ----------

    [Fact]
    public void Uncertainty_ServesClosestToBoundaryFirst()
    {
        // distances: candidate 3 is nearest the cut, then 1, then 0, then 2.
        var distances = new List<double?> { 0.30, 0.10, 0.90, 0.01 };
        var order = AnnotationQueueOrdering.OrderUncertainty(distances);
        order.Should().Equal(3, 1, 0, 2);
    }

    [Fact]
    public void Uncertainty_UnrankableSlicesSortLast()
    {
        var distances = new List<double?> { null, 0.4, null, 0.2 };
        var order = AnnotationQueueOrdering.OrderUncertainty(distances);
        order.Take(2).Should().Equal(3, 1);
        // Ties among unrankable slices break by index, deterministically.
        order.Skip(2).Should().Equal(0, 2);
    }

    [Fact]
    public void BoundaryDistance_NormalizesByCorpusRangeSoUnitsDoNotDominate()
    {
        // Two rules in the Category. The slice sits 1.0 away from a threshold on a measurement
        // whose corpus range is 100 (normalized 0.01), and 0.5 away on one whose range is 1
        // (normalized 0.5). The wide-range measurement is the nearer boundary even though its raw
        // distance is twice as large -- that is the whole point of normalizing.
        var rules = new[]
        {
            RuleWith("Belly", "Chubby", Meas("BellyVolume", 890f)),
            RuleWith("Belly", "Pregnant", Meas("belly_proj_to_hip", 0.41f)),
        };
        var values = new Dictionary<string, float?>
        {
            ["BellyVolume"] = 889f,        // 1.0 away, range 100 -> 0.01
            ["belly_proj_to_hip"] = 0.91f, // 0.5 away, range 1   -> 0.50
        };
        var ranges = new Dictionary<string, double>
        {
            ["BellyVolume"] = 100.0,
            ["belly_proj_to_hip"] = 1.0,
        };

        var d = AnnotationQueueOrdering.MinNormalizedBoundaryDistance(rules, values, ranges);
        d.Should().BeApproximately(0.01, 1e-6);
    }

    [Fact]
    public void BoundaryDistance_IgnoresDisabledBranchesAndDescriptorRefs()
    {
        var muted = RuleWith("Belly", "Fat", Meas("BellyVolume", 900f));
        muted.GroupsORlogic[0].IsDisabled = true;

        var refOnly = new MeasurementRule
        {
            Descriptor = new BodyShapeDescriptor.LabelSignature { Category = "Belly", Value = "Thin" },
            GroupsORlogic = new List<AndGatedMeasurementGroup>
            {
                new()
                {
                    ConditionsANDlogic = new List<MeasurementCondition>
                    {
                        new()
                        {
                            Kind = MeasurementConditionKind.DescriptorRef,
                            RefCategory = "Belly",
                            RefValue = "Muscular",
                            Negate = true,
                        },
                    },
                },
            },
        };

        var values = new Dictionary<string, float?> { ["BellyVolume"] = 899f };
        var ranges = new Dictionary<string, double> { ["BellyVolume"] = 100.0 };

        AnnotationQueueOrdering.MinNormalizedBoundaryDistance(new[] { muted, refOnly }, values, ranges)
            .Should().BeNull("a muted branch is not a live boundary and a DescriptorRef has no threshold");
    }

    [Fact]
    public void BoundaryDistance_SkipsMeasurementsWithNoUsableRange()
    {
        var rules = new[] { RuleWith("Belly", "Chubby", Meas("constant_measure", 5f)) };
        var values = new Dictionary<string, float?> { ["constant_measure"] = 5.5f };

        // ComputeRanges omits a measurement that never varies, so the lookup misses and the
        // condition is skipped rather than dividing by zero.
        var ranges = AnnotationQueueOrdering.ComputeRanges(
            new IReadOnlyDictionary<string, float?>[]
            {
                new Dictionary<string, float?> { ["constant_measure"] = 5.5f },
                new Dictionary<string, float?> { ["constant_measure"] = 5.5f },
            },
            new[] { "constant_measure" });

        ranges.Should().NotContainKey("constant_measure");
        AnnotationQueueOrdering.MinNormalizedBoundaryDistance(rules, values, ranges).Should().BeNull();
    }

    // ---------- Random ----------

    [Fact]
    public void Random_SameSeedReproducesTheSameSequenceTwice()
    {
        var a = AnnotationQueueOrdering.Order(AnnotationQueuePolicy.Random, Ramp(60), 0.25, seed: 20260918);
        var b = AnnotationQueueOrdering.Order(AnnotationQueuePolicy.Random, Ramp(60), 0.25, seed: 20260918);

        a.Select(x => x.Index).Should().Equal(b.Select(x => x.Index));
    }

    [Fact]
    public void Random_DifferentSeedGivesADifferentSequence()
    {
        var a = AnnotationQueueOrdering.Order(AnnotationQueuePolicy.Random, Ramp(60), 0.25, seed: 1);
        var b = AnnotationQueueOrdering.Order(AnnotationQueuePolicy.Random, Ramp(60), 0.25, seed: 2);

        a.Select(x => x.Index).Should().NotEqual(b.Select(x => x.Index));
        a.Select(x => x.Index).Should().BeEquivalentTo(b.Select(x => x.Index), "both are permutations of the same corpus");
    }

    [Fact]
    public void Random_DoesNotFlagItsOwnDrawsAsInterleavedRandom()
    {
        // Under Random every draw is random, so per-entry provenance carries no information and
        // the counters must not double-count the whole session as "random draws".
        var order = AnnotationQueueOrdering.Order(AnnotationQueuePolicy.Random, Ramp(30), 0.25, seed: 5);
        order.Should().OnlyContain(x => !x.FromRandomDraw);
    }

    // ---------- RandomFraction interleave ----------

    [Fact]
    public void RandomFraction_ServesExactlyThatShareFromTheRandomStream()
    {
        var order = AnnotationQueueOrdering.Order(AnnotationQueuePolicy.Spread, Ramp(100), 0.25, seed: 11);

        order.Should().HaveCount(100);
        order.Count(x => x.FromRandomDraw).Should().Be(25,
            "the share is an exact count the user can quote, not an expectation");
    }

    [Fact]
    public void RandomFraction_Zero_ServesThePolicyOrderUntouched()
    {
        var spread = AnnotationQueueOrdering.OrderSpread(Ramp(40), AnnotationQueueOrdering.DefaultBinCount, seed: 3);
        var order = AnnotationQueueOrdering.Order(AnnotationQueuePolicy.Spread, Ramp(40), 0.0, seed: 3);

        order.Select(x => x.Index).Should().Equal(spread);
        order.Should().OnlyContain(x => !x.FromRandomDraw);
    }

    [Fact]
    public void RandomFraction_NonZeroActuallyDisturbsThePolicyOrder()
    {
        var pure = AnnotationQueueOrdering.Order(AnnotationQueuePolicy.Uncertainty, Ramp(60), 0.0, seed: 8);
        var mixed = AnnotationQueueOrdering.Order(AnnotationQueuePolicy.Uncertainty, Ramp(60), 0.25, seed: 8);

        mixed.Select(x => x.Index).Should().NotEqual(pure.Select(x => x.Index),
            "a quarter of the positions come from a different stream");
    }

    [Fact]
    public void RandomFraction_TinyFractionOnAShortQueueStillServesOneRandomDraw()
    {
        // 6 candidates at 1% rounds to zero random slots. Silently dropping to a pure boundary
        // sample there would remove the error estimate exactly where the sample is smallest.
        var order = AnnotationQueueOrdering.Order(AnnotationQueuePolicy.Spread, Ramp(6), 0.01, seed: 2);
        order.Count(x => x.FromRandomDraw).Should().Be(1);
    }

    [Fact]
    public void Interleave_OutputIsAlwaysAPermutationOfTheInput()
    {
        foreach (var fraction in new[] { 0.0, 0.1, 0.25, 0.5, 0.9, 1.0 })
        {
            var order = AnnotationQueueOrdering.Order(AnnotationQueuePolicy.Spread, Ramp(37), fraction, seed: 17);
            order.Select(x => x.Index).Should().BeEquivalentTo(Enumerable.Range(0, 37),
                "fraction {0} must neither drop nor duplicate a slice", fraction);
        }
    }

    [Fact]
    public void Order_EmptyCandidateSetYieldsAnEmptyQueue()
    {
        AnnotationQueueOrdering.Order(AnnotationQueuePolicy.Spread, new List<double?>(), 0.25, seed: 1)
            .Should().BeEmpty();
    }

    // ---------- alias signature ----------

    [Fact]
    public void Signature_CollapsesIdenticalShapesUnderDifferentPresetNames()
    {
        // The corpus case this exists for: two uploads of the same body under different labels.
        var names = new List<string> { "waist_width", "belly_projection", "BellyVolume" };
        var ssbbw = new Dictionary<string, float?> { ["waist_width"] = 19.5f, ["belly_projection"] = 14.7f, ["BellyVolume"] = 890f };
        var alias = new Dictionary<string, float?> { ["waist_width"] = 19.5f, ["belly_projection"] = 14.7f, ["BellyVolume"] = 890f };

        AnnotationQueueOrdering.MeasurementSignature(ssbbw, names)
            .Should().Be(AnnotationQueueOrdering.MeasurementSignature(alias, names));
    }

    [Fact]
    public void Signature_SeparatesShapesThatDifferBeyondTheRoundingTolerance()
    {
        var names = new List<string> { "belly_projection" };
        var a = new Dictionary<string, float?> { ["belly_projection"] = 14.700f };
        var b = new Dictionary<string, float?> { ["belly_projection"] = 14.750f };

        AnnotationQueueOrdering.MeasurementSignature(a, names)
            .Should().NotBe(AnnotationQueueOrdering.MeasurementSignature(b, names));
    }

    [Fact]
    public void Signature_AbsorbsFloatingPointNoiseBelowTheTolerance()
    {
        var names = new List<string> { "belly_projection" };
        var a = new Dictionary<string, float?> { ["belly_projection"] = 14.70001f };
        var b = new Dictionary<string, float?> { ["belly_projection"] = 14.70002f };

        AnnotationQueueOrdering.MeasurementSignature(a, names)
            .Should().Be(AnnotationQueueOrdering.MeasurementSignature(b, names));
    }

    [Fact]
    public void Signature_DoesNotConflateAMissingValueWithZero()
    {
        // A measurement the evaluator could not compute is not the same body as one that
        // measured zero; collapsing them would alias unrelated shapes together.
        var names = new List<string> { "Spine_to_BellyFlab" };
        var missing = new Dictionary<string, float?> { ["Spine_to_BellyFlab"] = null };
        var zero = new Dictionary<string, float?> { ["Spine_to_BellyFlab"] = 0f };

        AnnotationQueueOrdering.MeasurementSignature(missing, names)
            .Should().NotBe(AnnotationQueueOrdering.MeasurementSignature(zero, names));
    }

    [Fact]
    public void Signature_IsOrderStableAcrossTheMeasurementList()
    {
        // Same values, same name list -> same key on every call; the queue relies on this to group
        // aliases across a rebuild.
        var names = new List<string> { "a", "b" };
        var values = new Dictionary<string, float?> { ["b"] = 2f, ["a"] = 1f };

        AnnotationQueueOrdering.MeasurementSignature(values, names).Should().Be("a=1|b=2");
    }
}
