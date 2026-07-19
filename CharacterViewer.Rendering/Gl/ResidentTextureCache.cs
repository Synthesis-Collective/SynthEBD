using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;

namespace CharacterViewer.Rendering;

/// <summary>
/// Render-context-owned cache of uploaded GL textures, keyed on resolved disk
/// path and bounded by a VRAM byte budget. Lets the offscreen renderer share
/// textures across its many short-lived per-render VMs — NPC after NPC
/// references the same vanilla and mod-shared diffuse/normal/skin/cubemap files,
/// so they upload to the GPU once instead of per render. The profiler showed GL
/// upload as the steady-state floor (~300 ms / 50-70% of a cached render); this
/// removes it.
///
/// <para><b>Threading / context:</b> GL handles are context-specific, so one
/// instance belongs to exactly one GL context (the offscreen render thread's),
/// and every method must run on that thread. All GL calls are confined there, so
/// no locking. The live preview — its own context, one long-lived VM — passes
/// null and keeps per-VM texture ownership.</para>
///
/// <para><b>Eviction (segmented LRU):</b> entries live in a probationary segment
/// until they're hit a second time, then graduate to a protected segment;
/// eviction drains probation first. So frequently-shared assets — vanilla skin /
/// eye cubemap / detail maps, and any popular mod hair/eyes — survive churn,
/// while one-shot unique head/face textures are reclaimed first. A render-epoch
/// additionally shields every texture used in the current render from eviction.</para>
///
/// <para><b>Sizing:</b> the budget tracks free VRAM dynamically — it grows to use
/// what's available (leaving headroom for the OS / other apps) and shrinks if
/// another process claims VRAM. Falls back to a fixed budget when no VRAM query
/// is available (e.g. Intel), and a GL_OUT_OF_MEMORY upload halves the budget so
/// the cache degrades gracefully rather than crashing.</para>
/// </summary>
public sealed class ResidentTextureCache : IDisposable
{
    private sealed class Entry
    {
        public string Key = "";
        public int Handle;
        public long Bytes;
        public long Epoch;          // render pass last added/used in
        public bool InProtected;    // which segment Node lives in
        public LinkedListNode<Entry> Node = null!;
    }

    private readonly Dictionary<string, Entry> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Entry> _protected = new();  // re-hit at least once; MRU at front
    private readonly LinkedList<Entry> _probation = new();  // single-use so far; MRU at front
    private readonly ICharacterViewerLogger? _logger;
    // Gate for the per-hit / per-add / per-evict diagnostic trace. Off by default;
    // the host's "Verbose Log" toggle (or a force-regen _Mugshot.txt) flips it on so
    // a poisoned re-render can be diffed against a post-restart render. The budget /
    // OOM lines below are NOT gated — they're low-volume and always useful.
    private readonly CharacterViewerLogGate? _logGate;

    private void LogVerbose(string message)
    {
        if (_logGate != null && _logGate.Verbose) _logger?.LogMessage(message);
    }

    private long _budgetBytes;
    private long _currentBytes;
    private long _protectedBytes;
    private long _epoch;
    // Upper bound the dynamic repoll won't climb back past after a GL_OUT_OF_MEMORY,
    // so a transient free-VRAM reading can't grow the budget back into the wall.
    private long _oomCeilingBytes = HardMaxBudgetBytes;

    // Dynamic-sizing state (queried once at construction).
    private readonly bool _vramQueryAvailable;
    private readonly long _totalVramBytes;       // 0 when unknown (ATI exposes free only)
    private readonly long? _overrideBudgetBytes; // host pin; disables dynamic polling
    private int _rendersSinceRepoll;

    private const long MinBudgetBytes = 256L * 1024 * 1024;
    private const long HardMaxBudgetBytes = 8L * 1024 * 1024 * 1024;
    private const long FallbackBudgetBytes = 512L * 1024 * 1024;
    private const long MinHeadroomBytes = 1024L * 1024 * 1024; // never let the cache eat the last GB
    private const double HeadroomFraction = 0.15;              // ...or 15% of total, whichever is larger
    private const double ProtectedFraction = 0.75;            // protected segment cap, as a fraction of budget
    private const int RepollEveryRenders = 16;

    public long BudgetBytes => _budgetBytes;
    public long CurrentBytes => _currentBytes;
    public int Count => _map.Count;

    /// <param name="overrideBudgetBytes">Host pin; null = auto-size and track VRAM.</param>
    /// <param name="logGate">Optional gate for the verbose per-hit/add/evict trace.</param>
    public ResidentTextureCache(ICharacterViewerLogger? logger, long? overrideBudgetBytes = null,
        CharacterViewerLogGate? logGate = null)
    {
        _logger = logger;
        _logGate = logGate;
        (_vramQueryAvailable, _totalVramBytes) = QueryVramCapabilities();

        if (overrideBudgetBytes is > 0)
        {
            _overrideBudgetBytes = Math.Clamp(overrideBudgetBytes.Value, MinBudgetBytes, HardMaxBudgetBytes);
            _budgetBytes = _overrideBudgetBytes.Value;
            _logger?.LogMessage($"ResidentTextureCache: host-pinned budget {_budgetBytes / (1024 * 1024)} MB");
        }
        else
        {
            _budgetBytes = ComputeDynamicBudget() ?? FallbackBudgetBytes;
            _logger?.LogMessage(
                $"ResidentTextureCache: initial budget {_budgetBytes / (1024 * 1024)} MB " +
                $"(VRAM query {(_vramQueryAvailable ? "available" : "unavailable — fixed fallback")})");
        }
    }

    /// <summary>Starts a render pass: bumps the epoch (shielding this render's
    /// textures from eviction) and periodically re-evaluates the budget against
    /// current free VRAM so the cache expands into spare memory and contracts
    /// when another process claims it.</summary>
    public void BeginRenderPass()
    {
        _epoch++;
        LogVerbose($"[ResidentTex] BeginRenderPass epoch={_epoch} " +
            $"budget={_budgetBytes / (1024 * 1024)}MB current={_currentBytes / (1024 * 1024)}MB " +
            $"count={_map.Count} (protected={_protected.Count}/probation={_probation.Count})");
        if (_overrideBudgetBytes == null && _vramQueryAvailable
            && ++_rendersSinceRepoll >= RepollEveryRenders)
        {
            _rendersSinceRepoll = 0;
            long? target = ComputeDynamicBudget();
            if (target.HasValue && target.Value != _budgetBytes)
            {
                _budgetBytes = target.Value;
                EvictToBudget();
            }
        }
    }

    /// <summary>Returns the cached handle for <paramref name="diskPath"/>, marking
    /// it used-this-pass and graduating it to the protected segment on its second
    /// hit. -1 on a miss.</summary>
    public int TryGet(string? diskPath)
    {
        if (string.IsNullOrEmpty(diskPath) || !_map.TryGetValue(diskPath, out var entry))
            return -1;

        entry.Epoch = _epoch;
        if (entry.InProtected)
        {
            _protected.Remove(entry.Node);
            entry.Node = _protected.AddFirst(entry);
        }
        else
        {
            // Second sighting → promote out of probation into protected.
            _probation.Remove(entry.Node);
            entry.InProtected = true;
            entry.Node = _protected.AddFirst(entry);
            _protectedBytes += entry.Bytes;
            EnforceProtectedCap();
        }
        LogVerbose($"[ResidentTex] HIT '{diskPath}' handle={entry.Handle} " +
            $"seg={(entry.InProtected ? "protected" : "probation")} epoch={_epoch}");
        return entry.Handle;
    }

    /// <summary>Records a freshly-uploaded handle in the probationary segment and
    /// evicts until within budget. No-op if the path is already present.</summary>
    public void Add(string? diskPath, int handle, long bytes)
    {
        if (string.IsNullOrEmpty(diskPath) || _map.ContainsKey(diskPath)) return;
        var entry = new Entry { Key = diskPath, Handle = handle, Bytes = bytes, Epoch = _epoch };
        entry.Node = _probation.AddFirst(entry);
        _map[diskPath] = entry;
        _currentBytes += bytes;
        LogVerbose($"[ResidentTex] ADD '{diskPath}' handle={handle} bytes={bytes / 1024}KB " +
            $"-> current={_currentBytes / (1024 * 1024)}MB/budget={_budgetBytes / (1024 * 1024)}MB count={_map.Count}");
        EvictToBudget();
    }

    /// <summary>Called by the texture manager when an upload reported
    /// GL_OUT_OF_MEMORY: halve the budget (down to the floor) and evict.</summary>
    public void ReduceBudgetAfterOom()
    {
        _budgetBytes = Math.Max(MinBudgetBytes, _budgetBytes / 2);
        _oomCeilingBytes = _budgetBytes; // don't let the dynamic repoll grow back past here
        EvictToBudget();
        _logger?.LogMessage(
            $"ResidentTextureCache: GL out-of-memory — budget reduced to {_budgetBytes / (1024 * 1024)} MB");
    }

    /// <summary>Deletes every cached texture. Call on environment rebuild (assets
    /// may have changed) or context teardown. Must run on the render thread.</summary>
    public void Clear()
    {
        foreach (var entry in _map.Values)
            GL.DeleteTexture(entry.Handle);
        _map.Clear();
        _protected.Clear();
        _probation.Clear();
        _currentBytes = 0;
        _protectedBytes = 0;
    }

    public void Dispose() => Clear();

    // Keeps the protected segment from starving probation of room for new
    // uploads: demote its oldest (non-in-use) entries back to probation, where
    // they become eligible for eviction.
    private void EnforceProtectedCap()
    {
        long cap = (long)(_budgetBytes * ProtectedFraction);
        var node = _protected.Last;
        while (_protectedBytes > cap && node != null)
        {
            var prev = node.Previous;
            var entry = node.Value;
            if (entry.Epoch != _epoch) // never demote-then-evict a texture used this render
            {
                _protected.Remove(node);
                _protectedBytes -= entry.Bytes;
                entry.InProtected = false;
                entry.Node = _probation.AddFirst(entry);
            }
            node = prev;
        }
    }

    private void EvictToBudget()
    {
        while (_currentBytes > _budgetBytes)
        {
            Entry? victim = PickVictim();
            if (victim == null) break; // everything left is in use this render — go transiently over budget
            (victim.InProtected ? _protected : _probation).Remove(victim.Node);
            _map.Remove(victim.Key);
            _currentBytes -= victim.Bytes;
            if (victim.InProtected) _protectedBytes -= victim.Bytes;
            GL.DeleteTexture(victim.Handle);
            LogVerbose($"[ResidentTex] EVICT '{victim.Key}' handle={victim.Handle} " +
                $"bytes={victim.Bytes / 1024}KB seg={(victim.InProtected ? "protected" : "probation")} " +
                $"-> current={_currentBytes / (1024 * 1024)}MB");
        }
    }

    // Prefer the oldest probationary (single-use) entry; only when probation has
    // nothing evictable fall back to the oldest protected entry. Skips entries
    // used this render in both segments.
    private static Entry? OldestEvictable(LinkedList<Entry> list, long epoch)
    {
        for (var node = list.Last; node != null; node = node.Previous)
            if (node.Value.Epoch != epoch) return node.Value;
        return null;
    }

    private Entry? PickVictim() =>
        OldestEvictable(_probation, _epoch) ?? OldestEvictable(_protected, _epoch);

    // ----- VRAM sizing -----

    /// <summary>Budget = current usage + (free VRAM − headroom), clamped. So the
    /// cache grows to fill spare VRAM while keeping headroom free, and shrinks if
    /// free VRAM drops (another app allocated). Returns null when unqueryable.</summary>
    private long? ComputeDynamicBudget()
    {
        long? freeKb = TryQueryFreeVramKb();
        if (freeKb == null) return null;

        long freeBytes = freeKb.Value * 1024L;
        long headroom = _totalVramBytes > 0
            ? Math.Max(MinHeadroomBytes, (long)(_totalVramBytes * HeadroomFraction))
            : MinHeadroomBytes;
        long target = _currentBytes + (freeBytes - headroom);
        return Math.Clamp(target, MinBudgetBytes, Math.Min(HardMaxBudgetBytes, _oomCeilingBytes));
    }

    private static (bool available, long totalBytes) QueryVramCapabilities()
    {
        try
        {
            bool hasNvx = false, hasAti = false;
            int extCount = GL.GetInteger(GetPName.NumExtensions);
            for (int i = 0; i < extCount; i++)
            {
                string ext = GL.GetString(StringNameIndexed.Extensions, i);
                if (ext == "GL_NVX_gpu_memory_info") hasNvx = true;
                else if (ext == "GL_ATI_meminfo") hasAti = true;
            }

            if (hasNvx)
            {
                const int GPU_MEMORY_INFO_DEDICATED_VIDMEM_NVX = 0x9047;
                int totalKb = GL.GetInteger((GetPName)GPU_MEMORY_INFO_DEDICATED_VIDMEM_NVX);
                long total = GL.GetError() == ErrorCode.NoError && totalKb > 0 ? totalKb * 1024L : 0;
                return (true, total);
            }
            if (hasAti) return (true, 0); // ATI_meminfo exposes free only, not total
        }
        catch { /* best-effort */ }
        return (false, 0);
    }

    /// <summary>Best-effort current-free-VRAM query (KB) via vendor extensions;
    /// null when neither is present or a GL call errors. Context must be current.</summary>
    private static long? TryQueryFreeVramKb()
    {
        try
        {
            bool hasNvx = false, hasAti = false;
            int extCount = GL.GetInteger(GetPName.NumExtensions);
            for (int i = 0; i < extCount; i++)
            {
                string ext = GL.GetString(StringNameIndexed.Extensions, i);
                if (ext == "GL_NVX_gpu_memory_info") hasNvx = true;
                else if (ext == "GL_ATI_meminfo") hasAti = true;
            }

            if (hasNvx)
            {
                const int GPU_MEMORY_INFO_CURRENT_AVAILABLE_VIDMEM_NVX = 0x9049;
                int kb = GL.GetInteger((GetPName)GPU_MEMORY_INFO_CURRENT_AVAILABLE_VIDMEM_NVX);
                if (GL.GetError() == ErrorCode.NoError && kb > 0) return kb;
            }
            if (hasAti)
            {
                const int TEXTURE_FREE_MEMORY_ATI = 0x87FC;
                int[] vals = new int[4];
                GL.GetInteger((GetPName)TEXTURE_FREE_MEMORY_ATI, vals); // vals[0] = total free, KB
                if (GL.GetError() == ErrorCode.NoError && vals[0] > 0) return vals[0];
            }
        }
        catch { /* best-effort */ }
        return null;
    }
}
