using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>
/// Multi-plugin counterpart of <see cref="UC_ModKeyPicker"/> — SynthEBD's native replacement for
/// Mutagen.Bethesda.WPF's <c>ModKeyMultiPicker</c> with the same binding contract
/// (<see cref="ModKeys"/>, <see cref="SearchableMods"/>). Selecting a plugin in the embedded entry
/// picker appends it to <see cref="ModKeys"/>; the list below shows each member's filename with a
/// remove button.
/// </summary>
public partial class UC_ModKeyMultiPicker : UserControl
{
    private bool _suppressEntryHandling = false;

    public UC_ModKeyMultiPicker()
    {
        InitializeComponent();

        // The embedded picker acts as an "add" field: committing a plugin appends it and resets the
        // field for the next entry.
        var modKeyDescriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
            UC_ModKeyPicker.ModKeyProperty, typeof(UC_ModKeyPicker));
        modKeyDescriptor.AddValueChanged(EntryPicker, (_, _) =>
        {
            if (_suppressEntryHandling || EntryPicker.ModKey.IsNull)
            {
                return;
            }
            var added = EntryPicker.ModKey;
            if (ModKeys != null && !ModKeys.Contains(added))
            {
                ModKeys.Add(added);
            }
            _suppressEntryHandling = true;
            EntryPicker.ModKey = ModKey.Null;
            _suppressEntryHandling = false;
        });
    }

    public static readonly DependencyProperty ModKeysProperty = DependencyProperty.Register(
        nameof(ModKeys), typeof(ObservableCollection<ModKey>), typeof(UC_ModKeyMultiPicker),
        new PropertyMetadata(null));

    public ObservableCollection<ModKey> ModKeys
    {
        get => (ObservableCollection<ModKey>)GetValue(ModKeysProperty);
        set => SetValue(ModKeysProperty, value);
    }

    // Passed straight through to the embedded UC_ModKeyPicker, so it accepts the same shapes
    // (IEnumerable&lt;ModKey&gt;, IEnumerable&lt;IModListingGetter&gt;, ILoadOrderGetter).
    public static readonly DependencyProperty SearchableModsProperty = DependencyProperty.Register(
        nameof(SearchableMods), typeof(object), typeof(UC_ModKeyMultiPicker),
        new PropertyMetadata(null));

    public object SearchableMods
    {
        get => GetValue(SearchableModsProperty);
        set => SetValue(SearchableModsProperty, value);
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ModKey entry)
        {
            ModKeys?.Remove(entry);
        }
    }
}
