using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Windows.Media;
using System.Windows.Threading;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// UI editor for <see cref="Settings_OBody.BodyTypeProfiles"/>. Hosts a list of
/// <see cref="VM_BodyTypeProfile"/> rows; the selected profile drives the right-side panels
/// (key vertices, measurements, rules, labeled examples). Owns a dedicated
/// <see cref="VM_CharacterViewer"/> + searchable BodySlide preset picker so the user can
/// author profiles without flipping back to the BodySlides menu.
/// </summary>
public class VM_BodyTypeProfileEditor : VM
{
    public delegate VM_BodyTypeProfileEditor Factory();

    private readonly Logger _logger;
    private readonly Func<VM_SettingsOBody> _oBodyVM;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _filterFactory;

    // Profile currently subscribed for BodyTypeName-change notifications, so the preset
    // dropdown re-filters when the user edits the profile's body-type assignment. Swapped
    // in the SelectedProfile PropertyChanged handler.
    private VM_BodyTypeProfile? _watchedProfile;

    private void OnWatchedProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM_BodyTypeProfile.BodyTypeName))
        {
            RebuildFilteredPresets();
        }
    }

    public VM_BodyTypeProfileEditor(
        Logger logger,
        Func<VM_CharacterViewer> characterViewerFactory,
        Func<VM_SettingsOBody> oBodyVM,
        IEnvironmentStateProvider environmentProvider,
        PatcherState patcherState,
        VM_BodyShapeDescriptorSelectionMenu.Factory filterFactory)
    {
        _logger = logger;
        _oBodyVM = oBodyVM;
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _filterFactory = filterFactory;

        CharacterViewer = characterViewerFactory();
        CharacterViewer.Mode = ViewerMode.ReadOnly;
        CharacterViewer.ShowClassifierControls = true;
        CharacterViewer.DisposeWith(this);

        // ApplyBodySlide may defer to _pendingBodySlide when the scene isn't yet rebuilt
        // (LoadNpcAsync returns before ProcessPendingScene runs on the GL thread). The
        // drain path runs later and calls ApplyBodySlide internally — RefreshPreviewAsync's
        // in-line RefreshMeasurementValues would have already run on an empty scene by then.
        // Subscribing here guarantees a post-deform recompute regardless of path.
        CharacterViewer.BodySlideApplied += () => SelectedProfile?.RefreshMeasurementValues();

        // When the viewer's GL context is swapped out from under it (user navigated off
        // the settings page and returned, which recreates UC_CharacterViewer with a fresh
        // GL context), the viewer has dropped its scene + GL state. Re-issue whatever
        // preview the user last selected so they don't have to click the preset again
        // to see their character.
        CharacterViewer.GlContextReset += () =>
        {
            if (SelectedPreset != null) _ = RefreshPreviewAsync();
        };

        AvailableWeights = new ObservableCollection<int> { 0, 25, 50, 75, 100 };

        AddProfile = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var profile = new VM_BodyTypeProfile(new BodyTypeProfile { Name = "New Profile" }, this);
                Profiles.Add(profile);
                SelectedProfile = profile;
            });

        DeleteSelectedProfile = new RelayCommand(
            canExecute: _ => SelectedProfile != null,
            execute: _ =>
            {
                var p = SelectedProfile;
                if (p == null) return;
                int idx = Profiles.IndexOf(p);
                Profiles.Remove(p);
                SelectedProfile = Profiles.Count == 0
                    ? null
                    : Profiles[Math.Min(idx, Profiles.Count - 1)];
            });

        ExportSelectedProfile = new RelayCommand(
            canExecute: _ => SelectedProfile != null,
            execute: _ => ExportProfile(SelectedProfile));

        ImportProfile = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DoImportProfile());

        RefreshPresetList = new RelayCommand(
            canExecute: _ => true,
            execute: _ => RebuildAvailablePresets());

        ScanAllPresetsCommand = new RelayCommand(
            canExecute: _ => !IsScanning && SelectedProfile != null,
            execute: _ => _ = RunScanAsync());

        CancelScanCommand = new RelayCommand(
            canExecute: _ => IsScanning,
            execute: _ => CancelScan());

        LoadScanResultCommand = new RelayCommand(
            canExecute: x => x is VM_PresetScanRow && !IsScanning,
            execute: x => { if (x is VM_PresetScanRow row) LoadScanResultInViewer(row); });

        AnnotationTable = new VM_PresetAnnotationTable(this);
        AnnotationEditor = new VM_PresetAnnotationEditor(this, _filterFactory);
        SuggestMeasurements = new VM_SuggestMeasurementsPanel(this);
        SuggestRules = new VM_SuggestRulesPanel(this);

        VM_CharacterViewer.AnyKeyVertexPicked += OnAnyKeyVertexPicked;
        VM_CharacterViewer.AnyKeyVertexBoxPicked += OnAnyKeyVertexBoxPicked;

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(PreviewGender):
                    RebuildAvailablePresets();
                    _ = RefreshPreviewAsync();
                    break;
                case nameof(PresetFilterText):
                    RebuildFilteredPresets();
                    break;
                case nameof(SelectedPreset):
                case nameof(PreviewWeight):
                case nameof(PreviewNpcOverride):
                    _ = RefreshPreviewAsync();
                    break;
                case nameof(SelectedProfile):
                    // Swap BodyTypeName-change subscription from the previous profile to the
                    // new one so the preset dropdown re-filters when the user edits the
                    // profile's body-type field, then rebuild now to reflect the new profile.
                    if (_watchedProfile != null)
                        _watchedProfile.PropertyChanged -= OnWatchedProfilePropertyChanged;
                    _watchedProfile = SelectedProfile;
                    if (_watchedProfile != null)
                        _watchedProfile.PropertyChanged += OnWatchedProfilePropertyChanged;
                    if (SelectedProfile != null)
                    {
                        SelectedProfile.AttachViewer(CharacterViewer);
                        SelectedProfile.RefreshMeasurementValues();
                    }
                    RebuildFilteredPresets();
                    // Match-Presets tab reflects the newly-selected profile's scan cache.
                    // If the profile already has cached measurements (from any earlier scan
                    // — Match Presets or Label-Then-Suggest), derive the descriptor list
                    // from cache + current rules so the table populates without forcing a
                    // re-scan on profile switch.
                    if (SelectedProfile != null && SelectedProfile.MeasurementCache.Count > 0)
                    {
                        SelectedProfile.RebuildScanResultsFromCache(SelectedProfile.DumpToModel(), includeDrafts: true);
                    }
                    ScanCacheStale = SelectedProfile?.ScanResultsStale ?? true;
                    ScanStatus = SelectedProfile == null
                        ? "No profile selected."
                        : (SelectedProfile.ScanResults.Count == 0 ? "No scan yet." : $"Cached scan: {SelectedProfile.ScanResults.Count} preset-weight combinations.");
                    RebuildWeightFilterOptions();
                    RefreshMatchingPresets();
                    break;
                case nameof(SelectedMatchRow):
                    // Arrow-key navigation in the Match Presets list auto-previews each row.
                    if (SelectedMatchRow != null && !IsScanning)
                        LoadScanResultInViewer(SelectedMatchRow);
                    break;
            }
        };

        // Preset list is populated on first view Loaded (see UC_BodyTypeProfileEditor.xaml.cs):
        // calling _oBodyVM() here would re-enter VM_SettingsOBody's ctor, which depends on
        // this editor, causing a DI stack overflow.
    }

    public ObservableCollection<VM_BodyTypeProfile> Profiles { get; } = new();
    public VM_BodyTypeProfile? SelectedProfile { get; set; }

    /// <summary>Body-type names sourced from <see cref="Settings_OBody.BodyTypeRegistry"/> so the per-profile dropdown stays consistent with the registry editor.</summary>
    public ObservableCollection<string> AvailableBodyTypeNames { get; } = new();

    /// <summary>Descriptor signatures available for use in rules/labeled examples. Sourced from <see cref="Settings_OBody.TemplateDescriptors"/>.</summary>
    public ObservableCollection<BodyShapeDescriptor.LabelSignature> AvailableDescriptors { get; } = new();

    public RelayCommand AddProfile { get; }
    public RelayCommand DeleteSelectedProfile { get; }
    public RelayCommand ExportSelectedProfile { get; }
    public RelayCommand ImportProfile { get; }
    public RelayCommand RefreshPresetList { get; }
    public RelayCommand ScanAllPresetsCommand { get; }
    public RelayCommand CancelScanCommand { get; }
    public RelayCommand LoadScanResultCommand { get; }

    /// <summary>Dedicated 3D viewer embedded in the editor. Drives both preset preview
    /// and vertex picking so the user does not have to flip over to the BodySlides menu.</summary>
    public VM_CharacterViewer CharacterViewer { get; }

    /// <summary>All presets sourced from <see cref="VM_SettingsOBody.BodySlidesUI"/>, filtered
    /// by <see cref="PreviewGender"/>. Rebuilt on demand via <see cref="RefreshPresetList"/>.</summary>
    public ObservableCollection<VM_BodySlidePlaceHolder> AvailablePresets { get; } = new();

    /// <summary>Substring-filtered view of <see cref="AvailablePresets"/> driven by
    /// <see cref="PresetFilterText"/>. Bound to the searchable picker.</summary>
    public ObservableCollection<VM_BodySlidePlaceHolder> FilteredPresets { get; } = new();

    public ObservableCollection<int> AvailableWeights { get; }

    /// <summary>Descriptor-selector VM bound to the Match Presets tab. Constructed lazily via
    /// <see cref="InitializeDescriptorFilter"/> from <see cref="VM_SettingsOBody"/> (which owns
    /// the <c>DescriptorUI</c> and <c>RaceGroupings</c> that the factory needs). Null until
    /// initialized — the XAML tolerates that by hiding the filter pane.</summary>
    public VM_BodyShapeDescriptorSelectionMenu DescriptorFilter { get; private set; }

    /// <summary>Presets whose cached scan results match the current <see cref="DescriptorFilter"/>
    /// selection, one row per (preset, weight) combination. Populated by
    /// <see cref="RefreshMatchingPresets"/> after a scan completes or the filter selection changes.
    /// Ordered by (Gender, PresetLabel, Weight) so keyboard navigation iterates through each
    /// conforming weight for each preset in a predictable sequence.</summary>
    public ObservableCollection<VM_PresetScanRow> MatchingPresets { get; } = new();

    /// <summary>Weight filter toggles — one per weight slot present in the scan cache. All
    /// selected by default; user unticks weights they don't want to see. Rebuilt after each
    /// scan to reflect whatever weights are actually represented in the cache.</summary>
    public ObservableCollection<VM_WeightFilterOption> WeightFilterOptions { get; } = new();

    /// <summary>Currently-selected row in the Match Presets list. Assigning it auto-loads the
    /// preset at the row's weight, so arrow-key navigation immediately previews each match.</summary>
    public VM_PresetScanRow SelectedMatchRow { get; set; }

    /// <summary>Human-readable status string for the Match Presets tab — "Scanning 23/84: X"
    /// during a run, summary counts after, or a "results stale" nudge when a rule/measurement/
    /// key-vertex edit invalidated the cache.</summary>
    public string ScanStatus { get; set; } = "No scan yet.";

    /// <summary>True while a scan is running; button state + spinner visibility bind to this.</summary>
    public bool IsScanning { get; set; }

    /// <summary>0..100 progress for the progress bar.</summary>
    public int ScanProgressPercent { get; set; }

    /// <summary>True when the cached scan results are out of date (an edit happened after the
    /// last scan). Prompts the user to re-scan before trusting the filter output.</summary>
    public bool ScanCacheStale { get; set; }

    /// <summary>Toggle on the Match Presets tab. When enabled, <see cref="RunScanAsync"/>
    /// emits structured diagnostic lines (target count, viewer/key-vertex/fingerprint shape
    /// comparison, first-iteration measurement snapshot, first-vs-last identity check, and
    /// summary) to help diagnose "scan returns zero matches" issues. Off by default so the
    /// Status Log isn't noisy during normal use.</summary>
    public bool VerboseScan { get; set; }

    public string PresetFilterText { get; set; } = "";
    public VM_BodySlidePlaceHolder? SelectedPreset { get; set; }
    public Gender PreviewGender { get; set; } = Gender.Female;
    public int PreviewWeight { get; set; } = 50;

    /// <summary>Optional NPC override. When null, the configured per-weight preview NPC is used
    /// (same policy as <see cref="VM_BodySlideSetting.RefreshPreview"/>).</summary>
    public FormKey PreviewNpcOverride { get; set; } = FormKey.Null;

    /// <summary>Exposed for the NPC picker's scoped-types filter.</summary>
    public IEnumerable<Type> NPCPickerFormKeys { get; } = typeof(INpcGetter).AsEnumerable();

    /// <summary>Current environment link cache; bound by the NPC picker's LinkCache. Public
    /// so the XAML FormKeyPicker can resolve candidate NPC records.</summary>
    public ILinkCache? lk { get; private set; }

    public override void Dispose()
    {
        VM_CharacterViewer.AnyKeyVertexPicked -= OnAnyKeyVertexPicked;
        VM_CharacterViewer.AnyKeyVertexBoxPicked -= OnAnyKeyVertexBoxPicked;
        base.Dispose();
    }

    public void CopyInViewModelFromModel(Settings_OBody model)
    {
        Profiles.Clear();
        AvailableBodyTypeNames.Clear();
        AvailableDescriptors.Clear();

        if (model == null) return;

        if (model.BodyTypeRegistry != null)
        {
            foreach (var entry in model.BodyTypeRegistry)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Name)) continue;
                if (!AvailableBodyTypeNames.Contains(entry.Name)) AvailableBodyTypeNames.Add(entry.Name);
            }
        }

        if (model.TemplateDescriptors != null)
        {
            foreach (var d in model.TemplateDescriptors)
            {
                if (d?.ID == null) continue;
                AvailableDescriptors.Add(new BodyShapeDescriptor.LabelSignature { Category = d.ID.Category, Value = d.ID.Value });
            }
        }

        if (model.BodyTypeProfiles != null)
        {
            foreach (var p in model.BodyTypeProfiles)
            {
                if (p == null) continue;
                Profiles.Add(new VM_BodyTypeProfile(p, this));
            }
        }

        SelectedProfile = Profiles.FirstOrDefault();
    }

    public void DumpViewModelToModel(Settings_OBody model)
    {
        if (model == null) return;
        model.BodyTypeProfiles = new List<BodyTypeProfile>();
        foreach (var vm in Profiles)
        {
            model.BodyTypeProfiles.Add(vm.DumpToModel());
        }
    }

    private void ExportProfile(VM_BodyTypeProfile? profile)
    {
        if (profile == null) return;
        var model = profile.DumpToModel();
        string defaultName = string.IsNullOrWhiteSpace(model.Name) ? "BodyTypeProfile.json" : SanitizeFileName(model.Name) + ".json";
        if (!IO_Aux.SelectFileSave("", "BodyType Profile (*.json)|*.json", ".json", "Export BodyType Profile", out string path, defaultName))
        {
            return;
        }
        JSONhandler<BodyTypeProfile>.SaveJSONFile(model, path, out bool success, out string exception);
        if (!success)
        {
            MessageWindow.DisplayNotificationOK("Export Failed", exception);
            return;
        }
        _logger?.LogMessage("BodyTypeProfileEditor: exported profile '" + model.Name + "' to " + path);
    }

    private void DoImportProfile()
    {
        if (!IO_Aux.SelectFile("", "BodyType Profile (*.json)|*.json", "Import BodyType Profile", out string path))
        {
            return;
        }
        var loaded = JSONhandler<BodyTypeProfile>.LoadJSONFile(path, out bool success, out string exception);
        if (!success || loaded == null)
        {
            MessageWindow.DisplayNotificationOK("Import Failed", exception);
            return;
        }

        // Always assign a fresh Id so imported profiles don't collide with existing ones.
        loaded.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(loaded.Name)) loaded.Name = "Imported Profile";

        var existingNames = new HashSet<string>(Profiles.Select(p => p.Name ?? ""), StringComparer.OrdinalIgnoreCase);
        string baseName = loaded.Name;
        int suffix = 2;
        while (existingNames.Contains(loaded.Name))
        {
            loaded.Name = baseName + " (" + suffix + ")";
            suffix++;
        }

        var vm = new VM_BodyTypeProfile(loaded, this);
        Profiles.Add(vm);
        SelectedProfile = vm;
        _logger?.LogMessage("BodyTypeProfileEditor: imported profile '" + loaded.Name + "' from " + path);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    private void OnAnyKeyVertexPicked(VM_CharacterViewer viewer, VM_CharacterViewer.KeyVertexPick pick)
    {
        // Scope picks to the editor's own viewer so the BodySlides-menu viewer doesn't
        // bleed into profile capture when both menus are open.
        if (!ReferenceEquals(viewer, CharacterViewer)) return;

        var profile = SelectedProfile;
        if (profile == null || !profile.CapturePicks) return;
        profile.OnVertexPickedFromViewer(viewer, pick);
    }

    private void OnAnyKeyVertexBoxPicked(VM_CharacterViewer viewer, VM_CharacterViewer.KeyVertexBoxPick pick)
    {
        if (!ReferenceEquals(viewer, CharacterViewer)) return;

        var profile = SelectedProfile;
        if (profile == null || !profile.CapturePicks) return;
        profile.OnBoxPickedFromViewer(viewer, pick);
    }

    /// <summary>Wires up <see cref="DescriptorFilter"/> using the same factory + dependencies
    /// used by <see cref="VM_BodySlidesMenu.InitializeDescriptorFilter"/>. Two-phase init
    /// because the factory needs <c>VM_SettingsOBody.DescriptorUI</c> and the race-grouping
    /// collection, neither of which is resolvable at editor ctor time (DI order). Call from
    /// <see cref="VM_SettingsOBody"/> after both pieces exist.</summary>
    public void InitializeDescriptorFilter(VM_SettingsOBody oBodyVM, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
        if (_filterFactory == null || oBodyVM == null) return;
        DescriptorFilter = _filterFactory(oBodyVM.DescriptorUI, raceGroupingVMs, oBodyVM, true, DescriptorMatchMode.All, false);

        // Re-run the filter whenever the user's selection changes. The selector fires its
        // Header string off every selection/match-mode change, so subscribing to it is a
        // cheap catch-all for "anything in the filter changed". Skip the initial emission
        // so we don't fire before the scan has any data.
        this.WhenAnyValue(x => x.DescriptorFilter.Header)
            .Skip(1)
            .Subscribe(_ => RefreshMatchingPresets())
            .DisposeWith(this);

        // Same DI prerequisites (DescriptorUI + race groupings) as the filter, so piggyback
        // on this call site to wire the new annotation editor's descriptor menu too.
        AnnotationEditor.InitializeMenu(oBodyVM, raceGroupingVMs);
    }

    /// <summary>
    /// Rebuilds <see cref="AvailablePresets"/> from the matching gender's BodySlide list
    /// on <see cref="VM_SettingsOBody.BodySlidesUI"/>. Also refreshes
    /// <see cref="FilteredPresets"/> so the picker reflects any current filter text.
    /// Safe to call before the BodySlides menu has been constructed — no-op in that case.
    /// </summary>
    public void RebuildAvailablePresets()
    {
        AvailablePresets.Clear();
        var menu = _oBodyVM?.Invoke()?.BodySlidesUI;
        if (menu == null)
        {
            RebuildFilteredPresets();
            return;
        }

        var source = PreviewGender == Gender.Male ? menu.BodySlidesMale : menu.BodySlidesFemale;
        if (source != null)
        {
            foreach (var ph in source.OrderBy(p => p?.Label ?? "", StringComparer.OrdinalIgnoreCase))
            {
                if (ph == null) continue;
                AvailablePresets.Add(ph);
            }
        }
        RebuildFilteredPresets();
    }

    private void RebuildFilteredPresets()
    {
        FilteredPresets.Clear();
        string filter = PresetFilterText?.Trim() ?? "";
        bool hasFilter = filter.Length > 0;

        // Body-type gate: only show presets whose SliderGroup matches the profile's
        // BodyTypeName (i.e., presets actually assigned to this body in the registry).
        // Skip gating when no profile is selected or BodyTypeName is unset, so the user
        // sees all presets instead of an empty dropdown that would look broken.
        string bodyType = SelectedProfile?.BodyTypeName?.Trim() ?? "";
        bool gateByBodyType = bodyType.Length > 0;

        foreach (var ph in AvailablePresets)
        {
            if (gateByBodyType)
            {
                var sg = ph?.AssociatedModel?.SliderGroup ?? "";
                if (!string.Equals(sg, bodyType, StringComparison.OrdinalIgnoreCase)) continue;
            }
            if (!hasFilter || (ph != null && ph.Label != null
                && ph.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                FilteredPresets.Add(ph!);
            }
        }
    }

    /// <summary>
    /// Loads the configured preview NPC for the current weight and applies the selected
    /// preset's BodySlide deformation. Mirrors <see cref="VM_BodySlideSetting.RefreshPreview"/>'s
    /// NPC-resolution policy so behavior matches the main BodySlides menu.
    /// </summary>
    private async System.Threading.Tasks.Task RefreshPreviewAsync()
    {
        try
        {
            var preset = SelectedPreset?.AssociatedModel;
            if (preset == null || lk == null) return;

            FormKey npc = FormKey.Null;
            if (!PreviewNpcOverride.IsNull)
            {
                npc = PreviewNpcOverride;
            }
            else
            {
                var preview = _patcherState?.OBodySettings?.PreviewNpcs;
                if (preview != null && preview.WeightPreviewNpcs.TryGetValue(PreviewWeight, out var pair) && pair != null)
                {
                    npc = PreviewGender == Gender.Female ? pair.FemaleNpc : pair.MaleNpc;
                }
            }

            if (npc.IsNull)
            {
                _logger?.LogMessage("BodyTypeProfileEditor: no preview NPC configured for weight " + PreviewWeight + " (" + PreviewGender + ")");
                return;
            }

            await CharacterViewer.LoadNpcAsync(npc, lk);
            CharacterViewer.ApplyBodySlide(preset, PreviewWeight);

            // Point the active profile at this viewer so live measurement readouts have
            // a source and pick-capture routes into the right profile by default.
            if (SelectedProfile != null)
            {
                SelectedProfile.AttachViewer(CharacterViewer);
                SelectedProfile.RefreshMeasurementValues();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("BodyTypeProfileEditor.RefreshPreviewAsync failed: " + ExceptionLogger.GetExceptionStack(ex));
        }
    }

    /// <summary>Logs a one-line diagnostic to the patcher's main log. Used by sub-VMs (suggest pass, etc.).</summary>
    internal void LogMessage(string message)
    {
        _logger?.LogMessage("BodyTypeProfileEditor: " + message);
    }

    // ---------- Match Presets: scan + filter ----------

    private System.Threading.CancellationTokenSource _scanCts;

    /// <summary>Runs the classifier (including drafts) across every BodySlide preset whose
    /// <c>SliderGroup</c> matches <see cref="VM_BodyTypeProfile.BodyTypeName"/>, at every
    /// weight in <c>DefaultWeightSlots</c>, caching matched descriptors on the profile. Drives
    /// the viewer sequentially (GL is UI-thread-only) so the user sees the mesh flicker
    /// through presets; progress reports via <see cref="ScanStatus"/> and
    /// <see cref="ScanProgressPercent"/>. Cancellable via <see cref="_scanCts"/>.</summary>
    public async System.Threading.Tasks.Task RunScanAsync()
    {
        var profile = SelectedProfile;
        if (profile == null || IsScanning) return;
        var menu = _oBodyVM?.Invoke()?.BodySlidesUI;
        if (menu == null) return;
        var viewer = CharacterViewer;
        if (viewer == null)
        {
            ScanStatus = "No viewer available.";
            return;
        }

        var weightSlots = _patcherState?.OBodySettings?.DefaultWeightSlots?.ToList();
        if (weightSlots == null || weightSlots.Count == 0) weightSlots = new List<int> { 0, 100 };

        // Reset progress/status before flipping IsScanning so the UI reflects the fresh
        // scan immediately (Fody fires PropertyChanged on assignment; the yield below
        // lets WPF actually paint the reset before the first ApplyBodySlide blocks the
        // UI thread). Must yield below DispatcherPriority.Render — Task.Yield posts at
        // Normal which preempts Render, leaving the bindings unpainted.
        ScanProgressPercent = 0;
        ScanStatus = "Initializing scan...";
        IsScanning = true;
        _scanCts = new System.Threading.CancellationTokenSource();
        var ct = _scanCts.Token;
        var savedPreset = SelectedPreset;
        var savedWeight = PreviewWeight;
        await Dispatcher.Yield(DispatcherPriority.Background);

        try
        {
            // Same SliderGroup == BodyTypeName match as the Key Vertices preset dropdown
            // (VM_BodyTypeProfileEditor.RebuildFilteredPresets). SliderGroup is populated
            // at load time by SaveLoader → ImportBodySlides so both paths see the same
            // non-empty values.
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
            if (VerboseScan)
            {
                _logger?.LogMessage($"BodyTypeProfile scan: body type '{bodyType}' matched {targets.Count} preset(s) × {weightSlots.Count} weight(s) = {total} evaluations.");

                // Surface viewer state vs profile expectations so the user can see at a glance
                // whether the loaded mesh's shape names match the profile's key-vertex
                // ShapeNames and the captured fingerprint. A topology mismatch + 100% failed
                // measurements is almost always a shape-name disagreement here.
                var viewerShapes = viewer.GetCurrentShapeVertexCounts();
                var viewerShapeStr = viewerShapes.Count == 0 ? "(no mesh loaded)" : string.Join(", ", viewerShapes.Select(kv => $"{kv.Key}={kv.Value}"));
                var kvShapes = profile.KeyVertices.Select(k => k.ShapeName).Distinct().OrderBy(s => s).ToList();
                var fingerprintShapes = _patcherState?.OBodySettings?.BodyTypeProfiles
                    ?.FirstOrDefault(p => p.Id == profile.Id)?.Fingerprint?.ShapeVertexCounts?.Keys
                    .OrderBy(s => s).ToList() ?? new List<string>();
                _logger?.LogMessage($"BodyTypeProfile scan diag: viewer shapes = [{viewerShapeStr}]");
                _logger?.LogMessage($"BodyTypeProfile scan diag: key-vertex ShapeNames = [{string.Join(", ", kvShapes)}]; profile fingerprint shapes = [{string.Join(", ", fingerprintShapes)}]");
            }

            if (total == 0)
            {
                profile.ScanResults.Clear();
                RebuildWeightFilterOptions();
                RefreshMatchingPresets();
                ScanStatus = $"No presets tagged with SliderGroup=\"{bodyType}\". Check the BodySlides menu or the profile's Body Type field.";
                return;
            }

            // KeyVertices / MeasurementDefinition changes invalidate the numbers in the cache.
            // Drop them now so the iteration below misses on every key and re-evaluates.
            // Rule-only edits leave MeasurementCacheStale false, so the cache survives —
            // re-scanning after a rule edit is then pure rule re-evaluation (cache hits
            // everywhere).
            if (profile.MeasurementCacheStale)
                profile.MeasurementCache.Clear();

            var profileModel = profile.DumpToModel();

            // Compute the work set: keys this scan needs that aren't in the cache yet. A
            // prior scan from the other tab (Label-Then-Suggest) at overlapping weights
            // populates cache entries that this scan can reuse — that's the whole point of
            // the shared cache.
            var missing = new List<(VM_BodySlidePlaceHolder ph, Gender gender, int weight)>();
            foreach (var (ph, gender) in targets)
            {
                var label = ph.AssociatedModel.Label ?? "";
                foreach (int weight in weightSlots)
                {
                    if (!profile.MeasurementCache.ContainsKey((label, gender, weight)))
                        missing.Add((ph, gender, weight));
                }
            }
            int reused = total - missing.Count;

            // All-hit fast path: every (preset, weight) slot is already cached. No mesh
            // work, no preview-NPC load, no viewer dependency. Just re-derive descriptors
            // from cached measurements + current rules.
            if (missing.Count == 0)
            {
                profile.RebuildScanResultsFromCache(profileModel, includeDrafts: true);
                int withMatchesAll = profile.ScanResults.Count(kv => kv.Value.Count > 0);
                int emptyAll = profile.ScanResults.Count - withMatchesAll;
                ScanStatus = $"Rebuilt from cache: {total} slice(s) reused, no scan needed. {withMatchesAll} with matches, {emptyAll} empty.";
                profile.ScanResultsStale = false;
                profile.MeasurementCacheStale = false;
                ScanCacheStale = false;
                ScanProgressPercent = 100;
                RebuildWeightFilterOptions();
                RefreshMatchingPresets();
                return;
            }

            // Pre-flight: the scan deforms whatever body mesh is currently in the viewer
            // through every preset. If the viewer is empty (first-open with no preset
            // selected; GL context loss with no SelectedPreset to auto-recover from;
            // viewer reset / dispose), every ApplyBodySlide call below would hit the
            // _cachedBodyMeshes.Count == 0 early-return path in CharacterViewer and
            // produce N queue-and-discard cycles with zero deformations and zero matches.
            // Auto-load a configured preview NPC so the scan is self-sufficient.
            if (viewer.GetCurrentShapeVertexCounts().Count == 0)
            {
                if (lk == null)
                {
                    ScanStatus = "No load order link cache available — cannot auto-load a preview NPC.";
                    return;
                }

                Gender loadGender = missing[0].gender;
                var previewSettings = _patcherState?.OBodySettings?.PreviewNpcs;
                FormKey loadNpc = FormKey.Null;
                if (previewSettings != null)
                {
                    if (previewSettings.WeightPreviewNpcs.TryGetValue(PreviewWeight, out var pair) && pair != null)
                    {
                        loadNpc = loadGender == Gender.Female ? pair.FemaleNpc : pair.MaleNpc;
                    }
                    // Fallback to any other configured weight slot for the same gender so the
                    // scan still proceeds when PreviewWeight's slot is empty.
                    if (loadNpc.IsNull)
                    {
                        foreach (var kv in previewSettings.WeightPreviewNpcs)
                        {
                            if (kv.Value == null) continue;
                            var candidate = loadGender == Gender.Female ? kv.Value.FemaleNpc : kv.Value.MaleNpc;
                            if (!candidate.IsNull) { loadNpc = candidate; break; }
                        }
                    }
                }

                if (loadNpc.IsNull)
                {
                    ScanStatus = $"No preview NPC configured for {loadGender}. Set one in OBody settings → Preview NPCs.";
                    return;
                }

                ScanStatus = $"Loading preview NPC for {loadGender}...";
                await viewer.LoadNpcAsync(loadNpc, lk);

                // LoadNpcAsync returns once the scene is queued; the GL upload that populates
                // _cachedBodyMeshes happens on the next render frame in ProcessPendingScene.
                // Poll until shapes appear so the first iteration's ApplyBodySlide hits the
                // direct-deform path. 60 × 50ms = 3s ceiling: plenty for a normal load,
                // short enough to fail loudly if something is wrong.
                int waitTicks = 0;
                while (viewer.GetCurrentShapeVertexCounts().Count == 0 && waitTicks++ < 60 && !ct.IsCancellationRequested)
                {
                    await System.Threading.Tasks.Task.Delay(50);
                }

                if (viewer.GetCurrentShapeVertexCounts().Count == 0)
                {
                    ScanStatus = "Preview NPC load did not commit a renderable scene — cannot scan.";
                    return;
                }
            }

            int done = 0;
            ScanProgressPercent = 0;

            // Diagnostics for "scan returns zero matches" investigations. Captured even when
            // VerboseScan is off (cheap dictionary copies); only emitted on completion when
            // the toggle is on. Identical first/last snapshots across many presets imply the
            // mesh isn't being re-deformed (ApplyBodySlide queueing or stale CpuPositions).
            // Only iterated entries (cache misses) participate; cached hits don't re-deform.
            Dictionary<string, float> firstMeasSnapshot = null;
            Dictionary<string, float> lastMeasSnapshot = null;
            string firstLabel = null;
            string lastLabel = null;

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
                // scan and only repainting once at the end. Background (4) is below
                // Render, so the dispatcher must drain Render before resuming us.
                // Doubles as the "wait for deferred-drain" yield ApplyBodySlide
                // sometimes needs when the scene is mid-rebuild.
                await Dispatcher.Yield(DispatcherPriority.Background);

                var result = BodySlideMeasurementEvaluator.Evaluate(viewer, profileModel, includeDrafts: true);

                // Persist measurements (not descriptors) into the shared cache. Descriptors
                // are derived later via DeriveDescriptorsFor + the profile's current rules.
                // Storing every defined measurement (null for failures) preserves the
                // "this measurement could not be computed" signal through the cache.
                var entry = new VM_BodyTypeProfile.MeasurementCacheEntry { TopologyMismatch = result.TopologyMismatch };
                if (profileModel.Measurements != null)
                {
                    foreach (var def in profileModel.Measurements)
                    {
                        if (def == null || string.IsNullOrEmpty(def.Name)) continue;
                        entry.Measurements[def.Name] = result.Measurements.TryGetValue(def.Name, out var v)
                            ? (float?)v
                            : null;
                    }
                }
                profile.MeasurementCache[(model.Label ?? "", gender, weight)] = entry;

                if (firstMeasSnapshot == null)
                {
                    firstMeasSnapshot = new Dictionary<string, float>(result.Measurements);
                    firstLabel = $"{model.Label}@W{weight}";
                    if (VerboseScan)
                    {
                        _logger?.LogMessage($"BodyTypeProfile scan diag: first iter '{firstLabel}' → measurements={result.Measurements.Count}, failed={result.FailedMeasurements.Count}, rule matches={result.Descriptors.Count}, topologyMismatch={result.TopologyMismatch}");
                    }
                }
                lastMeasSnapshot = new Dictionary<string, float>(result.Measurements);
                lastLabel = $"{model.Label}@W{weight}";

                done++;
                ScanProgressPercent = (done * 100) / missing.Count;
            }

            if (VerboseScan && firstMeasSnapshot != null && lastMeasSnapshot != null)
            {
                bool identical = firstMeasSnapshot.Count == lastMeasSnapshot.Count
                    && firstMeasSnapshot.All(kv => lastMeasSnapshot.TryGetValue(kv.Key, out var v) && Math.Abs(v - kv.Value) < 1e-4f);
                string firstSample = string.Join(", ", firstMeasSnapshot.Take(5).Select(kv => $"{kv.Key}={kv.Value:F3}"));
                string lastSample = string.Join(", ", lastMeasSnapshot.Take(5).Select(kv => $"{kv.Key}={kv.Value:F3}"));
                if (identical)
                {
                    _logger?.LogMessage($"BodyTypeProfile scan diag: FIRST and LAST measurements IDENTICAL across {done} evaluations — mesh not re-deforming. Sample: {firstSample}");
                }
                else
                {
                    _logger?.LogMessage($"BodyTypeProfile scan diag: first '{firstLabel}' vs last '{lastLabel}' differ. First: {firstSample}. Last: {lastSample}");
                }
            }

            // Restore the user's previously-loaded preset so the viewer isn't parked on a
            // random scan target when the run finishes.
            if (savedPreset?.AssociatedModel != null)
            {
                viewer.ApplyBodySlide(savedPreset.AssociatedModel, savedWeight);
            }

            if (ct.IsCancellationRequested)
            {
                ScanStatus = $"Scan cancelled at {done}/{missing.Count}.";
                // Even on cancel, derive descriptors for whatever we did populate so the
                // partial scan is visible in the Match Presets list.
                profile.RebuildScanResultsFromCache(profileModel, includeDrafts: true);
            }
            else
            {
                profile.RebuildScanResultsFromCache(profileModel, includeDrafts: true);
                int withMatches = profile.ScanResults.Count(kv => kv.Value.Count > 0);
                int empty = profile.ScanResults.Count - withMatches;
                ScanStatus = reused > 0
                    ? $"Scan complete: {done} evaluated, {reused} cached. {withMatches} with matches, {empty} empty."
                    : $"Scan complete: {done} evaluations across {targets.Count} preset(s). {withMatches} with matches, {empty} empty.";
                if (VerboseScan)
                {
                    _logger?.LogMessage($"BodyTypeProfile scan summary: {withMatches} (preset, weight) combos produced ≥1 match; {empty} produced none.");
                }
                profile.ScanResultsStale = false;
                profile.MeasurementCacheStale = false;
                ScanCacheStale = false;
            }
            RebuildWeightFilterOptions();
            RefreshMatchingPresets();
        }
        catch (Exception ex)
        {
            _logger?.LogError("BodyTypeProfile scan failed: " + ExceptionLogger.GetExceptionStack(ex));
            ScanStatus = "Scan failed (see log).";
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    /// <summary>Rebuilds <see cref="MatchingPresets"/> from the current profile's scan cache
    /// filtered by the current <see cref="DescriptorFilter"/> selection AND the current
    /// <see cref="WeightFilterOptions"/> selection. Called from the filter's Header
    /// subscription, the weight-option IsSelected subscription, and <see cref="RunScanAsync"/>.
    /// Empty descriptor selection = include every scanned (preset, weight). Empty weight
    /// selection = no rows (user filtered everything out).
    /// Emits one row per (preset, weight) so keyboard navigation iterates each conforming
    /// weight for each preset in a predictable order.</summary>
    public void RefreshMatchingPresets()
    {
        var previouslySelected = SelectedMatchRow;
        MatchingPresets.Clear();
        var profile = SelectedProfile;
        if (profile == null) return;
        if (profile.ScanResults.Count == 0) return;

        var filterSelection = DescriptorFilter?.DumpToHashSet() ?? new HashSet<BodyShapeDescriptor.LabelSignature>();
        var filterMode = DescriptorFilter?.MatchMode ?? DescriptorMatchMode.All;
        var selectionKeys = filterSelection.Select(s => (s.Category, s.Value)).ToHashSet();

        var allowedWeights = WeightFilterOptions
            .Where(o => o.IsSelected)
            .Select(o => o.Weight)
            .ToHashSet();
        // If the user has the weight filter collection but none are ticked, show nothing.
        // If the collection is empty (no scan yet), allowedWeights is empty and the loop
        // short-circuits below anyway.
        bool weightFilterActive = WeightFilterOptions.Count > 0;

        var ordered = profile.ScanResults
            .OrderBy(kv => kv.Key.Gender)
            .ThenBy(kv => kv.Key.PresetLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Weight);

        foreach (var kv in ordered)
        {
            if (weightFilterActive && !allowedWeights.Contains(kv.Key.Weight)) continue;
            if (!DescriptorFilterAccepts(kv.Value, selectionKeys, filterMode)) continue;
            MatchingPresets.Add(new VM_PresetScanRow(kv.Key.PresetLabel, kv.Key.Gender, kv.Key.Weight, kv.Value));
        }

        // Try to re-select the same (preset, gender, weight) row if it still exists so the
        // ListBox selection doesn't jump to row 0 on every filter edit.
        if (previouslySelected != null)
        {
            foreach (var row in MatchingPresets)
            {
                if (row.Weight == previouslySelected.Weight
                    && row.Gender == previouslySelected.Gender
                    && string.Equals(row.PresetLabel, previouslySelected.PresetLabel, StringComparison.Ordinal))
                {
                    SelectedMatchRow = row;
                    return;
                }
            }
        }
    }

    /// <summary>Rebuilds <see cref="WeightFilterOptions"/> from the distinct weights present
    /// in the current profile's scan cache. Preserves existing <see cref="VM_WeightFilterOption.IsSelected"/>
    /// values where possible so a re-scan doesn't clobber the user's weight filter.</summary>
    private void RebuildWeightFilterOptions()
    {
        var profile = SelectedProfile;
        var priorSelections = WeightFilterOptions.ToDictionary(o => o.Weight, o => o.IsSelected);

        // Detach old subscriptions
        foreach (var o in WeightFilterOptions) o.PropertyChanged -= OnWeightFilterOptionChanged;
        WeightFilterOptions.Clear();

        if (profile == null) return;
        var weights = profile.ScanResults.Keys.Select(k => k.Weight).Distinct().OrderBy(w => w);
        foreach (var w in weights)
        {
            bool selected = priorSelections.TryGetValue(w, out var wasSelected) ? wasSelected : true;
            var opt = new VM_WeightFilterOption { Weight = w, IsSelected = selected };
            opt.PropertyChanged += OnWeightFilterOptionChanged;
            WeightFilterOptions.Add(opt);
        }
    }

    private void OnWeightFilterOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM_WeightFilterOption.IsSelected))
            RefreshMatchingPresets();
    }

    /// <summary>Bound to the Match Presets "All" weight-filter button.</summary>
    public RelayCommand SelectAllWeightsCommand => new(
        canExecute: _ => WeightFilterOptions.Any(o => !o.IsSelected),
        execute: _ => { foreach (var o in WeightFilterOptions) o.IsSelected = true; });

    /// <summary>Bound to the Match Presets "None" weight-filter button.</summary>
    public RelayCommand SelectNoWeightsCommand => new(
        canExecute: _ => WeightFilterOptions.Any(o => o.IsSelected),
        execute: _ => { foreach (var o in WeightFilterOptions) o.IsSelected = false; });

    private static bool DescriptorFilterAccepts(
        IReadOnlyList<BodyShapeDescriptor.LabelSignature> matches,
        HashSet<(string Category, string Value)> selectionKeys,
        DescriptorMatchMode mode)
    {
        if (selectionKeys.Count == 0) return true;
        var matchSet = matches.Select(m => (m.Category, m.Value)).ToHashSet();
        if (mode == DescriptorMatchMode.All)
        {
            foreach (var s in selectionKeys) if (!matchSet.Contains(s)) return false;
            return true;
        }
        // Any
        foreach (var s in selectionKeys) if (matchSet.Contains(s)) return true;
        return false;
    }

    /// <summary>Called by <see cref="VM_BodyTypeProfile.MarkScanResultsStale"/>. If the stale
    /// profile is the one currently shown, flip the editor's stale flag so the XAML shows the
    /// "Re-scan" nudge.</summary>
    internal void OnProfileScanStale(VM_BodyTypeProfile profile)
    {
        if (ReferenceEquals(profile, SelectedProfile))
        {
            ScanCacheStale = true;
        }
    }

    /// <summary>Loads a scan-result row into the editor's viewer at the row's specific weight,
    /// so arrow-key navigation in the results list flips through each matching (preset, weight)
    /// combo. Routes via <see cref="SelectedPreset"/>/<see cref="PreviewWeight"/> so the
    /// existing RefreshPreviewAsync path fires (loads NPC + applies deformation + triggers
    /// measurement refresh).</summary>
    internal void LoadScanResultInViewer(VM_PresetScanRow row)
    {
        if (row == null || IsScanning) return;
        var menu = _oBodyVM?.Invoke()?.BodySlidesUI;
        if (menu == null) return;
        var source = row.Gender == Gender.Male ? menu.BodySlidesMale : menu.BodySlidesFemale;
        VM_BodySlidePlaceHolder ph = null;
        foreach (var p in source)
        {
            if (p?.AssociatedModel?.Label == row.PresetLabel) { ph = p; break; }
        }
        if (ph == null) return;

        PreviewGender = row.Gender;
        PreviewWeight = row.Weight;
        SelectedPreset = ph;
    }

    /// <summary>Cancels an in-flight scan. Safe to call when no scan is running.</summary>
    public void CancelScan() => _scanCts?.Cancel();

    /// <summary>Bottom-section VM for the new Label-then-Suggest tab. Owns the preset
    /// annotation table, scan state, and column visibility prefs.</summary>
    public VM_PresetAnnotationTable AnnotationTable { get; }

    /// <summary>Top-section VM for the new Label-then-Suggest tab. Hosts the descriptor menu
    /// in annotation mode and two-way syncs with <see cref="VM_PresetAnnotationTable.SelectedRow"/>
    /// so picking a row in the table reloads the menu's checks for that slice.</summary>
    public VM_PresetAnnotationEditor AnnotationEditor { get; }

    /// <summary>Phase 5 panel: ranks profile measurements by how well they discriminate
    /// between annotated descriptor-value groups. Output (curated by the user) feeds Phase 6's
    /// rule synthesis.</summary>
    public VM_SuggestMeasurementsPanel SuggestMeasurements { get; }

    /// <summary>Phase 6 panel: synthesizes draft <see cref="MeasurementRule"/>s from the
    /// curated measurement list and the user's annotations. Accepted rules land in the
    /// active profile's Rules collection with IsDraft = true.</summary>
    public VM_SuggestRulesPanel SuggestRules { get; }

    /// <summary>Internal accessor so the new annotation table can iterate the same preset list
    /// as Match Presets without re-implementing the lazy <c>_oBodyVM</c> resolution.</summary>
    internal VM_BodySlidesMenu GetBodySlidesMenu() => _oBodyVM?.Invoke()?.BodySlidesUI;

    /// <summary>Wrapper around <see cref="Logger.LogError"/> so the new annotation table doesn't
    /// need a private logger reference. Mirrors the pattern <see cref="RunScanAsync"/> uses
    /// inline.</summary>
    internal void LogScanError(Exception ex)
    {
        _logger?.LogError("BodyTypeProfile annotation scan failed: " + ExceptionLogger.GetExceptionStack(ex));
    }

    /// <summary>Pre-flight helper: when the viewer's scene is empty (first-open / context loss /
    /// reset), auto-load the configured preview NPC for <paramref name="gender"/> so the scan
    /// has a base mesh to deform. Returns true when the scene is ready to scan, false when no
    /// preview NPC is configured or the load did not commit a renderable scene.
    /// <para>Mirrors the pre-flight block inside <see cref="RunScanAsync"/> so both the legacy
    /// Match Presets scan and the new annotation-table scan share the same recovery path.</para></summary>
    internal async System.Threading.Tasks.Task<bool> EnsurePreviewNpcLoadedAsync(Gender gender, System.Threading.CancellationToken ct)
    {
        var viewer = CharacterViewer;
        if (viewer == null) return false;
        if (viewer.GetCurrentShapeVertexCounts().Count > 0) return true;

        if (lk == null) return false;
        var previewSettings = _patcherState?.OBodySettings?.PreviewNpcs;
        FormKey loadNpc = FormKey.Null;
        if (previewSettings != null)
        {
            if (previewSettings.WeightPreviewNpcs.TryGetValue(PreviewWeight, out var pair) && pair != null)
            {
                loadNpc = gender == Gender.Female ? pair.FemaleNpc : pair.MaleNpc;
            }
            // Fall back to any configured weight slot for the same gender.
            if (loadNpc.IsNull)
            {
                foreach (var kv in previewSettings.WeightPreviewNpcs)
                {
                    if (kv.Value == null) continue;
                    var candidate = gender == Gender.Female ? kv.Value.FemaleNpc : kv.Value.MaleNpc;
                    if (!candidate.IsNull) { loadNpc = candidate; break; }
                }
            }
        }

        if (loadNpc.IsNull) return false;

        await viewer.LoadNpcAsync(loadNpc, lk);

        // LoadNpcAsync returns once the scene is queued; the GL upload lands on the next
        // ProcessPendingScene tick. Poll until shapes appear (60 × 50ms = 3s ceiling).
        int waitTicks = 0;
        while (viewer.GetCurrentShapeVertexCounts().Count == 0 && waitTicks++ < 60 && !ct.IsCancellationRequested)
        {
            await System.Threading.Tasks.Task.Delay(50);
        }
        return viewer.GetCurrentShapeVertexCounts().Count > 0;
    }
}

/// <summary>
/// Single-profile editor. Owns sub-collections for key vertices, measurements, rules, and
/// labeled examples. Knows how to compute live measurement values against an attached viewer's
/// current mesh state via <see cref="VM_CharacterViewer.TryGetCurrentVertex"/>.
/// </summary>
public class VM_BodyTypeProfile : VM
{
    private readonly BodyTypeProfile _source;
    private readonly VM_BodyTypeProfileEditor _parent;

    public VM_BodyTypeProfile(BodyTypeProfile source, VM_BodyTypeProfileEditor parent)
    {
        _source = source ?? new BodyTypeProfile();
        _parent = parent;

        Id = _source.Id;
        Name = _source.Name ?? "";
        BodyTypeName = _source.BodyTypeName ?? "";

        FingerprintVertexCount = _source.Fingerprint?.VertexCount ?? 0;
        FingerprintShapeCounts = _source.Fingerprint?.ShapeVertexCounts != null
            ? string.Join(", ", _source.Fingerprint.ShapeVertexCounts.Select(kv => kv.Key + ":" + kv.Value))
            : "";

        if (_source.KeyVertices != null)
        {
            foreach (var k in _source.KeyVertices)
            {
                if (k == null) continue;
                KeyVertices.Add(new VM_NamedKeyVertex(k, this));
            }
        }
        if (_source.Measurements != null)
        {
            foreach (var m in _source.Measurements)
            {
                if (m == null) continue;
                Measurements.Add(new VM_MeasurementDefinition(m, this));
            }
        }
        if (_source.Rules != null)
        {
            foreach (var r in _source.Rules)
            {
                if (r == null) continue;
                Rules.Add(new VM_MeasurementRule(r, this));
            }
        }
        if (_source.PresetAnnotations != null)
        {
            foreach (var pa in _source.PresetAnnotations)
            {
                if (pa == null) continue;
                PresetAnnotations.Add(pa);
            }
        }
        AnnotatorPrefs = _source.AnnotatorPrefs ?? new AnnotatorPreferences();

        AddMeasurement = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Measurements.Add(new VM_MeasurementDefinition(new MeasurementDefinition { Name = NextDefaultName("Measurement", Measurements.Select(x => x.Name)) }, this)));

        AddRule = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var rule = new MeasurementRule { Descriptor = new BodyShapeDescriptor.LabelSignature() };
                rule.GroupsORlogic.Add(new AndGatedMeasurementGroup());
                Rules.Add(new VM_MeasurementRule(rule, this));
            });

        CaptureFingerprintFromActiveViewer = new RelayCommand(
            canExecute: _ => ActiveViewer != null,
            execute: _ =>
            {
                if (ActiveViewer == null) return;
                var counts = ActiveViewer.GetCurrentShapeVertexCounts();
                FingerprintVertexCount = counts.Values.Sum();
                FingerprintShapeCounts = string.Join(", ", counts.Select(kv => kv.Key + ":" + kv.Value));
            });

        RemoveSelectedKeyVertex = new RelayCommand(
            canExecute: _ => SelectedKeyVertex != null,
            execute: _ =>
            {
                var k = SelectedKeyVertex;
                if (k == null) return;
                KeyVertices.Remove(k);
            });

        ShowPicksInViewer = new RelayCommand(
            canExecute: _ => ActiveViewer != null && KeyVertices.Count > 0,
            execute: _ =>
            {
                if (ActiveViewer == null) return;
                var entries = KeyVertices
                    .Where(k => !string.IsNullOrEmpty(k.ShapeName) && k.VertexIndex >= 0)
                    .Select(k => (k.ShapeName, k.VertexIndex));
                ActiveViewer.ShowKeyVerticesInViewer(entries);
            });

        // Re-evaluate live values whenever the measurement collection changes shape or
        // any row's definition fields (Kind / Axis / VertexRefA..D) are edited in the grid.
        foreach (var m in Measurements) m.PropertyChanged += OnMeasurementRowPropertyChanged;
        Measurements.CollectionChanged += (_, args) =>
        {
            if (args.OldItems != null)
                foreach (VM_MeasurementDefinition m in args.OldItems) m.PropertyChanged -= OnMeasurementRowPropertyChanged;
            if (args.NewItems != null)
                foreach (VM_MeasurementDefinition m in args.NewItems) m.PropertyChanged += OnMeasurementRowPropertyChanged;
            RefreshMeasurementValues();
        };

        // Re-evaluate when the key-vertex roster changes (a measurement may reference a
        // newly-added vertex name, or lose a deleted one).
        KeyVertices.CollectionChanged += (_, __) => RefreshMeasurementValues();

        // Scan cache invalidation. Two distinct staleness signals:
        //   * MeasurementCache stale — KeyVertices or MeasurementDefinitions changed, so the
        //     cached numbers themselves are wrong. Requires a real re-scan (mesh deformation
        //     + Evaluate). Implies ScanResults stale too.
        //   * ScanResults stale (only) — Rules changed but the underlying measurements are
        //     still valid. A "re-scan" is just rule re-evaluation against the cache, so it's
        //     instantaneous and could even auto-rebuild; today it still requires a Scan click,
        //     but that scan does no mesh work (every iteration is a cache hit).
        KeyVertices.CollectionChanged += (_, __) => MarkMeasurementCacheStale();
        foreach (var m in Measurements) m.PropertyChanged += OnMeasurementCacheInvalidatingChange;
        Measurements.CollectionChanged += (_, args) =>
        {
            if (args.OldItems != null)
                foreach (VM_MeasurementDefinition m in args.OldItems) m.PropertyChanged -= OnMeasurementCacheInvalidatingChange;
            if (args.NewItems != null)
                foreach (VM_MeasurementDefinition m in args.NewItems) m.PropertyChanged += OnMeasurementCacheInvalidatingChange;
            MarkMeasurementCacheStale();
        };
        foreach (var r in Rules) HookRuleForScanInvalidation(r);
        Rules.CollectionChanged += (_, args) =>
        {
            if (args.OldItems != null)
                foreach (VM_MeasurementRule r in args.OldItems) UnhookRuleForScanInvalidation(r);
            if (args.NewItems != null)
                foreach (VM_MeasurementRule r in args.NewItems) HookRuleForScanInvalidation(r);
            MarkScanResultsStale();
        };

        RefreshMeasurementValues();

        // Repaint the measurement-line overlay whenever the user picks a different
        // measurement. Using the raw PropertyChanged event keeps this file free of
        // additional ReactiveUI wiring (the profile's lifetime is bounded by the
        // containing editor, so we skip the IDisposable dance).
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SelectedMeasurement))
            {
                RefreshMeasurementHighlight();
            }
            else if (args.PropertyName == nameof(SelectedKeyVertex))
            {
                // When the user clicks a KeyVertex row, ask the viewer to select the
                // matching pick (if the user previously clicked "Show Picks in Viewer"
                // so the pick exists). The viewer will then turn that marker green.
                // Works for both strategies: Explicit uses the stored VertexIndex, and
                // BoundingBox uses the cached resolved index (see RefreshBoundingBoxMarkerPositions
                // and RuleMatches, which both write the resolution back to kv.VertexIndex).
                // No-ops silently when the pick isn't present in the current picks list.
                var kv = SelectedKeyVertex;
                if (kv != null
                    && ActiveViewer != null
                    && !string.IsNullOrEmpty(kv.ShapeName)
                    && kv.VertexIndex >= 0)
                {
                    ActiveViewer.RequestSelectPickByShapeAndIndex(kv.ShapeName, kv.VertexIndex);
                }

                SyncPendingBoxEditSessionWithSelection(kv);
            }
        };
    }

    private void OnMeasurementRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // LiveValue updates are the result of recomputation; re-running on that would loop.
        if (e.PropertyName == nameof(VM_MeasurementDefinition.LiveValue)) return;
        RefreshMeasurementValues();
    }

    public string Id { get; }
    public string Name { get; set; }
    public string BodyTypeName { get; set; }

    public int FingerprintVertexCount { get; set; }
    public string FingerprintShapeCounts { get; set; }

    public ObservableCollection<VM_NamedKeyVertex> KeyVertices { get; } = new();
    public ObservableCollection<VM_MeasurementDefinition> Measurements { get; } = new();
    public ObservableCollection<VM_MeasurementRule> Rules { get; } = new();

    /// <summary>Persisted draft annotations from the new Label-then-Suggest workflow. One entry
    /// per (preset, gender, weight) slice the user has touched. Edited directly (no VM wrapper)
    /// since the data is small and only mutated programmatically by the annotation editor.</summary>
    public ObservableCollection<PresetAnnotation> PresetAnnotations { get; } = new();

    /// <summary>Persisted UI prefs for the new tab (weight slots, column visibility, algorithm
    /// choices). Held by reference -- mutating fields on this instance and re-saving the parent
    /// settings file is enough to persist; no separate VM wrapper.</summary>
    public AnnotatorPreferences AnnotatorPrefs { get; private set; }

    /// <summary>Returns the persisted prefs, allocating a fresh instance on first access if the
    /// profile predates this feature. Always returns non-null.</summary>
    public AnnotatorPreferences GetOrCreateAnnotatorPrefs()
    {
        if (AnnotatorPrefs == null) AnnotatorPrefs = new AnnotatorPreferences();
        return AnnotatorPrefs;
    }

    /// <summary>Looks up the annotation for one (preset, gender, weight) slice. Returns null
    /// when the user has not annotated that slice yet.</summary>
    public PresetAnnotation FindAnnotation(string presetLabel, Gender gender, int weight)
    {
        if (string.IsNullOrEmpty(presetLabel)) return null;
        foreach (var pa in PresetAnnotations)
        {
            if (pa == null) continue;
            if (pa.Weight != weight) continue;
            if (pa.PresetGender != gender) continue;
            if (!string.Equals(pa.PresetLabel, presetLabel, StringComparison.Ordinal)) continue;
            return pa;
        }
        return null;
    }

    /// <summary>Rules that match the currently-loaded preset's live measurements, including drafts.
    /// Rebuilt every time <see cref="RefreshMeasurementValues"/> runs. Unlike the real classifier
    /// pipeline, this list ignores <see cref="VM_MeasurementRule.IsDraft"/> so the user can calibrate
    /// draft thresholds without promoting them.</summary>
    public ObservableCollection<VM_PreviewMatch> PreviewMatches { get; } = new();

    /// <summary>Human-readable summary shown above <see cref="PreviewMatches"/>.</summary>
    public string PreviewStatus { get; set; } = "";

    public VM_NamedKeyVertex? SelectedKeyVertex { get; set; }

    /// <summary>Multi-selection routing: takes the full KeyVertices DataGrid selection and
    /// asks the active viewer to mirror that selection in its Picks list (so every matching
    /// marker turns green, not just the one SelectedKeyVertex points at). Called from the
    /// DataGrid's SelectionChanged handler in the code-behind.</summary>
    public void SelectKeyVerticesInViewer(IEnumerable<VM_NamedKeyVertex> selected)
    {
        if (ActiveViewer == null) return;
        var keys = new List<(string, int)>();
        if (selected != null)
        {
            foreach (var kv in selected)
            {
                if (kv == null) continue;
                if (string.IsNullOrEmpty(kv.ShapeName)) continue;
                if (kv.VertexIndex < 0) continue;
                keys.Add((kv.ShapeName, kv.VertexIndex));
            }
        }
        ActiveViewer.RequestSelectPicksByShapeAndIndices(keys);
    }

    /// <summary>Currently highlighted measurement. Drives the colored-line overlay in the active viewer.</summary>
    public VM_MeasurementDefinition? SelectedMeasurement { get; set; }

    /// <summary>When true, key-vertex picks from any viewer add a new entry to this profile.</summary>
    public bool CapturePicks { get; set; } = false;

    /// <summary>Most recent viewer to fire a pick targeting this profile. Used for live measurement readouts.</summary>
    public VM_CharacterViewer? ActiveViewer { get; private set; }

    /// <summary>When the user selects a BoundingBox-strategy row in the KeyVertices grid the
    /// profile reopens that row's box in the viewer's pending-box editor so the user can
    /// refine it. This field tracks which row is being edited so the follow-up confirm
    /// updates it in place instead of appending a new row. Null when no edit session is active.</summary>
    private VM_NamedKeyVertex? _pendingBoxEditTarget;

    /// <summary>Subscription that clears <see cref="_pendingBoxEditTarget"/> whenever the
    /// viewer's pending box is torn down (user cancels, or confirm path finishes). Rewired
    /// in <see cref="AttachViewer"/> so the profile follows whichever viewer is bound.</summary>
    private IDisposable? _viewerHasPendingBoxSub;

    /// <summary>Binds this profile to the supplied viewer so live readouts and the
    /// measurement-line overlay target the right scene. Called by the editor when a
    /// preset is loaded in its embedded viewer.</summary>
    public void AttachViewer(VM_CharacterViewer viewer)
    {
        ActiveViewer = viewer;

        // Follow the new viewer's pending-box lifecycle. When HasPendingBox flips to false
        // (cancel, or post-confirm cleanup) drop any edit-session target so a subsequent
        // drag-picked box doesn't accidentally overwrite a stale row.
        _viewerHasPendingBoxSub?.Dispose();
        _viewerHasPendingBoxSub = viewer?
            .WhenAnyValue(v => v.HasPendingBox)
            .Subscribe(hasBox => { if (!hasBox) _pendingBoxEditTarget = null; });
    }

    public RelayCommand AddMeasurement { get; }
    public RelayCommand AddRule { get; }
    public RelayCommand CaptureFingerprintFromActiveViewer { get; }
    public RelayCommand RemoveSelectedKeyVertex { get; }
    public RelayCommand ShowPicksInViewer { get; }

    public IEnumerable<string> AvailableMeasurementNames => Measurements.Select(m => m.Name).Where(n => !string.IsNullOrEmpty(n));
    public IEnumerable<string> AvailableKeyVertexNames => KeyVertices.Select(k => k.Name).Where(n => !string.IsNullOrEmpty(n));

    public ObservableCollection<string> AvailableBodyTypeNames => _parent.AvailableBodyTypeNames;
    public ObservableCollection<BodyShapeDescriptor.LabelSignature> AvailableDescriptors => _parent.AvailableDescriptors;

    public void OnVertexPickedFromViewer(VM_CharacterViewer viewer, VM_CharacterViewer.KeyVertexPick pick)
    {
        ActiveViewer = viewer;

        var shapeName = pick.Mesh?.ShapeName ?? "";
        // Dedup: if we already have this (shape, index), just select the existing row so
        // the user sees which entry matches the click instead of appending a duplicate.
        var existing = KeyVertices.FirstOrDefault(k =>
            k.VertexIndex == pick.VertexIndex
            && string.Equals(k.ShapeName ?? "", shapeName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            SelectedKeyVertex = existing;
            return;
        }

        var model = new NamedKeyVertex
        {
            Name = NextDefaultName("KV", KeyVertices.Select(k => k.Name)),
            ShapeName = shapeName,
            VertexIndex = pick.VertexIndex,
        };
        var vm = new VM_NamedKeyVertex(model, this);
        KeyVertices.Add(vm);
        SelectedKeyVertex = vm;

        RefreshMeasurementValues();
    }

    /// <summary>
    /// Receives a bounding-box authoring drag from the viewer. <c>Mirror*</c> criteria
    /// materialize two paired <see cref="VM_NamedKeyVertex"/> rows sharing the same AABB
    /// with opposite single-axis <see cref="BoundingBoxCriterion"/>s so one drag can
    /// author both sides of a symmetric landmark pair. Single-axis criteria create one
    /// row. Names default to sequential placeholders — the user renames afterwards.
    /// <para>When <see cref="_pendingBoxEditTarget"/> is set the confirm was for an edit
    /// session on an existing row (opened by selecting a BoundingBox row); the target is
    /// updated in place instead of appending. A mirror criterion during edit updates the
    /// target with the primary half and appends the partner row.</para>
    /// </summary>
    public void OnBoxPickedFromViewer(VM_CharacterViewer viewer, VM_CharacterViewer.KeyVertexBoxPick pick)
    {
        ActiveViewer = viewer;

        var shapeName = pick.ShapeName ?? "";
        bool isMirror =
            pick.Criterion == BoxCriterionSelection.MirrorX ||
            pick.Criterion == BoxCriterionSelection.MirrorY ||
            pick.Criterion == BoxCriterionSelection.MirrorZ ||
            pick.Criterion == BoxCriterionSelection.MirrorPinchX ||
            pick.Criterion == BoxCriterionSelection.MirrorBulgeX;

        // MirrorPinchX / MirrorBulgeX expand into the *paired* criteria so the two generated rows
        // resolve jointly (same Y-slice) and a PointDistance across them measures true horizontal
        // thickness. Single-side PinchMin/MaxX and BulgeMin/MaxX remain available for manual use
        // and for loading legacy profiles that stored those values directly.
        (BoundingBoxCriterion a, BoundingBoxCriterion b)? mirrorPair = isMirror
            ? pick.Criterion switch
            {
                BoxCriterionSelection.MirrorX      => (BoundingBoxCriterion.MaxX,           BoundingBoxCriterion.MinX),
                BoxCriterionSelection.MirrorY      => (BoundingBoxCriterion.MaxY,           BoundingBoxCriterion.MinY),
                BoxCriterionSelection.MirrorZ      => (BoundingBoxCriterion.MaxZ,           BoundingBoxCriterion.MinZ),
                BoxCriterionSelection.MirrorPinchX => (BoundingBoxCriterion.PinchPairMaxX,  BoundingBoxCriterion.PinchPairMinX),
                _                                  => (BoundingBoxCriterion.BulgePairMaxX, BoundingBoxCriterion.BulgePairMinX),
            }
            : null;

        var editTarget = _pendingBoxEditTarget;
        // Guard against the row being deleted mid-edit; drop stale target, fall through to
        // the new-row path so the pick isn't lost.
        if (editTarget != null && !KeyVertices.Contains(editTarget))
        {
            editTarget = null;
            _pendingBoxEditTarget = null;
        }

        var newRows = new List<VM_NamedKeyVertex>();
        if (editTarget != null)
        {
            var primaryCrit = mirrorPair?.a ?? (BoundingBoxCriterion)pick.Criterion;
            UpdateBoxRow(editTarget, shapeName, pick.BoxMin, pick.BoxMax, primaryCrit);
            newRows.Add(editTarget);
            if (mirrorPair.HasValue)
            {
                newRows.Add(AddBoxRow(shapeName, pick.BoxMin, pick.BoxMax, mirrorPair.Value.b));
            }
            _pendingBoxEditTarget = null;
        }
        else if (mirrorPair.HasValue)
        {
            newRows.Add(AddBoxRow(shapeName, pick.BoxMin, pick.BoxMax, mirrorPair.Value.a));
            newRows.Add(AddBoxRow(shapeName, pick.BoxMin, pick.BoxMax, mirrorPair.Value.b));
        }
        else
        {
            newRows.Add(AddBoxRow(shapeName, pick.BoxMin, pick.BoxMax, (BoundingBoxCriterion)pick.Criterion));
        }

        // Resolves kv.VertexIndex on each new BB row (via RefreshBoundingBoxMarkers).
        RefreshMeasurementValues();

        // Auto-send the newly-authored rows to the viewer's Picks list so the user sees an
        // orange marker appear at each resolved vertex the moment they confirm the box.
        // The broader "show a marker for every BB entry in the roster" behavior was removed;
        // picks now appear only via explicit user action (this auto-send or Show Picks in Viewer).
        var entries = newRows
            .Where(kv => !string.IsNullOrEmpty(kv.ShapeName) && kv.VertexIndex >= 0)
            .Select(kv => (kv.ShapeName, kv.VertexIndex));
        viewer.ShowKeyVerticesInViewer(entries);
    }

    /// <summary>Reopens a stored BoundingBox row in the viewer's pending-box editor so the
    /// user can refine its AABB/criterion and re-confirm. Called from the SelectedKeyVertex
    /// change handler; no-ops for Explicit rows or when no viewer is attached. Also cancels
    /// any prior edit session (on a different row) so only one box is ever pending.</summary>
    private void SyncPendingBoxEditSessionWithSelection(VM_NamedKeyVertex? kv)
    {
        var viewer = ActiveViewer;
        if (viewer == null) return;

        bool isEditable = kv != null && kv.Strategy == KeyVertexStrategy.BoundingBox;

        if (!isEditable)
        {
            // Selection moved off a BB row: tear down any active edit session. The viewer's
            // HasPendingBox subscription below clears _pendingBoxEditTarget.
            if (_pendingBoxEditTarget != null && viewer.HasPendingBox)
            {
                viewer.CancelPendingBox();
            }
            return;
        }

        // Reopen the row's stored box. The criterion enum is cast from BoundingBoxCriterion
        // (0-9) to BoxCriterionSelection — their numeric ranges are aligned for exactly this
        // reason (see BoxCriterionSelection doc comment).
        _pendingBoxEditTarget = kv;
        var initial = new VM_CharacterViewer.KeyVertexBoxPick(
            kv!.ShapeName ?? "",
            new OpenTK.Mathematics.Vector3(kv.BoxMinX, kv.BoxMinY, kv.BoxMinZ),
            new OpenTK.Mathematics.Vector3(kv.BoxMaxX, kv.BoxMaxY, kv.BoxMaxZ),
            (BoxCriterionSelection)(int)kv.Criterion);
        viewer.BeginPendingBox(initial);
    }

    private static void UpdateBoxRow(
        VM_NamedKeyVertex row,
        string shapeName,
        OpenTK.Mathematics.Vector3 boxMin,
        OpenTK.Mathematics.Vector3 boxMax,
        BoundingBoxCriterion criterion)
    {
        row.ShapeName = shapeName;
        row.Strategy = KeyVertexStrategy.BoundingBox;
        row.BoxMinX = boxMin.X; row.BoxMinY = boxMin.Y; row.BoxMinZ = boxMin.Z;
        row.BoxMaxX = boxMax.X; row.BoxMaxY = boxMax.Y; row.BoxMaxZ = boxMax.Z;
        row.Criterion = criterion;
    }

    private VM_NamedKeyVertex AddBoxRow(
        string shapeName,
        OpenTK.Mathematics.Vector3 boxMin,
        OpenTK.Mathematics.Vector3 boxMax,
        BoundingBoxCriterion criterion)
    {
        var model = new NamedKeyVertex
        {
            Name = NextDefaultName("KV", KeyVertices.Select(k => k.Name)),
            ShapeName = shapeName,
            Strategy = KeyVertexStrategy.BoundingBox,
            BoxMinX = boxMin.X, BoxMinY = boxMin.Y, BoxMinZ = boxMin.Z,
            BoxMaxX = boxMax.X, BoxMaxY = boxMax.Y, BoxMaxZ = boxMax.Z,
            Criterion = criterion,
        };
        var vm = new VM_NamedKeyVertex(model, this);
        KeyVertices.Add(vm);
        SelectedKeyVertex = vm;
        return vm;
    }

    /// <summary>
    /// Re-evaluates every measurement against <see cref="ActiveViewer"/> and writes the
    /// result back into each <see cref="VM_MeasurementDefinition.LiveValue"/>. Called after
    /// any structural change (vertex add, measurement edit) and externally when the viewer's
    /// preset/weight changes.
    /// </summary>
    public void RefreshMeasurementValues()
    {
        var viewer = ActiveViewer;

        RefreshBoundingBoxMarkers(viewer);

        if (Measurements.Count == 0)
        {
            RefreshMeasurementHighlight();
            return;
        }

        var keyVertsByName = KeyVertices
            .Where(k => !string.IsNullOrEmpty(k.Name))
            .GroupBy(k => k.Name)
            .ToDictionary(g => g.Key, g => g.First().DumpToModel(), StringComparer.Ordinal);

        foreach (var m in Measurements)
        {
            if (viewer == null)
            {
                m.LiveValue = null;
                continue;
            }
            if (MeasurementMath.TryEvaluate(m.DumpToModel(), keyVertsByName,
                (shape, idx) => viewer.TryGetCurrentVertex(shape, idx, out var p) ? (OpenTK.Mathematics.Vector3?)p : null,
                shape => viewer.GetShapePositions(shape),
                out float v))
            {
                m.LiveValue = v;
            }
            else
            {
                m.LiveValue = null;
            }
        }

        RefreshMeasurementHighlight();
        RefreshPreviewDescriptors();
    }

    /// <summary>Rebuilds <see cref="PreviewMatches"/> from the current <see cref="VM_MeasurementDefinition.LiveValue"/>s
    /// on every <see cref="Rules"/> row. Unlike <c>BodySlideMeasurementEvaluator.Evaluate</c> this
    /// ignores the <see cref="VM_MeasurementRule.IsDraft"/> flag so the user can see what draft
    /// rules would produce without flipping the flag (which would leak descriptors into the real
    /// patcher pipeline). Each match row carries a trace of the specific conditions that fired, so
    /// calibration is a glance, not a hunt.</summary>
    public void RefreshPreviewDescriptors()
    {
        var meas = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var m in Measurements)
        {
            if (m.LiveValue.HasValue) meas[m.Name] = m.LiveValue.Value;
        }

        PreviewMatches.Clear();
        int drafts = 0, promoted = 0;
        foreach (var r in Rules)
        {
            if (r == null) continue;
            if (string.IsNullOrEmpty(r.DescriptorCategory)) continue;
            if (string.IsNullOrEmpty(r.DescriptorValue)) continue;

            var model = r.DumpToModel();
            if (!MeasurementMath.RuleMatches(model, meas)) continue;

            PreviewMatches.Add(new VM_PreviewMatch
            {
                Category = r.DescriptorCategory,
                Value = r.DescriptorValue,
                IsDraft = r.IsDraft,
                ConditionTrace = BuildMatchTrace(model, meas),
            });
            if (r.IsDraft) drafts++; else promoted++;
        }

        if (meas.Count == 0)
        {
            PreviewStatus = "No live measurements (load a preset in the viewer).";
        }
        else if (PreviewMatches.Count == 0)
        {
            PreviewStatus = $"0 matches out of {Rules.Count} rule{(Rules.Count == 1 ? "" : "s")}.";
        }
        else
        {
            string summary = promoted > 0 && drafts > 0
                ? $"{promoted} promoted + {drafts} draft"
                : promoted > 0
                    ? $"{promoted} promoted"
                    : $"{drafts} draft";
            PreviewStatus = $"{PreviewMatches.Count} match{(PreviewMatches.Count == 1 ? "" : "es")} ({summary}) of {Rules.Count} rule{(Rules.Count == 1 ? "" : "s")}.";
        }
    }

    private static string BuildMatchTrace(MeasurementRule rule, IReadOnlyDictionary<string, float> meas)
    {
        if (rule.GroupsORlogic == null) return "";
        foreach (var g in rule.GroupsORlogic)
        {
            if (g?.ConditionsANDlogic == null || g.ConditionsANDlogic.Count == 0) continue;
            bool allMatch = true;
            var parts = new List<string>(g.ConditionsANDlogic.Count);
            foreach (var c in g.ConditionsANDlogic)
            {
                if (c == null || string.IsNullOrEmpty(c.MeasurementName)) { allMatch = false; break; }
                if (!meas.TryGetValue(c.MeasurementName, out var v)) { allMatch = false; break; }
                if (!MeasurementMath.Compare(v, c.Comparator, c.Value)) { allMatch = false; break; }
                parts.Add($"{c.MeasurementName}={v:F3} {ComparatorSymbol(c.Comparator)} {c.Value:F3}");
            }
            if (allMatch) return string.Join("  AND  ", parts);
        }
        return "";
    }

    private static string ComparatorSymbol(MeasurementComparator c) => c switch
    {
        MeasurementComparator.LessThan => "<",
        MeasurementComparator.LessThanOrEqual => "<=",
        MeasurementComparator.GreaterThan => ">",
        MeasurementComparator.GreaterThanOrEqual => ">=",
        MeasurementComparator.EqualTo => "==",
        MeasurementComparator.NotEqualTo => "!=",
        _ => "?",
    };

    /// <summary>
    /// Re-resolves every <see cref="KeyVertexStrategy.BoundingBox"/> row against the current
    /// mesh state and caches the resolved index onto the row VM (so the DataGrid reflects it,
    /// and <see cref="MeasurementMath"/> evaluators can read the cached index). When the index
    /// changes, also updates any corresponding orange pick in the viewer's Picks list via
    /// <see cref="VM_CharacterViewer.MigrateKeyVertexPick"/> — so entries the user has already
    /// sent to the viewer (via box-confirm or "Show Picks in Viewer") keep tracking live.
    /// <para>
    /// Does NOT push resolved positions to any renderer marker list by itself. Markers are
    /// added only when the user explicitly sends a KeyVertex to the viewer (box confirm →
    /// auto-add in <see cref="OnBoxPickedFromViewer"/>, or "Show Picks in Viewer" button),
    /// so merely defining a BB row in the profile doesn't cause a marker to appear.
    /// </para>
    /// </summary>
    private void RefreshBoundingBoxMarkers(VM_CharacterViewer viewer)
    {
        if (viewer == null) return;

        // Pre-snapshot every BB row as a plain model so pair-sibling lookup can see the peers
        // without each FindBestInBox call rebuilding the snapshot. Keyed by VM identity so we
        // can pull the snapshot back out for the current row without a second DumpToModel.
        var bbSnapshots = new Dictionary<VM_NamedKeyVertex, NamedKeyVertex>(ReferenceEqualityComparer.Instance);
        foreach (var vm in KeyVertices)
        {
            if (vm.Strategy != KeyVertexStrategy.BoundingBox) continue;
            bbSnapshots[vm] = vm.DumpToModel();
        }

        Func<NamedKeyVertex, NamedKeyVertex?> findSibling = self =>
            MeasurementMath.FindPairSibling(self, bbSnapshots.Values);

        foreach (var kv in KeyVertices)
        {
            if (kv.Strategy != KeyVertexStrategy.BoundingBox) continue;
            if (string.IsNullOrEmpty(kv.ShapeName)) continue;

            var positions = viewer.GetShapePositions(kv.ShapeName);
            if (positions == null || positions.Length == 0) continue;

            var model = bbSnapshots[kv];
            int? idx = MeasurementMath.FindBestInBox(positions, model, kv.Criterion, findSibling);
            if (idx == null) continue;

            int oldIdx = kv.VertexIndex;
            kv.VertexIndex = idx.Value;

            // Keep any orange pick previously shown (via "Show Picks in Viewer" or on box
            // confirm) in sync with the newly-resolved BB index so selecting this KeyVertex
            // in the editor continues to green-highlight the right marker across preset/weight
            // switches. No-op when the pick isn't in the viewer's list yet.
            if (oldIdx >= 0 && oldIdx != idx.Value)
            {
                viewer.MigrateKeyVertexPick(kv.ShapeName, oldIdx, idx.Value);
            }
        }
    }

    /// <summary>
    /// Pushes line segments for <see cref="SelectedMeasurement"/> into the active viewer's
    /// measurement-line overlay. PointDistance renders one A-B segment in the primary color;
    /// AxisDistance renders a four-segment decomposition (three axis-aligned legs A→P1→P2→B
    /// — one yellow for the measurement axis, two white for the secondary axes — plus a grey
    /// hypotenuse A-B) so the user can see how the A-B offset splits across axes, not just
    /// the slant; RatioDistance renders two segments (A-B primary, C-D secondary color).
    /// Clears the overlay when there is no selection, no viewer, or when the referenced
    /// vertices cannot be resolved.
    /// </summary>
    private void RefreshMeasurementHighlight()
    {
        var viewer = ActiveViewer;
        if (viewer == null) return;

        var sel = SelectedMeasurement;
        if (sel == null)
        {
            viewer.SetMeasurementLines(null);
            return;
        }

        var keyVertsByName = KeyVertices
            .Where(k => !string.IsNullOrEmpty(k.Name))
            .GroupBy(k => k.Name)
            .ToDictionary(g => g.Key, g => g.First().DumpToModel(), StringComparer.Ordinal);

        OpenTK.Mathematics.Vector3? Resolve(string refName)
        {
            if (string.IsNullOrEmpty(refName)) return null;
            if (!keyVertsByName.TryGetValue(refName, out var kv)) return null;
            if (kv == null || string.IsNullOrEmpty(kv.ShapeName)) return null;
            return viewer.TryGetCurrentVertex(kv.ShapeName, kv.VertexIndex, out var p)
                ? (OpenTK.Mathematics.Vector3?)p
                : null;
        }

        var segments = new List<(OpenTK.Mathematics.Vector3 A, OpenTK.Mathematics.Vector3 B, OpenTK.Mathematics.Vector3 Color)>();

        // Yellow for the primary pair, cyan for the ratio denominator pair. For AxisDistance
        // the three axis-aligned legs use: yellow (the measurement axis), white (the two
        // secondary axes), and grey (the A-B hypotenuse) — white/grey stand in for the
        // originally-planned dashed styling so the renderer can stay on flat-color lines.
        var primary = new OpenTK.Mathematics.Vector3(1.0f, 0.85f, 0.1f);
        var secondary = new OpenTK.Mathematics.Vector3(0.1f, 0.85f, 1.0f);
        var axisSecondary = new OpenTK.Mathematics.Vector3(1.0f, 1.0f, 1.0f);
        var axisHypotenuse = new OpenTK.Mathematics.Vector3(0.5f, 0.5f, 0.5f);

        var a = Resolve(sel.VertexRefA);
        var b = Resolve(sel.VertexRefB);
        if (a.HasValue && b.HasValue)
        {
            if (sel.Kind == MeasurementKind.AxisDistance)
            {
                // Decompose B-A into three axis-aligned legs walking A → P1 → P2 → B along
                // X, then Y, then Z. The leg matching the measurement axis takes the primary
                // (yellow) color; the other two take secondary (white). The direct A-B line
                // is drawn first in grey as the hypotenuse so the colored legs always paint
                // on top — matters when the vertices differ on a single axis, where the
                // hypotenuse is collinear with one leg and must not obscure it (depth test
                // is disabled for this overlay, so painter ordering decides who wins).
                // Zero-length legs are skipped.
                var av = a.Value;
                var bv = b.Value;
                var p1 = new OpenTK.Mathematics.Vector3(bv.X, av.Y, av.Z); // after X leg
                var p2 = new OpenTK.Mathematics.Vector3(bv.X, bv.Y, av.Z); // after Y leg

                var xColor = sel.Axis == MeasurementAxis.X ? primary : axisSecondary;
                var yColor = sel.Axis == MeasurementAxis.Y ? primary : axisSecondary;
                var zColor = sel.Axis == MeasurementAxis.Z ? primary : axisSecondary;

                segments.Add((av, bv, axisHypotenuse));
                if (av.X != bv.X) segments.Add((av, p1, xColor));
                if (av.Y != bv.Y) segments.Add((p1, p2, yColor));
                if (av.Z != bv.Z) segments.Add((p2, bv, zColor));
            }
            else
            {
                segments.Add((a.Value, b.Value, primary));
            }
        }

        if (sel.Kind == MeasurementKind.RatioDistance)
        {
            var c = Resolve(sel.VertexRefC);
            var d = Resolve(sel.VertexRefD);
            if (c.HasValue && d.HasValue)
            {
                segments.Add((c.Value, d.Value, secondary));
            }
        }

        if (segments.Count == 0)
        {
            viewer.SetMeasurementLines(null);
            return;
        }

        viewer.SetMeasurementLines(segments);
    }

    private static string NextDefaultName(string prefix, IEnumerable<string> existing)
    {
        var taken = new HashSet<string>(existing.Where(s => !string.IsNullOrEmpty(s)));
        for (int i = 1; i < 1000; i++)
        {
            var candidate = prefix + "_" + i;
            if (!taken.Contains(candidate)) return candidate;
        }
        return prefix + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
    }

    public BodyTypeProfile DumpToModel()
    {
        var model = new BodyTypeProfile
        {
            Id = Id,
            Name = Name?.Trim() ?? "",
            BodyTypeName = BodyTypeName?.Trim() ?? "",
            Fingerprint = new TopologyFingerprint
            {
                VertexCount = FingerprintVertexCount,
                ShapeVertexCounts = ParseShapeCounts(FingerprintShapeCounts),
                SampleIndices = _source.Fingerprint?.SampleIndices ?? new List<int>(),
            },
            KeyVertices = KeyVertices.Select(k => k.DumpToModel()).ToList(),
            Measurements = Measurements.Select(m => m.DumpToModel()).ToList(),
            Rules = Rules.Select(r => r.DumpToModel()).ToList(),
            PresetAnnotations = PresetAnnotations.Select(CloneAnnotation).ToList(),
            AnnotatorPrefs = CloneAnnotatorPrefs(AnnotatorPrefs),
        };
        return model;
    }

    private static PresetAnnotation CloneAnnotation(PresetAnnotation src)
    {
        if (src == null) return null;
        var copy = new PresetAnnotation
        {
            PresetLabel = src.PresetLabel ?? "",
            PresetGender = src.PresetGender,
            Weight = src.Weight,
        };
        if (src.Descriptors != null)
        {
            foreach (var d in src.Descriptors)
            {
                if (d == null) continue;
                copy.Descriptors.Add(new BodyShapeDescriptor.LabelSignature { Category = d.Category, Value = d.Value });
            }
        }
        return copy;
    }

    private static AnnotatorPreferences CloneAnnotatorPrefs(AnnotatorPreferences src)
    {
        var copy = new AnnotatorPreferences();
        if (src == null) return copy;
        copy.SelectionAlgorithm = src.SelectionAlgorithm;
        copy.SynthesisAlgorithm = src.SynthesisAlgorithm;
        if (src.WeightSlots != null) copy.WeightSlots = new List<int>(src.WeightSlots);
        if (src.VisibleMeasurementColumns != null) copy.VisibleMeasurementColumns = new List<string>(src.VisibleMeasurementColumns);
        return copy;
    }

    private static Dictionary<string, int> ParseShapeCounts(string csv)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(csv)) return result;
        foreach (var token in csv.Split(','))
        {
            var parts = token.Split(':');
            if (parts.Length != 2) continue;
            var name = parts[0].Trim();
            if (string.IsNullOrEmpty(name)) continue;
            if (int.TryParse(parts[1].Trim(), out int count))
            {
                result[name] = count;
            }
        }
        return result;
    }

    // --- Match Presets scan cache (Phase 5 authoring-time feature) ---

    /// <summary>Per-(preset, weight) matched descriptors from the last scan, keyed by
    /// (PresetLabel, Gender, Weight). The descriptor lists include both draft and promoted
    /// matches — the whole point of the scan is to preview what draft rules would do. Cleared
    /// and repopulated by <see cref="VM_BodyTypeProfileEditor.RunScanAsync"/>.</summary>
    public Dictionary<(string PresetLabel, Gender Gender, int Weight), List<BodyShapeDescriptor.LabelSignature>> ScanResults { get; } = new();

    /// <summary>True when the cache is empty or out of date (rules/measurements/key vertices
    /// changed after the last scan). Surfaces in the UI as a "Results stale — re-scan" nudge.</summary>
    public bool ScanResultsStale { get; set; } = true;

    /// <summary>PropertyChanged forwarder for leaf VM edits (measurement row fields, rule
    /// descriptor fields, condition threshold values) that should invalidate the scan cache.</summary>
    private void OnScanInvalidatingChange(object? sender, PropertyChangedEventArgs e) => MarkScanResultsStale();

    /// <summary>Marks the scan cache stale and tells the parent editor to refresh the
    /// Match Presets list so the stale badge appears immediately.</summary>
    private void MarkScanResultsStale()
    {
        ScanResultsStale = true;
        _parent?.OnProfileScanStale(this);
    }

    private void HookRuleForScanInvalidation(VM_MeasurementRule r)
    {
        if (r == null) return;
        r.PropertyChanged += OnScanInvalidatingChange;
        foreach (var g in r.Groups) HookGroupForScanInvalidation(g);
        r.Groups.CollectionChanged += (_, args) =>
        {
            if (args.OldItems != null)
                foreach (VM_AndGatedMeasurementGroup g in args.OldItems) UnhookGroupForScanInvalidation(g);
            if (args.NewItems != null)
                foreach (VM_AndGatedMeasurementGroup g in args.NewItems) HookGroupForScanInvalidation(g);
            MarkScanResultsStale();
        };
    }

    private void UnhookRuleForScanInvalidation(VM_MeasurementRule r)
    {
        if (r == null) return;
        r.PropertyChanged -= OnScanInvalidatingChange;
        foreach (var g in r.Groups) UnhookGroupForScanInvalidation(g);
    }

    private void HookGroupForScanInvalidation(VM_AndGatedMeasurementGroup g)
    {
        if (g == null) return;
        foreach (var c in g.Conditions) c.PropertyChanged += OnScanInvalidatingChange;
        g.Conditions.CollectionChanged += (_, args) =>
        {
            if (args.OldItems != null)
                foreach (VM_MeasurementCondition c in args.OldItems) c.PropertyChanged -= OnScanInvalidatingChange;
            if (args.NewItems != null)
                foreach (VM_MeasurementCondition c in args.NewItems) c.PropertyChanged += OnScanInvalidatingChange;
            MarkScanResultsStale();
        };
    }

    private void UnhookGroupForScanInvalidation(VM_AndGatedMeasurementGroup g)
    {
        if (g == null) return;
        foreach (var c in g.Conditions) c.PropertyChanged -= OnScanInvalidatingChange;
    }

    // --- Shared measurements cache (Match Presets ↔ Label-Then-Suggest) ---

    /// <summary>Per-iteration output of a Match Presets / Label-Then-Suggest scan, keyed by
    /// (preset, gender, weight). Both scans write here on a cache miss and read here on a
    /// cache hit, so scanning from one tab pre-populates the other. Survives rule edits —
    /// only changes that affect the underlying numbers (KeyVertices, MeasurementDefinitions)
    /// invalidate it via <see cref="MarkMeasurementCacheStale"/>.</summary>
    public Dictionary<(string PresetLabel, Gender Gender, int Weight), MeasurementCacheEntry> MeasurementCache { get; } = new();

    /// <summary>True when the measurements cache may not reflect the current key vertices /
    /// measurement definitions. Set by <see cref="MarkMeasurementCacheStale"/>; cleared
    /// after a scan repopulates the cache. Implies <see cref="ScanResultsStale"/> too —
    /// stale measurements means stale derived descriptors.</summary>
    public bool MeasurementCacheStale { get; set; } = true;

    /// <summary>One cache entry. Measurements stored as float? so "the evaluator could not
    /// compute this name" survives the cache as null instead of being indistinguishable
    /// from a missing key.</summary>
    public class MeasurementCacheEntry
    {
        public Dictionary<string, float?> Measurements { get; } = new(StringComparer.Ordinal);
        public bool TopologyMismatch { get; set; }
    }

    /// <summary>PropertyChanged forwarder for leaf VM edits on KeyVertices /
    /// MeasurementDefinitions — fields whose values change the numbers. Distinct from
    /// <see cref="OnScanInvalidatingChange"/>, which covers rule-only edits that don't
    /// invalidate the measurements cache.</summary>
    private void OnMeasurementCacheInvalidatingChange(object? sender, PropertyChangedEventArgs e) => MarkMeasurementCacheStale();

    /// <summary>Marks both the measurements cache and the derived descriptor list stale.
    /// Used for KeyVertices and MeasurementDefinition changes — the underlying numbers
    /// will differ, so any cached entry could be wrong.</summary>
    private void MarkMeasurementCacheStale()
    {
        MeasurementCacheStale = true;
        MarkScanResultsStale();
    }

    /// <summary>Re-derives one cache entry's descriptor list by running the profile's
    /// current rules against its cached measurements. Cheap: rule evaluation only, no
    /// mesh work. Mirrors the rule-loop in <see cref="BodySlideMeasurementEvaluator.Evaluate"/>
    /// so re-deriving from cache produces the same descriptors as a fresh evaluation.</summary>
    public List<BodyShapeDescriptor.LabelSignature> DeriveDescriptorsFor(
        (string PresetLabel, Gender Gender, int Weight) key,
        BodyTypeProfile profileModel,
        bool includeDrafts)
    {
        var result = new List<BodyShapeDescriptor.LabelSignature>();
        if (!MeasurementCache.TryGetValue(key, out var entry)) return result;
        if (profileModel?.Rules == null) return result;

        // RuleMatches expects float values, not float?. A null cache entry means the
        // evaluator failed to compute that measurement, so any rule depending on it
        // should fail to match — which is what dropping the key from the dict yields.
        var floats = new Dictionary<string, float>(entry.Measurements.Count, StringComparer.Ordinal);
        foreach (var kv in entry.Measurements)
            if (kv.Value.HasValue) floats[kv.Key] = kv.Value.Value;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in profileModel.Rules)
        {
            if (rule == null) continue;
            if (rule.IsDraft && !includeDrafts) continue;
            if (rule.Descriptor == null
                || string.IsNullOrEmpty(rule.Descriptor.Category)
                || string.IsNullOrEmpty(rule.Descriptor.Value)) continue;
            if (!MeasurementMath.RuleMatches(rule, floats)) continue;

            string k = rule.Descriptor.Category + "::" + rule.Descriptor.Value;
            if (!seen.Add(k)) continue;

            result.Add(new BodyShapeDescriptor.LabelSignature
            {
                Category = rule.Descriptor.Category,
                Value = rule.Descriptor.Value,
            });
        }
        return result;
    }

    /// <summary>Rebuilds <see cref="ScanResults"/> from the measurements cache + the
    /// profile's current rules. O(presets × rules) and pure CPU — no mesh work, no GL,
    /// no viewer needed. Called after a scan and on profile bind so the Match Presets
    /// display reflects whatever's in the cache without forcing another scan.</summary>
    public void RebuildScanResultsFromCache(BodyTypeProfile profileModel, bool includeDrafts = true)
    {
        ScanResults.Clear();
        foreach (var key in MeasurementCache.Keys)
            ScanResults[key] = DeriveDescriptorsFor(key, profileModel, includeDrafts);
    }
}

/// <summary>Row VM for a single <see cref="NamedKeyVertex"/>.</summary>
public class VM_NamedKeyVertex : VM
{
    private readonly VM_BodyTypeProfile _parent;

    public VM_NamedKeyVertex(NamedKeyVertex source, VM_BodyTypeProfile parent)
    {
        _parent = parent;
        Name = source.Name ?? "";
        ShapeName = source.ShapeName ?? "";
        VertexIndex = source.VertexIndex;
        Strategy = source.Strategy;
        BoxMinX = source.BoxMinX;
        BoxMinY = source.BoxMinY;
        BoxMinZ = source.BoxMinZ;
        BoxMaxX = source.BoxMaxX;
        BoxMaxY = source.BoxMaxY;
        BoxMaxZ = source.BoxMaxZ;
        Criterion = source.Criterion;

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => _parent.KeyVertices.Remove(this));
    }

    public string Name { get; set; }
    public string ShapeName { get; set; }
    public int VertexIndex { get; set; }
    public KeyVertexStrategy Strategy { get; set; }
    public float BoxMinX { get; set; }
    public float BoxMinY { get; set; }
    public float BoxMinZ { get; set; }
    public float BoxMaxX { get; set; }
    public float BoxMaxY { get; set; }
    public float BoxMaxZ { get; set; }
    public BoundingBoxCriterion Criterion { get; set; }

    public RelayCommand DeleteCommand { get; }

    public NamedKeyVertex DumpToModel() => new()
    {
        Name = Name?.Trim() ?? "",
        ShapeName = ShapeName?.Trim() ?? "",
        VertexIndex = VertexIndex,
        Strategy = Strategy,
        BoxMinX = BoxMinX,
        BoxMinY = BoxMinY,
        BoxMinZ = BoxMinZ,
        BoxMaxX = BoxMaxX,
        BoxMaxY = BoxMaxY,
        BoxMaxZ = BoxMaxZ,
        Criterion = Criterion,
    };
}

/// <summary>Row VM for a single <see cref="MeasurementDefinition"/>. Tracks the live readout value.</summary>
public class VM_MeasurementDefinition : VM
{
    private readonly VM_BodyTypeProfile _parent;

    public VM_MeasurementDefinition(MeasurementDefinition source, VM_BodyTypeProfile parent)
    {
        _parent = parent;
        Name = source.Name ?? "";
        Kind = source.Kind;
        Axis = source.Axis;
        NumeratorAxis = source.NumeratorAxis;
        DenominatorAxis = source.DenominatorAxis;

        var refs = source.VertexRefNames ?? new List<string>();
        VertexRefA = refs.Count > 0 ? refs[0] : "";
        VertexRefB = refs.Count > 1 ? refs[1] : "";
        VertexRefC = refs.Count > 2 ? refs[2] : "";
        VertexRefD = refs.Count > 3 ? refs[3] : "";

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => _parent.Measurements.Remove(this));
    }

    public string Name { get; set; }
    public MeasurementKind Kind { get; set; }
    public MeasurementAxis Axis { get; set; }

    /// <summary>How to reduce the numerator pair for a <see cref="MeasurementKind.RatioDistance"/> entry.
    /// Null = full 3D length (legacy). Ignored for non-ratio kinds.</summary>
    public MeasurementAxis? NumeratorAxis { get; set; }

    /// <summary>How to reduce the denominator pair for a <see cref="MeasurementKind.RatioDistance"/> entry.
    /// Null = full 3D length (legacy). Ignored for non-ratio kinds.</summary>
    public MeasurementAxis? DenominatorAxis { get; set; }

    public string VertexRefA { get; set; }
    public string VertexRefB { get; set; }
    public string VertexRefC { get; set; }
    public string VertexRefD { get; set; }

    /// <summary>Latest evaluated value against the active viewer; null when no viewer or evaluation failed.</summary>
    public float? LiveValue { get; set; }

    public RelayCommand DeleteCommand { get; }

    public IEnumerable<string> AvailableKeyVertexNames => _parent.AvailableKeyVertexNames;

    public bool ShowAxisField => Kind == MeasurementKind.AxisDistance;
    public bool ShowSecondPair => Kind == MeasurementKind.RatioDistance;

    /// <summary>Display-friendly labels for <see cref="MeasurementAxis"/>. Viewer positions are in
    /// HelixToolkit Y-up space (see BodySlideDeformer remarks), so X=left/right, Y=up/down, Z=front/back.</summary>
    public static IReadOnlyList<AxisOption> AxisOptions { get; } = new[]
    {
        new AxisOption(MeasurementAxis.X, "X (Horizontal)"),
        new AxisOption(MeasurementAxis.Y, "Y (Vertical)"),
        new AxisOption(MeasurementAxis.Z, "Z (Depth)"),
    };

    public IReadOnlyList<AxisOption> AxisOptionsList => AxisOptions;

    /// <summary>Display-friendly labels for the nullable per-pair axis used by RatioDistance. The 3D entry
    /// (Value = null) preserves the pre-fix behavior where ratios were full Euclidean lengths.</summary>
    public static IReadOnlyList<RatioAxisOption> RatioAxisOptions { get; } = new[]
    {
        new RatioAxisOption(null, "3D (Length)"),
        new RatioAxisOption(MeasurementAxis.X, "X (Horizontal)"),
        new RatioAxisOption(MeasurementAxis.Y, "Y (Vertical)"),
        new RatioAxisOption(MeasurementAxis.Z, "Z (Depth)"),
    };

    public IReadOnlyList<RatioAxisOption> RatioAxisOptionsList => RatioAxisOptions;

    public MeasurementDefinition DumpToModel()
    {
        var refs = new List<string>();
        if (!string.IsNullOrEmpty(VertexRefA)) refs.Add(VertexRefA);
        if (!string.IsNullOrEmpty(VertexRefB)) refs.Add(VertexRefB);
        if (Kind == MeasurementKind.RatioDistance)
        {
            if (!string.IsNullOrEmpty(VertexRefC)) refs.Add(VertexRefC);
            if (!string.IsNullOrEmpty(VertexRefD)) refs.Add(VertexRefD);
        }
        return new MeasurementDefinition
        {
            Name = Name?.Trim() ?? "",
            Kind = Kind,
            Axis = Axis,
            NumeratorAxis = Kind == MeasurementKind.RatioDistance ? NumeratorAxis : null,
            DenominatorAxis = Kind == MeasurementKind.RatioDistance ? DenominatorAxis : null,
            VertexRefNames = refs,
        };
    }
}

/// <summary>Paired (value, display-label) for the axis ComboBox in the measurement grid.</summary>
public sealed class AxisOption
{
    public AxisOption(MeasurementAxis value, string label)
    {
        Value = value;
        Label = label;
    }
    public MeasurementAxis Value { get; }
    public string Label { get; }
}

/// <summary>Paired (value, display-label) for the per-pair Ratio axis ComboBox. Value is nullable —
/// null encodes "use full 3D length" (the pre-fix default), so ratios that should stay 3D (e.g. the
/// arm-thickness diagonal) just leave the field unset.</summary>
public sealed class RatioAxisOption
{
    public RatioAxisOption(MeasurementAxis? value, string label)
    {
        Value = value;
        Label = label;
    }
    public MeasurementAxis? Value { get; }
    public string Label { get; }
}

/// <summary>Row VM for a <see cref="MeasurementRule"/>.</summary>
public class VM_MeasurementRule : VM
{
    private readonly VM_BodyTypeProfile _parent;
    private readonly string _id;

    public VM_MeasurementRule(MeasurementRule source, VM_BodyTypeProfile parent)
    {
        _parent = parent;
        _id = source.Id ?? Guid.NewGuid().ToString("N");
        DescriptorCategory = source.Descriptor?.Category ?? "";
        DescriptorValue = source.Descriptor?.Value ?? "";
        IsDraft = source.IsDraft;

        if (source.GroupsORlogic != null)
        {
            foreach (var g in source.GroupsORlogic)
            {
                if (g == null) continue;
                Groups.Add(new VM_AndGatedMeasurementGroup(g, this));
            }
        }

        AddGroup = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Groups.Add(new VM_AndGatedMeasurementGroup(new AndGatedMeasurementGroup(), this)));

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => _parent.Rules.Remove(this));

        PromoteFromDraft = new RelayCommand(
            canExecute: _ => IsDraft,
            execute: _ => IsDraft = false);
    }

    public string DescriptorCategory { get; set; }
    public string DescriptorValue { get; set; }
    public bool IsDraft { get; set; }

    public ObservableCollection<VM_AndGatedMeasurementGroup> Groups { get; } = new();

    public RelayCommand AddGroup { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand PromoteFromDraft { get; }

    public ObservableCollection<BodyShapeDescriptor.LabelSignature> AvailableDescriptors => _parent.AvailableDescriptors;

    public IEnumerable<string> AvailableMeasurementNames => _parent.AvailableMeasurementNames;

    public void RemoveGroup(VM_AndGatedMeasurementGroup group) => Groups.Remove(group);

    public MeasurementRule DumpToModel()
    {
        return new MeasurementRule
        {
            Id = _id,
            Descriptor = new BodyShapeDescriptor.LabelSignature
            {
                Category = DescriptorCategory?.Trim() ?? "",
                Value = DescriptorValue?.Trim() ?? "",
            },
            IsDraft = IsDraft,
            GroupsORlogic = Groups.Select(g => g.DumpToModel()).ToList(),
        };
    }
}

/// <summary>Row VM for an <see cref="AndGatedMeasurementGroup"/>.</summary>
public class VM_AndGatedMeasurementGroup : VM
{
    private readonly VM_MeasurementRule _parent;

    public VM_AndGatedMeasurementGroup(AndGatedMeasurementGroup source, VM_MeasurementRule parent)
    {
        _parent = parent;

        if (source.ConditionsANDlogic != null)
        {
            foreach (var c in source.ConditionsANDlogic)
            {
                if (c == null) continue;
                Conditions.Add(new VM_MeasurementCondition(c, this));
            }
        }

        AddCondition = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Conditions.Add(new VM_MeasurementCondition(new MeasurementCondition(), this)));

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => _parent.RemoveGroup(this));
    }

    public ObservableCollection<VM_MeasurementCondition> Conditions { get; } = new();
    public RelayCommand AddCondition { get; }
    public RelayCommand DeleteCommand { get; }

    public IEnumerable<string> AvailableMeasurementNames => _parent.AvailableMeasurementNames;

    public void RemoveCondition(VM_MeasurementCondition condition) => Conditions.Remove(condition);

    public AndGatedMeasurementGroup DumpToModel() => new()
    {
        ConditionsANDlogic = Conditions.Select(c => c.DumpToModel()).ToList(),
    };
}

/// <summary>Row VM for a <see cref="MeasurementCondition"/>.</summary>
public class VM_MeasurementCondition : VM
{
    private readonly VM_AndGatedMeasurementGroup _parent;

    public VM_MeasurementCondition(MeasurementCondition source, VM_AndGatedMeasurementGroup parent)
    {
        _parent = parent;
        MeasurementName = source.MeasurementName ?? "";
        Comparator = source.Comparator;
        Value = source.Value;

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => _parent.RemoveCondition(this));
    }

    public string MeasurementName { get; set; }
    public MeasurementComparator Comparator { get; set; }
    public float Value { get; set; }
    public RelayCommand DeleteCommand { get; }

    public IEnumerable<string> AvailableMeasurementNames => _parent.AvailableMeasurementNames;

    public MeasurementCondition DumpToModel() => new()
    {
        MeasurementName = MeasurementName?.Trim() ?? "",
        Comparator = Comparator,
        Value = Value,
    };
}

/// <summary>Row VM for one rule whose predicate matched the currently-loaded preset's live
/// measurements. Includes draft rules that would be skipped by the real classifier pipeline,
/// so the user can calibrate thresholds before promoting.</summary>
public class VM_PreviewMatch : VM
{
    public string Category { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsDraft { get; set; }

    /// <summary>Line showing which measurement values satisfied which thresholds in the
    /// matched AND-group — e.g., <c>chest_proj_to_chest_width=0.370 &gt;= 0.320  AND
    /// chest_proj_to_chest_width=0.370 &lt; 0.400</c>. Helps the user see exactly why a
    /// descriptor fired.</summary>
    public string ConditionTrace { get; set; } = "";

    public string DescriptorDisplay =>
        IsDraft ? $"{Category}: {Value}  (draft)" : $"{Category}: {Value}";

    /// <summary>Green for promoted matches, orange for drafts. Makes calibration status
    /// readable at a glance.</summary>
    public Brush DisplayBrush => IsDraft ? Brushes.DarkOrange : Brushes.DarkGreen;
}

/// <summary>Row VM for the Match Presets results list — one row per (preset, weight) combo
/// that matched the filter, so keyboard arrow navigation iterates through every conforming
/// weight for every conforming preset. Loading the row applies the preset at this row's
/// specific weight.</summary>
public class VM_PresetScanRow : VM
{
    public VM_PresetScanRow(
        string presetLabel,
        Gender gender,
        int weight,
        IReadOnlyList<BodyShapeDescriptor.LabelSignature> matches)
    {
        PresetLabel = presetLabel ?? "";
        Gender = gender;
        Weight = weight;
        Matches = matches ?? Array.Empty<BodyShapeDescriptor.LabelSignature>();
    }

    public string PresetLabel { get; }
    public Gender Gender { get; }
    public int Weight { get; }
    public IReadOnlyList<BodyShapeDescriptor.LabelSignature> Matches { get; }

    public string Display => $"{PresetLabel}  (W{Weight}, {Gender})";

    /// <summary>Comma-separated <c>Category:Value</c> list for this row's single weight.</summary>
    public string MatchSummary => string.Join(", ", Matches.Select(d => d.Category + ":" + d.Value));
}

/// <summary>Weight-filter toggle for the Match Presets tab. One per weight slot observed
/// in the scan cache. <see cref="IsSelected"/> change fires the parent editor's
/// <see cref="VM_BodyTypeProfileEditor.RefreshMatchingPresets"/>.</summary>
public class VM_WeightFilterOption : VM
{
    public int Weight { get; set; }
    public bool IsSelected { get; set; } = true;
    public string Display => $"W{Weight}";
}

