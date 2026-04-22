using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace SynthEBD;

public partial class UC_BodyTypeProfileEditor : UserControl
{
    private bool _presetsPrimed;

    public UC_BodyTypeProfileEditor()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => TryPrime();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded) TryPrime();
    }

    private void TryPrime()
    {
        if (_presetsPrimed) return;
        if (DataContext is VM_BodyTypeProfileEditor vm)
        {
            vm.RebuildAvailablePresets();
            _presetsPrimed = true;
        }
    }

    /// <summary>Forwards the KeyVertices DataGrid's full multi-selection to the current
    /// profile VM, which routes the (shape, vertex index) tuples to the viewer so every
    /// matching pick marker turns green. SelectedItem binding still fires SelectedKeyVertex
    /// PropertyChanged for single-item focus, but this handler is the authoritative path
    /// for multi-select green highlighting. The DataGrid lives inside a DataTemplate whose
    /// DataContext is VM_BodyTypeProfile (SelectedProfile), not the editor itself.</summary>
    private void KeyVerticesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.DataContext is not VM_BodyTypeProfile profile) return;
        var sel = new List<VM_NamedKeyVertex>(grid.SelectedItems.Count);
        foreach (var item in grid.SelectedItems)
            if (item is VM_NamedKeyVertex kv) sel.Add(kv);
        profile.SelectKeyVerticesInViewer(sel);
    }
}
