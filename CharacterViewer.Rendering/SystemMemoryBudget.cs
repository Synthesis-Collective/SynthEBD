using System;

namespace CharacterViewer.Rendering;

/// <summary>
/// Computes a dynamic byte budget for in-RAM caches (decoded DDS pixels, parsed
/// NIF geometry) from current system memory. This is the system-RAM analogue of
/// the VRAM budgeting in <see cref="ResidentTextureCache"/>: the budget tracks
/// free physical RAM, so a cache grows to use spare memory on a big machine and
/// shrinks on a constrained one -- instead of a fixed entry count that ignores
/// both the host's RAM and the per-entry size (a 4K texture is ~16x a 1K one, so
/// "128 entries" can mean 256 MB or 8 GB).
///
/// <para>Uses <see cref="GC.GetGCMemoryInfo()"/>, which needs no P/Invoke, works
/// cross-platform, and respects container memory limits. Its figures reflect the
/// last garbage collection, so they are approximate -- callers re-poll
/// periodically (e.g. every N inserts) rather than on every access.</para>
/// </summary>
internal static class SystemMemoryBudget
{
    // Always leave at least this much physical RAM for the OS and the rest of the
    // app (Mutagen load order, the WPF UI, plugin/link caches during patching).
    private const long MinHeadroomBytes = 2L * 1024 * 1024 * 1024; // 2 GB
    private const double HeadroomFraction = 0.20;                  // ...or 20% of RAM

    /// <summary>
    /// Returns a byte budget sized to a <paramref name="fraction"/> of the
    /// reclaimable-free RAM (OS-free RAM plus what the caller's cache already
    /// holds, since that is evictable), after reserving headroom, then clamped to
    /// [<paramref name="minBytes"/>, <paramref name="maxFractionOfTotal"/> of total
    /// physical RAM].
    ///
    /// <para>The ceiling is expressed as a fraction of total RAM, not a fixed byte
    /// constant, so it scales with the machine: a fixed cap would needlessly
    /// throttle a high-RAM host running batched 4K/8K renders. In normal use the
    /// free-RAM term is what binds; the fraction-of-total ceiling is only a backstop
    /// against an anomalous memory reading or a working set larger than will ever be
    /// re-referenced. The caches don't coordinate, but they share the free-RAM
    /// signal -- as one fills, free RAM drops, so the next one's budget shrinks --
    /// which collectively bounds them to the reserved headroom.</para>
    /// </summary>
    /// <param name="currentCacheBytes">Bytes the caller's cache currently holds.
    /// Added back to free space because it is reclaimable; this keeps the budget
    /// stable as the cache fills (otherwise the target would chase a shrinking
    /// free figure and oscillate).</param>
    /// <param name="fraction">Share of reclaimable-free RAM this cache may use
    /// (e.g. 0.5 for the dominant pixel cache, less for secondary caches).</param>
    /// <param name="minBytes">Floor so a busy machine still caches something.</param>
    /// <param name="maxFractionOfTotal">Ceiling as a share of total physical RAM, so
    /// the cap scales with the host instead of being a fixed throttle.</param>
    /// <summary>
    /// Mode-aware budget. <see cref="RenderCacheMode.PercentFreeRam"/> is the historical behaviour (a
    /// fraction of live free RAM). <see cref="RenderCacheMode.FixedRam"/> applies the same per-cache
    /// <paramref name="fraction"/> to <paramref name="fixedPoolBytes"/> instead of live free RAM, so the
    /// budget is a stable, machine-independent ceiling (the fixed pool is the notional total shared across
    /// caches; each takes its fraction of it). <see cref="RenderCacheMode.Disabled"/> returns 0 — the cache
    /// retains nothing (a render still holds the pixels it fetched, so this is safe, just non-reusing).
    /// </summary>
    public static long Compute(RenderCacheMode mode, long fixedPoolBytes, long currentCacheBytes,
        double fraction, long minBytes, double maxFractionOfTotal)
    {
        switch (mode)
        {
            case RenderCacheMode.Disabled:
                return 0;

            case RenderCacheMode.FixedRam:
            {
                long fixedTarget = (long)(Math.Max(0, fixedPoolBytes) * fraction);
                // Keep the fraction-of-total ceiling as a sanity backstop; no free-RAM floor, since the whole
                // point of a fixed budget is to honour the user's number even on a busy machine.
                long totalRam = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                long ceiling = totalRam > 0 ? Math.Max(minBytes, (long)(totalRam * maxFractionOfTotal)) : long.MaxValue;
                return Math.Clamp(fixedTarget, 0, ceiling);
            }

            default: // PercentFreeRam
                return Compute(currentCacheBytes, fraction, minBytes, maxFractionOfTotal);
        }
    }

    public static long Compute(long currentCacheBytes, double fraction, long minBytes, double maxFractionOfTotal)
    {
        GCMemoryInfo info = GC.GetGCMemoryInfo();
        long total = info.TotalAvailableMemoryBytes; // physical RAM, or container limit
        long load = info.MemoryLoadBytes;            // memory in use system-wide

        // Info unavailable (e.g. before the first GC): fall back to the floor.
        if (total <= 0)
            return minBytes;

        long free = Math.Max(0, total - load);
        long headroom = Math.Max(MinHeadroomBytes, (long)(total * HeadroomFraction));
        long reclaimable = Math.Max(0, free + currentCacheBytes - headroom);
        long target = (long)(reclaimable * fraction);
        long maxBytes = Math.Max(minBytes, (long)(total * maxFractionOfTotal));
        return Math.Clamp(target, minBytes, maxBytes);
    }
}
