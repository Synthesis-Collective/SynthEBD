using System.Collections.Generic;
using System.ComponentModel;

namespace CharacterViewer.Rendering;

/// <summary>
/// Settings consumed (and persisted via the host) by the CharacterViewer subsystem.
/// The viewer reads these on construction and wires reactive subscriptions to the
/// implementor's <see cref="INotifyPropertyChanged"/> events so that toolbar
/// toggles and preset selections round-trip back to host-owned settings storage.
///
/// SynthEBD adapts its <see cref="VM_Settings_General"/> behind this interface
/// (see <see cref="SynthEbdSettingsAdapter"/>); other host applications supply
/// their own implementation backed by their own settings file.
/// </summary>
public interface ICharacterViewerSettings : INotifyPropertyChanged
{
    /// <summary>Name of the active lighting layout preset (key/fill/rim positions).</summary>
    string CharacterViewerLightingLayout { get; set; }

    /// <summary>Name of the active lighting color scheme preset.</summary>
    string CharacterViewerLightingColorScheme { get; set; }

    /// <summary>User-saved lighting layouts. Mutating the underlying collection
    /// must raise <see cref="INotifyPropertyChanged.PropertyChanged"/> for the
    /// viewer to refresh its dropdown.</summary>
    IList<CharacterViewerLightingLayout> UserLightingLayouts { get; }

    /// <summary>User-saved lighting color schemes. Same mutation contract as
    /// <see cref="UserLightingLayouts"/>.</summary>
    IList<CharacterViewerLightingColorScheme> UserLightingColorSchemes { get; }

    /// <summary>Bidirectionally synced with <see cref="CharacterViewerLogGate.Verbose"/>.
    /// Toggling in the viewer toolbar writes back here so the choice persists across runs.</summary>
    bool CharacterViewerVerboseLog { get; set; }

    // CharacterViewer render-pipeline settings. The viewer seeds the matching
    // VM_CharacterViewer property from each of these at construction and writes back
    // on toolbar edit so the choice persists across runs. All are default interface
    // members (get returns the shared out-of-box default / set is a no-op) so existing
    // host adapters keep compiling unchanged; a host that persists these overrides them.
    // Getter defaults mirror NPC Plugin Chooser 2's InternalMugshotSettings so a
    // non-persisting host still gets the same portrait look.

    /// <summary>Green-wireframe placeholder for shapes whose diffuse texture failed to load.</summary>
    bool CharacterViewerRenderMissingTextureAsWireframe { get => true; set { } }

    /// <summary>ACES tone-mapping toggle. Seeds <see cref="VM_CharacterViewer.EnableToneMapping"/>.</summary>
    bool CharacterViewerEnableToneMapping { get => true; set { } }

    /// <summary>Shadow-map toggle. Seeds <see cref="VM_CharacterViewer.EnableShadows"/>.</summary>
    bool CharacterViewerEnableShadows { get => true; set { } }

    /// <summary>SSAO toggle. Seeds <see cref="VM_CharacterViewer.EnableAmbientOcclusion"/>.</summary>
    bool CharacterViewerEnableAmbientOcclusion { get => true; set { } }

    /// <summary>SSAO sample radius (world units). Seeds <see cref="VM_CharacterViewer.SsaoRadius"/>.</summary>
    float CharacterViewerSsaoRadius { get => 4.0f; set { } }

    /// <summary>SSAO depth bias (world units). Seeds <see cref="VM_CharacterViewer.SsaoBias"/>.</summary>
    float CharacterViewerSsaoBias { get => 0.05f; set { } }

    /// <summary>SSAO power-curve exponent. Seeds <see cref="VM_CharacterViewer.SsaoIntensity"/>.</summary>
    float CharacterViewerSsaoIntensity { get => 1.5f; set { } }

    /// <summary>SSAO occluder-thickness rejection (view-space units). Seeds <see cref="VM_CharacterViewer.SsaoThickness"/>.</summary>
    float CharacterViewerSsaoThickness { get => 1.5f; set { } }

    /// <summary>SSAO hair-to-background gap. Seeds <see cref="VM_CharacterViewer.SsaoHairGap"/>.</summary>
    float CharacterViewerSsaoHairGap { get => 0.8f; set { } }

    /// <summary>Eye catch-light toggle. Seeds <see cref="VM_CharacterViewer.EnableEyeCatchlight"/>.</summary>
    bool CharacterViewerEnableEyeCatchlight { get => true; set { } }

    /// <summary>Subsurface scattering strength. Seeds <see cref="VM_CharacterViewer.SubsurfaceStrength"/>.</summary>
    float CharacterViewerSubsurfaceStrength { get => 1.0f; set { } }

    /// <summary>Vignette inner radius (NDC). Seeds <see cref="VM_CharacterViewer.VignetteRadius"/>.</summary>
    float CharacterViewerVignetteRadius { get => 0.7f; set { } }

    /// <summary>Vignette darkening strength. Seeds <see cref="VM_CharacterViewer.VignetteIntensity"/>.</summary>
    float CharacterViewerVignetteIntensity { get => 0.3f; set { } }

    /// <summary>Skin-only saturation multiplier. Seeds <see cref="VM_CharacterViewer.SkinSaturationBoost"/>.</summary>
    float CharacterViewerSkinSaturationBoost { get => 1.0f; set { } }

    /// <summary>Tone-map exposure multiplier. Seeds <see cref="VM_CharacterViewer.Exposure"/>.</summary>
    float CharacterViewerExposure { get => 1.0f; set { } }

    /// <summary>Hair-relief finishing toggle. Seeds <see cref="VM_CharacterViewer.TonemapHairRelief"/>.</summary>
    bool CharacterViewerTonemapHairRelief { get => true; set { } }

    /// <summary>Neutral-white-tint hair albedo compensation strength (default 1.0).
    /// Seeds <see cref="VM_CharacterViewer.HairAlbedoCompensate"/>.</summary>
    float CharacterViewerHairAlbedoCompensate { get => 1.0f; set { } }

    /// <summary>Daylight directional-light boost toggle. Seeds <see cref="VM_CharacterViewer.DaylightBoost"/>.</summary>
    bool CharacterViewerDaylightBoost { get => true; set { } }

    /// <summary>Daylight boost gain. Seeds <see cref="VM_CharacterViewer.DaylightBoostIntensity"/>.</summary>
    float CharacterViewerDaylightBoostIntensity { get => 1.1f; set { } }

    /// <summary>Bloom glow toggle. Seeds <see cref="VM_CharacterViewer.EnableBloom"/>.</summary>
    bool CharacterViewerEnableBloom { get => true; set { } }

    /// <summary>Bloom composite gain. Seeds <see cref="VM_CharacterViewer.BloomIntensity"/>.</summary>
    float CharacterViewerBloomIntensity { get => 0.7f; set { } }

    /// <summary>How the in-RAM decode caches size their budget. Default (and the historical behaviour) is
    /// <see cref="RenderCacheMode.PercentFreeRam"/>. Implemented as a default member so existing host
    /// adapters keep the current behaviour without changes; hosts that expose a cache-mode setting override
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
