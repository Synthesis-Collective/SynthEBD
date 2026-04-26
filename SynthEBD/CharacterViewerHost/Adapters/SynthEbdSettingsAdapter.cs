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
}
