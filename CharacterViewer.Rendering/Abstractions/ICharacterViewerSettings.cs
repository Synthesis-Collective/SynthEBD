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
}
