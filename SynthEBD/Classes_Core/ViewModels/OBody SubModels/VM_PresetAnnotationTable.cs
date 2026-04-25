using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SynthEBD;

/// <summary>
/// Bottom-section VM for the new Label-then-Suggest tab. Owns the sortable preset table:
/// one row per (preset, gender, weight) slice for the active profile's body type, with
/// dynamic per-measurement columns built from the profile's <see cref="MeasurementDefinition"/>
/// list and the user's persisted column-visibility preferences.
///
/// The table populates itself by running the same evaluator the Match Presets scan uses,
/// but at every weight slot listed in <see cref="AnnotatorPreferences.WeightSlots"/> and
/// recording the raw measurement values (not just the matched descriptors). Clicking a row
/// reloads that (preset, weight) slice in the embedded viewer so the annotation editor in
/// Phase 4 can show the corresponding mesh while the user picks descriptors.
/// </summary>
public class VM_PresetAnnotationTable : VM
{
    private readonly VM_BodyTypeProfileEditor _editor;
    private CancellationTokenSource _scanCts;

    // Profile we're currently subscribed to so column-list and weight-slot changes refresh
    // the table layout. Swapped when the editor's SelectedProfile changes.
    private VM_BodyTypeProfile _watchedProfile;

    public VM_PresetAnnotationTable(VM_BodyTypeProfileEditor editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));

        ScanCommand = new RelayCommand(
            canExecute: _ => !IsScanning && _editor.SelectedProfile != null,
            execute: _ => _ = ScanAsync());

        CancelScanCommand = new RelayCommand(
            canExecute: _ => IsScanning,
            execute: _ => _scanCts?.Cancel());

        LoadRowInViewerCommand = new RelayCommand(
            canExecute: x => x is VM_PresetAnnotationRow && !IsScanning,
            execute: x => { if (x is VM_PresetAnnotationRow row) LoadRowInViewer(row); });

        ToggleColumnVisibilityCommand = new RelayCommand(
            canExecute: x => x is VM_AnnotationColumn,
            execute: x => { if (x is VM_AnnotationColumn col) ToggleColumnVisibility(col); });

        // React to editor-side profile selection so we rebind column list, weight slots, and
        // clear any stale rows when the user moves between profiles.
        _editor.PropertyChanged += OnEditorPropertyChanged;

        // Bind row clicks (DataGrid two-way binds SelectedItem -> SelectedRow) to the viewer:
        // selecting a row reloads its (preset, weight) slice so the Phase 4 annotation editor
        // shows the matching mesh. Guarded against re-fire by Fody-generated equality checks.
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SelectedRow) && SelectedRow != null && !IsScanning)
            {
                LoadRowInViewer(SelectedRow);
            }
        };

        BindToProfile(_editor.SelectedProfile);
    }

    /// <summary>One row per (preset, gender, weight) slice. Repopulated by <see cref="ScanAsync"/>;
    /// cleared when the active profile changes.</summary>
    public ObservableCollection<VM_PresetAnnotationRow> Rows { get; } = new();

    /// <summary>Every measurement in the active profile, paired with its visibility flag. Drives
    /// both the dynamic DataGrid column layout (code-behind watches <see cref="VisibleColumns"/>)
    /// and the column-visibility flyout.</summary>
    public ObservableCollection<VM_AnnotationColumn> AllColumns { get; } = new();

    /// <summary>Filtered view of <see cref="AllColumns"/> containing only visible entries, in
    /// stable order. The annotation table's code-behind listens to this collection's
    /// CollectionChanged event and rebuilds the DataGrid columns to match.</summary>
    public ObservableCollection<VM_AnnotationColumn> VisibleColumns { get; } = new();

    /// <summary>Weight slots the next scan will enumerate. Two-way bound to a slot editor in the
    /// UI; edits round-trip through <see cref="AnnotatorPreferences.WeightSlots"/> on the active
    /// profile so the choice persists across sessions.</summary>
    public ObservableCollection<int> WeightSlots { get; } = new();

    public bool IsScanning { get; private set; }
    public int ScanProgressPercent { get; private set; }
    public string ScanStatus { get; private set; } = "No profile selected.";

    /// <summary>Currently-selected row. Phase 4 binds the annotation editor's preset/weight to
    /// this so picking a descriptor commits to the right <see cref="PresetAnnotation"/>.</summary>
    public VM_PresetAnnotationRow SelectedRow { get; set; }

    public RelayCommand ScanCommand { get; }
    public RelayCommand CancelScanCommand { get; }
    public RelayCommand LoadRowInViewerCommand { get; }
    public RelayCommand ToggleColumnVisibilityCommand { get; }

    /// <summary>Loads a row's preset+weight into the editor's viewer using the same routing as
    /// Match Presets so the existing RefreshPreviewAsync path fires (NPC load + deformation +
    /// measurement refresh). Phase 4 hooks the resulting selection to the annotation editor.</summary>
    public void LoadRowInViewer(VM_PresetAnnotationRow row)
    {
        if (row == null || IsScanning) return;
        var menu = _editor.GetBodySlidesMenu();
        if (menu == null) return;
        var source = row.Gender == Gender.Male ? menu.BodySlidesMale : menu.BodySlidesFemale;
        VM_BodySlidePlaceHolder ph = null;
        foreach (var p in source)
        {
            if (p?.AssociatedModel?.Label == row.PresetLabel) { ph = p; break; }
        }
        if (ph == null) return;
        _editor.PreviewGender = row.Gender;
        _editor.PreviewWeight = row.Weight;
        _editor.SelectedPreset = ph;
        // Don't write SelectedRow back here -- the DataGrid is the source of truth via two-way
        // binding, and re-assigning the same row is a no-op under Fody's generated setter.
    }

    /// <summary>Scans every preset matching the profile's BodyTypeName at every configured
    /// weight slot, computing measurement values and matched descriptors. Mirrors
    /// <c>VM_BodyTypeProfileEditor.RunScanAsync</c>'s structure but stores raw measurement
    /// values instead of just descriptor signatures, since Phase 5/6 need the numbers to
    /// score discriminators and synthesize thresholds.</summary>
    public async Task ScanAsync()
    {
        var profile = _editor.SelectedProfile;
        if (profile == null || IsScanning) return;
        var menu = _editor.GetBodySlidesMenu();
        if (menu == null) { ScanStatus = "BodySlides menu not available."; return; }
        var viewer = _editor.CharacterViewer;
        if (viewer == null) { ScanStatus = "No viewer available."; return; }

        var weightSlots = WeightSlots.ToList();
        if (weightSlots.Count == 0) { ScanStatus = "No weight slots configured."; return; }

        IsScanning = true;
        ScanProgressPercent = 0;
        ScanStatus = "Initializing scan...";
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        try
        {
            // Same body-type filtering as Match Presets so the two tabs agree on which presets
            // belong to the active profile.
            var bodyType = profile.BodyTypeName?.Trim() ?? "";
            var targets = new List<(VM_BodySlidePlaceHolder ph, Gender gender)>();
            foreach (var ph in menu.BodySlidesMale)
            {
                if (ph?.AssociatedModel == null) continue;
                if (bodyType.Length > 0 && !string.Equals(ph.AssociatedModel.SliderGroup, bodyType, StringComparison.OrdinalIgnoreCase)) continue;
                targets.Add((ph, Gender.Male));
            }
            foreach (var ph in menu.BodySlidesFemale)
            {
                if (ph?.AssociatedModel == null) continue;
                if (bodyType.Length > 0 && !string.Equals(ph.AssociatedModel.SliderGroup, bodyType, StringComparison.OrdinalIgnoreCase)) continue;
                targets.Add((ph, Gender.Female));
            }

            int total = targets.Count * weightSlots.Count;
            if (total == 0)
            {
                Rows.Clear();
                ScanStatus = $"No presets matched body type \"{bodyType}\".";
                return;
            }

            // KeyVertices / MeasurementDefinition changes invalidate the numbers in the
            // shared cache. Drop them now so the iteration below misses on every key.
            if (profile.MeasurementCacheStale)
                profile.MeasurementCache.Clear();

            var profileModel = profile.DumpToModel();

            // Compute the work set: keys this scan needs that aren't in the shared cache yet.
            // Any prior Match Presets scan at overlapping weights populated entries we can
            // reuse.
            var missing = new List<(VM_BodySlidePlaceHolder ph, Gender gender, int weight)>();
            foreach (var (ph, gender) in targets)
            {
                var label = ph.AssociatedModel.Label ?? "";
                foreach (int weight in weightSlots)
                {
                    int clampedWeight = Math.Clamp(weight, 0, 100);
                    if (!profile.MeasurementCache.ContainsKey((label, gender, clampedWeight)))
                        missing.Add((ph, gender, clampedWeight));
                }
            }
            int reused = total - missing.Count;

            // All-hit fast path: every (preset, weight) slice is already cached. No mesh
            // work or preview-NPC load — just rebuild the table from the cache.
            if (missing.Count == 0)
            {
                PopulateRowsFromCache();
                ScanStatus = $"Rebuilt from cache: {total} slice(s) reused, no scan needed.";
                ScanProgressPercent = 100;
                return;
            }

            // Pre-flight: scan deforms whatever mesh is in the viewer. If the viewer is empty
            // (first-open / context loss) we'd loop with no shapes loaded and get all-null
            // measurements. Defer to the editor's existing auto-load helper.
            if (viewer.GetCurrentShapeVertexCounts().Count == 0)
            {
                ScanStatus = "Loading preview NPC...";
                bool loaded = await _editor.EnsurePreviewNpcLoadedAsync(missing[0].gender, ct);
                if (!loaded)
                {
                    ScanStatus = "No preview NPC available -- cannot scan.";
                    return;
                }
            }

            int done = 0;

            foreach (var (ph, gender, weight) in missing)
            {
                if (ct.IsCancellationRequested) break;
                var model = ph.AssociatedModel;
                ScanStatus = $"Scanning {done + 1}/{missing.Count}: {model.Label} @ {weight}" +
                             (reused > 0 ? $" ({reused} cached)" : "");

                viewer.ApplyBodySlide(model, weight);
                // Yield BELOW DispatcherPriority.Render so WPF actually paints the
                // progress-bar update before the next iteration. Task.Yield posts at
                // Normal (9), which preempts Render (7) — that meant the loop ran
                // back-to-back without ever rendering, freezing the UI for the whole
                // scan and only repainting once at the end. Background (4) yields to
                // Render. Same fix as RunScanAsync's per-iteration yield.
                await Dispatcher.Yield(DispatcherPriority.Background);

                var result = BodySlideMeasurementEvaluator.Evaluate(viewer, profileModel, includeDrafts: true);

                // Persist measurements into the shared cache. The Match Presets display
                // re-derives its descriptor list from these whenever rules change.
                var entry = new VM_BodyTypeProfile.MeasurementCacheEntry { TopologyMismatch = result.TopologyMismatch };
                foreach (var def in profileModel.Measurements)
                {
                    if (def == null || string.IsNullOrEmpty(def.Name)) continue;
                    entry.Measurements[def.Name] = result.Measurements.TryGetValue(def.Name, out var v) ? (float?)v : null;
                }
                profile.MeasurementCache[(model.Label ?? "", gender, weight)] = entry;

                done++;
                ScanProgressPercent = (done * 100) / missing.Count;
            }

            // Rebuild the table from the full cache (newly-scanned + previously-cached
            // entries from any prior scan). Then propagate the cache into Match Presets'
            // descriptor view too so switching tabs shows fresh data without re-scanning.
            PopulateRowsFromCache();
            profile.RebuildScanResultsFromCache(profileModel, includeDrafts: true);
            profile.ScanResultsStale = false;
            profile.MeasurementCacheStale = false;

            if (ct.IsCancellationRequested)
            {
                ScanStatus = $"Scan cancelled at {done}/{missing.Count}.";
            }
            else
            {
                ScanStatus = reused > 0
                    ? $"Scan complete: {done} evaluated, {reused} cached. {Rows.Count} row(s)."
                    : $"Scanned {done} (preset, weight) slices across {targets.Count} preset(s).";
            }
        }
        catch (Exception ex)
        {
            _editor.LogScanError(ex);
            ScanStatus = "Scan failed (see log).";
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    /// <summary>Toggles a column's visibility, updating both the live <see cref="VisibleColumns"/>
    /// collection (which the DataGrid mirrors) and the persisted preferences on the profile.</summary>
    public void ToggleColumnVisibility(VM_AnnotationColumn col)
    {
        if (col == null) return;
        col.IsVisible = !col.IsVisible;
        RebuildVisibleColumns();
        PersistColumnVisibility();
    }

    /// <summary>Re-applies the IsVisible flag on every <see cref="AllColumns"/> entry to the
    /// live <see cref="VisibleColumns"/> collection and persists the new visible-set to the
    /// active profile. Called from the column-visibility flyout after a CheckBox flips a flag
    /// via TwoWay binding (ToggleColumnVisibility is the imperative path; this is the
    /// declarative-edit path).</summary>
    public void SyncVisibleColumnsAndPersist()
    {
        RebuildVisibleColumns();
        PersistColumnVisibility();
    }

    /// <summary>Sets <see cref="VM_AnnotationColumn.IsVisible"/> = true on every entry in
    /// <see cref="AllColumns"/>, then refreshes <see cref="VisibleColumns"/> and persists.
    /// Wired to the "All" shortcut in the column-visibility flyout.</summary>
    public void ShowAllColumns()
    {
        foreach (var c in AllColumns) { if (c != null) c.IsVisible = true; }
        SyncVisibleColumnsAndPersist();
    }

    /// <summary>Sets <see cref="VM_AnnotationColumn.IsVisible"/> = false on every entry in
    /// <see cref="AllColumns"/>, then refreshes <see cref="VisibleColumns"/> and persists.
    /// Wired to the "None" shortcut in the column-visibility flyout.</summary>
    public void HideAllColumns()
    {
        foreach (var c in AllColumns) { if (c != null) c.IsVisible = false; }
        SyncVisibleColumnsAndPersist();
    }

    /// <summary>Replaces the live weight-slot list with <paramref name="slots"/> (clamped to
    /// [0, 100], deduped, sorted) and persists the new set to the active profile. Called from
    /// the weight-slots flyout's Apply button.</summary>
    public void SetWeightSlots(IEnumerable<int> slots)
    {
        var sanitized = new List<int>();
        var seen = new HashSet<int>();
        if (slots != null)
        {
            foreach (var w in slots)
            {
                int v = Math.Clamp(w, 0, 100);
                if (seen.Add(v)) sanitized.Add(v);
            }
        }
        sanitized.Sort();

        WeightSlots.Clear();
        foreach (var w in sanitized) WeightSlots.Add(w);

        var profile = _watchedProfile;
        if (profile != null)
        {
            var prefs = profile.GetOrCreateAnnotatorPrefs();
            prefs.WeightSlots = new List<int>(sanitized);
        }
    }

    private void OnEditorPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM_BodyTypeProfileEditor.SelectedProfile))
        {
            BindToProfile(_editor.SelectedProfile);
        }
    }

    private void BindToProfile(VM_BodyTypeProfile profile)
    {
        if (_watchedProfile != null)
        {
            _watchedProfile.Measurements.CollectionChanged -= OnProfileMeasurementsChanged;
        }
        _watchedProfile = profile;
        if (_watchedProfile != null)
        {
            _watchedProfile.Measurements.CollectionChanged += OnProfileMeasurementsChanged;
        }

        Rows.Clear();
        RebuildColumnsFromProfile();
        RebuildWeightSlotsFromProfile();

        // Auto-populate from the shared cache. If the user (or a prior session) ran a
        // Match Presets scan that filled MeasurementCache, the table shows those rows
        // immediately without forcing a Scan click here.
        PopulateRowsFromCache();
        if (profile == null)
            ScanStatus = "No profile selected.";
        else if (Rows.Count > 0)
            ScanStatus = $"Loaded {Rows.Count} row(s) from cache.";
        else
            ScanStatus = "Press Scan to populate the table.";
    }

    /// <summary>Rebuilds <see cref="Rows"/> from the profile's shared
    /// <see cref="VM_BodyTypeProfile.MeasurementCache"/>. Filters cache entries to the
    /// configured <see cref="WeightSlots"/> so the table reflects the user's per-profile
    /// weight preference; entries from a different weight set (e.g. a Match Presets scan
    /// at the global default weights) appear if they fall in the slot list. No mesh work,
    /// no viewer dependency.</summary>
    private void PopulateRowsFromCache()
    {
        Rows.Clear();
        var profile = _watchedProfile;
        if (profile == null || profile.MeasurementCache.Count == 0) return;

        var allowedWeights = new HashSet<int>(WeightSlots);
        var defs = profile.Measurements
            .Where(m => m != null && !string.IsNullOrEmpty(m.Name))
            .Select(m => m.Name)
            .ToList();

        // Stable order: gender, then preset label, then weight — matches the live scan's
        // output ordering so re-scanning a populated table doesn't reorder rows.
        var ordered = profile.MeasurementCache
            .Where(kv => allowedWeights.Contains(kv.Key.Weight))
            .OrderBy(kv => kv.Key.Gender)
            .ThenBy(kv => kv.Key.PresetLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Weight);

        foreach (var kv in ordered)
        {
            var (label, gender, weight) = kv.Key;
            var entry = kv.Value;
            var row = new VM_PresetAnnotationRow(label, gender, weight)
            {
                HasTopologyMismatch = entry.TopologyMismatch,
            };

            // Indexer binding requires a key for every column the grid renders. Pull from
            // cached measurements (null = "evaluator failed to compute") and from the
            // current profile's def list so newly-added defs without cached values still
            // show up as empty cells.
            foreach (var name in defs)
            {
                row.MeasurementValues[name] = entry.Measurements.TryGetValue(name, out var v) ? v : null;
            }

            // Hydrate user-applied annotations so re-binding preserves draft state.
            var existing = profile.FindAnnotation(label, gender, weight);
            if (existing != null)
            {
                foreach (var d in existing.Descriptors)
                {
                    if (d == null) continue;
                    row.CurrentDescriptors.Add(new BodyShapeDescriptor.LabelSignature
                    {
                        Category = d.Category,
                        Value = d.Value
                    });
                }
            }

            Rows.Add(row);
        }
    }

    private void OnProfileMeasurementsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildColumnsFromProfile();
    }

    private void RebuildColumnsFromProfile()
    {
        AllColumns.Clear();
        var profile = _watchedProfile;
        if (profile == null) { RebuildVisibleColumns(); return; }
        var prefs = profile.GetOrCreateAnnotatorPrefs();
        var visibleSet = new HashSet<string>(prefs.VisibleMeasurementColumns ?? new List<string>(), StringComparer.Ordinal);
        // Empty preference = show all measurements (default for new profiles).
        bool showAllByDefault = visibleSet.Count == 0;
        foreach (var m in profile.Measurements)
        {
            if (m == null || string.IsNullOrEmpty(m.Name)) continue;
            AllColumns.Add(new VM_AnnotationColumn
            {
                MeasurementName = m.Name,
                Header = m.Name,
                IsVisible = showAllByDefault || visibleSet.Contains(m.Name),
            });
        }
        RebuildVisibleColumns();
    }

    private void RebuildVisibleColumns()
    {
        VisibleColumns.Clear();
        foreach (var c in AllColumns)
        {
            if (c.IsVisible) VisibleColumns.Add(c);
        }
    }

    private void RebuildWeightSlotsFromProfile()
    {
        WeightSlots.Clear();
        var profile = _watchedProfile;
        if (profile == null) return;
        var prefs = profile.GetOrCreateAnnotatorPrefs();
        var slots = prefs.WeightSlots != null && prefs.WeightSlots.Count > 0
            ? prefs.WeightSlots
            : new List<int> { 0, 25, 50, 75, 100 };
        foreach (var w in slots) WeightSlots.Add(Math.Clamp(w, 0, 100));
    }

    private void PersistColumnVisibility()
    {
        var profile = _watchedProfile;
        if (profile == null) return;
        var prefs = profile.GetOrCreateAnnotatorPrefs();
        prefs.VisibleMeasurementColumns = AllColumns
            .Where(c => c.IsVisible)
            .Select(c => c.MeasurementName)
            .ToList();
    }
}

/// <summary>One column in the annotation table -- a single measurement, plus the user's
/// visibility preference for it. Persisted via the profile's
/// <see cref="AnnotatorPreferences.VisibleMeasurementColumns"/> list.</summary>
public class VM_AnnotationColumn : VM
{
    /// <summary>Name of the <see cref="MeasurementDefinition"/> this column displays. Used as
    /// the binding key into <see cref="VM_PresetAnnotationRow.MeasurementValues"/> and as the
    /// stable identifier in the persisted visibility list.</summary>
    public string MeasurementName { get; set; } = "";

    /// <summary>Display header for the DataGrid column. Defaults to the measurement name.</summary>
    public string Header { get; set; } = "";

    /// <summary>True when the column should be rendered in the DataGrid. Toggled by the
    /// column-visibility flyout.</summary>
    public bool IsVisible { get; set; } = true;
}
