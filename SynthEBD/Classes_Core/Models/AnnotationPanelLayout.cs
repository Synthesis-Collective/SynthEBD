using System;
using System.Collections.Generic;
using System.Linq;

namespace SynthEBD;

/// <summary>One slice as the annotation Panel orders it: its identity and its value on the Panel's
/// metric (null when the slice has none).</summary>
public readonly record struct PanelSample(string PresetLabel, int Weight, double? Value);

/// <summary>
/// Ordering, sampling and paging for the annotation queue's Panel window
/// (<see cref="VM_AnnotationPanel"/>). Pure and deterministic, like <see cref="SpreadStatistics"/>,
/// so which presets the Panel shows -- and in what order -- is unit-testable without the UI.
/// <para>Every ordering is (value ascending, preset label ordinal, weight), so ties resolve the same
/// way on every open and re-opening the Panel shows the same presets in the same places.</para>
/// </summary>
public static class AnnotationPanelLayout
{
    /// <summary>Cells per page (settled with the user: 24).</summary>
    public const int PageSize = 24;

    /// <summary>Default N for the Evenly spaced source.</summary>
    public const int DefaultEvenlySpacedCount = 24;

    private static bool HasFiniteValue(PanelSample s) => s.Value.HasValue && double.IsFinite(s.Value.Value);

    /// <summary>Indices of <paramref name="samples"/> sorted low to high by value; samples with no
    /// (finite) value come last, among themselves by label then weight.</summary>
    public static IReadOnlyList<int> SortByValue(IReadOnlyList<PanelSample> samples)
    {
        return Enumerable.Range(0, samples.Count)
            .OrderBy(i => HasFiniteValue(samples[i]) ? 0 : 1)
            .ThenBy(i => HasFiniteValue(samples[i]) ? samples[i].Value!.Value : 0.0)
            .ThenBy(i => samples[i].PresetLabel ?? "", StringComparer.Ordinal)
            .ThenBy(i => samples[i].Weight)
            .ToList();
    }

    /// <summary>
    /// Picks <paramref name="count"/> samples at even quantiles of the value -- sorted rank
    /// <c>floor((i + 0.5) / N * M)</c> for <c>i = 0..N-1</c> over the M samples with a finite value --
    /// and returns their indices low to high. A ladder over the whole population, for placing a
    /// continuum's cuts by eye.
    /// <para>Samples with no value are never picked: they have no place on the ladder. When N is at
    /// least M, every valued sample is returned. Ties sort by label then weight, so the pick is
    /// deterministic regardless of input order. N below 1 picks nothing.</para>
    /// </summary>
    public static IReadOnlyList<int> PickEvenlySpaced(IReadOnlyList<PanelSample> samples, int count)
    {
        var valued = SortByValue(samples).Where(i => HasFiniteValue(samples[i])).ToList();
        if (count <= 0 || valued.Count == 0) return Array.Empty<int>();
        if (count >= valued.Count) return valued;

        var picks = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            // Steps of M / N >= 1, so the floored ranks are strictly increasing: no repeats.
            int rank = (int)Math.Floor((i + 0.5) / count * valued.Count);
            picks.Add(valued[Math.Clamp(rank, 0, valued.Count - 1)]);
        }
        return picks;
    }

    /// <summary>Number of pages for <paramref name="total"/> items (at least 1, so an empty Panel
    /// still reads "page 1 / 1").</summary>
    public static int PageCount(int total, int pageSize = PageSize)
        => Math.Max(1, (Math.Max(0, total) + pageSize - 1) / pageSize);

    /// <summary>Final index of an item dragged from <paramref name="from"/> and dropped at insertion
    /// point <paramref name="insertIndex"/> (an index into the list before the item is removed, as
    /// drag-drop handlers report it): one less when inserting after the original position, since
    /// removing the item first shifts everything after it.</summary>
    public static int MoveTarget(int from, int insertIndex, int count)
    {
        int to = insertIndex > from ? insertIndex - 1 : insertIndex;
        return Math.Clamp(to, 0, Math.Max(0, count - 1));
    }

    /// <summary>
    /// The Panel's Copy Order text: a <c>#</c> header, then one <c>label | weight</c> line per slice
    /// in order, with <c>| aliases: ...</c> appended for an alias family. This is the annotation
    /// queue's plain-text worklist format (<see cref="AnnotationCaseList"/>), so the copied order can
    /// be pasted back as a List queue, and is trivial to read from a script.
    /// </summary>
    public static string FormatOrder(string header, IEnumerable<(string PresetLabel, int Weight, IReadOnlyList<string> Aliases)> entries)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("# ").AppendLine(header);
        foreach (var (label, weight, aliases) in entries)
        {
            sb.Append(label).Append(" | ").Append(weight);
            if (aliases != null && aliases.Count > 0) sb.Append(" | aliases: ").Append(string.Join(", ", aliases));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>The (start, count) item range of <paramref name="pageIndex"/> (0-based, clamped into
    /// range) for <paramref name="total"/> items.</summary>
    public static (int Start, int Count) PageRange(int total, int pageIndex, int pageSize = PageSize)
    {
        total = Math.Max(0, total);
        int page = Math.Clamp(pageIndex, 0, PageCount(total, pageSize) - 1);
        int start = page * pageSize;
        return (start, Math.Min(pageSize, total - start));
    }
}
