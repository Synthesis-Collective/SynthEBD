using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// ViewModel for the per-measurement histogram window. Snapshots the active profile's
/// <see cref="VM_BodyTypeProfile.MeasurementCache"/> at construction time, computes
/// summary statistics, and exposes a re-bindable list of <see cref="HistogramBin"/>
/// rows that the window renders as a bar chart.
/// <para>The snapshot is intentional: the user can keep the window open while editing
/// the underlying profile without the chart changing under them. To see new numbers
/// after a re-scan or edit, re-click the row's H button to open a fresh window.</para>
/// <para>Bin recomputation triggers on every change to <see cref="BinCount"/> or
/// <see cref="GenderFilter"/> via the property-changed handler set up in the ctor. Stats
/// re-derive from the filtered sample list (so changing GenderFilter narrows Min/Max/
/// Mean/etc. to the visible subset, not the snapshot's full population).</para>
/// </summary>
public class VM_MeasurementHistogram : VM
{
    /// <summary>Full sample population captured from the cache at ctor time. Items are
    /// (Gender, Value) tuples — PresetLabel + Weight are dropped because the histogram
    /// doesn't surface per-sample identity (just aggregated counts). Bin computation
    /// filters this list by <see cref="GenderFilter"/> on every recompute.</summary>
    private readonly List<(Gender Gender, float Value)> _samples;

    public string MeasurementName { get; }
    public string ProfileName { get; }
    public string WindowTitle => $"Histogram — {MeasurementName} ({ProfileName})";

    /// <summary>Bin count for the next recompute. Bound to a Slider in the window; clamped
    /// to [1, 200] on the binding (the rebuild itself handles any value &gt;0 safely).
    /// Initial value is overwritten in the ctor when persistence is on
    /// (see <see cref="_staticBinCount"/>).</summary>
    public int BinCount { get; set; } = 30;

    /// <summary>Bound to the "Persist" checkbox next to the bin-count slider. When checked
    /// and the window closes, <see cref="CommitPersistedSettings"/> writes the current
    /// <see cref="BinCount"/> into the process-static cache so the next histogram opens
    /// at the same bin count. "If multiple are open, last to close wins" — the most
    /// recently closed window's setting overwrites whatever an earlier-closed window left.
    /// The flag itself also persists (next window opens with the box pre-checked when
    /// the last close had it checked), so the toggle is sticky across the session.</summary>
    public bool PersistBinCount { get; set; }

    // ─── Process-static persistence backing store ────────────────────────────────────
    // Lives for the lifetime of the app process. Survives across multiple histogram
    // windows but resets on restart — not written to user settings because the bin
    // count is a per-investigation visualization preference, not a global app setting.
    // Both fields are written together by CommitPersistedSettings on Window.Closed.

    /// <summary>Last <see cref="BinCount"/> committed by a closing window. Null until at
    /// least one window has closed. Consumed by the ctor only when
    /// <see cref="_staticPersistBinCount"/> is true.</summary>
    private static int? _staticBinCount;

    /// <summary>Last <see cref="PersistBinCount"/> committed by a closing window. The new
    /// window's checkbox starts in this state so the user doesn't have to re-check it
    /// every time.</summary>
    private static bool _staticPersistBinCount;

    /// <summary>Currently-selected gender filter. Null = no filter (all samples). Bound to
    /// the gender ComboBox via SelectedValue. Only the genders actually present in the
    /// snapshot appear in <see cref="GenderFilterOptions"/>, so a single-gender profile's
    /// dropdown collapses to just "All" + that one gender.</summary>
    public Gender? GenderFilter { get; set; }

    /// <summary>Filter dropdown items. "All" is always first; the per-gender entries are
    /// added in declaration order (Male, Female) but only when at least one sample of that
    /// gender exists in the snapshot.</summary>
    public IReadOnlyList<GenderFilterOption> GenderFilterOptions { get; }

    /// <summary>Computed bars. Bound to an ItemsControl whose ItemsPanel is a UniformGrid
    /// (Rows=1) so the bars fill the chart area evenly regardless of count. Replaced (not
    /// in-place updated) on every recompute so the binding fires a single CollectionChanged
    /// rather than per-bar PropertyChanged events.</summary>
    public ObservableCollection<HistogramBin> Bins { get; } = new();

    public int TotalSamples { get; private set; }
    public double Min { get; private set; }
    public double Max { get; private set; }
    public double Mean { get; private set; }
    public double Median { get; private set; }
    public double StdDev { get; private set; }
    public double P25 { get; private set; }
    public double P75 { get; private set; }

    /// <summary>Pre-formatted single-line stats string for the header. Kept in the VM rather
    /// than the XAML so the InvariantCulture / F3 formatting stays consistent with the
    /// per-bin tooltips.</summary>
    public string StatsLine => TotalSamples == 0
        ? "No samples"
        : string.Format(CultureInfo.InvariantCulture,
            "n={0}  min={1:F3}  max={2:F3}  mean={3:F3}  median={4:F3}  σ={5:F3}  p25={6:F3}  p75={7:F3}",
            TotalSamples, Min, Max, Mean, Median, StdDev, P25, P75);

    /// <summary>Left-edge x-axis label = filtered min. Empty when no samples (so the
    /// window's axis row doesn't show a stale "0.000" against an empty chart).</summary>
    public string MinLabel => TotalSamples == 0 ? "" : Min.ToString("F3", CultureInfo.InvariantCulture);
    public string MaxLabel => TotalSamples == 0 ? "" : Max.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>Chart's pixel-space height. Bars compute their pixel height as
    /// <c>(Count / MaxBinCount) * ChartHeight</c> against this. Mutable so the window
    /// can update it on chart-area resize via <see cref="RescaleBars"/>; the initial
    /// value of 280 covers the default un-maximized layout.</summary>
    public double ChartHeight { get; private set; } = 280.0;

    /// <summary>Tallest bin's sample count from the most recent <see cref="Rebuild"/>.
    /// Public + auto-property so Fody fires PropertyChanged on assignment, which lets the
    /// Y-axis tick labels in the window (max, 3/4, 1/2, 1/4 of this value) refresh
    /// automatically whenever the filter or bin count changes the underlying distribution.
    /// Doubles as the normalization denominator for bar heights — kept here rather than
    /// re-derived from <see cref="Bins"/> so a chart-area resize doesn't pay the walk.</summary>
    public int MaxBinCount { get; private set; } = 1;

    // Y-axis tick label values. Computed from MaxBinCount; Fody recognizes the
    // dependency and re-fires PropertyChanged on these whenever MaxBinCount changes,
    // so the XAML bindings to the tick TextBlocks pick up the new values without an
    // explicit notify call. Rounded to int because the count axis is integer-valued.
    public int ThreeQuarterMaxBinCount => (int)Math.Round(MaxBinCount * 0.75);
    public int HalfMaxBinCount => (int)Math.Round(MaxBinCount * 0.50);
    public int QuarterMaxBinCount => (int)Math.Round(MaxBinCount * 0.25);

    /// <summary>Reads the profile's MeasurementCache at construction time, builds the
    /// initial histogram with default <see cref="BinCount"/> / <see cref="GenderFilter"/>,
    /// and subscribes to its own property-changed events so the bars / stats refresh when
    /// the user moves the slider or changes the dropdown.</summary>
    public VM_MeasurementHistogram(VM_BodyTypeProfile profile, VM_MeasurementDefinition definition)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        ProfileName = profile.Name ?? "(unnamed profile)";
        MeasurementName = definition.Name ?? "";

        // Snapshot: walk the cache once, copying out the non-null values for this
        // measurement name. The cache is mutable (re-scans clear and refill it), so
        // referencing it later would risk reading mid-rebuild. Tuple stays small (8B
        // gender enum + 4B float).
        _samples = new List<(Gender Gender, float Value)>();
        foreach (var kv in profile.MeasurementCache)
        {
            var entry = kv.Value;
            if (entry == null) continue;
            if (!entry.Measurements.TryGetValue(MeasurementName, out var v) || !v.HasValue) continue;
            _samples.Add((kv.Key.Gender, v.Value));
        }

        // Gender dropdown options. Always include "All"; include per-gender options only
        // when at least one sample exists for that gender — keeps the dropdown short for
        // single-gender profiles (the typical case for body-type-scoped profiles).
        var options = new List<GenderFilterOption> { new("All", null) };
        bool hasMale = _samples.Any(s => s.Gender == Gender.Male);
        bool hasFemale = _samples.Any(s => s.Gender == Gender.Female);
        if (hasFemale) options.Add(new("Female", Gender.Female));
        if (hasMale) options.Add(new("Male", Gender.Male));
        GenderFilterOptions = options;

        // Apply persisted bin-count + checkbox state from the last closed histogram (if
        // any). Done before Rebuild so the initial bar layout uses the persisted count.
        // Set before the PropertyChanged subscription is wired so the assignment doesn't
        // trigger a duplicate Rebuild.
        PersistBinCount = _staticPersistBinCount;
        if (_staticPersistBinCount && _staticBinCount.HasValue)
        {
            BinCount = _staticBinCount.Value;
        }

        Rebuild();

        // Property-changed wiring: any change to BinCount or GenderFilter recomputes bins
        // + stats. Direct subscription (no ReactiveUI / DisposeWith) because this VM's
        // lifetime is tied to a Window that's manually shown/closed — no DI-managed
        // disposal pipeline runs against it.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BinCount) || e.PropertyName == nameof(GenderFilter))
            {
                Rebuild();
            }
        };
    }

    /// <summary>Recomputes <see cref="Bins"/> and the summary stats from <see cref="_samples"/>
    /// after applying <see cref="GenderFilter"/>. Width-zero ranges (every sample landed at
    /// the same value) collapse to a single bin so the chart still renders something visible
    /// instead of a zero-width strip. Bar pixel height is normalized against the tallest
    /// bin so the chart fills the available vertical space without per-recompute
    /// re-measurement.</summary>
    private void Rebuild()
    {
        Bins.Clear();

        var filtered = GenderFilter.HasValue
            ? _samples.Where(s => s.Gender == GenderFilter.Value).Select(s => (double)s.Value).ToList()
            : _samples.Select(s => (double)s.Value).ToList();

        TotalSamples = filtered.Count;
        if (filtered.Count == 0)
        {
            Min = Max = Mean = Median = StdDev = P25 = P75 = 0.0;
            return;
        }

        filtered.Sort();
        Min = filtered[0];
        Max = filtered[^1];
        Mean = filtered.Average();
        Median = Percentile(filtered, 0.50);
        P25 = Percentile(filtered, 0.25);
        P75 = Percentile(filtered, 0.75);

        // Sample std-dev (n-1 denominator). Returns 0 when n<2 so the stats line doesn't
        // emit "NaN" or "Infinity" for a single-sample histogram (which is otherwise
        // rendered as one full-height bar at min==max).
        if (filtered.Count >= 2)
        {
            double mean = Mean;
            double sumSq = 0.0;
            foreach (var v in filtered)
            {
                double d = v - mean;
                sumSq += d * d;
            }
            StdDev = Math.Sqrt(sumSq / (filtered.Count - 1));
        }
        else
        {
            StdDev = 0.0;
        }

        int binCount = Math.Max(1, BinCount);
        double range = Max - Min;
        var binEdges = new double[binCount + 1];
        if (range < 1e-9)
        {
            // Degenerate: every sample equals Min. Render a single full bin centered on
            // the value so the chart shows "n at value v" instead of an empty plot.
            binCount = 1;
            binEdges = new double[] { Min - 0.5, Min + 0.5 };
        }
        else
        {
            double step = range / binCount;
            for (int i = 0; i <= binCount; i++) binEdges[i] = Min + step * i;
            // Floating-point: force the last edge to exactly Max so the inclusive end-
            // sample lands in the last bin instead of overshooting by ε.
            binEdges[binCount] = Max;
        }

        var counts = new int[binCount];
        foreach (var v in filtered)
        {
            // Linear bin search via index math (O(1) per sample). Subtract Min, divide by
            // step, clamp. The right-edge inclusion is handled by clamping to binCount-1
            // for values that hit exactly Max.
            int idx;
            if (range < 1e-9)
            {
                idx = 0;
            }
            else
            {
                idx = (int)Math.Floor((v - Min) / (range / binCount));
                if (idx < 0) idx = 0;
                else if (idx >= binCount) idx = binCount - 1;
            }
            counts[idx]++;
        }

        MaxBinCount = Math.Max(1, counts.Max());
        for (int i = 0; i < binCount; i++)
        {
            double height = (counts[i] / (double)MaxBinCount) * ChartHeight;
            Bins.Add(new HistogramBin
            {
                BinStart = binEdges[i],
                BinEnd = binEdges[i + 1],
                Count = counts[i],
                BarHeight = height,
            });
        }
    }

    /// <summary>Adjusts <see cref="ChartHeight"/> to <paramref name="availableHeight"/>
    /// and rescales every bin's <see cref="HistogramBin.BarHeight"/> in place — no
    /// re-binning, no allocations beyond setting properties. Called from the window's
    /// chart-area SizeChanged handler so the bars grow when the user maximizes the
    /// window and shrink when they un-maximize it.
    /// <para>Per-bin updates rely on <see cref="HistogramBin"/> raising PropertyChanged
    /// for <c>BarHeight</c> — that's why it inherits from <see cref="VM"/> rather than
    /// being a plain POCO; without INPC the existing item containers would keep their
    /// old pixel heights even after a resize.</para></summary>
    public void RescaleBars(double availableHeight)
    {
        if (availableHeight <= 0 || double.IsNaN(availableHeight) || double.IsInfinity(availableHeight)) return;
        ChartHeight = availableHeight;
        if (MaxBinCount <= 0 || Bins.Count == 0) return;
        double max = MaxBinCount;
        foreach (var bin in Bins)
        {
            bin.BarHeight = (bin.Count / max) * availableHeight;
        }
    }

    /// <summary>Copies the current <see cref="PersistBinCount"/> + <see cref="BinCount"/>
    /// into the process-static cache so the next histogram window opens with them.
    /// Always overwrites both fields — "last to close wins" per the original feature
    /// request. Called from <see cref="Window_MeasurementHistogram"/>'s Closed handler.
    /// <para>Both fields are always written, not just when PersistBinCount is true. That
    /// way the checkbox state itself is sticky even when transitioning persist on→off:
    /// closing a window with the box unchecked clears the on-state for the next open,
    /// rather than letting a previously-checked state silently linger.</para></summary>
    public void CommitPersistedSettings()
    {
        _staticPersistBinCount = PersistBinCount;
        if (PersistBinCount)
        {
            _staticBinCount = BinCount;
        }
    }

    /// <summary>Linear-interpolation percentile of a sorted (ascending) list. Matches
    /// numpy's default "linear" interpolation so the numbers reproduce against external
    /// tooling if the user is cross-checking. <paramref name="p"/> is in [0, 1].</summary>
    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0.0;
        if (sorted.Count == 1) return sorted[0];
        double rank = p * (sorted.Count - 1);
        int lo = (int)Math.Floor(rank);
        int hi = (int)Math.Ceiling(rank);
        if (lo == hi) return sorted[lo];
        double frac = rank - lo;
        return sorted[lo] * (1 - frac) + sorted[hi] * frac;
    }
}

/// <summary>One bar in the histogram. Carries display-ready geometry (pixel-space
/// <see cref="BarHeight"/>) plus the raw count + value range so the per-bar tooltip and
/// the per-bar x-axis label can describe what the bar covers without a value converter.
/// <para>Inherits from <see cref="VM"/> so Fody weaves PropertyChanged on
/// <see cref="BarHeight"/> — required for <see cref="VM_MeasurementHistogram.RescaleBars"/>
/// to update existing item containers in place when the chart area resizes (without INPC
/// the bound Rectangle.Height would keep its initial value after a window maximize).</para></summary>
public class HistogramBin : VM
{
    public double BinStart { get; set; }
    public double BinEnd { get; set; }
    public int Count { get; set; }
    public double BarHeight { get; set; }

    /// <summary>X-axis label shown under each bar — the bin's left edge value, F3-formatted.
    /// Format is just the start (not the full "start–end" range) because the consumer
    /// template rotates labels 45° rather than 90°, and the longer range form would
    /// overlap horizontally at typical bin counts. Adjacent labels reveal the bin width
    /// implicitly (label[i+1] - label[i]) and the last bin's right edge equals the
    /// histogram's max, shown in the corner Min/Max Y-axis labels.</summary>
    public string AxisLabel => BinStart.ToString("F3", CultureInfo.InvariantCulture);

    public string Tooltip => string.Format(CultureInfo.InvariantCulture,
        "[{0:F3}, {1:F3}{2}: {3} sample{4}",
        BinStart, BinEnd,
        // The last bin is closed on the right (sample == Max lands here); earlier bins are
        // half-open. Without per-bin awareness we just use ")" everywhere — keeps the
        // tooltip simple and the one-off edge case is invisible to the user.
        ")",
        Count,
        Count == 1 ? "" : "s");
}

/// <summary>(Label, Value) pair for the gender ComboBox so SelectedValuePath/DisplayMemberPath
/// can keep the Gender? enum out of XAML. Mirrors the pattern used by
/// <see cref="MarginScoreOption"/> elsewhere in the editor.</summary>
public sealed class GenderFilterOption
{
    public GenderFilterOption(string label, Gender? value)
    {
        Label = label;
        Value = value;
    }
    public string Label { get; }
    public Gender? Value { get; }
}
