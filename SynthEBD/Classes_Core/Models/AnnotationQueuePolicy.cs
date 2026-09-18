using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SynthEBD;

/// <summary>Sampling policy the annotation queue uses to decide which (preset, gender, weight)
/// slice to serve next.</summary>
public enum AnnotationQueuePolicy
{
    /// <summary>Stratified over the range of a chosen measurement: candidates are bucketed into
    /// quantile bins and the bins are visited round-robin, so the first handful of slices already
    /// span the whole distribution. The right starting mode for a Category with few or no
    /// annotations.</summary>
    Spread = 0,

    /// <summary>Closest to the current decision boundary for the target Category: candidates are
    /// ranked by the smallest normalized distance between one of their measurement values and a
    /// threshold in one of that Category's rules. Sharpens an existing cut. Degrades to
    /// <see cref="Spread"/> when the Category has no rules yet (the caller then supplies no
    /// distances, and every candidate ties).</summary>
    Uncertainty = 1,

    /// <summary>Uniform random over candidates, from a fixed seed so a sample is reproducible and
    /// can be quoted in the tracker.</summary>
    Random = 2,
}

/// <summary>
/// One slice of the ordered queue: an index into the caller's candidate list, plus whether this
/// position was filled from the random stream rather than the policy's own ordering.
/// <para>The provenance flag is not cosmetic. A rule fitted only on boundary-sampled verdicts
/// scored 66/70 in-sample and 8/14 on a random draw (decision <c>D25</c>), so the only verdicts
/// that can honestly estimate an error rate are the random ones. Carrying the flag per served
/// slice lets the session counters report "N of M served were random draws" instead of leaving
/// the user to guess which half of their labels can be quoted as a measurement.</para>
/// </summary>
public readonly struct AnnotationQueueEntry
{
    public AnnotationQueueEntry(int index, bool fromRandomDraw)
    {
        Index = index;
        FromRandomDraw = fromRandomDraw;
    }

    /// <summary>Position in the candidate list the caller passed to <see cref="AnnotationQueueOrdering.Order"/>.</summary>
    public int Index { get; }

    /// <summary>True when this position was taken from the uniform-random stream rather than from
    /// the policy's own ordering. Always false under <see cref="AnnotationQueuePolicy.Random"/>
    /// (every draw is random there, so flagging each one carries no information) and always true
    /// for the interleaved fraction under the other two policies.</summary>
    public bool FromRandomDraw { get; }
}

/// <summary>
/// Pure-logic ordering functions behind the Label-then-Suggest annotation queue. Takes a list of
/// per-candidate scalars and returns the order in which to serve them; knows nothing about row
/// view models, profiles, or the viewer, so every policy is unit-testable without the UI. Mirrors
/// the split <see cref="MeasurementDiscriminators"/> uses -- algorithms here, VM wiring elsewhere.
/// <para>Determinism is a feature, not an accident: every entry point takes an explicit seed and
/// no policy consults ambient state, so re-running a policy with the same inputs reproduces the
/// same sequence. That is what makes a sample quotable.</para>
/// </summary>
public static class AnnotationQueueOrdering
{
    /// <summary>Quantile bins <see cref="OrderSpread"/> uses when the caller doesn't specify.</summary>
    public const int DefaultBinCount = 8;

    /// <summary>Default share of positions served from the random stream under Spread /
    /// Uncertainty. Deliberately non-zero -- see <see cref="AnnotationQueueEntry.FromRandomDraw"/>.</summary>
    public const double DefaultRandomFraction = 0.25;

    /// <summary>Offset applied to the caller's seed for the interleaved random stream, so the
    /// random positions and the random draws aren't generated from the same sequence (which would
    /// correlate "which positions are random" with "which candidates they pick").</summary>
    private const int RandomStreamSalt = 0x5F3759DF;

    /// <summary>
    /// Orders <paramref name="scores"/>.Count candidates under <paramref name="policy"/>.
    /// </summary>
    /// <param name="policy">Which ordering to apply.</param>
    /// <param name="scores">Per-candidate scalar the policy reads. For <see cref="AnnotationQueuePolicy.Spread"/>
    /// this is the stratification measurement's value; for <see cref="AnnotationQueuePolicy.Uncertainty"/>
    /// it is the normalized distance to the nearest rule threshold (see
    /// <see cref="MinNormalizedBoundaryDistance"/>); for <see cref="AnnotationQueuePolicy.Random"/>
    /// it is ignored except for its length. <c>null</c> entries are candidates the policy cannot
    /// rank -- they are served last rather than dropped, since an unrankable slice is still a
    /// slice the user may want to label.</param>
    /// <param name="randomFraction">Share of positions to fill from a uniform-random stream instead
    /// of the policy's own ordering, clamped to [0, 1]. Ignored under
    /// <see cref="AnnotationQueuePolicy.Random"/>, which is already uniform.</param>
    /// <param name="seed">Seed for every random decision made here.</param>
    /// <param name="binCount">Quantile bins for <see cref="AnnotationQueuePolicy.Spread"/>.</param>
    public static IReadOnlyList<AnnotationQueueEntry> Order(
        AnnotationQueuePolicy policy,
        IReadOnlyList<double?> scores,
        double randomFraction,
        int seed,
        int binCount = DefaultBinCount)
    {
        int count = scores?.Count ?? 0;
        if (count == 0) return Array.Empty<AnnotationQueueEntry>();

        if (policy == AnnotationQueuePolicy.Random)
        {
            // Already a uniform sample; interleaving a second random stream would only reshuffle
            // it, and flagging every entry as a random draw tells the counters nothing.
            return OrderRandom(count, seed).Select(i => new AnnotationQueueEntry(i, false)).ToList();
        }

        var primary = policy switch
        {
            AnnotationQueuePolicy.Spread => OrderSpread(scores, binCount, seed),
            AnnotationQueuePolicy.Uncertainty => OrderUncertainty(scores),
            _ => OrderRandom(count, seed),
        };

        return Interleave(primary, OrderRandom(count, unchecked(seed ^ RandomStreamSalt)), randomFraction, seed);
    }

    /// <summary>
    /// Buckets candidates into <paramref name="binCount"/> equal-population (quantile) bins of
    /// <paramref name="values"/> and visits the bins round-robin, so consecutive slices land in
    /// different parts of the distribution and the first pass already spans the full range.
    /// <para>Quantile bins rather than equal-width bins: measurement distributions across a preset
    /// corpus are heavy-tailed (a handful of extreme presets stretch the axis), and equal-width
    /// bins would leave most bins empty and serve the same crowded middle over and over.</para>
    /// <para>Order within a bin is a seeded shuffle, so re-running with a different seed explores a
    /// different representative of each quantile instead of always the same one. Candidates with a
    /// <c>null</c> value cannot be placed in a quantile and are appended after the stratified pass.</para>
    /// </summary>
    public static IReadOnlyList<int> OrderSpread(IReadOnlyList<double?> values, int binCount, int seed)
    {
        int count = values?.Count ?? 0;
        if (count == 0) return Array.Empty<int>();
        if (binCount < 1) binCount = 1;

        var ranked = new List<int>(count);
        var unranked = new List<int>();
        for (int i = 0; i < count; i++)
        {
            if (values[i].HasValue) ranked.Add(i); else unranked.Add(i);
        }

        var rng = new Random(seed);
        if (ranked.Count == 0) return ShuffleInPlace(unranked, rng);

        // Ascending by value; index breaks ties so the bin assignment is reproducible.
        ranked.Sort((a, b) =>
        {
            int cmp = values[a].Value.CompareTo(values[b].Value);
            return cmp != 0 ? cmp : a.CompareTo(b);
        });

        int bins = Math.Min(binCount, ranked.Count);
        var buckets = new List<int>[bins];
        for (int b = 0; b < bins; b++) buckets[b] = new List<int>();
        for (int r = 0; r < ranked.Count; r++)
        {
            // Contiguous equal-population split of the sorted list: candidate r falls in the bin
            // its rank position lands in, which is what makes these quantiles.
            int bin = (int)((long)r * bins / ranked.Count);
            if (bin >= bins) bin = bins - 1;
            buckets[bin].Add(ranked[r]);
        }

        for (int b = 0; b < bins; b++) ShuffleInPlace(buckets[b], rng);

        var result = new List<int>(count);
        int deepest = buckets.Max(x => x.Count);
        for (int depth = 0; depth < deepest; depth++)
        {
            for (int b = 0; b < bins; b++)
            {
                if (depth < buckets[b].Count) result.Add(buckets[b][depth]);
            }
        }

        ShuffleInPlace(unranked, rng);
        result.AddRange(unranked);
        return result;
    }

    /// <summary>
    /// Ranks candidates by ascending <paramref name="distances"/> -- smallest distance to a rule
    /// threshold first. Candidates with no computable distance (<c>null</c>: the Category has no
    /// rule touching any measurement this slice has a value for) sort last, since serving a slice
    /// no rule can be near tells the user nothing about where the cut belongs.
    /// </summary>
    public static IReadOnlyList<int> OrderUncertainty(IReadOnlyList<double?> distances)
    {
        int count = distances?.Count ?? 0;
        if (count == 0) return Array.Empty<int>();

        var order = Enumerable.Range(0, count).ToList();
        order.Sort((a, b) =>
        {
            double? da = distances[a];
            double? db = distances[b];
            if (!da.HasValue && !db.HasValue) return a.CompareTo(b);
            if (!da.HasValue) return 1;
            if (!db.HasValue) return -1;
            int cmp = da.Value.CompareTo(db.Value);
            return cmp != 0 ? cmp : a.CompareTo(b);
        });
        return order;
    }

    /// <summary>Uniform random permutation of [0, <paramref name="count"/>) from
    /// <paramref name="seed"/>. Same seed, same sequence -- that is the whole point of exposing
    /// the seed in the UI.</summary>
    public static IReadOnlyList<int> OrderRandom(int count, int seed)
    {
        if (count <= 0) return Array.Empty<int>();
        return ShuffleInPlace(Enumerable.Range(0, count).ToList(), new Random(seed));
    }

    /// <summary>
    /// Merges a policy ordering with a uniform-random ordering so that exactly
    /// <c>round(n * fraction)</c> of the n positions come from the random stream, at positions
    /// chosen by <paramref name="seed"/>.
    /// <para>The count is exact rather than probabilistic: "a quarter of what I labelled this
    /// session was a random draw" has to be a fact the user can state, not an expectation that
    /// happened to come out at 3/20 on the run they actually did.</para>
    /// <para>Both inputs are permutations of the same index set, so whichever stream a position
    /// draws from, its cursor skips indices already emitted by the other. If one stream runs dry
    /// first the other supplies the remainder, which keeps the output a permutation of the input
    /// under every fraction.</para>
    /// </summary>
    public static IReadOnlyList<AnnotationQueueEntry> Interleave(
        IReadOnlyList<int> primary,
        IReadOnlyList<int> randomPool,
        double fraction,
        int seed)
    {
        int n = primary?.Count ?? 0;
        if (n == 0) return Array.Empty<AnnotationQueueEntry>();
        if (double.IsNaN(fraction)) fraction = 0.0;
        fraction = Math.Clamp(fraction, 0.0, 1.0);

        if (fraction <= 0.0 || randomPool == null || randomPool.Count == 0)
        {
            return primary.Select(i => new AnnotationQueueEntry(i, false)).ToList();
        }

        int randomSlots = (int)Math.Round(n * fraction, MidpointRounding.AwayFromZero);
        randomSlots = Math.Clamp(randomSlots, 0, n);
        // A non-zero fraction that rounds to zero on a short queue would silently disable the
        // honesty mechanism exactly when the sample is small enough for it to matter most.
        if (randomSlots == 0) randomSlots = 1;
        if (randomSlots >= n)
        {
            return randomPool.Select(i => new AnnotationQueueEntry(i, true)).ToList();
        }

        // Choose which positions are random draws: a seeded sample of `randomSlots` positions out
        // of n, without replacement.
        var positions = ShuffleInPlace(Enumerable.Range(0, n).ToList(), new Random(seed));
        var isRandomSlot = new bool[n];
        for (int k = 0; k < randomSlots; k++) isRandomSlot[positions[k]] = true;

        var emitted = new HashSet<int>();
        var result = new List<AnnotationQueueEntry>(n);
        int pi = 0, ri = 0;

        for (int pos = 0; pos < n; pos++)
        {
            if (isRandomSlot[pos])
            {
                while (ri < randomPool.Count && emitted.Contains(randomPool[ri])) ri++;
                if (ri < randomPool.Count)
                {
                    int idx = randomPool[ri++];
                    emitted.Add(idx);
                    result.Add(new AnnotationQueueEntry(idx, true));
                    continue;
                }
                // Random stream exhausted -- fall through to the primary one.
            }

            while (pi < primary.Count && emitted.Contains(primary[pi])) pi++;
            if (pi < primary.Count)
            {
                int idx = primary[pi++];
                emitted.Add(idx);
                result.Add(new AnnotationQueueEntry(idx, false));
                continue;
            }

            // Primary exhausted too (only reachable when the two streams aren't permutations of
            // the same set). Drain whatever the random stream has left.
            while (ri < randomPool.Count && emitted.Contains(randomPool[ri])) ri++;
            if (ri >= randomPool.Count) break;
            int tail = randomPool[ri++];
            emitted.Add(tail);
            result.Add(new AnnotationQueueEntry(tail, true));
        }

        return result;
    }

    /// <summary>
    /// Pulls same-key items forward into runs of at most <paramref name="maxRunLength"/>, so
    /// consecutive positions tend to share a key while the order of each run's <i>leading</i> item
    /// still follows <paramref name="ordered"/>.
    /// <para>The queue uses the (gender, weight) slot as the key. The preview NPC is chosen per
    /// weight slot and <c>VM_CharacterViewer.LoadAsync</c> short-circuits when the same NPC is
    /// already loaded, so advancing inside a run costs one mesh deformation while crossing a
    /// boundary costs a full NIF parse plus texture decode. Coalescing cuts that cost by roughly
    /// <paramref name="maxRunLength"/>.</para>
    /// <para>Runs are capped rather than fully grouped on purpose. Sorting the whole queue by
    /// weight would be cheaper still, but a session stopped halfway would then have labelled only
    /// low weights -- a sampling bias introduced by a performance optimization, which is exactly
    /// the kind of trade the policy is supposed to prevent. Capping keeps every run's first item in
    /// policy order, so coverage degrades gracefully if the user stops early.</para>
    /// <para>The output is always a permutation of the input: items are only reordered, never
    /// dropped or duplicated.</para>
    /// </summary>
    /// <typeparam name="T">Queue item type.</typeparam>
    /// <typeparam name="TKey">Coherence key type (the queue passes a (Gender, int) tuple).</typeparam>
    /// <param name="ordered">The policy ordering to coalesce.</param>
    /// <param name="keySelector">Extracts the coherence key from an item.</param>
    /// <param name="maxRunLength">Longest run of one key. Values below 2 return the input order
    /// unchanged, since a run of one is no run at all.</param>
    public static IReadOnlyList<T> CoalesceRuns<T, TKey>(
        IReadOnlyList<T> ordered,
        Func<T, TKey> keySelector,
        int maxRunLength)
        where TKey : notnull
    {
        int n = ordered?.Count ?? 0;
        if (n == 0) return Array.Empty<T>();
        if (keySelector == null || maxRunLength < 2) return ordered;

        var taken = new bool[n];
        var result = new List<T>(n);

        for (int i = 0; i < n; i++)
        {
            if (taken[i]) continue;

            // This item leads a run, and its position is the one the policy chose -- that is what
            // keeps coverage intact when a session stops partway through.
            taken[i] = true;
            result.Add(ordered[i]);

            var key = keySelector(ordered[i]);
            int runCount = 1;
            for (int j = i + 1; j < n && runCount < maxRunLength; j++)
            {
                if (taken[j]) continue;
                if (!EqualityComparer<TKey>.Default.Equals(keySelector(ordered[j]), key)) continue;
                taken[j] = true;
                result.Add(ordered[j]);
                runCount++;
            }
        }

        return result;
    }

    /// <summary>
    /// Smallest normalized distance between this slice's measurement values and any threshold in
    /// <paramref name="categoryRules"/> -- the "how close to a decision boundary is this slice?"
    /// score <see cref="AnnotationQueuePolicy.Uncertainty"/> ranks on.
    /// <para>Distances are divided by the measurement's corpus range so thresholds on measurements
    /// with different units are comparable; a raw <c>|v - threshold|</c> would rank every slice by
    /// whichever measurement happens to have the smallest numbers.</para>
    /// <para>Returns <c>null</c> when no threshold is comparable: the Category has no rules, every
    /// group is disabled, the conditions are all <see cref="MeasurementConditionKind.DescriptorRef"/>
    /// (which has no threshold to be near), or this slice has no value for any referenced
    /// measurement.</para>
    /// </summary>
    /// <param name="categoryRules">Rules whose descriptor Category is the queue's target. Draft
    /// rules are the caller's call to include or exclude -- both are legitimate boundaries to
    /// sharpen.</param>
    /// <param name="values">This slice's measurement values, keyed by measurement name.</param>
    /// <param name="ranges">Per-measurement (max - min) over the candidate corpus. A measurement
    /// missing from this map, or with a non-positive range, is skipped -- a constant measurement
    /// has no boundary to be near.</param>
    public static double? MinNormalizedBoundaryDistance(
        IEnumerable<MeasurementRule> categoryRules,
        IReadOnlyDictionary<string, float?> values,
        IReadOnlyDictionary<string, double> ranges)
    {
        if (categoryRules == null || values == null || ranges == null) return null;

        double? best = null;
        foreach (var rule in categoryRules)
        {
            if (rule?.GroupsORlogic == null) continue;
            foreach (var group in rule.GroupsORlogic)
            {
                // A muted branch is not a live boundary -- RuleMatches skips it too, so ranking
                // slices by their nearness to it would chase a cut that isn't firing.
                if (group == null || group.IsDisabled || group.ConditionsANDlogic == null) continue;
                foreach (var cond in group.ConditionsANDlogic)
                {
                    if (cond == null || cond.Kind != MeasurementConditionKind.Measurement) continue;
                    if (string.IsNullOrEmpty(cond.MeasurementName)) continue;
                    if (!values.TryGetValue(cond.MeasurementName, out var v) || !v.HasValue) continue;
                    if (!ranges.TryGetValue(cond.MeasurementName, out var range) || range <= 0.0) continue;

                    double d = Math.Abs(v.Value - cond.Value) / range;
                    if (!best.HasValue || d < best.Value) best = d;
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Per-measurement (max - min) over a set of slices, for the normalization
    /// <see cref="MinNormalizedBoundaryDistance"/> applies. Measurements with fewer than two
    /// non-null values are omitted rather than given a zero range, so a caller looking up a
    /// constant measurement misses and skips it instead of dividing by zero.
    /// </summary>
    public static Dictionary<string, double> ComputeRanges(
        IEnumerable<IReadOnlyDictionary<string, float?>> slices,
        IEnumerable<string> measurementNames)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        if (slices == null || measurementNames == null) return result;

        var names = measurementNames.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0) return result;

        var min = new Dictionary<string, double>(StringComparer.Ordinal);
        var max = new Dictionary<string, double>(StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var slice in slices)
        {
            if (slice == null) continue;
            foreach (var name in names)
            {
                if (!slice.TryGetValue(name, out var v) || !v.HasValue) continue;
                double d = v.Value;
                if (!seen.TryGetValue(name, out int c) || c == 0)
                {
                    min[name] = d; max[name] = d; seen[name] = 1;
                }
                else
                {
                    if (d < min[name]) min[name] = d;
                    if (d > max[name]) max[name] = d;
                    seen[name] = c + 1;
                }
            }
        }

        foreach (var name in names)
        {
            if (!seen.TryGetValue(name, out int c) || c < 2) continue;
            double range = max[name] - min[name];
            if (range > 0.0) result[name] = range;
        }
        return result;
    }

    /// <summary>
    /// Stable key grouping slices whose measurements are identical to <paramref name="decimals"/>
    /// places -- the alias detector.
    /// <para>The preset corpus is full of byte-identical re-uploads under different names
    /// (SSBBW2 vs "S4rMs' (ThickXXX) SSBBW2", "Curse of Eden" vs "Curse of Eden (Outfit)").
    /// Serving each one separately spends the user's attention twice on the same body and
    /// double-weights that shape in whatever is fitted on the verdicts, so the queue serves one
    /// representative per signature.</para>
    /// <para>Values are formatted with <see cref="CultureInfo.InvariantCulture"/> deliberately: this
    /// is a machine key, and a locale whose decimal separator is a comma would otherwise produce a
    /// different signature for the same body on a different machine.</para>
    /// </summary>
    /// <param name="values">The slice's measurement values.</param>
    /// <param name="measurementNames">Names to include, in a fixed order. Callers pass the
    /// profile's measurement list so two slices are only aliases when they agree on every
    /// measurement the profile defines.</param>
    /// <param name="decimals">Rounding applied before formatting. Absorbs floating-point noise
    /// between two evaluations of the same geometry.</param>
    public static string MeasurementSignature(
        IReadOnlyDictionary<string, float?> values,
        IReadOnlyList<string> measurementNames,
        int decimals = 3)
    {
        if (measurementNames == null || measurementNames.Count == 0) return "";
        if (decimals < 0) decimals = 0;
        if (decimals > 15) decimals = 15;

        var sb = new StringBuilder();
        foreach (var name in measurementNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (sb.Length > 0) sb.Append('|');
            sb.Append(name).Append('=');
            if (values != null && values.TryGetValue(name, out var v) && v.HasValue)
            {
                // A null and a genuine 0 must not collide: a slice the evaluator could not compute
                // is not the same body as one that measured zero.
                sb.Append(Math.Round((double)v.Value, decimals).ToString("R", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append('?');
            }
        }
        return sb.ToString();
    }

    private static List<int> ShuffleInPlace(List<int> items, Random rng)
    {
        // Fisher-Yates. System.Random is seeded explicitly by every caller, so the permutation is
        // reproducible for a given seed on a given runtime -- which is what the UI's seed box
        // promises.
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        return items;
    }
}
