namespace SkyRender;

/// <summary>
/// Settings the core rendering services read (<see cref="AssetCache"/>, <see cref="NifMeshBuilder"/>):
/// how the in-RAM decode caches size their budgets. Every member has a default, so a minimal host
/// implements nothing and passes an empty implementation (or null where accepted). Read live at each
/// periodic budget re-poll, so an implementation may return changing values.
/// </summary>
public interface IRenderSettings
{
    /// <summary>How the in-RAM decode caches size their budget. Default (and the historical behaviour) is
    /// <see cref="RenderCacheMode.PercentFreeRam"/>. A default member, so a host that doesn't size caches implements nothing; hosts that expose a cache-mode setting override
    /// it. The caches read this live at each periodic budget re-poll, so a change takes effect within a few
    /// renders without a restart.</summary>
    RenderCacheMode CacheMode => RenderCacheMode.PercentFreeRam;

    /// <summary>The fixed cache pool in bytes, used only when <see cref="CacheMode"/> is
    /// <see cref="RenderCacheMode.FixedRam"/>. It is the notional total shared across the decode caches
    /// (each takes its usual fraction of it). Ignored in the other modes.</summary>
    long FixedCacheBudgetBytes => 0;

    /// <summary>Total share of free RAM (0-100) the decode caches may collectively use when
    /// <see cref="CacheMode"/> is <see cref="RenderCacheMode.PercentFreeRam"/>. At the default 85 each cache
    /// gets its calibrated fixed fraction (0.75 pixel + 0.09 mesh + 0.01 cubemap = 0.85); the caches keep
    /// that 75:9:1 ratio and this one value scales all of them together. It also sets their upper cap (the
    /// same share applied to total RAM), so it is the single knob governing % Free RAM sizing -- there is no
    /// separate per-cache ceiling. Implemented as a default member so existing host adapters keep the default
    /// behaviour without changes. Read live at each periodic budget re-poll.</summary>
    double FreeRamCachePercent => 85.0;
}
