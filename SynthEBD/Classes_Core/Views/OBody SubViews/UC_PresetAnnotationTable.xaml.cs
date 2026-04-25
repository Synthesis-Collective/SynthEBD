using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SynthEBD;

/// <summary>
/// Code-behind for <see cref="VM_PresetAnnotationTable"/>'s view. Carries the dynamic-column
/// logic (DataGrid columns can't be data-bound in XAML, so they're appended programmatically
/// from the VM's <see cref="VM_PresetAnnotationTable.VisibleColumns"/> collection) and the
/// custom Sorting handler that lets the user sort on indexer-bound measurement columns
/// (ListCollectionView's SortDescriptions don't natively walk indexer paths).
/// </summary>
public partial class UC_PresetAnnotationTable : UserControl
{
    // Static columns defined in XAML before the dynamic measurement columns. Used as the index
    // boundary when rebuilding -- everything at or after this index is dynamic and gets cleared.
    private const int FixedColumnCount = 5;

    private VM_PresetAnnotationTable _vm;

    public UC_PresetAnnotationTable()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, __) => RebuildDynamicColumns();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.VisibleColumns.CollectionChanged -= OnVisibleColumnsChanged;
            _vm.WeightSlots.CollectionChanged -= OnWeightSlotsChanged;
        }
        _vm = e.NewValue as VM_PresetAnnotationTable;
        if (_vm != null)
        {
            _vm.VisibleColumns.CollectionChanged += OnVisibleColumnsChanged;
            _vm.WeightSlots.CollectionChanged += OnWeightSlotsChanged;
            RebuildDynamicColumns();
            SyncWeightSlotsBox();
        }
    }

    private void OnVisibleColumnsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildDynamicColumns();
    }

    private void OnWeightSlotsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        SyncWeightSlotsBox();
    }

    private void RebuildDynamicColumns()
    {
        if (AnnotationDataGrid == null) return;
        // Wipe any previously-appended dynamic columns; preserve the fixed ones declared in XAML.
        while (AnnotationDataGrid.Columns.Count > FixedColumnCount)
        {
            AnnotationDataGrid.Columns.RemoveAt(FixedColumnCount);
        }
        if (_vm == null) return;

        foreach (var col in _vm.VisibleColumns)
        {
            if (col == null || string.IsNullOrEmpty(col.MeasurementName)) continue;
            string path = $"MeasurementValues[{col.MeasurementName}]";
            var binding = new Binding(path)
            {
                Mode = BindingMode.OneWay,
                StringFormat = "0.000",
                FallbackValue = "",
                TargetNullValue = "",
            };
            var gridCol = new DataGridTextColumn
            {
                Header = col.Header ?? col.MeasurementName,
                Binding = binding,
                // SortMemberPath is parsed by our Sorting handler -- the default ListCollectionView
                // sort doesn't walk indexer paths, so we intercept and sort the underlying
                // ObservableCollection ourselves.
                SortMemberPath = path,
                MinWidth = 70,
                Width = new DataGridLength(90),
            };
            AnnotationDataGrid.Columns.Add(gridCol);
        }
    }

    private void OnDataGridSorting(object sender, DataGridSortingEventArgs e)
    {
        if (_vm == null || e.Column == null) return;
        var path = e.Column.SortMemberPath ?? "";
        if (!path.StartsWith("MeasurementValues[", StringComparison.Ordinal)) return;
        // Indexer path: parse out the measurement name, sort Rows in place, suppress the default
        // sort (which would walk the path with reflection and silently no-op).
        e.Handled = true;

        int keyStart = path.IndexOf('[') + 1;
        int keyEnd = path.IndexOf(']');
        if (keyStart < 1 || keyEnd <= keyStart) return;
        string key = path.Substring(keyStart, keyEnd - keyStart);

        var newDirection = e.Column.SortDirection == System.ComponentModel.ListSortDirection.Ascending
            ? System.ComponentModel.ListSortDirection.Descending
            : System.ComponentModel.ListSortDirection.Ascending;

        // Clear sort indicators on every other column.
        foreach (var c in AnnotationDataGrid.Columns)
        {
            if (!ReferenceEquals(c, e.Column)) c.SortDirection = null;
        }
        e.Column.SortDirection = newDirection;

        var rows = _vm.Rows.ToList();
        rows.Sort((a, b) =>
        {
            float? va = a != null && a.MeasurementValues.TryGetValue(key, out var x) ? x : null;
            float? vb = b != null && b.MeasurementValues.TryGetValue(key, out var y) ? y : null;
            int cmp = CompareNullable(va, vb);
            return newDirection == System.ComponentModel.ListSortDirection.Ascending ? cmp : -cmp;
        });

        // Repopulate. SelectedRow may be reset to null when ItemsSource items reorder; we
        // restore it explicitly so the annotation editor in Phase 4 keeps its target slice.
        var selected = _vm.SelectedRow;
        _vm.Rows.Clear();
        foreach (var r in rows) _vm.Rows.Add(r);
        if (selected != null) _vm.SelectedRow = selected;
    }

    // Nullable-aware comparator that always sorts nulls to the end of the list, regardless of
    // direction. The default Comparer<float?> places nulls first when ascending; users find that
    // disorienting in a measurement table where missing values are diagnostics, not "lowest".
    private static int CompareNullable(float? a, float? b)
    {
        if (!a.HasValue && !b.HasValue) return 0;
        if (!a.HasValue) return 1;
        if (!b.HasValue) return -1;
        return a.Value.CompareTo(b.Value);
    }

    private void OnColumnVisibilityClicked(object sender, RoutedEventArgs e)
    {
        // CheckBox TwoWay binding has already updated VM_AnnotationColumn.IsVisible. Tell the
        // VM to mirror it into VisibleColumns and persist the new selection on the profile.
        _vm?.SyncVisibleColumnsAndPersist();
    }

    private void OnShowAllColumns(object sender, RoutedEventArgs e)
    {
        _vm?.ShowAllColumns();
    }

    private void OnHideAllColumns(object sender, RoutedEventArgs e)
    {
        _vm?.HideAllColumns();
    }

    private void OnApplyWeightSlots(object sender, RoutedEventArgs e)
    {
        if (_vm == null || WeightSlotsBox == null) return;
        _vm.SetWeightSlots(ParseWeightSlots(WeightSlotsBox.Text));
    }

    private void SyncWeightSlotsBox()
    {
        if (WeightSlotsBox == null || _vm == null) return;
        WeightSlotsBox.Text = string.Join(",", _vm.WeightSlots);
    }

    private static List<int> ParseWeightSlots(string text)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var seen = new HashSet<int>();
        foreach (var token in text.Split(','))
        {
            if (!int.TryParse(token.Trim(), out int v)) continue;
            v = Math.Clamp(v, 0, 100);
            if (seen.Add(v)) result.Add(v);
        }
        result.Sort();
        return result;
    }
}
