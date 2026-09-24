using System;
using System.Collections.Generic;
using System.Linq;

namespace SynthEBD;

/// <summary>Which representative a Show Spread column depicts.</summary>
public enum SpreadStatistic
{
    Min,
    Mean,
    Median,
    Peak,
    Max,
}

/// <summary>One (preset, gender, weight) cache slice and its value on the metric being spread
/// (a measurement's cached value, or a descriptor value's margin score).</summary>
public readonly record struct SpreadSample(string PresetLabel, Gender Gender, int Weight, double Value);

/// <summary>The slice chosen to represent <see cref="Statistic"/>, plus the statistic's own value.
/// <see cref="StatisticValue"/> differs from <c>Sample.Value</c> whenever the statistic falls
/// between slices: the mean, an even-count median, and the Peak bin's center.</summary>
public sealed record SpreadPick(SpreadStatistic Statistic, SpreadSample Sample, double StatisticValue);

/// <summary>
/// Picks the representative slices for the Body Type Profile editor's Show Spread window.
/// Pure and deterministic so the choice of preset can be unit-tested without the UI.
/// <para>Samples are ordered by (Value, PresetLabel ordinal, Weight, Gender) before picking, so
/// every tie resolves toward the lower-ordered slice and repeat opens show the same presets.</para>
/// </summary>
public static class SpreadStatistics
{
    /// <summary>Bin count for <see cref="SpreadStatistic.Peak"/> when the caller doesn't supply one.
    /// Matches the annotation queue's default spread bin count.</summary>
    public const int DefaultPeakBinCount = 8;

    /// <summary>Returns one <see cref="SpreadPick"/> per <see cref="SpreadStatistic"/>, in enum order,
    /// or an empty list when <paramref name="samples"/> has no finite values.
    /// <list type="bullet">
    ///   <item>Min / Max: the lowest / highest slice.</item>
    ///   <item>Mean: the slice closest to the arithmetic mean.</item>
    ///   <item>Median: the middle slice; with an even count, the lower of the two middle slices
    ///   (the statistic value is still the true median, the average of the two).</item>
    ///   <item>Peak: the slice closest to the center of the fullest of
    ///   <paramref name="peakBinCount"/> equal-width bins over [min, max]. Binned exactly like
    ///   <see cref="VM_MeasurementHistogram"/>, so it names the same bar the histogram shows as tallest.
    ///   The pick is restricted to that bin's members, so it always lies inside the peak.</item>
    /// </list></summary>
    public static IReadOnlyList<SpreadPick> Pick(IEnumerable<SpreadSample> samples, int peakBinCount = DefaultPeakBinCount)
    {
        var sorted = (samples ?? Enumerable.Empty<SpreadSample>())
            .Where(s => double.IsFinite(s.Value))
            .OrderBy(s => s.Value)
            .ThenBy(s => s.PresetLabel ?? "", StringComparer.Ordinal)
            .ThenBy(s => s.Weight)
            .ThenBy(s => s.Gender)
            .ToList();
        if (sorted.Count == 0) return Array.Empty<SpreadPick>();

        int n = sorted.Count;
        var min = sorted[0];
        var max = sorted[n - 1];

        double mean = sorted.Average(s => s.Value);
        var meanPick = sorted[NearestIndex(sorted, 0, n, mean)];

        int medianIndex = n % 2 == 1 ? n / 2 : n / 2 - 1;
        double median = n % 2 == 1 ? sorted[n / 2].Value : (sorted[n / 2 - 1].Value + sorted[n / 2].Value) / 2.0;

        var (peakPick, peakCenter) = PickPeak(sorted, Math.Max(1, peakBinCount));

        return new[]
        {
            new SpreadPick(SpreadStatistic.Min, min, min.Value),
            new SpreadPick(SpreadStatistic.Mean, meanPick, mean),
            new SpreadPick(SpreadStatistic.Median, sorted[medianIndex], median),
            new SpreadPick(SpreadStatistic.Peak, peakPick, peakCenter),
            new SpreadPick(SpreadStatistic.Max, max, max.Value),
        };
    }

    /// <summary>Natural (numeric) row order: indices of <paramref name="rowValues"/> sorted by each
    /// row's median, ascending — so a Category split by thresholds on one measurement reads
    /// Skinny → Normal → Thick however its values are spelled. Rows with no finite values keep their
    /// original relative order after every valued row; equal medians keep original order too.</summary>
    public static IReadOnlyList<int> OrderByMedian(IReadOnlyList<IEnumerable<double>> rowValues)
    {
        var keyed = new List<(int Index, double? Median)>();
        for (int i = 0; i < (rowValues?.Count ?? 0); i++)
        {
            var sorted = (rowValues![i] ?? Enumerable.Empty<double>()).Where(double.IsFinite).OrderBy(v => v).ToList();
            double? median = sorted.Count == 0 ? null
                : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2]
                : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;
            keyed.Add((i, median));
        }
        return keyed
            .OrderBy(k => k.Median.HasValue ? 0 : 1)
            .ThenBy(k => k.Median ?? 0.0)
            .ThenBy(k => k.Index)
            .Select(k => k.Index)
            .ToList();
    }

    /// <summary>Fewest decimal places, within [<paramref name="minDecimals"/>, <paramref name="maxDecimals"/>],
    /// at which every distinct finite value in <paramref name="values"/> still rounds to a distinct
    /// number — so labels never show two different values (e.g. 31.499737 and 31.500027, either side
    /// of a 31.5 threshold) as the same "31.5". Returns <paramref name="maxDecimals"/> when even that
    /// can't separate them (float noise). Exactly-equal values don't count as needing separation.</summary>
    public static int DecimalsToDistinguish(IEnumerable<double> values, int minDecimals = 3, int maxDecimals = 7)
    {
        var distinct = (values ?? Enumerable.Empty<double>()).Where(double.IsFinite).Distinct().ToList();
        for (int d = minDecimals; d < maxDecimals; d++)
        {
            if (distinct.Select(v => Math.Round(v, d, MidpointRounding.AwayFromZero)).Distinct().Count() == distinct.Count)
                return d;
        }
        return maxDecimals;
    }

    /// <summary>Fullest-bin representative. Bin edges and index math mirror
    /// <see cref="VM_MeasurementHistogram"/>'s Rebuild: equal-width bins over [min, max], values
    /// equal to max clamp into the last bin, and a zero-width range collapses to one bin. Ties
    /// between equally full bins go to the lowest bin.</summary>
    private static (SpreadSample Pick, double Center) PickPeak(List<SpreadSample> sorted, int binCount)
    {
        double lo = sorted[0].Value;
        double range = sorted[^1].Value - lo;
        if (range < 1e-9) return (sorted[0], lo);

        double step = range / binCount;
        var counts = new int[binCount];
        var firstIndex = new int[binCount];
        for (int i = 0; i < sorted.Count; i++)
        {
            int idx = BinIndex(sorted[i].Value, lo, step, binCount);
            if (counts[idx] == 0) firstIndex[idx] = i;
            counts[idx]++;
        }

        int best = 0;
        for (int b = 1; b < binCount; b++)
        {
            if (counts[b] > counts[best]) best = b;
        }

        double center = lo + step * (best + 0.5);
        // Samples are sorted, so a bin's members are contiguous.
        int start = firstIndex[best];
        return (sorted[NearestIndex(sorted, start, start + counts[best], center)], center);
    }

    private static int BinIndex(double v, double lo, double step, int binCount)
    {
        int idx = (int)Math.Floor((v - lo) / step);
        if (idx < 0) return 0;
        return idx >= binCount ? binCount - 1 : idx;
    }

    /// <summary>Index in [start, end) of the sample closest to <paramref name="target"/>;
    /// the first (lowest-ordered) wins a tie.</summary>
    private static int NearestIndex(List<SpreadSample> sorted, int start, int end, double target)
    {
        int best = start;
        double bestDist = Math.Abs(sorted[start].Value - target);
        for (int i = start + 1; i < end; i++)
        {
            double d = Math.Abs(sorted[i].Value - target);
            if (d < bestDist)
            {
                best = i;
                bestDist = d;
            }
        }
        return best;
    }
}
