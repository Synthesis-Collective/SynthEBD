using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;

namespace SynthEBD;

/// <summary>
/// Single-plugin ModKey picker — SynthEBD's native replacement for Mutagen.Bethesda.WPF's
/// <c>ModKeyPicker</c>, with the same binding contract (<see cref="ModKey"/> two-way,
/// <see cref="SearchableMods"/>) so migration is a drop-in element swap. It was introduced because
/// the Mutagen picker's template merges Noggog's dark theme dictionary into its own
/// <c>ControlTemplate.Resources</c>, which shadows SynthEBD's theme brushes and left the picker
/// dark on light themes. Suggestions are the searchable mods filtered by culture-aware filename
/// matching (Turkish translations must match correctly); a typed filename with a valid plugin
/// extension is accepted directly even when it is not in the list (parity with the Mutagen picker).
/// </summary>
public partial class UC_ModKeyPicker : UserControl
{
    private const int MaxSuggestions = 500;

    private bool _suppressSearchHandling = false;

    public UC_ModKeyPicker()
    {
        InitializeComponent();
        SuggestionList.ItemsSource = Suggestions;
        RefreshDisplayFromModKey();
    }

    public ObservableCollection<ModKey> Suggestions { get; } = new();

    public static readonly DependencyProperty ModKeyProperty = DependencyProperty.Register(
        nameof(ModKey), typeof(ModKey), typeof(UC_ModKeyPicker),
        new FrameworkPropertyMetadata(default(ModKey), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((UC_ModKeyPicker)d).RefreshDisplayFromModKey()));

    public ModKey ModKey
    {
        get => (ModKey)GetValue(ModKeyProperty);
        set => SetValue(ModKeyProperty, value);
    }

    // Typed as object (not IEnumerable&lt;ModKey&gt;) to match Mutagen's ModKeyPicker: callers bind
    // it to different shapes - IEnumerable&lt;ModKey&gt;, IEnumerable&lt;IModListingGetter&gt;, or an
    // ILoadOrderGetter (e.g. VM_BlockedPlugin/VM_NPCAttribute). GetSearchableModKeys normalizes them.
    public static readonly DependencyProperty SearchableModsProperty = DependencyProperty.Register(
        nameof(SearchableMods), typeof(object), typeof(UC_ModKeyPicker),
        new PropertyMetadata(null));

    public object SearchableMods
    {
        get => GetValue(SearchableModsProperty);
        set => SetValue(SearchableModsProperty, value);
    }

    private IEnumerable<ModKey> GetSearchableModKeys() => ExtractModKeys(SearchableMods);

    /// <summary>Normalizes the loosely-typed <see cref="SearchableMods"/> into a ModKey sequence,
    /// mirroring the shapes Mutagen's ModKeyPicker accepts. Callers bind it to any of these:
    /// an <see cref="ILoadOrderGetter"/> (VM_BlockedPlugin/VM_NPCAttribute/VM_Settings_General),
    /// an <see cref="IEnumerable{ModKey}"/> (VM_HeadPartImport/VM_SettingsTexMesh/VM_AssetPackMiscMenu),
    /// or an <see cref="IEnumerable{IModListingGetter}"/>.</summary>
    public static IEnumerable<ModKey> ExtractModKeys(object? searchableMods)
    {
        return searchableMods switch
        {
            ILoadOrderGetter loadOrder => loadOrder.ListedOrder, // non-generic ListedOrder is IEnumerable<ModKey>
            IEnumerable<ModKey> modKeys => modKeys,
            IEnumerable<IModListingGetter> listings => listings.Select(l => l.ModKey),
            _ => Enumerable.Empty<ModKey>(),
        };
    }

    /// <summary>Shows the current ModKey's filename in the search box (without opening the popup).</summary>
    private void RefreshDisplayFromModKey()
    {
        _suppressSearchHandling = true;
        try
        {
            SearchBox.Text = ModKey.IsNull ? "" : ModKey.ToString();
        }
        finally
        {
            _suppressSearchHandling = false;
        }
    }

    private void RefreshSuggestions()
    {
        var searchText = SearchBox.Text?.Trim() ?? "";
        // When the box still shows the current selection (e.g. just focused), treat it as no search
        // so the full list drops down for browsing - the user replaces it by typing to filter.
        if (!ModKey.IsNull && string.Equals(searchText, ModKey.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            searchText = "";
        }
        Suggestions.Clear();
        IEnumerable<ModKey> matches = GetSearchableModKeys();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            matches = matches.Where(m => Matches(m, searchText));
        }
        foreach (var match in matches.OrderBy(m => m.ToString(), StringComparer.CurrentCultureIgnoreCase).Take(MaxSuggestions))
        {
            Suggestions.Add(match);
        }
        SuggestionPopup.IsOpen = Suggestions.Any() && SearchBox.IsKeyboardFocusWithin;
    }

    /// <summary>Culture-aware containment match on the plugin filename.</summary>
    internal static bool Matches(ModKey candidate, string searchText)
    {
        return CultureInfo.CurrentCulture.CompareInfo.IndexOf(candidate.ToString(), searchText, CompareOptions.IgnoreCase) >= 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearchHandling)
        {
            return;
        }

        // A typed/pasted plugin filename (Name + a valid plugin extension) is accepted immediately,
        // even if it is not in the searchable set (e.g. an inactive plugin) - parity with Mutagen.
        var text = SearchBox.Text?.Trim() ?? "";
        if (ModKey.TryFromNameAndExtension(text, out var typedKey))
        {
            SetCurrentValue(ModKeyProperty, typedKey);
            SuggestionPopup.IsOpen = false;
            return;
        }

        RefreshSuggestions();
    }

    private void SearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Show the full candidate list on focus so browsing without typing works.
        _suppressSearchHandling = true;
        SearchBox.SelectAll();
        _suppressSearchHandling = false;
        RefreshSuggestions();
    }

    private void SearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!SuggestionList.IsKeyboardFocusWithin)
        {
            SuggestionPopup.IsOpen = false;
            RefreshDisplayFromModKey(); // restore the resolved display if the user typed a partial search
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && SuggestionPopup.IsOpen && SuggestionList.Items.Count > 0)
        {
            SuggestionList.SelectedIndex = 0;
            ((ListBoxItem?)SuggestionList.ItemContainerGenerator.ContainerFromIndex(0))?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && SuggestionList.Items.Count > 0)
        {
            CommitSelection((ModKey)SuggestionList.Items[0]);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SuggestionPopup.IsOpen = false;
            RefreshDisplayFromModKey();
            e.Handled = true;
        }
    }

    private void SuggestionList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SuggestionList.SelectedItem is ModKey selected)
        {
            CommitSelection(selected);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SuggestionPopup.IsOpen = false;
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private void SuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ModKey clicked)
        {
            CommitSelection(clicked);
        }
    }

    /// <summary>Applies a chosen suggestion to the ModKey property and closes the popup.</summary>
    protected virtual void CommitSelection(ModKey selected)
    {
        SetCurrentValue(ModKeyProperty, selected);
        SuggestionPopup.IsOpen = false;
        RefreshDisplayFromModKey();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentValue(ModKeyProperty, ModKey.Null);
        SuggestionPopup.IsOpen = false;
    }
}
