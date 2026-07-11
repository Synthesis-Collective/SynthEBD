using System;
using System.IO;

namespace CharacterViewer.Rendering;

/// <summary>
/// Opt-in utilization diagnostics for the three byte-budgeted in-RAM decode caches
/// (decoded DDS pixels / parsed NIF meshes / decoded cubemaps — see
/// <see cref="SystemMemoryBudget"/> for how their budgets keep a fixed 75:9:1
/// ratio). Each cache reports its inserts and evictions here; the diag tracks the
/// running peak of resident bytes per cache plus cumulative inserted / evicted
/// bytes, so a measurement run can compare the caches' EMPIRICAL working-set
/// ratio against the calibrated 75:9:1 split — and tell whether a cache's peak
/// was budget-clamped (evictions fired, so the peak understates natural demand).
///
/// <para>Off by default and zero-cost when off (a single static bool check on the
/// insert path). Enable by dropping a <c>LogCacheBudgetDiag.txt</c> file next to
/// the exe (the same trigger-file convention as <c>LogNifCacheDiag.txt</c>) — a
/// summary line for all three caches is appended to
/// <c>RenderLogs/CacheBudgetDiag.log</c> every <see cref="LogEveryInserts"/>
/// inserts. A profiling host can instead set <see cref="Enabled"/> = true
/// programmatically and read the <see cref="CacheStats"/> counters directly.</para>
/// </summary>
public static class CacheBudgetDiag
{
    /// <summary>Latched from the trigger file at type load; a host may also set it
    /// programmatically (tests). When false, Report* calls return immediately and
    /// no counters are touched.</summary>
    public static bool Enabled { get; set; } =
        File.Exists(Path.Combine(AppContext.BaseDirectory, "LogCacheBudgetDiag.txt"));

    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "RenderLogs", "CacheBudgetDiag.log");

    // Leaf lock: taken only inside Report* while the caller already holds its own
    // cache lock; the diag never calls back into a cache, so no ordering hazard.
    private static readonly object Lock = new();

    private const int LogEveryInserts = 32;
    private static int _insertsSinceLog;

    /// <summary>Running utilization counters for one cache. Written under the diag
    /// lock; reads are diagnostic-grade (torn reads across fields are acceptable).</summary>
    public sealed class CacheStats
    {
        public string Name { get; }
        internal CacheStats(string name) => Name = name;

        /// <summary>Resident bytes as of the last report.</summary>
        public long CurrentBytes { get; internal set; }
        /// <summary>Byte budget as of the last report.</summary>
        public long BudgetBytes { get; internal set; }
        /// <summary>Highest resident-byte figure ever reported (pre-eviction, so it
        /// captures the demand spike that triggered an eviction).</summary>
        public long PeakBytes { get; internal set; }
        /// <summary>Total bytes ever inserted. For a single-pass workload with few
        /// re-inserts this approximates the cache's unclamped cumulative demand.</summary>
        public long InsertedBytes { get; internal set; }
        public int InsertCount { get; internal set; }
        /// <summary>Total bytes evicted (any cause). Nonzero means <see cref="PeakBytes"/>
        /// understates the natural working set.</summary>
        public long EvictedBytes { get; internal set; }
        public int EvictionCount { get; internal set; }
        /// <summary>Evictions forced by an entry-count cap rather than the byte budget
        /// (only the parsed-NIF cache has one — see <c>NifMeshBuilder.CacheMaxEntries</c>).</summary>
        public int CountCapEvictionCount { get; internal set; }
        /// <summary>True if resident bytes ever exceeded the byte budget — i.e. the
        /// budget, not natural demand, bounded this cache at least once.</summary>
        public bool EverBudgetClamped { get; internal set; }
    }

    public static CacheStats Pixel { get; } = new("pixel");
    public static CacheStats Mesh { get; } = new("mesh");
    public static CacheStats Cubemap { get; } = new("cubemap");

    /// <summary>Record an insert: <paramref name="residentBytes"/> is the cache's
    /// resident total AFTER adding <paramref name="addedBytes"/> and BEFORE any
    /// eviction pass, so the peak reflects demand rather than the post-evict floor.</summary>
    public static void ReportInsert(CacheStats cache, long addedBytes, long residentBytes, long budgetBytes)
    {
        if (!Enabled) return;
        lock (Lock)
        {
            cache.CurrentBytes = residentBytes;
            cache.BudgetBytes = budgetBytes;
            cache.InsertedBytes += addedBytes;
            cache.InsertCount++;
            if (residentBytes > cache.PeakBytes) cache.PeakBytes = residentBytes;
            if (budgetBytes > 0 && residentBytes > budgetBytes) cache.EverBudgetClamped = true;

            if (++_insertsSinceLog >= LogEveryInserts)
            {
                _insertsSinceLog = 0;
                WriteLine(SummaryLineLocked());
            }
        }
    }

    /// <summary>Record one evicted entry. <paramref name="residentBytes"/> is the
    /// resident total after removal; <paramref name="byCountCap"/> marks an eviction
    /// forced by an entry-count cap rather than the byte budget.</summary>
    public static void ReportEviction(CacheStats cache, long evictedBytes, long residentBytes, bool byCountCap = false)
    {
        if (!Enabled) return;
        lock (Lock)
        {
            cache.CurrentBytes = residentBytes;
            cache.EvictedBytes += evictedBytes;
            cache.EvictionCount++;
            if (byCountCap) cache.CountCapEvictionCount++;
            else cache.EverBudgetClamped = true;
        }
    }

    /// <summary>Record a full cache clear (env rebuild). Peaks and cumulative
    /// counters are preserved — only the resident figure resets.</summary>
    public static void ReportClear(CacheStats cache)
    {
        if (!Enabled) return;
        lock (Lock)
        {
            cache.CurrentBytes = 0;
            WriteLine($"--- {cache.Name} cache cleared ---");
        }
    }

    /// <summary>One-line snapshot of all three caches — the same format as the
    /// periodic log line. Callable by a measurement host for per-step logging.</summary>
    public static string SummaryLine()
    {
        lock (Lock) return SummaryLineLocked();
    }

    private static string SummaryLineLocked() =>
        $"{Format(Pixel)} | {Format(Mesh)} | {Format(Cubemap)}";

    private static string Format(CacheStats c)
    {
        static double Mb(long b) => b / (1024.0 * 1024.0);
        string evict = c.EvictionCount == 0
            ? "evict=0"
            : $"evict={c.EvictionCount}({Mb(c.EvictedBytes):F0}MB" +
              (c.CountCapEvictionCount > 0 ? $", countCap:{c.CountCapEvictionCount}" : "") + ")";
        return $"{c.Name} cur={Mb(c.CurrentBytes):F1}MB peak={Mb(c.PeakBytes):F1}MB " +
               $"budget={Mb(c.BudgetBytes):F0}MB ins={c.InsertCount}({Mb(c.InsertedBytes):F0}MB) {evict}" +
               (c.EverBudgetClamped ? " CLAMPED" : "");
    }

    /// <summary>Zero every counter on all three caches (test isolation between runs).</summary>
    public static void Reset()
    {
        lock (Lock)
        {
            foreach (var c in new[] { Pixel, Mesh, Cubemap })
            {
                c.CurrentBytes = 0; c.BudgetBytes = 0; c.PeakBytes = 0;
                c.InsertedBytes = 0; c.InsertCount = 0;
                c.EvictedBytes = 0; c.EvictionCount = 0; c.CountCapEvictionCount = 0;
                c.EverBudgetClamped = false;
            }
            _insertsSinceLog = 0;
        }
    }

    private static void WriteLine(string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff ") + line + "\n");
        }
        catch { /* diagnostics must never disrupt a render */ }
    }
}
