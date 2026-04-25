using System;
using System.Collections.Generic;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// One concrete threshold suggestion produced by a <see cref="RuleSynthesisAlgorithm"/>:
/// "the rule should fire when <c>MeasurementName Comparator Threshold</c> holds." Phase 6 wraps
/// each suggestion in a single-condition <see cref="AndGatedMeasurementGroup"/> and OR-combines
/// them into one <see cref="MeasurementRule"/> per (Category, Value) target.
/// </summary>
public readonly struct ThresholdSuggestion
{
    /// <summary>Name of the <see cref="MeasurementDefinition"/> the threshold applies to.</summary>
    public string MeasurementName { get; }

    /// <summary>Direction of the threshold check.</summary>
    public MeasurementComparator Comparator { get; }

    /// <summary>Threshold value (in the measurement's native units).</summary>
    public float Threshold { get; }

    /// <summary>Algorithm-specific quality score: higher = stronger signal. Informational; the
    /// caller can use it to sort, filter, or hide weak suggestions.
    /// <list type="bullet">
    ///   <item>OptimalThresholdPerValue: max Youden's J on the candidate sweep, in [0, 1].</item>
    ///   <item>MedianSplit: |median_pos - median_neg| (raw measurement-unit gap).</item>
    ///   <item>DecisionStump: parent_gini - best_weighted_child_gini, in [0, 0.5].</item>
    /// </list></summary>
    public double Score { get; }

    public ThresholdSuggestion(string measurementName, MeasurementComparator comparator, float threshold, double score)
    {
        MeasurementName = measurementName;
        Comparator = comparator;
        Threshold = threshold;
        Score = score;
    }
}

/// <summary>
/// Phase 2 of the Label-then-Suggest overhaul.
///
/// Pure-logic threshold-finding routines used by the Suggest Rules panel. Each algorithm takes
/// per-measurement positive / negative sample arrays for one (Category, Value) target and emits
/// zero or more <see cref="ThresholdSuggestion"/>s. Phase 6 wraps the output in
/// <see cref="MeasurementRule"/>s with <c>IsDraft = true</c> for the user to review and accept.
/// </summary>
public static class RuleSynthesizers
{
    /// <summary>
    /// Per-measurement input: the measurement's value at every positive sample (annotations
    /// carrying the target descriptor) and at every negative sample (annotations carrying the
    /// same Category but a different Value). Driver iterates measurements; samples come from
    /// the live measurement table populated by the annotation scan.
    /// </summary>
    public readonly struct PosNegSamples
    {
        public IReadOnlyList<double> Positives { get; }
        public IReadOnlyList<double> Negatives { get; }

        public PosNegSamples(IReadOnlyList<double> positives, IReadOnlyList<double> negatives)
        {
            Positives = positives ?? Array.Empty<double>();
            Negatives = negatives ?? Array.Empty<double>();
        }
    }

    /// <summary>Dispatches to the specific algorithm. Phase 6 calls this with the user's chosen
    /// <see cref="RuleSynthesisAlgorithm"/>.</summary>
    public static List<ThresholdSuggestion> Synthesize(
        RuleSynthesisAlgorithm algo,
        IReadOnlyDictionary<string, PosNegSamples> samplesPerMeasurement)
    {
        return algo switch
        {
            RuleSynthesisAlgorithm.OptimalThresholdPerValue => OptimalThresholdPerValue(samplesPerMeasurement),
            RuleSynthesisAlgorithm.MedianSplit => MedianSplit(samplesPerMeasurement),
            RuleSynthesisAlgorithm.DecisionStump => DecisionStump(samplesPerMeasurement),
            _ => new List<ThresholdSuggestion>(),
        };
    }

    /// <summary>One suggestion per measurement: best (threshold, direction) by Youden's J
    /// (TPR - FPR). Sweeps midpoints between consecutive distinct sorted values; tries both
    /// "value &gt;= t" and "value &lt;= t" interpretations and reports the better one.
    /// <para>Skips measurements with J &lt;= 0 in both directions -- those carry no signal for
    /// the target and would only noise up the rule.</para></summary>
    public static List<ThresholdSuggestion> OptimalThresholdPerValue(IReadOnlyDictionary<string, PosNegSamples> samplesPerMeasurement)
    {
        var result = new List<ThresholdSuggestion>();
        if (samplesPerMeasurement == null) return result;

        foreach (var kv in samplesPerMeasurement)
        {
            var s = kv.Value;
            if (s.Positives.Count == 0 || s.Negatives.Count == 0) continue;

            var labelled = new List<(double v, bool isPos)>(s.Positives.Count + s.Negatives.Count);
            foreach (var v in s.Positives) labelled.Add((v, true));
            foreach (var v in s.Negatives) labelled.Add((v, false));
            labelled.Sort((x, y) => x.v.CompareTo(y.v));

            int totalPos = s.Positives.Count;
            int totalNeg = s.Negatives.Count;

            // Sweep thresholds at midpoints between consecutive distinct values. For each
            // candidate evaluate both directions and keep the best.
            int leftPos = 0, leftNeg = 0;
            double bestJ = 0.0;
            float bestThreshold = 0f;
            MeasurementComparator bestCmp = MeasurementComparator.GreaterThanOrEqual;

            for (int i = 0; i < labelled.Count - 1; i++)
            {
                if (labelled[i].isPos) leftPos++; else leftNeg++;
                if (labelled[i].v == labelled[i + 1].v) continue;

                double threshold = 0.5 * (labelled[i].v + labelled[i + 1].v);
                int rightPos = totalPos - leftPos;
                int rightNeg = totalNeg - leftNeg;

                // Direction ">= threshold" => predict positive on the RIGHT side.
                double tprGe = rightPos / (double)totalPos;
                double fprGe = rightNeg / (double)totalNeg;
                double jGe = tprGe - fprGe;

                // Direction "<= threshold" => predict positive on the LEFT side.
                double tprLe = leftPos / (double)totalPos;
                double fprLe = leftNeg / (double)totalNeg;
                double jLe = tprLe - fprLe;

                if (jGe > bestJ)
                {
                    bestJ = jGe;
                    bestThreshold = (float)threshold;
                    bestCmp = MeasurementComparator.GreaterThanOrEqual;
                }
                if (jLe > bestJ)
                {
                    bestJ = jLe;
                    bestThreshold = (float)threshold;
                    bestCmp = MeasurementComparator.LessThanOrEqual;
                }
            }

            if (bestJ > 0.0)
            {
                result.Add(new ThresholdSuggestion(kv.Key, bestCmp, bestThreshold, bestJ));
            }
        }

        // Strongest-signal measurements first -- the user reviews top-down.
        result.Sort((a, b) => b.Score.CompareTo(a.Score));
        return result;
    }

    /// <summary>One suggestion per measurement: threshold halfway between the positive median
    /// and the negative median, comparator chosen so the positives sit on the matching side.
    /// <para>Faithful to the legacy "Suggest Thresholds" heuristic but generalised to per-Value
    /// targets. Skips measurements where the two medians coincide -- a midpoint there picks an
    /// arbitrary side and produces a coin-flip rule.</para></summary>
    public static List<ThresholdSuggestion> MedianSplit(IReadOnlyDictionary<string, PosNegSamples> samplesPerMeasurement)
    {
        var result = new List<ThresholdSuggestion>();
        if (samplesPerMeasurement == null) return result;

        foreach (var kv in samplesPerMeasurement)
        {
            var s = kv.Value;
            if (s.Positives.Count == 0 || s.Negatives.Count == 0) continue;

            double mp = Median(s.Positives);
            double mn = Median(s.Negatives);
            if (mp == mn) continue;

            float threshold = (float)(0.5 * (mp + mn));
            MeasurementComparator cmp = mp > mn
                ? MeasurementComparator.GreaterThanOrEqual
                : MeasurementComparator.LessThanOrEqual;

            result.Add(new ThresholdSuggestion(kv.Key, cmp, threshold, Math.Abs(mp - mn)));
        }

        result.Sort((a, b) => b.Score.CompareTo(a.Score));
        return result;
    }

    /// <summary>At most one suggestion total: the single (measurement, threshold, direction)
    /// that minimises weighted Gini impurity over the positive / negative labels. Picks the
    /// best classifier across the entire measurement set rather than emitting one rule per
    /// measurement.
    /// <para>Useful when the user wants the leanest possible rule and is comfortable that one
    /// measurement carries the bulk of the discriminative signal.</para></summary>
    public static List<ThresholdSuggestion> DecisionStump(IReadOnlyDictionary<string, PosNegSamples> samplesPerMeasurement)
    {
        var result = new List<ThresholdSuggestion>();
        if (samplesPerMeasurement == null) return result;

        ThresholdSuggestion? best = null;
        double bestGain = 0.0;

        foreach (var kv in samplesPerMeasurement)
        {
            var s = kv.Value;
            if (s.Positives.Count == 0 || s.Negatives.Count == 0) continue;

            int totalPos = s.Positives.Count;
            int totalNeg = s.Negatives.Count;
            int N = totalPos + totalNeg;
            double parentGini = GiniBinary(totalPos, totalNeg);

            var labelled = new List<(double v, bool isPos)>(N);
            foreach (var v in s.Positives) labelled.Add((v, true));
            foreach (var v in s.Negatives) labelled.Add((v, false));
            labelled.Sort((x, y) => x.v.CompareTo(y.v));

            int leftPos = 0, leftNeg = 0;
            for (int i = 0; i < labelled.Count - 1; i++)
            {
                if (labelled[i].isPos) leftPos++; else leftNeg++;
                if (labelled[i].v == labelled[i + 1].v) continue;

                int rightPos = totalPos - leftPos;
                int rightNeg = totalNeg - leftNeg;
                int leftN = leftPos + leftNeg;
                int rightN = rightPos + rightNeg;

                double childGini = (leftN / (double)N) * GiniBinary(leftPos, leftNeg)
                                 + (rightN / (double)N) * GiniBinary(rightPos, rightNeg);
                double gain = parentGini - childGini;
                if (gain <= bestGain) continue;

                double threshold = 0.5 * (labelled[i].v + labelled[i + 1].v);
                // Direction: whichever side has the higher positive-fraction is the predicted
                // positive side. ">=" predicts positive on the right; "<=" predicts on the left.
                double rightPosFrac = rightN > 0 ? rightPos / (double)rightN : 0.0;
                double leftPosFrac = leftN > 0 ? leftPos / (double)leftN : 0.0;
                MeasurementComparator cmp = rightPosFrac >= leftPosFrac
                    ? MeasurementComparator.GreaterThanOrEqual
                    : MeasurementComparator.LessThanOrEqual;

                bestGain = gain;
                best = new ThresholdSuggestion(kv.Key, cmp, (float)threshold, gain);
            }
        }

        if (best.HasValue) result.Add(best.Value);
        return result;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        int n = sorted.Length;
        if (n == 0) return 0.0;
        return n % 2 == 1
            ? sorted[n / 2]
            : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
    }

    private static double GiniBinary(int pos, int neg)
    {
        int n = pos + neg;
        if (n <= 0) return 0.0;
        double p = pos / (double)n;
        double q = 1.0 - p;
        return 1.0 - (p * p + q * q);
    }
}
