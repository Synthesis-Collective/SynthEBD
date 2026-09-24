using System.Linq;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Coverage for <see cref="SpreadStatistics.Pick"/>, which chooses the representative
/// (preset, weight) slices the Body Type Profile editor's Show Spread window renders.
/// </summary>
public class SpreadStatisticsTests
{
    private static SpreadSample S(string label, double value, int weight = 0) =>
        new(label, Gender.Female, weight, value);

    private static SpreadPick Get(System.Collections.Generic.IReadOnlyList<SpreadPick> picks, SpreadStatistic stat) =>
        picks.Single(p => p.Statistic == stat);

    [Fact]
    public void Empty_ReturnsNoPicks()
    {
        SpreadStatistics.Pick(Enumerable.Empty<SpreadSample>()).Should().BeEmpty();
    }

    [Fact]
    public void NonFiniteValues_AreIgnored()
    {
        var picks = SpreadStatistics.Pick(new[] { S("A", double.NaN), S("B", double.PositiveInfinity), S("C", 2) });
        picks.Should().OnlyContain(p => p.Sample.PresetLabel == "C");
    }

    [Fact]
    public void ReturnsOnePickPerStatistic_InEnumOrder()
    {
        var picks = SpreadStatistics.Pick(new[] { S("A", 1), S("B", 2), S("C", 3) });
        picks.Select(p => p.Statistic).Should().Equal(
            SpreadStatistic.Min, SpreadStatistic.Mean, SpreadStatistic.Median, SpreadStatistic.Peak, SpreadStatistic.Max);
    }

    [Fact]
    public void MinAndMax_CarryTheirOwnWeights()
    {
        // The min is a preset at weight 0 and the max a preset at weight 100: each must be
        // drawn at its own weight, not a shared one.
        var picks = SpreadStatistics.Pick(new[] { S("Low", 1, weight: 0), S("Mid", 5, weight: 50), S("High", 9, weight: 100) });
        Get(picks, SpreadStatistic.Min).Sample.Should().Be(S("Low", 1, weight: 0));
        Get(picks, SpreadStatistic.Max).Sample.Should().Be(S("High", 9, weight: 100));
    }

    [Fact]
    public void Mean_PicksTheSliceClosestToTheMean()
    {
        // Mean = (0 + 1 + 2 + 9) / 4 = 3; closest slice is 2.
        var picks = SpreadStatistics.Pick(new[] { S("A", 0), S("B", 1), S("C", 2), S("D", 9) });
        var mean = Get(picks, SpreadStatistic.Mean);
        mean.StatisticValue.Should().Be(3);
        mean.Sample.PresetLabel.Should().Be("C");
    }

    [Fact]
    public void Median_OddCount_IsTheMiddleSlice()
    {
        var picks = SpreadStatistics.Pick(new[] { S("C", 3), S("A", 1), S("B", 2) });
        var median = Get(picks, SpreadStatistic.Median);
        median.Sample.PresetLabel.Should().Be("B");
        median.StatisticValue.Should().Be(2);
    }

    [Fact]
    public void Median_EvenCount_PicksLowerMiddle_ButReportsTrueMedian()
    {
        var picks = SpreadStatistics.Pick(new[] { S("A", 1), S("B", 2), S("C", 4), S("D", 8) });
        var median = Get(picks, SpreadStatistic.Median);
        median.Sample.PresetLabel.Should().Be("B");
        median.StatisticValue.Should().Be(3);
    }

    [Fact]
    public void Ties_ResolveByPresetLabelThenWeight()
    {
        var picks = SpreadStatistics.Pick(new[] { S("Zed", 1, weight: 0), S("Abe", 1, weight: 100), S("Abe", 1, weight: 0) });
        Get(picks, SpreadStatistic.Min).Sample.Should().Be(S("Abe", 1, weight: 0));
        Get(picks, SpreadStatistic.Max).Sample.Should().Be(S("Zed", 1, weight: 0));
    }

    [Fact]
    public void SingleSample_FillsEveryStatistic()
    {
        var picks = SpreadStatistics.Pick(new[] { S("Only", 4.5, weight: 50) });
        picks.Should().HaveCount(5);
        picks.Should().OnlyContain(p => p.Sample == S("Only", 4.5, 50) && p.StatisticValue == 4.5);
    }

    [Fact]
    public void Peak_PicksClosestSliceToTheFullestBinCenter()
    {
        // Range [0, 10] in 5 bins of width 2. Bin [6, 8) holds three slices, the most of any
        // bin; its center is 7, and 6.9 is the closest member.
        var samples = new[] { S("A", 0), S("B", 1), S("C", 6.1), S("D", 6.9), S("E", 7.9), S("F", 10) };
        var peak = Get(SpreadStatistics.Pick(samples, peakBinCount: 5), SpreadStatistic.Peak);
        peak.StatisticValue.Should().BeApproximately(7, 1e-9);
        peak.Sample.PresetLabel.Should().Be("D");
    }

    [Fact]
    public void Peak_EqualBins_GoToTheLowestBin()
    {
        var samples = new[] { S("A", 0), S("B", 10) };
        var peak = Get(SpreadStatistics.Pick(samples, peakBinCount: 2), SpreadStatistic.Peak);
        peak.Sample.PresetLabel.Should().Be("A");
        peak.StatisticValue.Should().Be(2.5);
    }

    [Fact]
    public void OrderByMedian_SortsRowsNumerically()
    {
        // Thighs in alphabetical order: Normal, Skinny, Thick. Numerically: Skinny, Normal, Thick.
        var rows = new[]
        {
            new double[] { 5, 6, 7 },   // Normal
            new double[] { 1, 2, 3 },   // Skinny
            new double[] { 9, 10 },     // Thick
        };
        SpreadStatistics.OrderByMedian(rows).Should().Equal(1, 0, 2);
    }

    [Fact]
    public void OrderByMedian_EmptyRowsGoLast_InOriginalOrder()
    {
        var rows = new[]
        {
            new double[0],
            new double[] { 4 },
            new double[] { double.NaN },
            new double[] { 2 },
        };
        SpreadStatistics.OrderByMedian(rows).Should().Equal(3, 1, 0, 2);
    }

    [Fact]
    public void OrderByMedian_TiesKeepOriginalOrder()
    {
        var rows = new[] { new double[] { 3 }, new double[] { 1, 5 }, new double[] { 3 } };
        SpreadStatistics.OrderByMedian(rows).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void DecimalsToDistinguish_SeparatesValuesStraddlingAThreshold()
    {
        // Real case: Shoulder Width Medium max vs Wide min around a 31.5 threshold. At 3 decimals
        // both read 31.500; at 4 the Wide one (31.5000) still reads as the threshold itself; 5
        // separates all three (31.49974 < 31.5 < 31.50003).
        SpreadStatistics.DecimalsToDistinguish(new[] { 31.499737, 31.500027, 31.5 }).Should().Be(5);
        // Without the threshold in the set, 4 decimals already separates the two presets.
        SpreadStatistics.DecimalsToDistinguish(new[] { 31.499737, 31.500027 }).Should().Be(4);
    }

    [Fact]
    public void DecimalsToDistinguish_UsesTheMinimumWhenItSuffices()
    {
        SpreadStatistics.DecimalsToDistinguish(new[] { 1.25, 2.5, 2.5, 30.125 }).Should().Be(3);
    }

    [Fact]
    public void DecimalsToDistinguish_CapsAtTheMaximum()
    {
        SpreadStatistics.DecimalsToDistinguish(new[] { 1.0, 1.0 + 1e-12 }, maxDecimals: 7).Should().Be(7);
    }

    [Fact]
    public void Peak_ZeroWidthRange_UsesTheSharedValue()
    {
        var picks = SpreadStatistics.Pick(new[] { S("A", 3), S("B", 3) });
        var peak = Get(picks, SpreadStatistic.Peak);
        peak.StatisticValue.Should().Be(3);
        peak.Sample.PresetLabel.Should().Be("A");
    }
}
