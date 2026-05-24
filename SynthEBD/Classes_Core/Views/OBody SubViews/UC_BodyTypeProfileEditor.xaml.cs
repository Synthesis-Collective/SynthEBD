using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace SynthEBD;

public partial class UC_BodyTypeProfileEditor : UserControl
{
    private bool _presetsPrimed;

    /// <summary>Active iteration timer when one of the Ctrl+I shortcuts has put the editor
    /// in an auto-cycle mode; null otherwise. Tested as the on/off flag.</summary>
    private DispatcherTimer? _iterationTimer;

    /// <summary>Which collection the active iteration is walking. Ignored when
    /// <see cref="_iterationTimer"/> is null.</summary>
    private IterationMode _iterationMode = IterationMode.Presets;

    /// <summary>Current per-step delay while iterating. Bounded by
    /// <see cref="MinIterationDelayMs"/> and <see cref="MaxIterationDelayMs"/>;
    /// + / - apply <see cref="IterationSpeedFactor"/> multiplicatively.</summary>
    private int _iterationDelayMs = 1000;

    private const int MinIterationDelayMs = 500;
    private const int MaxIterationDelayMs = 10_000;
    private const double IterationSpeedFactor = 0.75;

    /// <summary>Distinguishes the two Ctrl+I iteration variants. <see cref="Presets"/>
    /// walks <c>FilteredPresets</c> at the user's chosen <c>PreviewWeight</c>, leaving
    /// the weight pinned across the run. <see cref="Filtered"/> walks
    /// <c>MatchingPresets</c> (one row per (preset, weight) that survived the descriptor +
    /// weight filters), so each tick steps through both axes per the scan-results order.</summary>
    private enum IterationMode { Presets, Filtered }

    public UC_BodyTypeProfileEditor()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => StopIteration();
        DataContextChanged += OnDataContextChanged;
        // PreviewKeyDown is the tunneling phase, fired top-down from the root before
        // any child gets the event in the bubbling phase. Necessary here because the
        // viewer's HwndHost surface and DataGrid cells routinely consume keystrokes
        // before bubbling reaches the outer UserControl, and we want the iteration
        // shortcuts to work no matter where focus sits inside the editor.
        PreviewKeyDown += OnEditorPreviewKeyDown;
    }

    /// <summary>Handles the editor's global keyboard shortcuts.
    /// <list type="bullet">
    ///   <item>Ctrl+I — toggle auto-iteration through the current
    ///         <c>VM_BodyTypeProfileEditor.FilteredPresets</c> at
    ///         <see cref="_iterationDelayMs"/>, cycling indefinitely until
    ///         Escape or another Ctrl+I.</item>
    ///   <item>Ctrl+Shift+I — same cadence, but walks
    ///         <c>VM_BodyTypeProfileEditor.MatchingPresets</c> instead — i.e.
    ///         only the (preset, weight) rows that survived the Match Presets
    ///         tab's descriptor + weight filters, in their list order.</item>
    ///   <item>+ / - (top-row or numpad) — halve / double the per-step delay
    ///         while iteration is active, clamped to
    ///         [<see cref="MinIterationDelayMs"/>,
    ///         <see cref="MaxIterationDelayMs"/>].</item>
    ///   <item>Escape — stop iteration.</item>
    /// </list>
    /// Sets <c>e.Handled</c> only on keys we actually consume so normal text
    /// input, DataGrid navigation, and the existing per-tab shortcuts continue
    /// to work undisturbed.</summary>
    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+Shift+I → filtered iteration. Checked BEFORE the Ctrl+I branch so the
        // (Shift==0) gate below doesn't accidentally claim the chord. Alt is rejected
        // for the same reason the plain Ctrl+I branch rejects it: keep the chord
        // shape strict so neighbor shortcuts don't bleed into iteration toggles.
        if (e.Key == Key.I
            && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift
            && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            ToggleIteration(IterationMode.Filtered);
            e.Handled = true;
            return;
        }

        // Ctrl+I toggles iteration. Reject Shift/Alt modifiers so Ctrl+Shift+I
        // (browser dev tools muscle memory) doesn't accidentally fire.
        if (e.Key == Key.I
            && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (Keyboard.Modifiers & ModifierKeys.Shift) == 0
            && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            ToggleIteration(IterationMode.Presets);
            e.Handled = true;
            return;
        }

        // The remaining shortcuts only fire while iteration is active — they'd be
        // surprising to consume otherwise (e.g. + and - are common in numeric grids).
        if (_iterationTimer == null) return;

        switch (e.Key)
        {
            case Key.Escape:
                StopIteration();
                e.Handled = true;
                break;
            case Key.OemPlus:  // top-row '=' / '+'
            case Key.Add:      // numpad '+'
                ChangeIterationSpeed(faster: true);
                e.Handled = true;
                break;
            case Key.OemMinus: // top-row '-' / '_'
            case Key.Subtract: // numpad '-'
                ChangeIterationSpeed(faster: false);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Starts iteration in <paramref name="mode"/> if currently off; stops if
    /// already running in the same mode; switches modes (stop + restart) if running in
    /// a different mode. Iteration walks the per-mode collection at
    /// <see cref="_iterationDelayMs"/> intervals, wrapping at the end. The first tick
    /// advances PAST the currently-selected item so the user sees a change immediately
    /// even though the current item is already rendered.
    /// <list type="bullet">
    ///   <item><see cref="IterationMode.Presets"/>: walks <c>FilteredPresets</c> with
    ///         <c>PreviewWeight</c> pinned to the user's current choice.</item>
    ///   <item><see cref="IterationMode.Filtered"/>: walks <c>MatchingPresets</c>, which
    ///         already encodes one row per (preset, weight) — stepping it advances both
    ///         axes per the scan-results order.</item>
    /// </list></summary>
    private void ToggleIteration(IterationMode mode)
    {
        if (_iterationTimer != null)
        {
            // Same mode → toggle off. Different mode → switch (stop then start fresh
            // below, so the new mode's count + setup logs fire).
            bool wasSameMode = _iterationMode == mode;
            StopIteration();
            if (wasSameMode) return;
        }
        if (DataContext is not VM_BodyTypeProfileEditor vm) return;

        int count;
        switch (mode)
        {
            case IterationMode.Presets:
                count = vm.FilteredPresets?.Count ?? 0;
                if (count == 0)
                {
                    vm.Logger?.LogMessage("BodyTypeProfile iteration: no presets to cycle.");
                    return;
                }
                break;
            case IterationMode.Filtered:
                count = vm.MatchingPresets?.Count ?? 0;
                if (count == 0)
                {
                    vm.Logger?.LogMessage("BodyTypeProfile iteration: no filter-matching presets to cycle. Run a scan and/or adjust the filter.");
                    return;
                }
                break;
            default:
                return;
        }

        _iterationMode = mode;
        _iterationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_iterationDelayMs),
        };
        _iterationTimer.Tick += OnIterationTick;
        _iterationTimer.Start();
        string scope = mode == IterationMode.Filtered
            ? $"{count} filter-matching row(s)"
            : $"{count} preset(s), weight {vm.PreviewWeight}";
        vm.Logger?.LogMessage($"BodyTypeProfile iteration ({mode}): started at {_iterationDelayMs}ms/step "
            + $"({scope}). +/- adjusts speed, Escape stops.");
    }

    private void StopIteration()
    {
        if (_iterationTimer == null) return;
        var mode = _iterationMode;
        _iterationTimer.Stop();
        _iterationTimer.Tick -= OnIterationTick;
        _iterationTimer = null;
        if (DataContext is VM_BodyTypeProfileEditor vm)
        {
            vm.Logger?.LogMessage($"BodyTypeProfile iteration ({mode}): stopped.");
        }
    }

    /// <summary>One step of the iteration. Re-resolves the current row's position in
    /// the active mode's collection each tick rather than carrying an index across ticks,
    /// so a mid-iteration filter / scan / list rebuild doesn't desynchronize. Falls off
    /// to <c>StopIteration</c> when the active list becomes empty (filter text typed to
    /// a no-match string, gender changed to one with no presets, descriptor filter
    /// excluded everything, etc.).</summary>
    private void OnIterationTick(object? sender, EventArgs e)
    {
        if (DataContext is not VM_BodyTypeProfileEditor vm)
        {
            StopIteration();
            return;
        }

        switch (_iterationMode)
        {
            case IterationMode.Presets:
            {
                var presets = vm.FilteredPresets;
                if (presets == null || presets.Count == 0)
                {
                    StopIteration();
                    return;
                }
                int currentIdx = vm.SelectedPreset != null
                    ? presets.IndexOf(vm.SelectedPreset)
                    : -1;
                int nextIdx = currentIdx < 0 ? 0 : (currentIdx + 1) % presets.Count;
                vm.SelectedPreset = presets[nextIdx];
                break;
            }
            case IterationMode.Filtered:
            {
                var rows = vm.MatchingPresets;
                if (rows == null || rows.Count == 0)
                {
                    StopIteration();
                    return;
                }
                int currentIdx = vm.SelectedMatchRow != null
                    ? rows.IndexOf(vm.SelectedMatchRow)
                    : -1;
                int nextIdx = currentIdx < 0 ? 0 : (currentIdx + 1) % rows.Count;
                // Setting SelectedMatchRow fires the editor's PropertyChanged handler
                // which calls LoadScanResultInViewer — so PreviewWeight + SelectedPreset
                // update for free, with the same preview path arrow-key navigation uses.
                vm.SelectedMatchRow = rows[nextIdx];
                break;
            }
        }
    }

    private void ChangeIterationSpeed(bool faster)
    {
        if (_iterationTimer == null) return;
        int newDelay = faster
            ? Math.Max(MinIterationDelayMs, (int)Math.Round(_iterationDelayMs * IterationSpeedFactor))
            : Math.Min(MaxIterationDelayMs, (int)Math.Round(_iterationDelayMs / IterationSpeedFactor));
        if (newDelay == _iterationDelayMs) return; // already clamped at the bound
        _iterationDelayMs = newDelay;
        _iterationTimer.Interval = TimeSpan.FromMilliseconds(_iterationDelayMs);
        if (DataContext is VM_BodyTypeProfileEditor vm)
        {
            vm.Logger?.LogMessage($"BodyTypeProfile iteration: {_iterationDelayMs}ms/step");
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => TryPrime();

    /// <summary>Opens a SynthEBD-style OK dialog listing every keyboard shortcut
    /// available in this editor, broken down per tab plus the global
    /// preset-iteration shortcuts. Text is hard-coded here rather than fetched
    /// from XAML because (a) WPF's per-tab DockPanel.InputBindings don't expose
    /// a single enumerable view, (b) keeping the prose tight enough to read at
    /// a glance is more important than auto-derivation, and (c) any future
    /// shortcut author touches both this list and the XAML in the same edit
    /// anyway. If a shortcut here ever falls out of sync with the actual
    /// bindings, the fix is to update both.</summary>
    private void KeyboardShortcuts_Click(object sender, RoutedEventArgs e)
    {
        // Each entry is a single paragraph; the window's TextWrapping=Wrap handles
        // long lines at render time. Avoid mid-description newlines — they create
        // misaligned "hanging" lines under WPF's TextBox wrap, because wrapped
        // continuation doesn't track the original column indent.
        const string text =
@"GLOBAL (anywhere in the Body Type Profiles menu)

Ctrl+I: Toggle iteration through the filtered preset list at the currently-selected weight. Default cadence is 1 sec / step.

Ctrl+Shift+I: Toggle iteration through only the Match Presets rows that survived the descriptor + weight filters. Each step advances both the preset and the weight per the scan-results order. Press Ctrl+Shift+I again to stop; pressing Ctrl+I while filter-mode iteration is active switches modes.

+ / -: While iterating, speed up / slow down. Multiplicative step of 0.75, clamped to 500-10000 ms/step.

Esc: While iterating, stop.

KEY VERTICES TAB

Ctrl+S: Save Key Vertices to JSON.

Ctrl+L: Load Key Vertices from JSON. Replaces the current list after a confirm dialog.

MEASUREMENTS TAB

Ctrl+S: Save Measurements to JSON. Definitions only — no live values.

Ctrl+L: Load Measurements from JSON. Replaces the current list after a confirm dialog.

Ctrl+Shift+S: Save Measurements to CSV including the LiveValue column evaluated against the currently-loaded preset and weight. Default filename: {PresetLabel}_w{Weight}_Measurements.csv

Ctrl+C: Copy the Measurements table to the clipboard as TSV. Pastes directly into Excel / Sheets without an import wizard.

Ctrl+Alt+Shift+S: Cumulative CSV across every (preset, weight) target for this profile — one row per slice, one column per measurement. Drives a full scan first when the cache isn't already complete. Default filename: {ProfileName}_AllMeasurements.csv

RULES TAB

Ctrl+S: Save Rules to JSON.

Ctrl+L: Load Rules from JSON. Replaces the current list after a confirm dialog.

LABEL-THEN-SUGGEST TAB

Ctrl+Shift+S: Save Measurements + LiveValues to CSV for the currently-loaded preset and weight. Same format and filename convention as the Measurements tab.

PREVIEW TAB

Ctrl+Shift+S: Save Measurements + LiveValues to CSV for the currently-previewed preset and weight. Same format and filename convention as the Measurements tab.

MATCH PRESETS TAB

Ctrl+Shift+S: Save Measurements + LiveValues to CSV for whichever scan row is currently selected. Arrow-key navigation auto-loads each row.

Ctrl+Alt+Shift+S: Long-format descriptor-matches CSV across every scanned (preset, weight) — one row per (descriptor, preset, weight) match, sorted by Category then Value. Drives a scan first when the cache is stale. Default filename: {ProfileName}_DescriptorMatches.csv

Ctrl+C: Copy the selected row's ""{PresetLabel} ({Weight})"" identifier to the clipboard.
";
        MessageWindow.DisplayNotificationOK("Body Type Profiles — Keyboard Shortcuts", text);
    }

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

    /// <summary>Handles a selection in the Rules-tab tree. TreeView.SelectedItem is read-only
    /// so we can't bind it directly — push the new value into the profile VM here, where it
    /// drives FilteredRules via the VM's OnSelectedRuleTreeNodeChanged hook. The TreeView's
    /// DataContext is VM_BodyTypeProfile (the per-profile DataTemplate wrapping the editor).</summary>
    private void RuleTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (sender is not TreeView tree) return;
        if (tree.DataContext is not VM_BodyTypeProfile profile) return;
        profile.SelectedRuleTreeNode = e.NewValue; // null when nothing's selected
    }

    /// <summary>Re-pulls the live TemplateDescriptors from VM_SettingsOBody.DescriptorUI
    /// every time the Rules tab becomes visible, so descriptors the user added in OBody
    /// Misc Settings → Descriptors (which only writes back to Settings_OBody on save) are
    /// picked up by the Rules-tab tree the moment the user comes back. WPF TabControl
    /// toggles each TabItem's IsVisible on selection switch, so this fires once per
    /// activation. No-op when the tab is being hidden (e.IsVisible=false) and when the
    /// outer DataContext isn't the editor VM (e.g. during early load).</summary>
    private void ProfileTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged bubbles up from any nested TabControl (Match Presets has its own,
        // Label-then-Suggest's Expanders host their own). Gate to the outer TabControl so we
        // only react when the user switches between Key Vertices / Measurements / Rules /
        // etc. — not when a nested control's selection moves.
        if (!ReferenceEquals(e.OriginalSource, sender)) return;
        if (sender is not TabControl tc) return;

        // The TabControl + TabItems live inside the per-profile DataTemplate, so x:Name
        // doesn't generate a code-behind field for the Rules TabItem. Match by Header text
        // instead — the literal "Rules" header is the same constant the user sees.
        if (tc.SelectedItem is not TabItem ti) return;
        if (ti.Header as string != "Rules") return;

        if (this.DataContext is VM_BodyTypeProfileEditor editor)
        {
            editor.RefreshAvailableDescriptorsFromLiveSettings();
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
