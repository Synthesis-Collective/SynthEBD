using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

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

    /// <summary>Handles a selection in the Criterion column's Popup TreeView. Categories
    /// (non-leaf nodes) are ignored so clicking a header doesn't blank the row's criterion.
    /// The target row VM travels via TreeView.Tag (bound to the cell's DataContext in XAML),
    /// which keeps this handler decoupled from the surrounding DataGrid's selected row —
    /// selecting in row B's popup while row A is the DataGrid's SelectedItem still writes to
    /// row B. The popup is closed by walking the logical tree (Popup is in the logical tree
    /// but not the visual tree, so VisualTreeHelper wouldn't find it).</summary>
    private void CriterionTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (sender is not TreeView tree) return;
        if (e.NewValue is not BoundingBoxCriterionLeaf leaf) return;
        if (tree.Tag is not VM_NamedKeyVertex row) return;
        row.Criterion = leaf.Value;

        DependencyObject? parent = tree;
        while (parent != null && parent is not Popup)
            parent = LogicalTreeHelper.GetParent(parent);
        if (parent is Popup popup) popup.IsOpen = false;
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

    /// <summary>Intercepts a Name-column commit on either the KeyVertices or Measurements
    /// grid. When the typed value collides with another row's name, pops a modal asking the
    /// user to choose Overwrite (remove the other row(s)), Rename (auto-suffix this row to
    /// {typed}_2/_3/...), or Cancel (revert via DataGrid.CancelEdit). Non-Name columns and
    /// non-Commit actions are passed through untouched. The handler is shared between the
    /// two grids and discriminates by inspecting the row item's runtime type.</summary>
    private void NameCell_EditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (sender is not DataGrid grid) return;
        // Header text is the cheapest discriminator — both Name columns use literal "Name"
        // (no localization in this view). Avoids depending on column ordering or a tag.
        if (e.Column?.Header as string != "Name") return;
        if (e.EditingElement is not TextBox tb) return;

        switch (e.Row.Item)
        {
            case VM_NamedKeyVertex kv when grid.DataContext is VM_BodyTypeProfile p1:
                HandleNameCollision(grid, e, tb, kv.Name, tb.Text,
                    siblings: p1.KeyVertices.Where(r => !ReferenceEquals(r, kv)),
                    getName: r => r.Name,
                    removeRow: r => p1.KeyVertices.Remove(r),
                    rowKindLabel: "key vertex");
                break;

            case VM_MeasurementDefinition m when grid.DataContext is VM_BodyTypeProfile p2:
                HandleNameCollision(grid, e, tb, m.Name, tb.Text,
                    siblings: p2.Measurements.Where(r => !ReferenceEquals(r, m)),
                    getName: r => r.Name,
                    removeRow: r => p2.Measurements.Remove(r),
                    rowKindLabel: "measurement");
                break;
        }
    }

    /// <summary>Core collision logic shared by both grids. Type-parameterized over the row
    /// VM so the caller's <paramref name="siblings"/> stays strongly-typed for the
    /// <paramref name="removeRow"/> callback. Mutates <paramref name="tb"/> directly in the
    /// Rename branch (the DataGrid's binding then writes the suffixed name to the source);
    /// cancels the edit via DataGrid.CancelEdit in the Cancel branch so the cell exits edit
    /// mode with its original value rather than staying open with the typed-but-rejected
    /// text.</summary>
    private static void HandleNameCollision<TRow>(
        DataGrid grid,
        DataGridCellEditEndingEventArgs e,
        TextBox tb,
        string originalName,
        string typedName,
        IEnumerable<TRow> siblings,
        System.Func<TRow, string> getName,
        System.Action<TRow> removeRow,
        string rowKindLabel)
        where TRow : class
    {
        var typed = (typedName ?? "").Trim();
        var original = (originalName ?? "").Trim();
        // No-op for unchanged commits and for empty names (an empty name is filtered out
        // of the downstream lookup, so it can't cause ambiguity — no need to badger the
        // user about clearing a field).
        if (string.IsNullOrEmpty(typed)) return;
        if (string.Equals(typed, original, System.StringComparison.Ordinal)) return;

        var siblingsList = siblings.ToList();
        var collisions = siblingsList
            .Where(r => string.Equals((getName(r) ?? "").Trim(), typed, System.StringComparison.Ordinal))
            .ToList();
        if (collisions.Count == 0) return;

        var msg =
            $"\"{typed}\" is already in use by another {rowKindLabel} in this profile.\n\n" +
            "  Yes    →  Overwrite (delete the existing row(s), this one keeps the name)\n" +
            "  No     →  Rename (auto-rename this row to a unique suffix)\n" +
            "  Cancel →  Revert this row to its previous name";

        var result = MessageBox.Show(
            Window.GetWindow(grid),
            msg,
            "Duplicate name",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        switch (result)
        {
            case MessageBoxResult.Yes:
                // Overwrite: remove every other row that has this name. The DataGrid's
                // binding then writes the typed value to the surviving row's source.
                foreach (var dup in collisions) removeRow(dup);
                break;

            case MessageBoxResult.No:
                // Rename: pick the first {typed}_N (N starting at 2) that doesn't collide.
                var taken = siblingsList.Select(r => getName(r) ?? "");
                tb.Text = VM_BodyTypeProfile.GenerateUniqueNameFrom(typed, taken);
                break;

            default:
                // Cancel: prevent the binding from writing and bounce out of edit mode with
                // the original value. e.Cancel = true alone leaves the cell open with the
                // typed text still visible; deferring CancelEdit via Dispatcher exits the
                // edit cleanly after the current event finishes unwinding.
                e.Cancel = true;
                grid.Dispatcher.BeginInvoke(
                    new System.Action(() => grid.CancelEdit(DataGridEditingUnit.Cell)),
                    DispatcherPriority.Background);
                break;
        }
    }
}
