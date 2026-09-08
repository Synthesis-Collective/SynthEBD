using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;

namespace SynthEBD;

/// <summary>
/// Single-record FormKey picker that displays record NAMES (falling back to EditorID, then the raw
/// FormKey) — SynthEBD's replacement for Mutagen.Bethesda.WPF's FormKeyPicker, with the same
/// binding contract (<see cref="FormKey"/> two-way, <see cref="LinkCache"/>,
/// <see cref="ScopedTypes"/>) so migration is a drop-in element swap. Suggestions come from
/// <see cref="RecordNameIndexer"/>'s background-built per-type index, filtered with
/// culture-aware matching (Turkish translations must match correctly); a pasted parseable FormKey
/// is accepted directly even when it is not in the index.
/// </summary>
public partial class UC_NamedFormKeyPicker : UserControl
{
    private const int DebounceMs = 250;
    private const int MaxSuggestions = 500;

    private readonly DispatcherTimer _debounceTimer;
    private bool _suppressSearchHandling = false;

    public UC_NamedFormKeyPicker()
    {
        InitializeComponent();
        SuggestionList.ItemsSource = Suggestions;
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
        _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); _ = RefreshSuggestionsAsync(); };
        RefreshDisplayFromFormKey();
    }

    public ObservableCollection<RecordDisplayData> Suggestions { get; } = new();

    public static readonly DependencyProperty FormKeyProperty = DependencyProperty.Register(
        nameof(FormKey), typeof(FormKey), typeof(UC_NamedFormKeyPicker),
        new FrameworkPropertyMetadata(default(FormKey), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((UC_NamedFormKeyPicker)d).RefreshDisplayFromFormKey()));

    public FormKey FormKey
    {
        get => (FormKey)GetValue(FormKeyProperty);
        set => SetValue(FormKeyProperty, value);
    }

    public static readonly DependencyProperty LinkCacheProperty = DependencyProperty.Register(
        nameof(LinkCache), typeof(ILinkCache), typeof(UC_NamedFormKeyPicker),
        new PropertyMetadata(null, (d, _) => ((UC_NamedFormKeyPicker)d).RefreshDisplayFromFormKey()));

    public ILinkCache LinkCache
    {
        get => (ILinkCache)GetValue(LinkCacheProperty);
        set => SetValue(LinkCacheProperty, value);
    }

    public static readonly DependencyProperty ScopedTypesProperty = DependencyProperty.Register(
        nameof(ScopedTypes), typeof(IEnumerable<Type>), typeof(UC_NamedFormKeyPicker),
        new PropertyMetadata(null, (d, _) => ((UC_NamedFormKeyPicker)d).RefreshDisplayFromFormKey()));

    public IEnumerable<Type> ScopedTypes
    {
        get => (IEnumerable<Type>)GetValue(ScopedTypesProperty);
        set => SetValue(ScopedTypesProperty, value);
    }

    public static readonly DependencyProperty CandidateFormKeysProperty = DependencyProperty.Register(
        nameof(CandidateFormKeys), typeof(IEnumerable<FormKey>), typeof(UC_NamedFormKeyPicker),
        new PropertyMetadata(null, (d, _) => ((UC_NamedFormKeyPicker)d).OnCandidatesChanged()));

    /// <summary>
    /// Optional allow-list restricting which records the suggestion popup offers. Null (the
    /// default) offers everything in scope, which is how every pre-existing caller behaves.
    ///
    /// <para>This narrows SUGGESTIONS only. The display of the current
    /// <see cref="FormKey"/> still resolves against the full index, and a typed or pasted
    /// parseable FormKey is still accepted outright — so a caller can restrict the browse
    /// list without preventing the user from naming a record outside it.</para>
    ///
    /// <para>Used by the BodySlide Compare window to narrow the preview-NPC picker to NPCs at
    /// the pane's chosen weight.</para>
    /// </summary>
    public IEnumerable<FormKey> CandidateFormKeys
    {
        get => (IEnumerable<FormKey>)GetValue(CandidateFormKeysProperty);
        set => SetValue(CandidateFormKeysProperty, value);
    }

    /// <summary>
    /// Tracks the bound candidate collection's own change notifications. Callers typically bind
    /// a long-lived <see cref="ObservableCollection{T}"/> that they REFILL — the property never
    /// changes instance, so the DP callback alone would only ever see the collection's initial
    /// (usually empty) state. Without this the picker would silently offer nothing.
    /// </summary>
    private System.Collections.Specialized.INotifyCollectionChanged? _observedCandidates;

    private void OnCandidatesChanged()
    {
        if (_observedCandidates != null)
        {
            _observedCandidates.CollectionChanged -= CandidateCollectionChanged;
            _observedCandidates = null;
        }

        if (CandidateFormKeys is System.Collections.Specialized.INotifyCollectionChanged observable)
        {
            _observedCandidates = observable;
            _observedCandidates.CollectionChanged += CandidateCollectionChanged;
        }

        RefreshIfBrowsing();
    }

    private void CandidateCollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshIfBrowsing();

    /// <summary>Re-filters in place only while the popup is open, so a candidate set that
    /// arrives or changes mid-browse doesn't leave stale entries on screen. When it's closed
    /// there is nothing to update — the next open reads the collection fresh.</summary>
    private void RefreshIfBrowsing()
    {
        if (SuggestionPopup.IsOpen) _ = RefreshSuggestionsAsync();
    }

    /// <summary>Shows the resolved display string for the current FormKey in the search box
    /// (without opening the suggestion popup).</summary>
    private async void RefreshDisplayFromFormKey()
    {
        _suppressSearchHandling = true;
        try
        {
            if (FormKey.IsNull)
            {
                SearchBox.Text = "";
                return;
            }
            SearchBox.Text = FormKey.ToString();
            var index = await GetIndexOrNullAsync();
            var display = index != null && index.TryGetValue(FormKey, out var data) ? data.DisplayString : null;
            if (display != null)
            {
                _suppressSearchHandling = true;
                SearchBox.Text = display;
            }
        }
        finally
        {
            _suppressSearchHandling = false;
        }
    }

    private async Task<IReadOnlyDictionary<FormKey, RecordDisplayData>?> GetIndexOrNullAsync()
    {
        var linkCache = LinkCache;
        var scopedTypes = ScopedTypes;
        if (linkCache == null || scopedTypes == null)
        {
            return null;
        }
        return await RecordNameIndexer.Instance.GetIndexAsync(linkCache, scopedTypes);
    }

    private async Task RefreshSuggestionsAsync()
    {
        var searchText = SearchBox.Text?.Trim() ?? "";
        var index = await GetIndexOrNullAsync();
        if (index == null)
        {
            return;
        }

        Suggestions.Clear();
        IEnumerable<RecordDisplayData> matches = index.Values;
        // Allow-list first: it is a set lookup and typically cuts the candidate pool by
        // orders of magnitude, so the per-record text match below runs over far less.
        // Snapshotted per refresh rather than cached on the property-changed callback, because
        // the bound collection is refilled in place (see _observedCandidates) and a cached set
        // would freeze at whatever it held when the binding first attached.
        var candidates = CandidateFormKeys;
        if (candidates != null)
        {
            var candidateSet = candidates as IReadOnlySet<FormKey> ?? new HashSet<FormKey>(candidates);
            matches = matches.Where(x => candidateSet.Contains(x.FormKey));
        }
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            matches = matches.Where(x => Matches(x, searchText));
        }
        foreach (var match in matches.OrderBy(x => x.DisplayString, StringComparer.CurrentCultureIgnoreCase).Take(MaxSuggestions))
        {
            Suggestions.Add(match);
        }
        SuggestionPopup.IsOpen = Suggestions.Any() && SearchBox.IsKeyboardFocusWithin;
    }

    /// <summary>Culture-aware containment match on name, EditorID, and FormKey text.</summary>
    internal static bool Matches(RecordDisplayData candidate, string searchText)
    {
        var comparison = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
        const System.Globalization.CompareOptions options = System.Globalization.CompareOptions.IgnoreCase;
        return (candidate.Name != null && comparison.IndexOf(candidate.Name, searchText, options) >= 0)
            || (candidate.EditorID != null && comparison.IndexOf(candidate.EditorID, searchText, options) >= 0)
            || comparison.IndexOf(candidate.FormKey.ToString(), searchText, options) >= 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearchHandling)
        {
            return;
        }

        // A pasted/typed parseable FormKey is accepted immediately, even if it is not in the
        // index (e.g. a record from an unloaded plugin) - parity with the Mutagen picker.
        var text = SearchBox.Text?.Trim() ?? "";
        if (FormKey.TryFactory(text, out var typedKey))
        {
            SetCurrentValue(FormKeyProperty, typedKey);
            SuggestionPopup.IsOpen = false;
            return;
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void SearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Show the full candidate list on focus so browsing without typing works.
        _debounceTimer.Stop();
        _suppressSearchHandling = true;
        SearchBox.SelectAll();
        _suppressSearchHandling = false;
        _ = RefreshSuggestionsAsync();
    }

    private void SearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!SuggestionList.IsKeyboardFocusWithin)
        {
            SuggestionPopup.IsOpen = false;
            RefreshDisplayFromFormKey(); // restore the resolved display if the user typed a partial search
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
            CommitSelection((RecordDisplayData)SuggestionList.Items[0]);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SuggestionPopup.IsOpen = false;
            RefreshDisplayFromFormKey();
            e.Handled = true;
        }
    }

    private void SuggestionList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SuggestionList.SelectedItem is RecordDisplayData selected)
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
        if ((e.OriginalSource as FrameworkElement)?.DataContext is RecordDisplayData clicked)
        {
            CommitSelection(clicked);
        }
    }

    /// <summary>Applies a chosen suggestion to the FormKey property and closes the popup.</summary>
    protected virtual void CommitSelection(RecordDisplayData selected)
    {
        SetCurrentValue(FormKeyProperty, selected.FormKey);
        SuggestionPopup.IsOpen = false;
        RefreshDisplayFromFormKey();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentValue(FormKeyProperty, FormKey.Null);
        SuggestionPopup.IsOpen = false;
    }
}
