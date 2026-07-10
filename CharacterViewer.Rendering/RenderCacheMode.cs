using System.ComponentModel;

namespace CharacterViewer.Rendering;

/// <summary>
/// How the in-RAM decode caches (decoded DDS pixels, parsed NIF geometry) size their byte budget. Selected
/// by the host via <see cref="ICharacterViewerSettings.CacheMode"/>. The <see cref="DescriptionAttribute"/>
/// text is what a host UI shows in a mode dropdown.
/// </summary>
public enum RenderCacheMode
{
    /// <summary>Default. Each cache targets a fraction of live free system RAM (see
    /// <see cref="SystemMemoryBudget"/>) — grows into spare memory, shrinks under external pressure.</summary>
    [Description("% Free RAM")]
    PercentFreeRam = 0,

    /// <summary>Each cache targets a fraction of a fixed pool (<see cref="ICharacterViewerSettings.FixedCacheBudgetBytes"/>)
    /// instead of live free RAM — a predictable, machine-independent ceiling.</summary>
    [Description("Fixed RAM")]
    FixedRam = 1,

    /// <summary>Caching off (budget 0): renders decode fresh each time and retain essentially nothing.
    /// Useful for isolating memory growth that is <em>not</em> the decode caches.</summary>
    [Description("Disabled")]
    Disabled = 2,
}
