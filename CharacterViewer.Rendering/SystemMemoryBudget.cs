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

    /// <summary>Sum of the per-cache free-RAM fractions (0.75 pixel + 0.09 mesh + 0.01 cubemap), i.e. the
    /// collective share of free RAM the caches use at the baseline percent. A caller's <c>fraction</c>
    /// divided by this yields that cache's share of the collective budget, which is held fixed while the
    /// total is scaled by the user's <c>freeRamPercent</c>. The ratio was calibrated from a 50-NPC prewarm
    /// measurement (pixel demand dominates; see the per-cache constants).</summary>
    public const double BaselineFreeRamFraction = 0.85;

    /// <summary>The baseline collective share expressed as a percent (85). The default of
    /// <see cref="ICharacterViewerSettings.FreeRamCachePercent"/>, and the value at which each cache gets
    /// exactly its defined fraction.</summary>
    public const double BaselineFreeRamPercent = BaselineFreeRamFraction * 100.0;

    /// <summary>
    /// Mode-aware in-RAM cache byte budget. The three decode caches keep a fixed ratio among themselves
    /// (each caller's <paramref name="fraction"/> over <see cref="BaselineFreeRamFraction"/> is its share);
    /// a single knob scales the collective total.
    ///
    /// <para><see cref="RenderCacheMode.PercentFreeRam"/> (default): the caches may collectively use
    /// <paramref name="freeRamPercent"/>% of reclaimable-free RAM (OS-free RAM plus what this cache already
    /// holds, since that is evictable), after reserving headroom. This cache gets its ratio share of that.
    /// The same percent applied to total physical RAM is the upper cap -- so the percent is the single
    /// source of truth for both target and ceiling; there is no independent per-cache ceiling. The ceiling
    /// normally sits above the free-RAM target and only binds on an anomalous (too-high) free reading.
    /// <paramref name="freeRamPercent"/> at <see cref="BaselineFreeRamPercent"/> gives each cache exactly
    /// its defined fraction.</para>
    ///
    /// <para><see cref="RenderCacheMode.FixedRam"/> applies the raw <paramref name="fraction"/> to
    /// <paramref name="fixedPoolBytes"/> (a stable, machine-independent budget the user sets), capped at
    /// this cache's ratio share of total physical RAM so an oversized pool can't exceed the machine.
    /// <see cref="RenderCacheMode.Disabled"/> returns 0 -- the cache retains nothing (a render still holds
    /// the pixels it fetched, so this is safe, just non-reusing).</para>
    /// </summary>
    /// <param name="freeRamPercent">Collective share of free RAM (0-100) the caches may use in
    /// PercentFreeRam mode; also drives the ceiling. See <see cref="BaselineFreeRamPercent"/>.</param>
    /// <param name="currentCacheBytes">Bytes the caller's cache currently holds. Added back to free space
    /// because it is reclaimable; this keeps the budget stable as the cache fills (otherwise the target
    /// would chase a shrinking free figure and oscillate).</param>
    /// <param name="fraction">This cache's baseline share of free RAM (0.75 pixel / 0.09 mesh / 0.01 cubemap);
    /// its ratio among the caches is <c>fraction / BaselineFreeRamFraction</c>.</param>
    /// <param name="minBytes">Floor so a busy machine still caches something (capped to the ceiling).</param>
    public static long Compute(RenderCacheMode mode, long fixedPoolBytes, double freeRamPercent,
        long currentCacheBytes, double fraction, long minBytes)
    {
        if (mode == RenderCacheMode.Disabled)
            return 0;

        GCMemoryInfo info = GC.GetGCMemoryInfo();
        long total = info.TotalAvailableMemoryBytes; // physical RAM, or container limit

        // This cache's share of the collective budget, from its ratio among the default fractions. Holding
        // the ratio fixed lets one knob (the total percent) scale all three caches together.
        double cacheShare = fraction / BaselineFreeRamFraction;

        if (mode == RenderCacheMode.FixedRam)
        {
            long fixedTarget = (long)(Math.Max(0, fixedPoolBytes) * fraction);
            // No free-RAM floor (honour the user's number even on a busy machine), but never let a single
            // cache exceed its ratio share of total physical RAM.
            long fixedCeiling = total > 0 ? Math.Max(0L, (long)(total * cacheShare)) : long.MaxValue;
            return Math.Clamp(fixedTarget, 0, fixedCeiling);
        }

        // PercentFreeRam. Info unavailable (e.g. before the first GC): fall back to the floor.
        if (total <= 0)
            return minBytes;

        double coeff = cacheShare * (Math.Clamp(freeRamPercent, 0, 100) / 100.0); // this cache's effective fraction
        long load = info.MemoryLoadBytes;                                          // memory in use system-wide
        long free = Math.Max(0, total - load);
        long headroom = Math.Max(MinHeadroomBytes, (long)(total * HeadroomFraction));
        long reclaimable = Math.Max(0, free + currentCacheBytes - headroom);

        long target = (long)(reclaimable * coeff);
        long ceiling = (long)(total * coeff);       // percent drives the cap too -- single source of truth
        long floor = Math.Min(minBytes, ceiling);   // keep floor viable if a low percent puts the cap under it
        return Math.Clamp(target, floor, ceiling);
    }
}
