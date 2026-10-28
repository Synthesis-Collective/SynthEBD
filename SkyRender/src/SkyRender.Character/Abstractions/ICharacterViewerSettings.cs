using System.Collections.Generic;
using System.ComponentModel;

namespace SkyRender.Character;

/// <summary>
/// Settings consumed (and persisted via the host) by the CharacterViewer subsystem: the
/// character-viewer tier's lighting presets and render toggles, on top of the core
/// <see cref="IRenderSettings"/> (cache sizing) it extends.
/// The viewer reads these on construction and wires reactive subscriptions to the
/// implementor's <see cref="INotifyPropertyChanged"/> events so that toolbar
/// toggles and preset selections round-trip back to host-owned settings storage.
///
/// A host adapts its own settings behind this interface and supplies
/// their own implementation backed by their own settings file.
/// </summary>
public interface ICharacterViewerSettings : IRenderSettings, INotifyPropertyChanged
{
    /// <summary>Name of the active lighting layout preset (key/fill/rim positions).</summary>
    string CharacterViewerLightingLayout { get; set; }

    /// <summary>Name of the active lighting color scheme preset.</summary>
    string CharacterViewerLightingColorScheme { get; set; }

    /// <summary>User-saved lighting layouts. Mutating the underlying collection
    /// must raise <see cref="INotifyPropertyChanged.PropertyChanged"/> for the
    /// viewer to refresh its dropdown.</summary>
    IList<LightingLayout> UserLightingLayouts { get; }

    /// <summary>User-saved lighting color schemes. Same mutation contract as
    /// <see cref="UserLightingLayouts"/>.</summary>
    IList<LightingColorScheme> UserLightingColorSchemes { get; }

    /// <summary>Bidirectionally synced with <see cref="RenderLogGate.Verbose"/>.
    /// Toggling in the viewer toolbar writes back here so the choice persists across runs.</summary>
    bool CharacterViewerVerboseLog { get; set; }

    // CharacterViewer render-pipeline settings. The viewer seeds the matching
    // VM_CharacterViewer property from each of these at construction and writes back
    // on toolbar edit so the choice persists across runs. All are default interface
    // members (get returns the shared out-of-box default / set is a no-op) so existing
    // host adapters keep compiling unchanged; a host that persists these overrides them.
    // Getter defaults mirror a host's InternalMugshotSettings so a
    // non-persisting host still gets the same portrait look.

    /// <summary>Green-wireframe placeholder for shapes whose diffuse texture failed to load.</summary>
    bool CharacterViewerRenderMissingTextureAsWireframe { get => true; set { } }

    /// <summary>ACES tone-mapping toggle. Seeds <see cref="VM_CharacterViewer.EnableToneMapping"/>.</summary>
    bool CharacterViewerEnableToneMapping { get => true; set { } }

    /// <summary>Shadow-map toggle. Seeds <see cref="VM_CharacterViewer.EnableShadows"/>.</summary>
    bool CharacterViewerEnableShadows { get => true; set { } }

    // Hair-shadow "brow ridge" mitigations (Strategies A/B/C). B (soften)
    // ships ON — it de-warps the shadow edge and relieves the over-dark neck
    // under the jaw; A and C default OFF and stay available for comparison.

    /// <summary>Strategy A: exclude hair from the shadow caster set.
    /// Seeds <see cref="VM_CharacterViewer.ExcludeHairShadowCaster"/>.</summary>
    bool CharacterViewerExcludeHairShadowCaster { get => false; set { } }

    /// <summary>Strategy B (default ON): soft constant-bias hair shadow.
    /// Seeds <see cref="VM_CharacterViewer.SoftenShadowEdges"/>.</summary>
    bool CharacterViewerSoftenShadowEdges { get => true; set { } }

    /// <summary>Strategy B PCF kernel step (texels). Seeds
    /// <see cref="VM_CharacterViewer.ShadowPcfRadius"/>.</summary>
    float CharacterViewerShadowPcfRadius { get => 1.5f; set { } }

    /// <summary>Strategy C: tighten the light frustum. Seeds
    /// <see cref="VM_CharacterViewer.TightShadowFrustum"/>.</summary>
    bool CharacterViewerTightShadowFrustum { get => false; set { } }

    /// <summary>Strategy C light-frustum radius (world units). Seeds
    /// <see cref="VM_CharacterViewer.ShadowFrustumRadius"/>.</summary>
    float CharacterViewerShadowFrustumRadius { get => 100f; set { } }

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
}
