using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;

namespace SynthEBD;

/// <summary>
/// Adapts SynthEBD's <see cref="VM_Settings_General"/> to
/// <see cref="ICharacterViewerSettings"/>. PropertyChanged events forward
/// transparently so the viewer's reactive subscriptions
/// (e.g. <c>WhenAnyValue(s =&gt; s.CharacterViewerVerboseLog)</c>) still fire
/// on the underlying VM.
/// </summary>
public sealed class SynthEbdSettingsAdapter : ICharacterViewerSettings
{
    private readonly VM_Settings_General _inner;

    public SynthEbdSettingsAdapter(VM_Settings_General inner)
    {
        _inner = inner;
        _inner.PropertyChanged += OnInnerPropertyChanged;
    }

    private void OnInnerPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        PropertyChanged?.Invoke(this, e);

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CharacterViewerLightingLayout
    {
        get => _inner.CharacterViewerLightingLayout;
        set => _inner.CharacterViewerLightingLayout = value;
    }

    public string CharacterViewerLightingColorScheme
    {
        get => _inner.CharacterViewerLightingColorScheme;
        set => _inner.CharacterViewerLightingColorScheme = value;
    }

    // ObservableCollection<T> implements IList<T>, so the viewer can mutate the
    // underlying collection directly — adds/removes propagate to the WPF UI
    // through the inner ObservableCollection's CollectionChanged events as
    // before, and the JSON-serialized round-trip in CopyInFromModel/DumpToModel
    // sees the mutations on save.
    public IList<CharacterViewerLightingLayout> UserLightingLayouts => _inner.UserLightingLayouts;
    public IList<CharacterViewerLightingColorScheme> UserLightingColorSchemes => _inner.UserLightingColorSchemes;

    public bool CharacterViewerVerboseLog
    {
        get => _inner.CharacterViewerVerboseLog;
        set => _inner.CharacterViewerVerboseLog = value;
    }

    // CharacterViewer render-pipeline settings — forward each to the underlying
    // VM_Settings_General property so viewer edits round-trip to persisted JSON.
    public bool CharacterViewerRenderMissingTextureAsWireframe
    {
        get => _inner.CharacterViewerRenderMissingTextureAsWireframe;
        set => _inner.CharacterViewerRenderMissingTextureAsWireframe = value;
    }

    public bool CharacterViewerEnableToneMapping
    {
        get => _inner.CharacterViewerEnableToneMapping;
        set => _inner.CharacterViewerEnableToneMapping = value;
    }

    public bool CharacterViewerEnableShadows
    {
        get => _inner.CharacterViewerEnableShadows;
        set => _inner.CharacterViewerEnableShadows = value;
    }

    public bool CharacterViewerEnableAmbientOcclusion
    {
        get => _inner.CharacterViewerEnableAmbientOcclusion;
        set => _inner.CharacterViewerEnableAmbientOcclusion = value;
    }

    public float CharacterViewerSsaoRadius
    {
        get => _inner.CharacterViewerSsaoRadius;
        set => _inner.CharacterViewerSsaoRadius = value;
    }

    public float CharacterViewerSsaoBias
    {
        get => _inner.CharacterViewerSsaoBias;
        set => _inner.CharacterViewerSsaoBias = value;
    }

    public float CharacterViewerSsaoIntensity
    {
        get => _inner.CharacterViewerSsaoIntensity;
        set => _inner.CharacterViewerSsaoIntensity = value;
    }

    public float CharacterViewerSsaoThickness
    {
        get => _inner.CharacterViewerSsaoThickness;
        set => _inner.CharacterViewerSsaoThickness = value;
    }

    public float CharacterViewerSsaoHairGap
    {
        get => _inner.CharacterViewerSsaoHairGap;
        set => _inner.CharacterViewerSsaoHairGap = value;
    }

    public bool CharacterViewerEnableEyeCatchlight
    {
        get => _inner.CharacterViewerEnableEyeCatchlight;
        set => _inner.CharacterViewerEnableEyeCatchlight = value;
    }

    public float CharacterViewerSubsurfaceStrength
    {
        get => _inner.CharacterViewerSubsurfaceStrength;
        set => _inner.CharacterViewerSubsurfaceStrength = value;
    }

    public float CharacterViewerVignetteRadius
    {
        get => _inner.CharacterViewerVignetteRadius;
        set => _inner.CharacterViewerVignetteRadius = value;
    }

    public float CharacterViewerVignetteIntensity
    {
        get => _inner.CharacterViewerVignetteIntensity;
        set => _inner.CharacterViewerVignetteIntensity = value;
    }

    public float CharacterViewerSkinSaturationBoost
    {
        get => _inner.CharacterViewerSkinSaturationBoost;
        set => _inner.CharacterViewerSkinSaturationBoost = value;
    }

    public float CharacterViewerExposure
    {
        get => _inner.CharacterViewerExposure;
        set => _inner.CharacterViewerExposure = value;
    }

    public bool CharacterViewerTonemapHairRelief
    {
        get => _inner.CharacterViewerTonemapHairRelief;
        set => _inner.CharacterViewerTonemapHairRelief = value;
    }

    public bool CharacterViewerDaylightBoost
    {
        get => _inner.CharacterViewerDaylightBoost;
        set => _inner.CharacterViewerDaylightBoost = value;
    }

    public float CharacterViewerDaylightBoostIntensity
    {
        get => _inner.CharacterViewerDaylightBoostIntensity;
        set => _inner.CharacterViewerDaylightBoostIntensity = value;
    }

    public bool CharacterViewerEnableBloom
    {
        get => _inner.CharacterViewerEnableBloom;
        set => _inner.CharacterViewerEnableBloom = value;
    }

    public float CharacterViewerBloomIntensity
    {
        get => _inner.CharacterViewerBloomIntensity;
        set => _inner.CharacterViewerBloomIntensity = value;
    }

    public RenderCacheMode CacheMode
    {
        get => _inner.CacheMode;
        set => _inner.CacheMode = value;
    }

    // The renderer wants bytes; the VM stores GB. Clamp negatives to 0 so a
    // stray value can't be read as a huge unsigned budget.
    public long FixedCacheBudgetBytes =>
        (long)(Math.Max(0, _inner.CacheFixedBudgetGB) * 1024L * 1024L * 1024L);

    public double FreeRamCachePercent => Math.Clamp(_inner.CacheFreeRamPercent, 0, 100);
}
