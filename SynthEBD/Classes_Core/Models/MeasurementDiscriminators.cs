using System;
using System.Collections.Generic;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// Phase 2 of the Label-then-Suggest overhaul.
///
/// Pure-logic scoring functions used by the Suggest Measurements panel to rank how well each
/// <see cref="MeasurementDefinition"/> discriminates between the descriptor-value groups the
/// user has annotated. Operates on grouped scalar samples; knows nothing about the rest of the
/// pipeline (annotations, viewers, profiles), so the algorithms are independently testable
/// and can be unit-checked against hand-computed values.
///
/// Each algorithm takes <c>groups</c>: an outer list of groups (one per descriptor Value), each
/// inner list the measurement values from annotations carrying that Value. Returns a non-negative
/// score where higher = better separation. Returns <c>0</c> when the input is degenerate
/// (fewer than two non-empty groups, or no within-group variation).
/// </summary>
public static class MeasurementDiscriminators
{
    /// <summary>Dispatches to the specific algorithm. Phase 5 calls this with the user's chosen
    /// <see cref="MeasurementSelectionAlgorithm"/>.</summary>
    public static double Score(MeasurementSelectionAlgorithm algo, IReadOnlyList<IReadOnlyList<double>> groups)
    {
        return algo switch
        {
            MeasurementSelectionAlgorithm.Anova => ScoreAnova(groups),
            MeasurementSelectionAlgorithm.CohenD => ScoreCohenD(groups),
            MeasurementSelectionAlgorithm.InformationGain => ScoreInformationGain(groups),
            _ => 0.0,
        };
    }

    /// <summary>One-way ANOVA F-statistic. F = (SSB / dfB) / (SSW / dfW).
    /// <para>When SSW is zero (all values within every group are identical) but SSB &gt; 0, returns
    /// <see cref="double.PositiveInfinity"/> -- a perfectly separating measurement deserves a top
    /// rank. Returns 0 when there is only one non-empty group, when total samples &lt;= group count,
    /// or when both SSB and SSW are zero (no signal).</para></summary>
    public static double ScoreAnova(IReadOnlyList<IReadOnlyList<double>> groups)
    {
        var nonEmpty = (groups ?? Array.Empty<IReadOnlyList<double>>())
            .Where(g => g != null && g.Count > 0)
            .ToList();
        if (nonEmpty.Count < 2) return 0.0;

        int k = nonEmpty.Count;
        int N = nonEmpty.Sum(g => g.Count);
        if (N <= k) return 0.0;

        double grandMean = nonEmpty.SelectMany(g => g).Sum() / N;

        double ssb = 0.0;
        double ssw = 0.0;
        foreach (var g in nonEmpty)
        {
            double mean = g.Average();
            ssb += g.Count * (mean - grandMean) * (mean - grandMean);
            foreach (var v in g)
            {
                double d = v - mean;
                ssw += d * d;
            }
        }

        double dfB = k - 1;
        double dfW = N - k;
        double msb = ssb / dfB;
        double msw = ssw / dfW;

        if (msw <= 0.0)
        {
            return msb > 0.0 ? double.PositiveInfinity : 0.0;
        }
        return msb / msw;
    }

    /// <summary>Cohen's d (|mean_a - mean_b| / pooled std). For Categories with more than two
    /// groups, returns the maximum d across all unordered pairs -- the most-separable pair drives
    /// the ranking, which matches user intuition ("at least one pair of values is well-separated
    /// by this measurement").
    /// <para>Returns <see cref="double.PositiveInfinity"/> when a pair has equal-sized
    /// non-overlapping point-masses (pooled std = 0 but means differ). Returns 0 when no pair
    /// has both groups with &gt;= 2 samples (cannot estimate variance).</para></summary>
    public static double ScoreCohenD(IReadOnlyList<IReadOnlyList<double>> groups)
    {
        var nonEmpty = (groups ?? Array.Empty<IReadOnlyList<double>>())
            .Where(g => g != null && g.Count > 0)
            .ToList();
        if (nonEmpty.Count < 2) return 0.0;

        double best = 0.0;
        for (int i = 0; i < nonEmpty.Count; i++)
        {
            for (int j = i + 1; j < nonEmpty.Count; j++)
            {
                double d = PairwiseCohenD(nonEmpty[i], nonEmpty[j]);
                if (d > best) best = d;
            }
        }
        return best;
    }

    private static double PairwiseCohenD(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        if (a.Count < 2 || b.Count < 2) return 0.0;

        double meanA = a.Average();
        double meanB = b.Average();

        double varA = 0.0, varB = 0.0;
        foreach (var v in a) { var d = v - meanA; varA += d * d; }
        foreach (var v in b) { var d = v - meanB; varB += d * d; }
        // Sample variances (n-1 denominator).
        varA /= (a.Count - 1);
        varB /= (b.Count - 1);

        double pooled = Math.Sqrt(((a.Count - 1) * varA + (b.Count - 1) * varB) / (a.Count + b.Count - 2));
        double diff = Math.Abs(meanA - meanB);
        if (pooled <= 0.0)
        {
            return diff > 0.0 ? double.PositiveInfinity : 0.0;
        }
        return diff / pooled;
    }

    /// <summary>Information gain from the best single-threshold binary split on this measurement.
    /// Score = H(parent) - H_weighted(children), where H is Shannon entropy over the
    /// group-membership labels (one label per descriptor Value).
    /// <para>Sweeps every midpoint between consecutive distinct sorted values as a candidate
    /// threshold and reports the maximum gain. Returns 0 when there is no separating threshold
    /// (all samples identical, or fewer than two non-empty groups).</para></summary>
    public static double ScoreInformationGain(IReadOnlyList<IReadOnlyList<double>> groups)
    {
        var nonEmpty = (groups ?? Array.Empty<IReadOnlyList<double>>())
            .Where(g => g != null && g.Count > 0)
            .ToList();
        if (nonEmpty.Count < 2) return 0.0;

        // Flatten into (value, groupIndex) pairs, sorted by value.
        var labelled = new List<(double v, int gid)>();
        for (int i = 0; i < nonEmpty.Count; i++)
        {
            foreach (var v in nonEmpty[i]) labelled.Add((v, i));
        }
        if (labelled.Count < 2) return 0.0;
        labelled.Sort((x, y) => x.v.CompareTo(y.v));

        // H(parent) over class proportions.
        int N = labelled.Count;
        var totalCounts = new int[nonEmpty.Count];
        foreach (var (_, gid) in labelled) totalCounts[gid]++;
        double parentEntropy = Entropy(totalCounts, N);

        // Sweep candidate thresholds: midpoint between consecutive distinct values.
        var leftCounts = new int[nonEmpty.Count];
        double bestGain = 0.0;
        for (int i = 0; i < N - 1; i++)
        {
            leftCounts[labelled[i].gid]++;
            // Only evaluate a split when the next value is strictly greater (no point splitting
            // at a tie -- both sides would inherit the tied samples ambiguously).
            if (labelled[i].v == labelled[i + 1].v) continue;

            int leftN = i + 1;
            int rightN = N - leftN;

            double leftEntropy = Entropy(leftCounts, leftN);
            // Right counts = total - left, computed inline to avoid allocating per candidate.
            double rightEntropy = EntropyOfDifference(totalCounts, leftCounts, rightN);

            double weighted = (leftN / (double)N) * leftEntropy + (rightN / (double)N) * rightEntropy;
            double gain = parentEntropy - weighted;
            if (gain > bestGain) bestGain = gain;
        }
        return bestGain;
    }

    private static double Entropy(IReadOnlyList<int> counts, int total)
    {
        if (total <= 0) return 0.0;
        double h = 0.0;
        for (int i = 0; i < counts.Count; i++)
        {
            if (counts[i] <= 0) continue;
            double p = counts[i] / (double)total;
            h -= p * Math.Log(p, 2.0);
        }
        return h;
    }

    private static double EntropyOfDifference(IReadOnlyList<int> total, IReadOnlyList<int> left, int rightN)
    {
        if (rightN <= 0) return 0.0;
        double h = 0.0;
        for (int i = 0; i < total.Count; i++)
        {
            int c = total[i] - left[i];
            if (c <= 0) continue;
            double p = c / (double)rightN;
            h -= p * Math.Log(p, 2.0);
        }
        return h;
    }
}
