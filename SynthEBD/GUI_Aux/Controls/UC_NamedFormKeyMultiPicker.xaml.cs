using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;

namespace SynthEBD;

/// <summary>
/// Multi-record counterpart of <see cref="UC_NamedFormKeyPicker"/> — SynthEBD's replacement for
/// Mutagen.Bethesda.WPF's FormKeyMultiPicker with the same binding contract
/// (<see cref="FormKeys"/>, <see cref="LinkCache"/>, <see cref="ScopedTypes"/>). Selecting a
/// record in the embedded entry picker appends it to <see cref="FormKeys"/>; the list below shows
/// each member's resolved display name with a remove button.
/// </summary>
public partial class UC_NamedFormKeyMultiPicker : UserControl
{
    private bool _suppressEntryHandling = false;

    public UC_NamedFormKeyMultiPicker()
    {
        InitializeComponent();
        EntryList.ItemsSource = EntryDisplays;

        // The embedded picker acts as an "add" field: committing a record appends it and resets
        // the field for the next entry.
        var formKeyDescriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
            UC_NamedFormKeyPicker.FormKeyProperty, typeof(UC_NamedFormKeyPicker));
        formKeyDescriptor.AddValueChanged(EntryPicker, (_, _) =>
        {
            if (_suppressEntryHandling || EntryPicker.FormKey.IsNull)
            {
                return;
            }
            var added = EntryPicker.FormKey;
            if (FormKeys != null && !FormKeys.Contains(added))
            {
                FormKeys.Add(added);
            }
            _suppressEntryHandling = true;
            EntryPicker.FormKey = FormKey.Null;
            _suppressEntryHandling = false;
        });
    }

    public ObservableCollection<RecordDisplayData> EntryDisplays { get; } = new();

    public static readonly DependencyProperty FormKeysProperty = DependencyProperty.Register(
        nameof(FormKeys), typeof(ObservableCollection<FormKey>), typeof(UC_NamedFormKeyMultiPicker),
        new PropertyMetadata(null, OnFormKeysChanged));

    public ObservableCollection<FormKey> FormKeys
    {
        get => (ObservableCollection<FormKey>)GetValue(FormKeysProperty);
        set => SetValue(FormKeysProperty, value);
    }

    public static readonly DependencyProperty LinkCacheProperty = DependencyProperty.Register(
        nameof(LinkCache), typeof(ILinkCache), typeof(UC_NamedFormKeyMultiPicker),
        new PropertyMetadata(null, (d, _) => ((UC_NamedFormKeyMultiPicker)d).RefreshEntryDisplays()));

    public ILinkCache LinkCache
    {
        get => (ILinkCache)GetValue(LinkCacheProperty);
        set => SetValue(LinkCacheProperty, value);
    }

    public static readonly DependencyProperty ScopedTypesProperty = DependencyProperty.Register(
        nameof(ScopedTypes), typeof(IEnumerable<Type>), typeof(UC_NamedFormKeyMultiPicker),
        new PropertyMetadata(null, (d, _) => ((UC_NamedFormKeyMultiPicker)d).RefreshEntryDisplays()));

    public IEnumerable<Type> ScopedTypes
    {
        get => (IEnumerable<Type>)GetValue(ScopedTypesProperty);
        set => SetValue(ScopedTypesProperty, value);
    }

    private static void OnFormKeysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (UC_NamedFormKeyMultiPicker)d;
        if (e.OldValue is ObservableCollection<FormKey> oldCollection)
        {
            oldCollection.CollectionChanged -= picker.FormKeys_CollectionChanged;
        }
        if (e.NewValue is ObservableCollection<FormKey> newCollection)
        {
            newCollection.CollectionChanged += picker.FormKeys_CollectionChanged;
        }
        picker.RefreshEntryDisplays();
    }

    private void FormKeys_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshEntryDisplays();
    }

    /// <summary>Rebuilds the member list's display entries from the FormKeys collection, resolving
    /// names through the background index (unresolvable keys fall back to their FormKey string).</summary>
    private async void RefreshEntryDisplays()
    {
        var formKeys = FormKeys?.ToList();
        EntryDisplays.Clear();
        if (formKeys == null)
        {
            return;
        }

        IReadOnlyDictionary<FormKey, RecordDisplayData>? index = null;
        if (LinkCache != null && ScopedTypes != null)
        {
            index = await RecordNameIndexer.Instance.GetIndexAsync(LinkCache, ScopedTypes);
            if (FormKeys == null || !formKeys.SequenceEqual(FormKeys))
            {
                return; // collection changed while resolving; a newer refresh is already queued
            }
            EntryDisplays.Clear();
        }

        foreach (var formKey in formKeys)
        {
            EntryDisplays.Add(index != null && index.TryGetValue(formKey, out var data)
                ? data
                : new RecordDisplayData(formKey, null, null));
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RecordDisplayData entry)
        {
            FormKeys?.Remove(entry.FormKey);
        }
    }
}
