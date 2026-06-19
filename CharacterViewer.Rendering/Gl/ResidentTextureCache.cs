using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;

namespace CharacterViewer.Rendering;

/// <summary>
/// Render-context-owned LRU cache of uploaded GL textures, keyed on resolved
/// disk path and bounded by a VRAM byte budget. Lets the offscreen renderer
/// share textures across its many short-lived per-render VMs — NPC after NPC
/// references the same vanilla and mod-shared diffuse/normal/skin/cubemap files,
/// so they're uploaded to the GPU once instead of per render. The profiler
/// showed GL upload as the steady-state floor (~300 ms / 50-70% of a cached
/// render), almost all of it re-uploading shared textures; this removes it.
///
/// <para><b>Threading / context:</b> GL handles are context-specific, so one
/// instance belongs to exactly one GL context (the offscreen render thread's),
/// and every method must run on that thread. Since all GL calls are confined
/// there, no locking is needed. The live preview — its own context, one
/// long-lived VM — passes null and keeps per-VM texture ownership.</para>
///
/// <para><b>Low-VRAM safety:</b> the budget auto-sizes from a VRAM query when a
/// vendor extension is available, clamped to a conservative range, and falls
/// back to a small fixed budget otherwise — so a 4 GB card never tries to hoard
/// like a 24 GB one. <see cref="ReduceBudgetAfterOom"/> additionally shrinks the
/// budget if an upload ever reports GL_OUT_OF_MEMORY, so the cache degrades
/// gracefully instead of crashing.</para>
/// </summary>
public sealed class ResidentTextureCache : IDisposable
{
    private sealed class Entry
    {
        public int Handle;
        public long Bytes;
        public long Epoch;            // render pass this entry was last added/used in
        public LinkedListNode<string> LruNode = null!;
    }

    private readonly Dictionary<string, Entry> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new(); // most-recently-used at the front
    private readonly ICharacterViewerLogger? _logger;
    private long _budgetBytes;
    private long _currentBytes;
    private long _epoch;

    private const long MinBudgetBytes = 256L * 1024 * 1024;   // floor — still useful on 4 GB cards
    private const long MaxBudgetBytes = 2048L * 1024 * 1024;  // ceiling — a mod's working set fits well under this
    private const long FallbackBudgetBytes = 512L * 1024 * 1024; // when VRAM can't be queried

    public long BudgetBytes => _budgetBytes;
    public long CurrentBytes => _currentBytes;
    public int Count => _map.Count;

    /// <param name="overrideBudgetBytes">Host override; null = auto-size from VRAM.</param>
    public ResidentTextureCache(ICharacterViewerLogger? logger, long? overrideBudgetBytes = null)
    {
        _logger = logger;
        _budgetBytes = overrideBudgetBytes is > 0
            ? Math.Clamp(overrideBudgetBytes.Value, MinBudgetBytes, MaxBudgetBytes)
            : ComputeDefaultBudgetBytes(logger);
    }

    /// <summary>Marks the start of a render pass. Textures added or hit during
    /// this pass are protected from eviction until the next pass begins, so a
    /// render whose working set exceeds the budget can't delete a texture it's
    /// still using this frame (it goes transiently over budget instead; the next
    /// pass reclaims the now-old entries).</summary>
    public void BeginRenderPass() => _epoch++;

    /// <summary>Returns the cached GL handle for <paramref name="diskPath"/> and
    /// marks it most-recently-used (and used this pass), or -1 on a miss.</summary>
    public int TryGet(string? diskPath)
    {
        if (string.IsNullOrEmpty(diskPath)) return -1;
        if (_map.TryGetValue(diskPath, out var entry))
        {
            entry.Epoch = _epoch;
            _lru.Remove(entry.LruNode);
            _lru.AddFirst(entry.LruNode);
            return entry.Handle;
        }
        return -1;
    }

    /// <summary>Records a freshly-uploaded handle and evicts LRU entries until the
    /// budget is satisfied. No-op if the path is already present (first writer wins).</summary>
    public void Add(string? diskPath, int handle, long bytes)
    {
        if (string.IsNullOrEmpty(diskPath) || _map.ContainsKey(diskPath)) return;
        var node = _lru.AddFirst(diskPath);
        _map[diskPath] = new Entry { Handle = handle, Bytes = bytes, Epoch = _epoch, LruNode = node };
        _currentBytes += bytes;
        EvictToBudget();
    }

    /// <summary>Called by the texture manager when an upload reported
    /// GL_OUT_OF_MEMORY: halve the budget (down to the floor) and evict so we
    /// stop pushing past what the GPU actually has.</summary>
    public void ReduceBudgetAfterOom()
    {
        _budgetBytes = Math.Max(MinBudgetBytes, _budgetBytes / 2);
        EvictToBudget();
        _logger?.LogMessage(
            $"ResidentTextureCache: GL out-of-memory — budget reduced to {_budgetBytes / (1024 * 1024)} MB");
    }

    private void EvictToBudget()
    {
        while (_currentBytes > _budgetBytes && _lru.Last != null)
        {
            string oldestKey = _lru.Last.Value;
            var entry = _map[oldestKey];
            // Entries touched this render pass cluster at the LRU front; once the
            // tail is one of them, everything left is in use this frame — stop
            // rather than delete a texture the current render still references.
            if (entry.Epoch == _epoch) break;
            _lru.RemoveLast();
            _map.Remove(oldestKey);
            _currentBytes -= entry.Bytes;
            GL.DeleteTexture(entry.Handle);
        }
    }

    /// <summary>Deletes every cached texture. Call on environment rebuild (assets
    /// may have changed) or context teardown.</summary>
    public void Clear()
    {
        foreach (var entry in _map.Values)
            GL.DeleteTexture(entry.Handle);
        _map.Clear();
        _lru.Clear();
        _currentBytes = 0;
    }

    public void Dispose() => Clear();

    private static long ComputeDefaultBudgetBytes(ICharacterViewerLogger? logger)
    {
        long? freeKb = TryQueryFreeVramKb();
        if (freeKb == null)
        {
            logger?.LogMessage(
                $"ResidentTextureCache: VRAM query unavailable; using {FallbackBudgetBytes / (1024 * 1024)} MB texture budget");
            return FallbackBudgetBytes;
        }

        long budget = Math.Clamp((long)(freeKb.Value * 1024L * 0.33), MinBudgetBytes, MaxBudgetBytes);
        logger?.LogMessage(
            $"ResidentTextureCache: ~{freeKb.Value / 1024} MB VRAM free; texture budget {budget / (1024 * 1024)} MB");
        return budget;
    }

    /// <summary>Best-effort free-VRAM query (KB) via vendor extensions. Returns
    /// null when neither NVIDIA's GL_NVX_gpu_memory_info nor AMD's GL_ATI_meminfo
    /// is present (e.g. Intel), or any GL call errors. Must run with the context
    /// current.</summary>
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
        catch
        {
            // Query is purely advisory — any failure falls back to the fixed budget.
        }
        return null;
    }
}
