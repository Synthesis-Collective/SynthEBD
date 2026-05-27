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

    /// <summary>Monotonic call counter for <see cref="RefreshPreviewAsync"/>. Each invocation
    /// captures its own generation on entry; after the async LoadNpcAsync await, the call
    /// re-checks this field — if a newer call has incremented it in the meantime, the older
    /// call bails before applying the BodySlide deformation. Without this, navigating the
    /// Match Presets list (or anywhere else that sets PreviewWeight + SelectedPreset in
    /// sequence) fires two concurrent RefreshPreviewAsync invocations whose ApplyBodySlide
    /// calls race, producing non-deterministic body shapes on repeat clicks. Field, not
    /// VM property, because it's pure plumbing — never read or set from XAML.</summary>
    private int _refreshPreviewGeneration = 0;
    /// <summary>Logger accessor for child VMs (per-profile) that need to emit messages
    /// through the editor's shared log sink. Internal because VM_BodyTypeProfile is the
    /// only legitimate consumer; making it public would invite misuse from unrelated VMs.</summary>
    internal Logger? Logger => _logger;
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
        // Lock the embedded viewer at default model scale (HeightOverride = 1.0) so the
        // body isn't scaled to the preview NPC's record Height. BoundingBox key vertices
        // are authored in mesh-local coordinates that don't carry through ModelScale, so a
        // non-1 scale would shrink/grow the mesh out from under the stored box AABBs and
        // the resolved vertex would no longer match the anatomical landmark. Override is
        // sticky across NPC loads — once set on the viewer instance it wins over every
        // future NpcBaseHeight pulled from a record. The Assignments viewers leave
        // HeightOverride null so they can still honor per-NPC heights.
        CharacterViewer.HeightOverride = 1.0f;
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

        DuplicateSelectedProfile = new RelayCommand(
            canExecute: _ => SelectedProfile != null,
            execute: _ => DoDuplicateProfile(SelectedProfile));

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

        // Ctrl+C on the Match Presets tab: copies the currently-selected row's preset+weight
        // identifier ("{Label} ({Weight})") to the clipboard so the user can paste it into
        // notes, rule descriptions, etc. Operates on SelectedMatchRow rather than the
        // viewer's loaded preset so the clipboard reflects exactly the highlighted list row.
        CopySelectedMatchToClipboardCommand = new RelayCommand(
            canExecute: _ => SelectedMatchRow != null,
            execute: _ => CopySelectedMatchToClipboard());

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
                    // Drop any preview marker the previous profile's selected row may have
                    // left in the viewer — the new profile's SelectedKeyVertex won't fire a
                    // PropertyChanged on profile switch (the value carries over), so without
                    // this the old marker would linger until the user clicked into the new
                    // profile's grid.
                    CharacterViewer?.SetPreviewKeyVertex(null, -1);
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
                    RefreshMeasurementValueOptions();
                    RefreshMatchPresetMeasurementOverlay();
                    RefreshMatchingPresets();
                    break;
                case nameof(SelectedMatchRow):
                    // Arrow-key navigation in the Match Presets list auto-previews each row.
                    if (SelectedMatchRow != null && !IsScanning)
                        LoadScanResultInViewer(SelectedMatchRow);
                    break;
                case nameof(ScoreSortMode):
                    // Mode change re-sorts the existing rows; no descriptor / scan state
                    // change so we skip the full descriptor-filter and weight-filter rebuild
                    // by going through RefreshMatchingPresets, which is cheap enough.
                    // Set IsSimilarityScoreMode / IsMeasurementValueScoreMode here so Fody
                    // fires PropertyChanged on them (XAML visibility for the per-mode
                    // secondary dropdowns binds to those flags); also refresh the per-mode
                    // option lists since their valid values track filter / profile state.
                    IsSimilarityScoreMode = ScoreSortMode.IsSimilarity();
                    IsMeasurementValueScoreMode = ScoreSortMode == MarginScoreMode.MeasurementValue;
                    RefreshSimilarityTargetOptions();
                    RefreshMeasurementValueOptions();
                    RefreshMatchingPresets();
                    break;
                case nameof(SimilarityTarget):
                    // Target change re-sorts when a Similarity sort is active; ignored
                    // otherwise. RefreshMatchingPresets short-circuits when scoringActive
                    // is false, so the no-op path is cheap.
                    if (ScoreSortMode.IsSimilarity()) RefreshMatchingPresets();
                    break;
                case nameof(SelectedMeasurementForSort):
                    // Mirrors the SimilarityTarget branch: re-sort only when the matching
                    // mode is active; otherwise leave MatchingPresets alone.
                    if (ScoreSortMode == MarginScoreMode.MeasurementValue) RefreshMatchingPresets();
                    break;
                case nameof(ShowMatchPresetMeasurements):
                    RefreshMatchPresetMeasurementOverlay();
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
    public RelayCommand DuplicateSelectedProfile { get; }
    public RelayCommand RefreshPresetList { get; }
    public RelayCommand ScanAllPresetsCommand { get; }
    public RelayCommand CancelScanCommand { get; }
    public RelayCommand LoadScanResultCommand { get; }
    public RelayCommand CopySelectedMatchToClipboardCommand { get; }

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

    /// <summary>When exactly one descriptor value is selected in <see cref="DescriptorFilter"/>,
    /// re-sorts <see cref="MatchingPresets"/> by how well each preset clears the matching
    /// rule's thresholds. <see cref="MarginScoreMode.Off"/> keeps the default
    /// (Gender, PresetLabel, Weight) order. The two enabled modes differ only in how the raw
    /// (value − threshold) margin is normalized; both pick the OR-group with the most slack
    /// and use that group's tightest condition (min margin) as the row's score. Ignored when
    /// zero or 2+ descriptors are selected — the default order applies because there's no
    /// single rule to score against.</summary>
    public MarginScoreMode ScoreSortMode { get; set; } = MarginScoreMode.Off;

    /// <summary>Pretty labels for the score-mode ComboBox so the enum names don't leak to
    /// the UI. Order matches <see cref="MarginScoreMode"/>'s declaration so SelectedIndex
    /// round-trips against the enum value cleanly.</summary>
    public IReadOnlyList<MarginScoreOption> ScoreSortModeOptions { get; } = new[]
    {
        new MarginScoreOption(MarginScoreMode.Off, "Default order"),
        new MarginScoreOption(MarginScoreMode.StdDevNormalized, "Match strength: σ-normalized margin"),
        new MarginScoreOption(MarginScoreMode.PercentOfThreshold, "Match strength: % of threshold"),
        new MarginScoreOption(MarginScoreMode.SimilarityToStdDevNormalized, "Similarity to: σ-normalized margin"),
        new MarginScoreOption(MarginScoreMode.SimilarityToPercentOfThreshold, "Similarity to: % of threshold"),
        new MarginScoreOption(MarginScoreMode.MeasurementValue, "Measurement: value (largest first)"),
    };

    /// <summary>Whether the current <see cref="ScoreSortMode"/> is one of the Similarity
    /// variants. XAML bindings consult this to show/hide the target-value picker.
    /// Kept as an auto-property (rather than a computed getter) so Fody's PropertyChanged
    /// weaver fires the notification when the ScoreSortMode handler reassigns it — an
    /// extension-method-based computed getter wouldn't be detected as dependent.</summary>
    public bool IsSimilarityScoreMode { get; private set; }

    /// <summary>Sibling values available as the comparison target for the Similarity sort
    /// modes. Populated from the filter's currently-selected category: every value with at
    /// least one rule in that category, *excluding* the filter's own selected value
    /// (comparing a descriptor to itself collapses to the existing Match-strength sort).
    /// Empty when no filter is selected or 2+ values are selected — same activation guard
    /// as the existing scoring path.</summary>
    public ObservableCollection<string> SimilarityTargetOptions { get; } = new();

    /// <summary>Currently-selected comparison target (a descriptor value in the same category
    /// as the filter selection). Null when nothing's picked yet or when the previous pick
    /// became invalid after a filter change. Drives which rule the Similarity score modes
    /// project each row onto.</summary>
    public string? SimilarityTarget { get; set; }

    /// <summary>Whether the current <see cref="ScoreSortMode"/> is
    /// <see cref="MarginScoreMode.MeasurementValue"/>. XAML bindings consult this to show/hide
    /// the per-measurement picker. Same Fody-friendly auto-property pattern as
    /// <see cref="IsSimilarityScoreMode"/>.</summary>
    public bool IsMeasurementValueScoreMode { get; private set; }

    /// <summary>Measurement names available for the MeasurementValue sort mode. Populated from
    /// the active profile's <see cref="VM_BodyTypeProfile.Measurements"/> collection
    /// (distinct, ordinal-sorted). Empty when no profile is selected. Drives the secondary
    /// dropdown that appears when ScoreSortMode is MeasurementValue.</summary>
    public ObservableCollection<string> MeasurementValueOptions { get; } = new();

    /// <summary>Name of the measurement chosen as the value-sort target. Null when nothing's
    /// picked yet or when the previous pick became invalid after a profile / measurements
    /// edit. Each row's <see cref="VM_PresetScanRow.Score"/> is set to the cached value of
    /// this measurement for that row's (preset, weight), with the rows sorted descending.</summary>
    public string? SelectedMeasurementForSort { get; set; }

    /// <summary>"Show Measurements" toggle on the Match Presets tab. When on, every measurement
    /// referenced by any rule whose descriptor is currently checked in
    /// <see cref="DescriptorFilter"/> is pushed into the viewer's measurement-line overlay
    /// (via the same channel the Measurements grid's multi-selection uses). Replaces the
    /// user's manual Measurements-grid selection while active; unchecking restores the
    /// fallback to <see cref="VM_BodyTypeProfile.SelectedMeasurement"/>. Refreshed when the
    /// filter selection or active profile changes.</summary>
    public bool ShowMatchPresetMeasurements { get; set; }

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

    /// <summary>Re-syncs <see cref="AvailableDescriptors"/> from the live
    /// <c>DescriptorUI</c> state on the parent <see cref="VM_SettingsOBody"/>. Called when
    /// the Rules tab becomes visible so descriptors added in OBody Misc Settings →
    /// Descriptors (which only writes back to <c>Settings_OBody.TemplateDescriptors</c> on
    /// save) are picked up here without forcing a save round-trip.
    /// <para>
    /// Performs an additive/subtractive diff so downstream subscribers (each profile's
    /// <see cref="VM_BodyTypeProfile.RebuildRuleTree"/>) fire at most once per actually-
    /// changed (Category, Value) — wholesale Clear+AddAll would emit one rebuild per item.
    /// </para></summary>
    public void RefreshAvailableDescriptorsFromLiveSettings()
    {
        var oBody = _oBodyVM?.Invoke();
        if (oBody?.DescriptorUI == null) return;

        // DumpToViewModels round-trips through the model type so we get the same shape
        // CopyInViewModelFromModel originally read at startup — no risk of de-syncing with
        // whatever the model-side considers the canonical form.
        var live = oBody.DescriptorUI.DumpToViewModels();
        if (live == null) return;

        var liveKeys = new HashSet<(string Cat, string Val)>();
        foreach (var d in live.Flatten())
        {
            if (d?.ID == null) continue;
            var cat = d.ID.Category ?? "";
            var val = d.ID.Value ?? "";
            if (string.IsNullOrEmpty(cat) || string.IsNullOrEmpty(val)) continue;
            liveKeys.Add((cat, val));
        }

        // Remove first so additions only see the post-removal state — gives the smallest
        // possible set of CollectionChanged events to the tree-rebuild subscribers.
        for (int i = AvailableDescriptors.Count - 1; i >= 0; i--)
        {
            var d = AvailableDescriptors[i];
            (string Cat, string Val) key = (d?.Category ?? "", d?.Value ?? "");
            if (!liveKeys.Contains(key))
            {
                AvailableDescriptors.RemoveAt(i);
            }
        }

        var existing = new HashSet<(string Cat, string Val)>();
        foreach (var d in AvailableDescriptors)
        {
            existing.Add((d?.Category ?? "", d?.Value ?? ""));
        }
        foreach (var key in liveKeys)
        {
            if (!existing.Contains(key))
            {
                AvailableDescriptors.Add(new BodyShapeDescriptor.LabelSignature
                {
                    Category = key.Cat,
                    Value = key.Val,
                });
            }
        }
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
            foreach (var d in model.TemplateDescriptors.Flatten())
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

    /// <summary>Duplicates <paramref name="source"/> in place — equivalent to
    /// Export-then-Import without the disk round-trip. Round-trips through DumpToModel so
    /// the copy is a fully independent deep clone (no shared row VMs, no shared
    /// MeasurementCache, no shared annotation entries); a fresh Id keeps it distinct from
    /// the original; the default Name is "<original> - copy" with the same
    /// " (N)" collision suffix the import path uses. The new profile is selected so the
    /// user can immediately edit it.</summary>
    private void DoDuplicateProfile(VM_BodyTypeProfile? source)
    {
        if (source == null) return;
        var clone = source.DumpToModel();
        // Fresh Id — the model's own Id field is the stable cross-session reference and
        // must be unique. Without this the duplicate would shadow the original in any
        // lookup keyed by Id.
        clone.Id = Guid.NewGuid().ToString("N");

        string baseName = string.IsNullOrWhiteSpace(clone.Name)
            ? "Profile - copy"
            : clone.Name.TrimEnd() + " - copy";
        var existingNames = new HashSet<string>(Profiles.Select(p => p.Name ?? ""), StringComparer.OrdinalIgnoreCase);
        clone.Name = baseName;
        int suffix = 2;
        while (existingNames.Contains(clone.Name))
        {
            clone.Name = baseName + " (" + suffix + ")";
            suffix++;
        }

        var vm = new VM_BodyTypeProfile(clone, this);
        Profiles.Add(vm);
        SelectedProfile = vm;
        _logger?.LogMessage("BodyTypeProfileEditor: duplicated profile '" + source.Name
            + "' as '" + clone.Name + "'");
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
        if (profile == null) return;
        // Box picks bypass the CapturePicks toggle — that toggle gates the per-click
        // single-vertex flow where every Pick Vertex click would otherwise spam the roster.
        // Box confirms are always explicit (a deliberate button press on the pending-box
        // editor, often after the user opened an edit session by clicking a row), so the
        // gate is overly conservative here and would silently swallow the confirm.
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
        // Also refresh the Similarity target list and (when the "Show Measurements" toggle
        // is on) the measurement-line overlay off the same signal — both depend entirely
        // on which descriptor(s) the user has selected.
        this.WhenAnyValue(x => x.DescriptorFilter.Header)
            .Skip(1)
            .Subscribe(_ =>
            {
                RefreshSimilarityTargetOptions();
                RefreshMatchPresetMeasurementOverlay();
                RefreshMatchingPresets();
            })
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
        // Claim a generation slot. The PropertyChanged handler kicks off this method
        // every time SelectedPreset, PreviewWeight, PreviewGender, or PreviewNpcOverride
        // changes — and LoadScanResultInViewer flips two or three of those in sequence per
        // click, firing this method 2-3× back-to-back. Snapshot all the read-from-VM inputs
        // now (so an in-flight call doesn't observe a newer setter's value mid-flight) and
        // re-check our generation after the LoadNpcAsync await so older calls bail before
        // their ApplyBodySlide races against the newer call's deformation.
        int myGen = ++_refreshPreviewGeneration;
        var snapshotPreset = SelectedPreset?.AssociatedModel;
        int snapshotWeight = PreviewWeight;
        Gender snapshotGender = PreviewGender;
        FormKey snapshotNpcOverride = PreviewNpcOverride;
        var snapshotProfile = SelectedProfile;

        try
        {
            if (snapshotPreset == null || lk == null) return;

            FormKey npc = FormKey.Null;
            if (!snapshotNpcOverride.IsNull)
            {
                npc = snapshotNpcOverride;
            }
            else
            {
                var preview = _patcherState?.OBodySettings?.PreviewNpcs;
                if (preview != null && preview.WeightPreviewNpcs.TryGetValue(snapshotWeight, out var pair) && pair != null)
                {
                    npc = snapshotGender == Gender.Female ? pair.FemaleNpc : pair.MaleNpc;
                }
            }

            if (npc.IsNull)
            {
                _logger?.LogMessage("BodyTypeProfileEditor: no preview NPC configured for weight " + snapshotWeight + " (" + snapshotGender + ")");
                return;
            }

            await CharacterViewer.LoadNpcAsync(npc, lk);

            // A newer RefreshPreviewAsync call has superseded us — its captured (preset,
            // weight) is the canonical "what the user wants to see" now, so bail before
            // ApplyBodySlide. Without this we'd ApplyBodySlide with our stale snapshot
            // after a later call already applied the fresh one (or worse, our call wins
            // and the user sees old-preset-at-new-weight).
            if (myGen != _refreshPreviewGeneration) return;

            CharacterViewer.ApplyBodySlide(snapshotPreset, snapshotWeight);

            // Point the active profile at this viewer so live measurement readouts have
            // a source and pick-capture routes into the right profile by default. Use the
            // snapshot too — SelectedProfile might have changed under us, and the older
            // value's measurements are what our just-applied deformation belongs to.
            if (snapshotProfile != null)
            {
                snapshotProfile.AttachViewer(CharacterViewer);
                snapshotProfile.RefreshMeasurementValues();
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

    /// <summary>Opens a <see cref="Window_MeasurementHistogram"/> for the named measurement
    /// on <paramref name="profile"/>, driving a scan first if the cache is stale so the
    /// histogram always reflects current geometry. The window is non-modal
    /// (<see cref="System.Windows.Window.Show"/>, not ShowDialog) so the user can keep it
    /// open while continuing to edit measurements / rules in the main editor. The histogram
    /// VM takes a snapshot of the cache at open time — re-clicking H after a re-scan opens
    /// a fresh window with the updated numbers.
    /// <para>Guards against re-entry while a scan is already in progress (returns silently);
    /// the row's command's canExecute keeps the H button enabled by name presence, so
    /// during a scan a click is a no-op rather than a queued action — matches the behavior
    /// of <see cref="ScanAllPresetsCommand"/>'s !IsScanning gate.</para></summary>
    public async System.Threading.Tasks.Task OpenMeasurementHistogramAsync(
        VM_BodyTypeProfile profile, VM_MeasurementDefinition definition)
    {
        if (profile == null || definition == null) return;
        string name = definition.Name?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) return;

        if (IsScanning) return;

        // Drive a scan when the cache may not reflect current geometry. Same gate the
        // Match Presets tab uses: MeasurementCacheStale flips on any edit that affects
        // the underlying numbers (KeyVertex / MeasurementDefinition edits, profile
        // switch). An empty cache (never scanned) also triggers a scan because the
        // histogram would otherwise be empty for a workflow where the user opened the
        // profile and immediately clicked H without ever hitting "Scan All Presets".
        if (profile.MeasurementCacheStale || profile.MeasurementCache.Count == 0)
        {
            // RunScanAsync uses SelectedProfile to know which profile to scan, so
            // temporarily ensure it points at the right profile. In practice the row VM
            // can only be clicked when its owning profile IS the selected one (the
            // Measurements grid is only visible for SelectedProfile), so this is a
            // belt-and-braces guard rather than the normal path.
            if (!ReferenceEquals(SelectedProfile, profile)) SelectedProfile = profile;
            await RunScanAsync();
            if (profile.MeasurementCache.Count == 0)
            {
                // Scan completed but produced no entries — probably no presets matched the
                // profile's body type. Surface a notification instead of opening an empty
                // window.
                MessageWindow.DisplayNotificationOK(
                    "No data for histogram",
                    $"No cached measurements available for '{name}'. The scan returned no (preset, weight) entries — check that this profile's Body Type matches at least one preset's SliderGroup.");
                return;
            }
        }

        var histogramVm = new VM_MeasurementHistogram(profile, definition);
        if (histogramVm.TotalSamples == 0)
        {
            MessageWindow.DisplayNotificationOK(
                "No data for histogram",
                $"The cache has entries but '{name}' couldn't be evaluated on any of them (every value is null). Re-scan with Verbose Scan enabled to diagnose, or fix any invalid vertex refs on this row.");
            return;
        }

        var window = new Window_MeasurementHistogram
        {
            DataContext = histogramVm,
        };
        window.Show();
    }

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

                // Per-iteration diagnostic: snapshot viewer state right before the
                // ApplyBodySlide so we can see whether the call entered the "queued
                // for later commit" branch or actually deformed.
                bool preIsSceneReady = false;
                int preNpcWeight = -1;
                int preCachedMeshCount = -1;
                if (VerboseScan)
                {
                    try { preIsSceneReady = viewer.IsSceneReady; } catch { }
                    try { preNpcWeight = viewer.NpcWeight; } catch { }
                    try { preCachedMeshCount = viewer.GetCurrentShapeVertexCounts().Count; } catch { }
                }

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

                // Pass the iteration's gender so RuleGender-filtered rules behave correctly
                // (Male-only rules only fire on male presets, etc.).
                var result = BodySlideMeasurementEvaluator.Evaluate(viewer, profileModel, includeDrafts: true, evaluationGender: gender);

                if (VerboseScan)
                {
                    // Dump key measurements + viewer state so we can correlate scan
                    // output with CSV exports and detect deformation issues at extreme
                    // weights. Keys chosen to cover the most commonly-misbehaving rules:
                    // Cup/Chest (cp), UnrealisticWaist (wh), ChestSag (csr), Arms (att),
                    // Belly/Shape Apple gate (bp), Hips (hpt), waist_width raw, hip_width.
                    string FmtMeas(string key)
                    {
                        if (result.Measurements.TryGetValue(key, out var v))
                            return v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                        if (result.FailedMeasurements.TryGetValue(key, out var why))
                            return "FAIL(" + why + ")";
                        return "—";
                    }
                    int postNpcWeight = -1;
                    bool postIsSceneReady = false;
                    try { postNpcWeight = viewer.NpcWeight; } catch { }
                    try { postIsSceneReady = viewer.IsSceneReady; } catch { }
                    _logger?.LogMessage($"BodyTypeProfile scan iter [{done + 1}/{missing.Count}] '{model.Label}'@W{weight}: "
                        + $"pre(sceneReady={preIsSceneReady},npcW={preNpcWeight},cachedMeshes={preCachedMeshCount}) "
                        + $"post(sceneReady={postIsSceneReady},npcW={postNpcWeight}) "
                        + $"cp={FmtMeas("chest_projection")} wh={FmtMeas("waist_to_hip")} "
                        + $"csr={FmtMeas("chest_sag_ratio")} att={FmtMeas("arm_thickness_to_torso")} "
                        + $"bp={FmtMeas("belly_projection")} hpt={FmtMeas("hip_to_torso")} "
                        + $"ww={FmtMeas("waist_width")} hipW={FmtMeas("hip_width")} "
                        + $"meas={result.Measurements.Count} failed={result.FailedMeasurements.Count} "
                        + $"topoMismatch={result.TopologyMismatch} ruleMatches={result.Descriptors.Count}");
                }

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
    /// weight for each preset in a predictable order.
    /// <para>When <see cref="ScoreSortMode"/> is enabled AND exactly one descriptor value is
    /// selected, each row is also scored against the rule(s) for that descriptor and the
    /// list is re-sorted by score (descending). See <see cref="ScoreRuleAgainstMeasurements"/>
    /// for the per-rule margin computation.</para></summary>
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

        // Stage rows in a list first so a score-based re-sort can replace the default order
        // before we publish to the ObservableCollection. Adding incrementally and then
        // re-ordering in-place would fire one CollectionChanged per row, which the ListBox
        // would render through visibly.
        var staged = new List<VM_PresetScanRow>();
        foreach (var kv in ordered)
        {
            if (weightFilterActive && !allowedWeights.Contains(kv.Key.Weight)) continue;
            if (!DescriptorFilterAccepts(kv.Value, selectionKeys, filterMode)) continue;
            staged.Add(new VM_PresetScanRow(kv.Key.PresetLabel, kv.Key.Gender, kv.Key.Weight, kv.Value));
        }

        // MeasurementValue sort is independent of the descriptor filter — the row is scored
        // by reading the selected measurement's cached value directly, regardless of which
        // descriptors (if any) are checked. Handled before the rule-margin path so the two
        // don't interact.
        if (ScoreSortMode == MarginScoreMode.MeasurementValue
            && !string.IsNullOrEmpty(SelectedMeasurementForSort))
        {
            string measurementName = SelectedMeasurementForSort!;
            foreach (var row in staged)
            {
                if (!profile.MeasurementCache.TryGetValue(
                        (row.PresetLabel, row.Gender, row.Weight), out var entry))
                    continue;
                if (!entry.Measurements.TryGetValue(measurementName, out var vBox) || !vBox.HasValue)
                    continue;
                row.Score = vBox.Value;
                row.ScoreDisplay = FormatScore(vBox.Value, MarginScoreMode.MeasurementValue);
                // Tooltip helps the user remember which measurement they're looking at when
                // skimming a long list; mirrors the sibling-scores tooltip slot on the badge.
                row.SiblingScoresTooltip = $"{measurementName} = {vBox.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}";
            }

            staged = staged
                .OrderByDescending(r => r.Score.HasValue)
                .ThenByDescending(r => r.Score ?? 0.0)
                .ThenBy(r => r.Gender)
                .ThenBy(r => r.PresetLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Weight)
                .ToList();

            foreach (var row in staged) MatchingPresets.Add(row);

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
            return;
        }

        // Score-based sort only applies when exactly one descriptor value is selected — with
        // 0 or 2+ there's no single rule to project onto, so we leave the default sort alone
        // rather than picking an arbitrary descriptor. Similarity modes additionally require
        // a non-empty SimilarityTarget; without one there's no second rule to score against,
        // so we treat the mode as Off for this refresh.
        bool isSimilarity = ScoreSortMode.IsSimilarity();
        bool scoringActive = ScoreSortMode != MarginScoreMode.Off
                             && selectionKeys.Count == 1
                             && (!isSimilarity || !string.IsNullOrEmpty(SimilarityTarget));
        if (scoringActive)
        {
            var (cat, val) = selectionKeys.First();
            // Similarity modes score against the user-picked target value in the same
            // category rather than the filter's own value; the rest of the scoring pipeline
            // is identical, so we just swap the row-target name before picking matchingRules.
            // The metric (σ vs %) is recovered via SimilarityBaseMode for the Similarity
            // variants so ScoreRuleAgainstMeasurements / FormatScore receive the underlying
            // enum value they already understand.
            string scoredValue = isSimilarity ? SimilarityTarget! : val;
            MarginScoreMode scoringMetric = isSimilarity ? ScoreSortMode.SimilarityBaseMode() : ScoreSortMode;
            var matchingRules = profile.Rules
                .Where(r => string.Equals(r.DescriptorCategory, cat, StringComparison.Ordinal)
                         && string.Equals(r.DescriptorValue, scoredValue, StringComparison.Ordinal))
                .ToList();
            // Sibling rules = same Category, any Value. Used for the per-row near-miss
            // tooltip ("you matched Pear but you're close to Rectangle"). Includes the
            // selected value's own rules so the tooltip line for the selected value lines
            // up with the badge score — caller doesn't have to special-case the selected
            // value to know it's the row's primary score.
            var siblingRules = profile.Rules
                .Where(r => string.Equals(r.DescriptorCategory, cat, StringComparison.Ordinal))
                .ToList();
            if (matchingRules.Count > 0)
            {
                // Std-dev normalization needs population statistics across the full cache.
                // Compute once per refresh (not per row) so the cost is O(presets × names)
                // rather than O(rows × presets × names). Pass siblingRules (a superset of
                // matchingRules) so sibling-score normalization uses the same sigmas as the
                // primary score — otherwise σ values per measurement would shift between
                // badge and tooltip whenever a sibling rule references a measurement the
                // primary rule doesn't.
                Dictionary<string, double> stdDevs = null;
                if (scoringMetric == MarginScoreMode.StdDevNormalized)
                    stdDevs = ComputePopulationStdDevs(profile, siblingRules);

                foreach (var row in staged)
                {
                    if (!profile.MeasurementCache.TryGetValue(
                            (row.PresetLabel, row.Gender, row.Weight), out var entry))
                        continue;
                    double? best = null;
                    foreach (var rule in matchingRules)
                    {
                        double? rs = ScoreRuleAgainstMeasurements(rule, entry.Measurements, scoringMetric, stdDevs);
                        if (rs.HasValue && (!best.HasValue || rs.Value > best.Value))
                            best = rs;
                    }
                    row.Score = best;
                    row.ScoreDisplay = FormatScore(best, scoringMetric);
                    row.SiblingScoresTooltip = BuildSiblingScoresTooltip(
                        cat, siblingRules, entry.Measurements, scoringMetric, stdDevs);
                }

                // Sort: highest score first, scored rows ahead of unscored ones, ties broken
                // by the default (Gender, PresetLabel, Weight) order so identical-score rows
                // group predictably across re-runs.
                staged = staged
                    .OrderByDescending(r => r.Score.HasValue)
                    .ThenByDescending(r => r.Score ?? 0.0)
                    .ThenBy(r => r.Gender)
                    .ThenBy(r => r.PresetLabel, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.Weight)
                    .ToList();
            }
        }

        foreach (var row in staged) MatchingPresets.Add(row);

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

    /// <summary>Rebuilds <see cref="SimilarityTargetOptions"/> from the current filter state.
    /// The available targets are every descriptor value in the filter's category that has at
    /// least one rule on the active profile, minus the filter's own selected value (a
    /// "Similarity to self" comparison is a no-op compared to the existing Match-strength
    /// sort, so we exclude it to keep the dropdown short).
    /// <para>The previously-selected target survives the refresh when it's still valid;
    /// otherwise it falls back to the first available option. Called from the filter-change
    /// subscription and the ScoreSortMode property-changed branch so the list tracks both
    /// signals.</para></summary>
    private void RefreshSimilarityTargetOptions()
    {
        var profile = SelectedProfile;
        var filterSelection = DescriptorFilter?.DumpToHashSet() ?? new HashSet<BodyShapeDescriptor.LabelSignature>();
        var selectionKeys = filterSelection.Select(s => (s.Category, s.Value)).ToHashSet();

        SimilarityTargetOptions.Clear();
        if (profile == null || selectionKeys.Count != 1) { SimilarityTarget = null; return; }

        var (cat, selectedVal) = selectionKeys.First();
        // Distinct sibling values, ordinal-sorted for stable display. Excludes the filter's
        // own value because comparing it to itself yields the same scores as the existing
        // Match-strength sort modes.
        var siblings = profile.Rules
            .Where(r => string.Equals(r.DescriptorCategory, cat, StringComparison.Ordinal))
            .Select(r => r.DescriptorValue)
            .Where(v => !string.IsNullOrEmpty(v) && !string.Equals(v, selectedVal, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var v in siblings) SimilarityTargetOptions.Add(v);

        // Preserve the user's existing pick across a category-preserving refresh (e.g., the
        // user clicks a different *weight* slot but keeps Shape:Hourglass selected). Drop
        // it only when the previous target is no longer in the new list — typical when the
        // filter category itself changes.
        if (string.IsNullOrEmpty(SimilarityTarget) || !siblings.Contains(SimilarityTarget))
        {
            SimilarityTarget = siblings.FirstOrDefault();
        }
    }

    /// <summary>Rebuilds <see cref="MeasurementValueOptions"/> from the active profile's
    /// measurement definitions. Distinct ordinal-sorted names, empty when no profile is
    /// selected. <see cref="SelectedMeasurementForSort"/> survives the refresh when it's
    /// still valid; otherwise falls back to the first option.</summary>
    private void RefreshMeasurementValueOptions()
    {
        MeasurementValueOptions.Clear();
        var profile = SelectedProfile;
        if (profile == null) { SelectedMeasurementForSort = null; return; }

        var names = profile.Measurements
            .Select(m => m?.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var n in names) MeasurementValueOptions.Add(n);

        if (string.IsNullOrEmpty(SelectedMeasurementForSort) || !names.Contains(SelectedMeasurementForSort))
        {
            SelectedMeasurementForSort = names.FirstOrDefault();
        }
    }

    /// <summary>Pushes the descriptor-derived measurement set into the active profile's
    /// viewer overlay channel when <see cref="ShowMatchPresetMeasurements"/> is on; clears
    /// the channel otherwise. The derivation walks every rule on the profile whose descriptor
    /// is currently checked in <see cref="DescriptorFilter"/>, collects each Measurement-kind
    /// condition's <see cref="VM_MeasurementCondition.MeasurementName"/>, and maps those names
    /// to the matching <see cref="VM_MeasurementDefinition"/> rows (deduplicated by reference).
    /// <para>"Replace" semantics: while the toggle is on the descriptor-derived list owns the
    /// overlay; toggling off restores the Measurements-grid fallback via the empty-list path
    /// in <see cref="VM_BodyTypeProfile.UpdateSelectedMeasurements"/>.</para></summary>
    private void RefreshMatchPresetMeasurementOverlay()
    {
        var profile = SelectedProfile;
        if (profile == null) return;

        if (!ShowMatchPresetMeasurements)
        {
            profile.UpdateSelectedMeasurements(Array.Empty<VM_MeasurementDefinition>());
            return;
        }

        var selectionKeys = DescriptorFilter?.DumpToHashSet()
            ?.Select(s => (s.Category ?? "", s.Value ?? ""))
            .ToHashSet() ?? new HashSet<(string, string)>();
        if (selectionKeys.Count == 0)
        {
            profile.UpdateSelectedMeasurements(Array.Empty<VM_MeasurementDefinition>());
            return;
        }

        // Collect every Measurement-kind condition name referenced by any rule whose
        // descriptor matches one of the checked filter entries. Ordinal dedupe keeps the
        // viewer from drawing the same line twice when multiple rules share a measurement.
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in profile.Rules)
        {
            if (rule == null) continue;
            var key = (rule.DescriptorCategory ?? "", rule.DescriptorValue ?? "");
            if (!selectionKeys.Contains(key)) continue;
            foreach (var group in rule.Groups)
            {
                if (group?.Conditions == null) continue;
                foreach (var cond in group.Conditions)
                {
                    if (cond == null) continue;
                    if (cond.Kind != MeasurementConditionKind.Measurement) continue;
                    if (string.IsNullOrEmpty(cond.MeasurementName)) continue;
                    names.Add(cond.MeasurementName);
                }
            }
        }

        // Map names → MeasurementDefinitions. First match per name (matching the runtime
        // evaluator's first-row-wins policy when the user has duplicate names). Definitions
        // whose name is referenced but no longer exists are silently skipped.
        var byName = new Dictionary<string, VM_MeasurementDefinition>(StringComparer.Ordinal);
        foreach (var def in profile.Measurements)
        {
            if (def == null || string.IsNullOrEmpty(def.Name)) continue;
            if (!byName.ContainsKey(def.Name)) byName[def.Name] = def;
        }

        var picked = new List<VM_MeasurementDefinition>();
        foreach (var n in names)
        {
            if (byName.TryGetValue(n, out var d)) picked.Add(d);
        }

        profile.UpdateSelectedMeasurements(picked);
    }

    /// <summary>Welford-style single-pass std-dev across the profile's full
    /// <see cref="VM_BodyTypeProfile.MeasurementCache"/> for every measurement name that
    /// appears in any continuous (≤, &lt;, ≥, &gt;) condition inside <paramref name="rules"/>.
    /// Names referenced only by Equal/NotEqual/DescriptorRef conditions are omitted because
    /// the scorer skips those — including them would still be safe, just wasted work.
    /// <para>Std-dev is computed as sample std-dev (n−1 denominator); 0 or fewer than two
    /// samples returns 0.0 which the scorer treats as the fallback signal.</para></summary>
    private static Dictionary<string, double> ComputePopulationStdDevs(
        VM_BodyTypeProfile profile, List<VM_MeasurementRule> rules)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            foreach (var group in rule.Groups)
            {
                foreach (var cond in group.Conditions)
                {
                    if (cond == null) continue;
                    if (cond.Kind != MeasurementConditionKind.Measurement) continue;
                    if (!IsContinuousComparator(cond.Comparator)) continue;
                    if (string.IsNullOrEmpty(cond.MeasurementName)) continue;
                    names.Add(cond.MeasurementName);
                }
            }
        }

        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            int n = 0;
            double mean = 0, m2 = 0;
            foreach (var entry in profile.MeasurementCache.Values)
            {
                if (entry == null) continue;
                if (!entry.Measurements.TryGetValue(name, out var v) || !v.HasValue) continue;
                n++;
                double delta = v.Value - mean;
                mean += delta / n;
                double delta2 = v.Value - mean;
                m2 += delta * delta2;
            }
            result[name] = n >= 2 ? Math.Sqrt(m2 / (n - 1)) : 0.0;
        }
        return result;
    }

    /// <summary>True for comparators that produce a continuous "value − threshold" margin
    /// (the four directional comparators). Equal/NotEqual are excluded because their margin
    /// is essentially binary (within ε or not) and would dominate the min-across-conditions
    /// reduction with values incomparable to the directional margins.</summary>
    private static bool IsContinuousComparator(MeasurementComparator cmp)
        => cmp == MeasurementComparator.LessThan
        || cmp == MeasurementComparator.LessThanOrEqual
        || cmp == MeasurementComparator.GreaterThan
        || cmp == MeasurementComparator.GreaterThanOrEqual;

    /// <summary>Computes a single rule's match-strength score against a row's cached
    /// measurements. Score = max over OR-groups of (min over continuous conditions in that
    /// group of normalized margin). Groups containing only DescriptorRef / Equal / NotEqual
    /// conditions score 0.0 (passed but un-rankable). Returns null when no group is fully
    /// satisfied — typically only happens when rules were edited after the last scan and the
    /// cache is now stale.</summary>
    /// <param name="rule">Rule to evaluate. Must already have a matching descriptor; caller
    /// filters by descriptor before calling.</param>
    /// <param name="measurements">Row's cached values, keyed by measurement name. Float?
    /// because failed evaluations land in the cache as null.</param>
    /// <param name="mode">Normalization choice. <see cref="MarginScoreMode.Off"/> returns
    /// null — caller is expected to gate on Off itself, this is just defensive.</param>
    /// <param name="stdDevs">Population std-dev per measurement name. Required when mode is
    /// StdDevNormalized; ignored when PercentOfThreshold.</param>
    private static double? ScoreRuleAgainstMeasurements(
        VM_MeasurementRule rule,
        IReadOnlyDictionary<string, float?> measurements,
        MarginScoreMode mode,
        Dictionary<string, double> stdDevs)
    {
        if (mode == MarginScoreMode.Off) return null;
        if (rule == null || rule.Groups.Count == 0) return null;

        double? best = null;
        foreach (var group in rule.Groups)
        {
            if (group?.Conditions == null || group.Conditions.Count == 0) continue;

            double? groupMin = null;
            bool hasScoredCondition = false;
            bool groupValid = true;

            foreach (var cond in group.Conditions)
            {
                if (cond == null) { groupValid = false; break; }
                // Binary conditions (DescriptorRef + Equal/NotEqual) don't contribute a
                // numeric margin. We still trust the upstream scan's verdict on whether
                // the group as a whole matched — only continuous conditions feed the min.
                if (cond.Kind != MeasurementConditionKind.Measurement) continue;
                if (!IsContinuousComparator(cond.Comparator)) continue;
                if (string.IsNullOrEmpty(cond.MeasurementName)) continue;
                if (!measurements.TryGetValue(cond.MeasurementName, out var vBox) || !vBox.HasValue)
                {
                    // A continuous condition can't be evaluated → treat the group as
                    // un-scorable. Move on to the next group rather than feeding a
                    // sentinel into the min.
                    groupValid = false;
                    break;
                }

                double v = vBox.Value;
                double t = cond.Value;
                var cmp = cond.Comparator;
                double raw = (cmp == MeasurementComparator.LessThan || cmp == MeasurementComparator.LessThanOrEqual)
                    ? t - v
                    : v - t;

                double normalized;
                switch (mode)
                {
                    case MarginScoreMode.StdDevNormalized:
                    {
                        double sigma = 0.0;
                        stdDevs?.TryGetValue(cond.MeasurementName, out sigma);
                        // sigma ≤ 0 means every preset has the same value for this measurement
                        // (or n<2). Fall back to percent-of-threshold so the row still gets a
                        // meaningful score instead of dividing by zero.
                        if (sigma > 1e-9)
                            normalized = raw / sigma;
                        else if (Math.Abs(t) > 1e-6)
                            normalized = raw / Math.Abs(t);
                        else
                            normalized = raw;
                        break;
                    }
                    case MarginScoreMode.PercentOfThreshold:
                        normalized = Math.Abs(t) > 1e-6 ? raw / Math.Abs(t) : raw;
                        break;
                    default:
                        normalized = raw;
                        break;
                }

                if (!groupMin.HasValue || normalized < groupMin.Value) groupMin = normalized;
                hasScoredCondition = true;
            }

            if (!groupValid) continue;
            double groupScore = hasScoredCondition ? groupMin!.Value : 0.0;
            if (!best.HasValue || groupScore > best.Value) best = groupScore;
        }
        return best;
    }

    /// <summary>Builds the per-row tooltip text listing this row's margin score against
    /// every descriptor value in the selected Category, sorted closest-to-matching first.
    /// Lets the user see at a glance whether a barely-matched row is "almost Rectangle" or
    /// "almost Hourglass" without re-selecting each descriptor in turn.
    /// <para>Returns null when there are no sibling rules to report (the selected category
    /// has only one descriptor with a rule, so the tooltip would just repeat the badge). WPF
    /// suppresses null tooltips rather than rendering an empty box.</para></summary>
    /// <param name="category">Descriptor Category of the currently-selected value. Used in
    /// the tooltip header so the user remembers which axis they're looking at.</param>
    /// <param name="siblingRules">Every rule with Descriptor.Category == <paramref name="category"/>.
    /// Multiple rules per value are collapsed via max-score, mirroring the primary score's
    /// max-across-matching-rules logic.</param>
    /// <param name="measurements">Row's cached measurement values.</param>
    /// <param name="mode">Same mode that drives the primary score so the units agree.</param>
    /// <param name="stdDevs">Population sigmas (StdDevNormalized only). Caller must compute
    /// these against <paramref name="siblingRules"/> so every value's score uses the same
    /// per-measurement sigma.</param>
    private static string BuildSiblingScoresTooltip(
        string category,
        List<VM_MeasurementRule> siblingRules,
        IReadOnlyDictionary<string, float?> measurements,
        MarginScoreMode mode,
        Dictionary<string, double> stdDevs)
    {
        if (siblingRules == null || siblingRules.Count == 0) return null;

        // Collapse multiple rules per value to one entry via max-score, matching the primary
        // score's behavior so the tooltip line for the selected value lines up with the badge.
        var byValue = new Dictionary<string, double?>(StringComparer.Ordinal);
        foreach (var rule in siblingRules)
        {
            var v = rule.DescriptorValue ?? "";
            if (string.IsNullOrEmpty(v)) continue;
            double? s = ScoreRuleAgainstMeasurements(rule, measurements, mode, stdDevs);
            if (!s.HasValue) continue;
            if (!byValue.TryGetValue(v, out var existing) || !existing.HasValue || s.Value > existing.Value)
                byValue[v] = s;
        }
        if (byValue.Count == 0) return null;

        // Sort closest-to-matching first. Within the tooltip a near-miss (-0.1σ) is just as
        // interesting as the actual match (+0.3σ); the descending sort puts both at the top
        // and pushes the truly-far-from-matching siblings to the bottom.
        var ordered = byValue
            .Where(kv => kv.Value.HasValue)
            .OrderByDescending(kv => kv.Value.Value)
            .ToList();

        // No column padding — WPF's default ToolTip renders in a proportional font, so
        // PadRight'd spaces wouldn't line up anyway. One descriptor per line is enough
        // structure for the reader to map value → score.
        var sb = new System.Text.StringBuilder();
        sb.Append(category).Append(" — sibling scores (closest first):");
        foreach (var kv in ordered)
        {
            sb.Append('\n').Append("  ").Append(kv.Key).Append(": ");
            sb.Append(FormatScoreNumber(kv.Value, mode));
        }
        return sb.ToString();
    }

    /// <summary>Pre-formats <paramref name="score"/> for the row's <c>ScoreDisplay</c>
    /// property — i.e., the badge next to the preset name. Wraps <see cref="FormatScoreNumber"/>
    /// with a per-mode label prefix ("score" for the rule-margin modes, "value" for the
    /// MeasurementValue mode) so the badge reads as a complete sentence. Returns empty string
    /// when score is null so the binding renders no extra line.</summary>
    private static string FormatScore(double? score, MarginScoreMode mode)
    {
        var bare = FormatScoreNumber(score, mode);
        if (bare.Length == 0) return "";
        return mode == MarginScoreMode.MeasurementValue ? "value " + bare : "score " + bare;
    }

    /// <summary>Pre-formats just the numeric portion of a score (e.g. <c>"+0.03σ"</c>,
    /// <c>"-45%"</c>, <c>"1.234"</c>) for contexts that already supply their own label — most
    /// importantly the sibling-scores tooltip, where each line has the descriptor name as the
    /// label and only needs the value. Empty string for null scores; unit suffix differs by
    /// mode. The rule-margin modes get a signed prefix (clearance past the threshold is the
    /// meaningful signal); MeasurementValue is reported as a plain F3 value (sign is part of
    /// the number itself, not a margin-direction indicator).</summary>
    private static string FormatScoreNumber(double? score, MarginScoreMode mode)
    {
        if (!score.HasValue) return "";
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        switch (mode)
        {
            case MarginScoreMode.StdDevNormalized:
            {
                string sign = score.Value >= 0 ? "+" : "";
                return sign + score.Value.ToString("F2", inv) + "σ";
            }
            case MarginScoreMode.PercentOfThreshold:
            {
                string sign = score.Value >= 0 ? "+" : "";
                return sign + (score.Value * 100.0).ToString("F0", inv) + "%";
            }
            case MarginScoreMode.MeasurementValue:
                return score.Value.ToString("F3", inv);
            default:
                return "";
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

    /// <summary>Bound to Ctrl+C on the Match Presets tab. Writes "{PresetLabel} ({Weight})"
    /// for the currently-selected scan row to the system clipboard. Format mirrors the row's
    /// display text minus the (Gender) qualifier so the result is a clean preset reference.
    /// No-op when no row is selected — the caller's canExecute already gates this.</summary>
    private void CopySelectedMatchToClipboard()
    {
        var row = SelectedMatchRow;
        if (row == null) return;
        try
        {
            string text = (row.PresetLabel ?? "") + " (" + row.Weight + ")";
            System.Windows.Clipboard.SetText(text);
            _logger?.LogMessage("BodyTypeProfileEditor: copied '" + text + "' to clipboard.");
        }
        catch (Exception ex)
        {
            _logger?.LogError("CopySelectedMatchToClipboard failed: " + ex.Message);
        }
    }

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

        // Developer convenience: uniform Ctrl+S/Ctrl+L on every Body Type Profile sub-tab
        // (KeyVertices, Measurements, Rules) saves and loads that tab's collection as a
        // standalone JSON envelope. Format matches the Revised_BodyTypeProfile_Rules.json
        // reference style — top-level { "IsPatch": ..., "<CollectionName>": [...] } with
        // documentation __comment_* siblings tolerated on load (Newtonsoft ignores unknown
        // properties).
        //
        // Load auto-detects the mode from the IsPatch flag: false / absent → wholesale-replace
        // with a YesNo confirmation when the target collection is non-empty (legacy behavior;
        // identical for files written before the flag existed); true → add-or-replace by Name
        // (KeyVertices / Measurements) or by Id (Rules) via the Apply*Patch helpers, with a
        // distinct "Apply patch?" confirmation. Rules patches additionally honor RulesToDelete
        // (delete-then-add ordering avoids visual flicker).
        //
        // Save is multi-shortcut: Ctrl+S writes a full snapshot (IsPatch=false); patch saves
        // use Ctrl+Shift+P (Measurements: selected rows; Rules: rules under the selected tree
        // node); Rules also has Ctrl+Shift+Alt+P which opens a picker window for marking
        // additional deletions. KeyVertices Ctrl+S is smart: 0 / all rows selected → full
        // snapshot, strict subset selected → patch.
        SaveKeyVerticesToJson = new RelayCommand(
            canExecute: _ => KeyVertices.Count > 0,
            execute: _ => SaveKeyVerticesToJsonFile());
        LoadKeyVerticesFromJson = new RelayCommand(
            canExecute: _ => true,
            execute: _ => LoadKeyVerticesFromJsonFile());
        SaveMeasurementsToJson = new RelayCommand(
            canExecute: _ => Measurements.Count > 0,
            execute: _ => SaveMeasurementsToJsonFile());
        SaveMeasurementsPatchToJson = new RelayCommand(
            canExecute: _ => Measurements.Count > 0,
            execute: _ => SaveMeasurementsPatchToJsonFile());
        LoadMeasurementsFromJson = new RelayCommand(
            canExecute: _ => true,
            execute: _ => LoadMeasurementsFromJsonFile());
        SaveRulesToJson = new RelayCommand(
            canExecute: _ => Rules.Count > 0,
            execute: _ => SaveRulesToJsonFile());
        SaveRulesPatchToJson = new RelayCommand(
            canExecute: _ => Rules.Count > 0,
            execute: _ => SaveRulesPatchToJsonFile());
        SaveRulesPatchWithDeletesToJson = new RelayCommand(
            canExecute: _ => Rules.Count > 0,
            execute: _ => SaveRulesPatchWithDeletesToJsonFile());
        LoadRulesFromJson = new RelayCommand(
            canExecute: _ => true,
            execute: _ => LoadRulesFromJsonFile());

        // Measurements tab also has secondary export shortcuts that pre-date the uniform
        // JSON contract — Ctrl+Shift+S writes a CSV for spreadsheet analysis (includes
        // the LiveValue column resolved against the loaded mesh), Ctrl+C copies the same
        // table as TSV onto the clipboard for direct Excel/Sheets paste. Both are
        // one-way developer conveniences — round-trip authoring uses the JSON path.
        SaveMeasurementsToCsv = new RelayCommand(
            canExecute: _ => Measurements.Count > 0,
            execute: _ => SaveMeasurementsToCsvFile());
        CopyMeasurementsToClipboard = new RelayCommand(
            canExecute: _ => Measurements.Count > 0,
            execute: _ => CopyMeasurementsToClipboardTsv());

        // Ctrl+Alt+Shift+S on the Measurements tab: drives a scan across every (preset,
        // weight) target for this profile and dumps the resulting measurement table as a
        // single cumulative CSV. Skips the scan when the cache is already fresh + complete.
        // Fire-and-forget — the async helper awaits the scan internally.
        SaveAllMeasurementsToCsv = new RelayCommand(
            canExecute: _ => Measurements.Count > 0 && !_parent.IsScanning,
            execute: _ => _ = SaveAllMeasurementsToCsvAsync());

        // Ctrl+Shift+H on the Measurements tab: bulk-export one histogram CSV per
        // measurement into a user-chosen folder. Drives a scan first if the cache is
        // stale or empty (matches the per-row H button's behavior). Uses the persisted
        // bin count when "Persist" is on, else the histogram default — so a Ctrl+Shift+H
        // produces the same binning the user would see when opening individual H windows.
        SaveAllMeasurementHistogramsToCsv = new RelayCommand(
            canExecute: _ => Measurements.Count > 0 && !_parent.IsScanning,
            execute: _ => _ = SaveAllMeasurementHistogramsToCsvAsync());

        // Ctrl+Alt+Shift+S on the Match Presets tab: dumps the current ScanResults as a
        // long-format CSV (one row per matched descriptor per preset/weight). Drives a
        // scan first if the cache is stale or empty so the export reflects today's rules.
        SaveDescriptorMatchesToCsv = new RelayCommand(
            canExecute: _ => Rules.Count > 0 && !_parent.IsScanning,
            execute: _ => _ = SaveDescriptorMatchesToCsvAsync());

        AddRule = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var rule = new MeasurementRule { Descriptor = new BodyShapeDescriptor.LabelSignature() };
                // Pre-fill Descriptor (Category, Value) from the current tree selection so adding
                // a rule from a focused value node lands the new rule under that branch instead
                // of forcing the user to pick the Category + Value again. Category-level
                // selection only pre-fills the Category; Value stays blank until the user picks
                // one in the new rule's editor. No selection → fully blank rule (old behavior).
                if (SelectedRuleTreeNode is VM_RuleTreeValueNode valNode)
                {
                    rule.Descriptor.Category = valNode.Category;
                    rule.Descriptor.Value = valNode.Value;
                }
                else if (SelectedRuleTreeNode is VM_RuleTreeCategoryNode catNode)
                {
                    rule.Descriptor.Category = catNode.Category;
                }
                rule.GroupsORlogic.Add(new AndGatedMeasurementGroup());
                var vmRule = new VM_MeasurementRule(rule, this);
                Rules.Add(vmRule);
                // RebuildRuleTree + RefreshFilteredRules already fire from the Rules
                // CollectionChanged hook wired in the ctor, so vmRule appears in FilteredRules
                // automatically when its descriptor matches the current selection.
            });

        AddDescriptorCommand = new RelayCommand(
            canExecute: _ => CanAddDescriptor(),
            execute: _ => AddDescriptorFromInputs());

        DeleteSelectedTreeNodeCommand = new RelayCommand(
            canExecute: _ => CanDeleteSelectedTreeNode(),
            execute: _ => DeleteSelectedTreeNode());

        CaptureFingerprintFromActiveViewer = new RelayCommand(
            canExecute: _ => ActiveViewer != null,
            execute: _ =>
            {
                if (ActiveViewer == null) return;
                var counts = ActiveViewer.GetCurrentShapeVertexCounts();
                FingerprintVertexCount = counts.Values.Sum();
                FingerprintShapeCounts = string.Join(", ", counts.Select(kv => kv.Key + ":" + kv.Value));
                AutoRemapKeyVertexShapeNames(counts);
                RefreshMeasurementValues();
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

        // Explicit one-shot import that bypasses the CapturePicks toggle: takes whatever pick
        // rows are currently highlighted in the viewer's pick-info list and appends them as
        // KeyVertex rows, skipping any (shape, index) already present so re-clicking the
        // button is a safe no-op. Lets the user pick freely in the viewer, then promote only
        // the interesting ones instead of having every click flow into the roster.
        CaptureSelectedPicks = new RelayCommand(
            canExecute: _ => ActiveViewer != null && ActiveViewer.SelectedPicks.Count > 0,
            execute: _ =>
            {
                var viewer = ActiveViewer;
                if (viewer == null) return;
                VM_NamedKeyVertex? firstAdded = null;
                foreach (var row in viewer.SelectedPicks)
                {
                    if (row == null) continue;
                    var shapeName = row.ShapeName ?? "";
                    if (string.IsNullOrEmpty(shapeName) || row.VertexIndex < 0) continue;
                    bool duplicate = KeyVertices.Any(k =>
                        k.VertexIndex == row.VertexIndex
                        && string.Equals(k.ShapeName ?? "", shapeName, StringComparison.OrdinalIgnoreCase));
                    if (duplicate) continue;
                    var model = new NamedKeyVertex
                    {
                        Name = NextDefaultName("KV", KeyVertices.Select(k => k.Name)),
                        ShapeName = shapeName,
                        VertexIndex = row.VertexIndex,
                    };
                    var vm = new VM_NamedKeyVertex(model, this);
                    KeyVertices.Add(vm);
                    firstAdded ??= vm;
                }
                if (firstAdded != null)
                {
                    SelectedKeyVertex = firstAdded;
                    RefreshMeasurementValues();
                }
            });

        // Re-evaluate live values whenever the measurement collection changes shape or
        // any row's definition fields (Kind / Axis / VertexRefA..D) are edited in the grid.
        foreach (var m in Measurements) m.PropertyChanged += OnMeasurementRowPropertyChanged;
        Measurements.CollectionChanged += (_, args) =>
        {
            if (args.OldItems != null)
                foreach (VM_MeasurementDefinition m in args.OldItems)
                {
                    m.PropertyChanged -= OnMeasurementRowPropertyChanged;
                    _measurementLastNames.Remove(m);
                }
            if (args.NewItems != null)
                foreach (VM_MeasurementDefinition m in args.NewItems)
                {
                    m.PropertyChanged += OnMeasurementRowPropertyChanged;
                    _measurementLastNames[m] = m.Name ?? "";
                }
            RefreshMeasurementValues();
            RecomputeDuplicateMeasurementNames();
            // Adding a Measurement can fix a previously-invalid condition reference;
            // removing one can break previously-valid ones. Re-derive both ways.
            RecomputeConditionRefValidity();
        };

        // Re-evaluate when the key-vertex roster changes (a measurement may reference a
        // newly-added vertex name, or lose a deleted one).
        KeyVertices.CollectionChanged += (_, __) =>
        {
            RefreshMeasurementValues();
            // Same dual-direction logic as Measurements above: deleting a referenced KV
            // breaks the measurements that pointed at it, adding one can fix stale refs.
            RecomputeMeasurementRefValidity();
        };

        // Mirror the Measurement hookup for KeyVertices so per-row Name edits drive the
        // duplicate-name highlight. Kept as its own subscription rather than folded into
        // the existing scan-invalidation block below so the duplicate logic is easy to
        // locate next to its companion Measurement subscription.
        foreach (var k in KeyVertices) k.PropertyChanged += OnKeyVertexRowPropertyChanged;
        KeyVertices.CollectionChanged += (_, args) =>
        {
            if (args.OldItems != null)
                foreach (VM_NamedKeyVertex k in args.OldItems)
                {
                    k.PropertyChanged -= OnKeyVertexRowPropertyChanged;
                    _kvLastNames.Remove(k);
                }
            if (args.NewItems != null)
                foreach (VM_NamedKeyVertex k in args.NewItems)
                {
                    k.PropertyChanged += OnKeyVertexRowPropertyChanged;
                    _kvLastNames[k] = k.Name ?? "";
                }
            RecomputeDuplicateKeyVertexNames();
        };

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
        foreach (var r in Rules) HookRuleForTreeRebuild(r);
        Rules.CollectionChanged += (_, args) =>
        {
            if (args.OldItems != null)
            {
                foreach (VM_MeasurementRule r in args.OldItems)
                {
                    UnhookRuleForScanInvalidation(r);
                    UnhookRuleForTreeRebuild(r);
                }
            }
            if (args.NewItems != null)
            {
                foreach (VM_MeasurementRule r in args.NewItems)
                {
                    HookRuleForScanInvalidation(r);
                    HookRuleForTreeRebuild(r);
                }
            }
            MarkScanResultsStale();
            RebuildRuleTree();
            RefreshFilteredRules();
        };

        // The Rules tab tree is driven by AvailableDescriptors (TemplateDescriptors). Rebuild
        // when descriptors are added/removed (e.g. the user uses the Add-from-tree command
        // or edits TemplateDescriptors in OBody Misc Settings).
        _parent.AvailableDescriptors.CollectionChanged += (_, __) =>
        {
            RebuildRuleTree();
            RefreshFilteredRules();
        };

        // Initial tree build: ctor's Rules-add loop preceded the CollectionChanged hook, so
        // populate the tree now from whatever's in place. The tree is empty until this fires.
        RebuildRuleTree();
        RefreshFilteredRules();

        RefreshMeasurementValues();

        // First-paint duplicate-name detection. Profiles loaded from JSON (legacy or
        // hand-edited) may already contain colliding names; without this call the red
        // highlights and banner counts wouldn't appear until the user actually edited a row.
        RecomputeDuplicateKeyVertexNames();
        RecomputeDuplicateMeasurementNames();

        // Prime the per-row Name caches so the first user-driven rename produces a correct
        // (old, new) diff. Subsequent rows added via the grid get their entry in the
        // CollectionChanged handler above.
        foreach (var k in KeyVertices) _kvLastNames[k] = k.Name ?? "";
        foreach (var m in Measurements) _measurementLastNames[m] = m.Name ?? "";

        // First-paint ref-validity detection. Same rationale as the duplicate-name block:
        // a JSON-loaded profile may already reference a non-existent key vertex (deleted
        // in an earlier session) and the red wash needs to appear on first paint.
        RecomputeMeasurementRefValidity();
        RecomputeConditionRefValidity();

        // Repaint the measurement-line overlay whenever the user picks a different
        // measurement. Using the raw PropertyChanged event keeps this file free of
        // additional ReactiveUI wiring (the profile's lifetime is bounded by the
        // containing editor, so we skip the IDisposable dance).
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SelectedMeasurement))
            {
                // Measurement highlight is suppressed while the bulge debug overlay is on;
                // the toggle's off-handler restores it via the same RefreshMeasurementHighlight
                // call, so picking a different measurement while the overlay is active
                // simply queues the new highlight to appear after the next toggle-off.
                if (!ShowBulgeOverlay) RefreshMeasurementHighlight();
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
                    // Transient "you-are-here" sphere for the selected row, drawn even when
                    // no matching Picks entry exists. Doesn't add a Picks row (so the picks
                    // ListBox stays uncluttered) and is cleared automatically when the user
                    // selects a different row, deselects, or switches profile.
                    ActiveViewer.SetPreviewKeyVertex(kv.ShapeName, kv.VertexIndex);
                }
                else
                {
                    // Deselection (or unresolved row): drop the preview so it doesn't linger
                    // on the model after the user clicks away from a row.
                    ActiveViewer?.SetPreviewKeyVertex(null, -1);
                }

                SyncPendingBoxEditSessionWithSelection(kv);

                // The selected row defines which box the bulge debug overlay targets when
                // no pending-box edit is active, so refresh when switching rows.
                if (ShowBulgeOverlay) RefreshBulgeOverlay();
            }
            else if (args.PropertyName == nameof(ShowBulgeOverlay))
            {
                if (ShowBulgeOverlay)
                {
                    RefreshBulgeOverlay();
                }
                else
                {
                    // Restore whatever measurement highlight was selected before the overlay
                    // took over the line channel. SetMeasurementLines(null) inside the
                    // refresh covers the no-selection case.
                    RefreshMeasurementHighlight();
                }
            }
        };
    }

    private void OnMeasurementRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // LiveValue updates are the result of recomputation; re-running on that would loop.
        if (e.PropertyName == nameof(VM_MeasurementDefinition.LiveValue)) return;
        // HasDuplicateName + ref-validity flags are written by the recompute methods
        // themselves; reacting to them would recurse.
        if (e.PropertyName == nameof(VM_MeasurementDefinition.HasDuplicateName)) return;
        if (e.PropertyName == nameof(VM_MeasurementDefinition.IsRefAValid)) return;
        if (e.PropertyName == nameof(VM_MeasurementDefinition.IsRefBValid)) return;
        if (e.PropertyName == nameof(VM_MeasurementDefinition.IsRefCValid)) return;
        if (e.PropertyName == nameof(VM_MeasurementDefinition.IsRefDValid)) return;
        RefreshMeasurementValues();
        if (e.PropertyName == nameof(VM_MeasurementDefinition.Name)
            && sender is VM_MeasurementDefinition mr)
        {
            // Cascade rename into MeasurementCondition.MeasurementName before recomputing
            // condition validity. Without the cascade, otherwise-valid conditions would
            // briefly flash red between the Name commit and a manual re-edit.
            if (_measurementLastNames.TryGetValue(mr, out var oldName))
            {
                CascadeMeasurementRename(oldName, mr.Name ?? "");
            }
            _measurementLastNames[mr] = mr.Name ?? "";
            RecomputeDuplicateMeasurementNames();
            RecomputeConditionRefValidity();
        }
        // User-driven edits to a vertex-ref field (or to Kind, which gates whether C/D
        // matter) can leave the row in an invalid state — re-derive the row's flags.
        if (e.PropertyName == nameof(VM_MeasurementDefinition.VertexRefA)
            || e.PropertyName == nameof(VM_MeasurementDefinition.VertexRefB)
            || e.PropertyName == nameof(VM_MeasurementDefinition.VertexRefC)
            || e.PropertyName == nameof(VM_MeasurementDefinition.VertexRefD)
            || e.PropertyName == nameof(VM_MeasurementDefinition.Kind))
        {
            RecomputeMeasurementRefValidity();
        }
    }

    /// <summary>Per-row PropertyChanged handler for the KeyVertices grid that drives the
    /// duplicate-name highlight, the rename cascade into Measurements, and the
    /// downstream measurement-ref validity flags. Mirror of
    /// <see cref="OnMeasurementRowPropertyChanged"/>; kept separate so each grid's
    /// invalidation rules stay readable.</summary>
    private void OnKeyVertexRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM_NamedKeyVertex.HasDuplicateName)) return;
        if (e.PropertyName == nameof(VM_NamedKeyVertex.ResolutionState)) return;
        if (e.PropertyName == nameof(VM_NamedKeyVertex.Name)
            && sender is VM_NamedKeyVertex kv)
        {
            // Cascade rename into Measurement.VertexRefA/B/C/D so existing references
            // survive the rename. Done before the validity recompute so cascaded
            // references stay marked valid without flickering.
            if (_kvLastNames.TryGetValue(kv, out var oldName))
            {
                CascadeKeyVertexRename(oldName, kv.Name ?? "");
            }
            _kvLastNames[kv] = kv.Name ?? "";
            RecomputeDuplicateKeyVertexNames();
            RecomputeMeasurementRefValidity();
        }
        // User-initiated edit to VertexIndex (manual cell edit) or Strategy switch (Explicit
        // -> BoundingBox) implicitly resolves the post-Capture "this index is stale on the new
        // topology" warning, so clear the flag and refresh the resolution badges immediately
        // (otherwise the banner would linger until the next preset change). The auto-remap path
        // sets ShapeName before NeedsRepick, so it's already false when the ShapeName edit fires
        // there — only manual edits land here with NeedsRepick still true.
        if ((e.PropertyName == nameof(VM_NamedKeyVertex.VertexIndex)
                || e.PropertyName == nameof(VM_NamedKeyVertex.Strategy)
                || e.PropertyName == nameof(VM_NamedKeyVertex.ShapeName))
            && sender is VM_NamedKeyVertex repickRow
            && repickRow.NeedsRepick)
        {
            repickRow.NeedsRepick = false;
            RecomputeKeyVertexResolutionStates();
        }
    }

    /// <summary>Set of trimmed names that appear on more than one row in <see cref="KeyVertices"/>.
    /// Authoritative source of truth for each row's <see cref="VM_NamedKeyVertex.HasDuplicateName"/>;
    /// rows never compute their own state.</summary>
    private readonly HashSet<string> _duplicateKeyVertexNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _duplicateMeasurementNames = new(StringComparer.Ordinal);

    /// <summary>Count of distinct duplicate names in <see cref="KeyVertices"/> (each shared
    /// name counted once, not per row). Drives the text of the warning banner above the
    /// KeyVertices grid; banner visibility uses <see cref="HasDuplicateKeyVertexNames"/>
    /// instead because WPF's DataTrigger Value="0" comparison against a boxed int via
    /// int.Equals("0") always returns false, which would leave the banner visible even at
    /// zero. The bool variant goes through the standard BoolToVisibilityConverter and
    /// behaves correctly.</summary>
    public int DuplicateKeyVertexCount { get; set; }
    public int DuplicateMeasurementCount { get; set; }
    public bool HasDuplicateKeyVertexNames { get; set; }
    public bool HasDuplicateMeasurementNames { get; set; }

    /// <summary>Recomputes <see cref="_duplicateKeyVertexNames"/> + <see cref="DuplicateKeyVertexCount"/>
    /// and pushes <see cref="VM_NamedKeyVertex.HasDuplicateName"/> onto every row. Called from
    /// the KeyVertices CollectionChanged handler, the per-row Name change handler, and once at
    /// the end of the ctor. O(n) where n is row count — negligible at typical profile sizes.</summary>
    private void RecomputeDuplicateKeyVertexNames()
    {
        // Trim + Ordinal match the downstream dictionary builders at RefreshMeasurementValues
        // and BodySlideMeasurementEvaluator. Empty names are skipped — an empty Name is already
        // filtered out of the lookup dictionary (AvailableKeyVertexNames), so collisions on ""
        // aren't user-facing ambiguity.
        _duplicateKeyVertexNames.Clear();
        foreach (var grp in KeyVertices
            .Where(k => !string.IsNullOrWhiteSpace(k.Name))
            .GroupBy(k => (k.Name ?? "").Trim(), StringComparer.Ordinal))
        {
            if (grp.Skip(1).Any()) _duplicateKeyVertexNames.Add(grp.Key);
        }
        foreach (var kv in KeyVertices)
        {
            var key = (kv.Name ?? "").Trim();
            kv.HasDuplicateName = !string.IsNullOrEmpty(key) && _duplicateKeyVertexNames.Contains(key);
        }
        DuplicateKeyVertexCount = _duplicateKeyVertexNames.Count;
        HasDuplicateKeyVertexNames = _duplicateKeyVertexNames.Count > 0;
    }

    private void RecomputeDuplicateMeasurementNames()
    {
        _duplicateMeasurementNames.Clear();
        foreach (var grp in Measurements
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .GroupBy(m => (m.Name ?? "").Trim(), StringComparer.Ordinal))
        {
            if (grp.Skip(1).Any()) _duplicateMeasurementNames.Add(grp.Key);
        }
        foreach (var m in Measurements)
        {
            var key = (m.Name ?? "").Trim();
            m.HasDuplicateName = !string.IsNullOrEmpty(key) && _duplicateMeasurementNames.Contains(key);
        }
        DuplicateMeasurementCount = _duplicateMeasurementNames.Count;
        HasDuplicateMeasurementNames = _duplicateMeasurementNames.Count > 0;
    }

    // ─── Reference-name cascade + validity tracking ────────────────────────────────────
    //
    // When the user renames a KeyVertex, every Measurement that referenced its old name
    // through VertexRefA/B/C/D is rewritten to the new name so the logical link survives.
    // Symmetric for Measurement renames into MeasurementCondition.MeasurementName. When a
    // referenced row is *deleted*, the cascade can't preserve the link — instead we mark
    // the orphaned reference invalid so the editor paints it red.
    //
    // Cascade needs the OLD name. Fody's PropertyChanged raises after the value changed,
    // so we cache each row's last-known name in a dictionary and diff in the handler.

    /// <summary>Last-known Name per KeyVertex row. Diffed in <see cref="OnKeyVertexRowPropertyChanged"/>
    /// to derive the (old, new) pair Fody's PropertyChanged event doesn't expose. Primed at
    /// the end of the ctor with every loaded row's initial name so the first user-driven
    /// edit produces a correct diff rather than treating the seed name as "old".</summary>
    private readonly Dictionary<VM_NamedKeyVertex, string> _kvLastNames = new();
    private readonly Dictionary<VM_MeasurementDefinition, string> _measurementLastNames = new();

    /// <summary>Rewrites every <see cref="VM_MeasurementDefinition.VertexRefA"/>/B/C/D
    /// equal to <paramref name="oldName"/> (Ordinal) to <paramref name="newName"/>. Triggered
    /// from <see cref="OnKeyVertexRowPropertyChanged"/> when a KV's Name actually changes.
    /// No-ops when oldName is empty (initial value) or unchanged.</summary>
    private void CascadeKeyVertexRename(string oldName, string newName)
    {
        if (string.IsNullOrEmpty(oldName)) return;
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return;
        foreach (var m in Measurements)
        {
            if (string.Equals(m.VertexRefA, oldName, StringComparison.Ordinal)) m.VertexRefA = newName;
            if (string.Equals(m.VertexRefB, oldName, StringComparison.Ordinal)) m.VertexRefB = newName;
            if (string.Equals(m.VertexRefC, oldName, StringComparison.Ordinal)) m.VertexRefC = newName;
            if (string.Equals(m.VertexRefD, oldName, StringComparison.Ordinal)) m.VertexRefD = newName;
        }
    }

    /// <summary>Rewrites every <see cref="VM_MeasurementCondition.MeasurementName"/> equal
    /// to <paramref name="oldName"/> across all Rules → Groups → Conditions.</summary>
    private void CascadeMeasurementRename(string oldName, string newName)
    {
        if (string.IsNullOrEmpty(oldName)) return;
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return;
        foreach (var r in Rules)
        {
            if (r?.Groups == null) continue;
            foreach (var g in r.Groups)
            {
                if (g?.Conditions == null) continue;
                foreach (var c in g.Conditions)
                {
                    if (string.Equals(c.MeasurementName, oldName, StringComparison.Ordinal))
                        c.MeasurementName = newName;
                }
            }
        }
    }

    /// <summary>Pushes <see cref="VM_MeasurementDefinition.IsRefAValid"/>/B/C/D onto every
    /// row based on the current <see cref="AvailableKeyVertexNames"/>. Empty refs are
    /// considered valid (they're inert, not broken). C and D are also considered valid when
    /// the measurement's <see cref="VM_MeasurementDefinition.ShowSecondPair"/> is false —
    /// for non-Ratio kinds, stale C/D fields don't affect evaluation and the red wash there
    /// would be noise.</summary>
    private void RecomputeMeasurementRefValidity()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kv in KeyVertices)
        {
            var n = (kv.Name ?? "").Trim();
            if (!string.IsNullOrEmpty(n)) names.Add(n);
        }
        foreach (var m in Measurements)
        {
            m.IsRefAValid = RefIsValid(m.VertexRefA, names);
            m.IsRefBValid = RefIsValid(m.VertexRefB, names);
            m.IsRefCValid = !m.ShowSecondPair || RefIsValid(m.VertexRefC, names);
            m.IsRefDValid = !m.ShowSecondPair || RefIsValid(m.VertexRefD, names);
        }
    }

    private static bool RefIsValid(string? r, HashSet<string> names)
    {
        var t = (r ?? "").Trim();
        return string.IsNullOrEmpty(t) || names.Contains(t);
    }

    /// <summary>Pushes <see cref="VM_MeasurementCondition.IsMeasurementRefValid"/> onto every
    /// condition across all Rules → Groups → Conditions based on the current
    /// <see cref="AvailableMeasurementNames"/>.</summary>
    private void RecomputeConditionRefValidity()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in Measurements)
        {
            var n = (m.Name ?? "").Trim();
            if (!string.IsNullOrEmpty(n)) names.Add(n);
        }
        foreach (var r in Rules)
        {
            if (r?.Groups == null) continue;
            foreach (var g in r.Groups)
            {
                if (g?.Conditions == null) continue;
                foreach (var c in g.Conditions)
                {
                    c.IsMeasurementRefValid = RefIsValid(c.MeasurementName, names);
                }
            }
        }
    }

    public string Id { get; }
    public string Name { get; set; }
    public string BodyTypeName { get; set; }

    public int FingerprintVertexCount { get; set; }
    public string FingerprintShapeCounts { get; set; }

    public ObservableCollection<VM_NamedKeyVertex> KeyVertices { get; } = new();
    public ObservableCollection<VM_MeasurementDefinition> Measurements { get; } = new();
    public ObservableCollection<VM_MeasurementRule> Rules { get; } = new();

    /// <summary>Tree representation of the Rules tab. One <see cref="VM_RuleTreeCategoryNode"/>
    /// per distinct <c>Category</c> in <see cref="VM_BodyTypeProfileEditor.AvailableDescriptors"/>,
    /// each with one <see cref="VM_RuleTreeValueNode"/> per <c>Value</c>. Rebuilt by
    /// <see cref="RebuildRuleTree"/> whenever the descriptor catalog or the rule collection
    /// changes. The Rules tab XAML binds its TreeView to this collection; <see cref="SelectedRuleTreeNode"/>
    /// drives <see cref="FilteredRules"/> on the right pane.</summary>
    public ObservableCollection<VM_RuleTreeCategoryNode> RuleTreeCategories { get; } = new();

    /// <summary>Currently-selected node in the Rules tab tree (either
    /// <see cref="VM_RuleTreeCategoryNode"/> or <see cref="VM_RuleTreeValueNode"/>, or null
    /// when nothing is selected). Setting this triggers <see cref="RefreshFilteredRules"/>
    /// via Fody's auto-PropertyChanged.</summary>
    public object? SelectedRuleTreeNode { get; set; }

    /// <summary>Subset of <see cref="Rules"/> filtered by the current tree selection: only
    /// rules whose Descriptor (Category, Value) matches the selected node. A Category-level
    /// selection includes every value in that category; a Value-level selection narrows to
    /// just that pair; null selection shows no rules. The Rules tab right-pane ItemsControl
    /// binds here instead of the full Rules list, so the editor only renders relevant rules.</summary>
    public ObservableCollection<VM_MeasurementRule> FilteredRules { get; } = new();

    /// <summary>Inline-form input for "Add new Category" — bound to the Category TextBox below
    /// the tree. AddDescriptorCommand reads this together with <see cref="NewValueInput"/> and
    /// appends a TemplateDescriptor through the parent editor's AvailableDescriptors. The
    /// existing tree row "+" buttons also bind to AddDescriptorCommand with a category
    /// parameter so the user doesn't have to retype.</summary>
    public string NewCategoryInput { get; set; } = "";

    /// <summary>Inline-form input for "Add new Value". See <see cref="NewCategoryInput"/>.</summary>
    public string NewValueInput { get; set; } = "";

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

    /// <summary>Currently highlighted measurement (the DataGrid's primary / last-focused row).
    /// Drives the colored-line overlay in the active viewer when no broader multi-selection is
    /// active. When the user multi-selects, <see cref="_selectedMeasurements"/> drives the overlay
    /// instead and this property tracks the focus row for live-value display / live-evaluator hookups.</summary>
    public VM_MeasurementDefinition? SelectedMeasurement { get; set; }

    /// <summary>Full set of measurements selected in the DataGrid (one or more). Populated by the
    /// code-behind's SelectionChanged handler via <see cref="UpdateSelectedMeasurements"/>. When
    /// non-empty, every entry contributes lines to the viewer overlay; when empty,
    /// <see cref="SelectedMeasurement"/> is used as a fallback.</summary>
    private List<VM_MeasurementDefinition> _selectedMeasurements = new();

    /// <summary>Multi-selection routing: replaces the tracked Measurements selection with the
    /// caller's list (typically the DataGrid's SelectedItems) and repaints the viewer's
    /// line overlay so every selected measurement is drawn at once. Suppressed while the
    /// bulge debug overlay owns the line channel; the toggle's off-handler restores the
    /// multi-selection highlight via <see cref="RefreshMeasurementHighlight"/>.</summary>
    public void UpdateSelectedMeasurements(IEnumerable<VM_MeasurementDefinition> selected)
    {
        _selectedMeasurements = selected?.Where(m => m != null).ToList() ?? new();
        if (!ShowBulgeOverlay) RefreshMeasurementHighlight();
    }

    /// <summary>Full set of key vertices currently highlighted in the KeyVertices DataGrid.
    /// Mirrors <see cref="_selectedMeasurements"/> for the Measurements tab — populated by the
    /// code-behind's SelectionChanged handler via <see cref="UpdateSelectedKeyVertices"/>.
    /// Used by the patch-export branch of <see cref="SaveKeyVerticesToJsonFile"/>: when a
    /// strict subset is selected, the export is written as a partial patch
    /// (<c>IsPatch: true</c>) containing only those rows.</summary>
    private List<VM_NamedKeyVertex> _selectedKeyVertices = new();

    /// <summary>Replaces the tracked KeyVertices selection with the caller's list (typically
    /// the DataGrid's SelectedItems). Called from the same code-behind handler that drives
    /// <see cref="SelectKeyVerticesInViewer"/> so the patch-export branch and the viewer
    /// highlight stay in sync.</summary>
    public void UpdateSelectedKeyVertices(IEnumerable<VM_NamedKeyVertex> selected)
    {
        _selectedKeyVertices = selected?.Where(k => k != null).ToList() ?? new();
    }

    /// <summary>Routes a per-row "open histogram" request from a <see cref="VM_MeasurementDefinition"/>
    /// up to the editor, which handles the cache-stale gate and the window construction.
    /// Kept as a one-line forwarder so the row VM doesn't need to know about the editor
    /// (the profile already does; the editor never appears in the row's binding context).</summary>
    public System.Threading.Tasks.Task OpenMeasurementHistogramAsync(VM_MeasurementDefinition definition)
        => _parent.OpenMeasurementHistogramAsync(this, definition);

    /// <summary>When true, key-vertex picks from any viewer add a new entry to this profile.</summary>
    public bool CapturePicks { get; set; } = false;

    /// <summary>Mirror of the viewer toolbar's <c>VM_CharacterViewer.ShowBulgeBinOverlay</c>
    /// checkbox, wired up in <see cref="AttachViewer"/>. When true, the editor draws one
    /// line per Y-bin used by the paired Pinch/Bulge X algorithm against the
    /// currently-editable bounding box, with the winner (largest width for Bulge, smallest
    /// for Pinch) drawn in cyan and the rest in white. Only renders for paired criteria;
    /// non-pair criteria leave the overlay empty. Replaces the SelectedMeasurement
    /// highlight while active; toggling off restores it.</summary>
    public bool ShowBulgeOverlay { get; set; } = false;

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

    /// <summary>Subscription that re-resolves the pending-box pick preview (purple markers)
    /// whenever any of the pending-box inputs change — coords, criterion, shape, or pending
    /// flag. Rewired in <see cref="AttachViewer"/>.</summary>
    private IDisposable? _viewerPendingBoxPreviewSub;

    /// <summary>Subscription that mirrors the viewer toolbar's bulge-bin overlay checkbox
    /// into this profile's <see cref="ShowBulgeOverlay"/> so the editor's existing
    /// property-changed wiring drives the actual refresh.</summary>
    private IDisposable? _viewerBulgeOverlaySub;

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

        // Recompute the purple pick-preview markers whenever any pending-box input changes —
        // the user adjusting a coord spinner or swapping the criterion gets immediate visual
        // feedback for what Confirm would commit. Merging single-property streams (rather
        // than the 9-arg WhenAnyValue) keeps the wiring readable.
        _viewerBulgeOverlaySub?.Dispose();
        _viewerBulgeOverlaySub = viewer?
            .WhenAnyValue(v => v.ShowBulgeBinOverlay)
            .Subscribe(b => ShowBulgeOverlay = b);

        _viewerPendingBoxPreviewSub?.Dispose();
        _viewerPendingBoxPreviewSub = viewer == null ? null : Observable.Merge(
            viewer.WhenAnyValue(v => v.HasPendingBox).Select(_ => 0),
            viewer.WhenAnyValue(v => v.PendingBoxShapeName).Select(_ => 0),
            viewer.WhenAnyValue(v => v.PendingBoxFinalCriterion).Select(_ => 0),
            viewer.WhenAnyValue(v => v.PendingBoxMinX).Select(_ => 0),
            viewer.WhenAnyValue(v => v.PendingBoxMaxX).Select(_ => 0),
            viewer.WhenAnyValue(v => v.PendingBoxMinY).Select(_ => 0),
            viewer.WhenAnyValue(v => v.PendingBoxMaxY).Select(_ => 0),
            viewer.WhenAnyValue(v => v.PendingBoxMinZ).Select(_ => 0),
            viewer.WhenAnyValue(v => v.PendingBoxMaxZ).Select(_ => 0)
        ).Subscribe(_ => RefreshPendingBoxPreview(viewer));
    }

    /// <summary>Re-runs <see cref="MeasurementMath.FindBestInBox"/> against the viewer's
    /// current pending-box state and pushes the resolved positions to
    /// <see cref="VM_CharacterViewer.SetPreviewPickMarkers"/>. Returns immediately and clears
    /// the preview when the pending box is dismissed, the shape isn't loaded, or the box is
    /// empty. Mirror-style authoring criteria expand into multiple resolutions; the synthetic
    /// rows are made each other's pair siblings so PinchPair / BulgePair criteria resolve via
    /// the joint-Y-slice path instead of the non-paired fallback.
    /// <para>For a non-mirror single-criterion edit (e.g. authoring just <c>BulgePairMinX</c>),
    /// the synthetic list contains only that one row — but if the profile already has the
    /// partner row (committed earlier with an identical box), we synthesize a sibling at the
    /// pending-box coords so the preview still runs the paired algorithm. Without this,
    /// preview falls back to the single-side variant in <see cref="MeasurementMath.FindBestInBox"/>
    /// and shows a marker at a different Y than what the committed resolve would produce.</para></summary>
    private void RefreshPendingBoxPreview(VM_CharacterViewer? viewer)
    {
        if (viewer == null) return;
        if (!viewer.HasPendingBox) { viewer.SetPreviewPickMarkers(null); return; }

        var shapeName = viewer.PendingBoxShapeName ?? "";
        if (string.IsNullOrEmpty(shapeName)) { viewer.SetPreviewPickMarkers(null); return; }

        var positions = viewer.GetShapePositions(shapeName);
        if (positions == null || positions.Length == 0) { viewer.SetPreviewPickMarkers(null); return; }

        var boxMin = new OpenTK.Mathematics.Vector3(viewer.PendingBoxMinX, viewer.PendingBoxMinY, viewer.PendingBoxMinZ);
        var boxMax = new OpenTK.Mathematics.Vector3(viewer.PendingBoxMaxX, viewer.PendingBoxMaxY, viewer.PendingBoxMaxZ);

        var criteria = ExpandSelectionToPersistedCriteria(viewer.PendingBoxFinalCriterion);
        var synthetics = criteria.Select(c => new NamedKeyVertex
        {
            ShapeName = shapeName,
            Strategy = KeyVertexStrategy.BoundingBox,
            BoxMinX = boxMin.X, BoxMinY = boxMin.Y, BoxMinZ = boxMin.Z,
            BoxMaxX = boxMax.X, BoxMaxY = boxMax.Y, BoxMaxZ = boxMax.Z,
            Criterion = c,
        }).ToList();

        // Pair-criterion preview fallback: for each synthetic whose paired partner isn't
        // already in synthetics, look in the profile's KeyVertices for a row with the same
        // shape and the partner criterion. If found, synthesize a partner stub at the
        // pending-box coords so FindPairSibling (which requires exact box-coord match)
        // succeeds. The stub is only used for sibling lookup — we don't add it to the
        // displayed-marker iteration below, so the preview only renders dots for the
        // criteria the user is actually editing.
        var siblingPool = new List<NamedKeyVertex>(synthetics);
        foreach (var s in synthetics)
        {
            if (!MeasurementMath.IsPairCriterion(s.Criterion)) continue;
            var partnerCrit = MeasurementMath.PartnerCriterion(s.Criterion);
            bool alreadyInSynthetics = false;
            foreach (var existing in synthetics)
            {
                if (existing.Criterion == partnerCrit) { alreadyInSynthetics = true; break; }
            }
            if (alreadyInSynthetics) continue;
            bool partnerExistsInProfile = false;
            foreach (var kv in KeyVertices)
            {
                if (kv == null) continue;
                if (kv.Strategy != KeyVertexStrategy.BoundingBox) continue;
                if (kv.Criterion != partnerCrit) continue;
                if (!string.Equals(kv.ShapeName, s.ShapeName, StringComparison.OrdinalIgnoreCase)) continue;
                partnerExistsInProfile = true;
                break;
            }
            if (!partnerExistsInProfile) continue;
            siblingPool.Add(new NamedKeyVertex
            {
                ShapeName = s.ShapeName,
                Strategy = KeyVertexStrategy.BoundingBox,
                BoxMinX = s.BoxMinX, BoxMinY = s.BoxMinY, BoxMinZ = s.BoxMinZ,
                BoxMaxX = s.BoxMaxX, BoxMaxY = s.BoxMaxY, BoxMaxZ = s.BoxMaxZ,
                Criterion = partnerCrit,
            });
        }

        Func<NamedKeyVertex, NamedKeyVertex?> findSibling =
            self => MeasurementMath.FindPairSibling(self, siblingPool);

        // Bone-info is only consulted when one of the synthetics uses a BoneTransition criterion;
        // fetch lazily so the common Pinch/Bulge/AxisExtremum preview paths don't pay for it.
        int[]? previewBoneIndices = null;
        float[]? previewBoneWeights = null;
        bool boneFetched = false;

        var resolved = new List<OpenTK.Mathematics.Vector3>(synthetics.Count);
        foreach (var s in synthetics)
        {
            if (MeasurementMath.IsBoneTransitionCriterion(s.Criterion) && !boneFetched)
            {
                (previewBoneIndices, previewBoneWeights) = viewer.GetShapeBoneInfo(shapeName);
                boneFetched = true;
            }
            int? idx = MeasurementMath.FindBestInBox(positions, s, s.Criterion, findSibling, previewBoneIndices, previewBoneWeights);
            if (idx == null) continue;
            if (idx.Value < 0 || idx.Value >= positions.Length) continue;
            resolved.Add(positions[idx.Value]);
        }
        viewer.SetPreviewPickMarkers(resolved);

        // The debug overlay reads the same pending-box state, so re-run it whenever the
        // preview refreshes. Internal guard makes this a no-op when the toggle is off.
        if (ShowBulgeOverlay) RefreshBulgeOverlay();
    }

    /// <summary>Expands an authoring-time <see cref="BoxCriterionSelection"/> into the one
    /// or two persisted <see cref="BoundingBoxCriterion"/> values it would materialize as
    /// rows on Confirm. Mirror shortcuts return both sides; everything else returns the
    /// matching single value. Kept aligned with the editor's mirrorPair switch in
    /// <see cref="OnBoxPickedFromViewer"/> — both need to map shortcuts to the same persisted
    /// pair, just consumed differently.</summary>
    private static IReadOnlyList<BoundingBoxCriterion> ExpandSelectionToPersistedCriteria(BoxCriterionSelection sel) => sel switch
    {
        BoxCriterionSelection.MirrorX              => new[] { BoundingBoxCriterion.MaxX,           BoundingBoxCriterion.MinX },
        BoxCriterionSelection.MirrorY              => new[] { BoundingBoxCriterion.MaxY,           BoundingBoxCriterion.MinY },
        BoxCriterionSelection.MirrorZ              => new[] { BoundingBoxCriterion.MaxZ,           BoundingBoxCriterion.MinZ },
        BoxCriterionSelection.MirrorPinchX         => new[] { BoundingBoxCriterion.PinchPairMaxX,  BoundingBoxCriterion.PinchPairMinX },
        BoxCriterionSelection.MirrorBulgeX         => new[] { BoundingBoxCriterion.BulgePairMaxX,  BoundingBoxCriterion.BulgePairMinX },
        BoxCriterionSelection.MinYMirroredAcrossX  => new[] { BoundingBoxCriterion.MinYRightOfX,   BoundingBoxCriterion.MinYLeftOfX },
        BoxCriterionSelection.MaxYMirroredAcrossX  => new[] { BoundingBoxCriterion.MaxYRightOfX,   BoundingBoxCriterion.MaxYLeftOfX },
        BoxCriterionSelection.MinZMirroredAcrossX  => new[] { BoundingBoxCriterion.MinZRightOfX,   BoundingBoxCriterion.MinZLeftOfX },
        BoxCriterionSelection.MaxZMirroredAcrossX  => new[] { BoundingBoxCriterion.MaxZRightOfX,   BoundingBoxCriterion.MaxZLeftOfX },
        BoxCriterionSelection.MirrorBoneTransitionX => new[] { BoundingBoxCriterion.BoneTransitionPairMaxX, BoundingBoxCriterion.BoneTransitionPairMinX },
        _                                          => new[] { (BoundingBoxCriterion)sel },
    };

    public RelayCommand AddMeasurement { get; }
    public RelayCommand AddRule { get; }
    public RelayCommand CaptureFingerprintFromActiveViewer { get; }
    public RelayCommand RemoveSelectedKeyVertex { get; }
    public RelayCommand ShowPicksInViewer { get; }
    public RelayCommand CaptureSelectedPicks { get; }
    public RelayCommand SaveMeasurementsToCsv { get; }
    public RelayCommand CopyMeasurementsToClipboard { get; }
    public RelayCommand SaveAllMeasurementsToCsv { get; }
    public RelayCommand SaveAllMeasurementHistogramsToCsv { get; }
    public RelayCommand SaveDescriptorMatchesToCsv { get; }
    public RelayCommand SaveKeyVerticesToJson { get; }
    public RelayCommand LoadKeyVerticesFromJson { get; }
    public RelayCommand SaveMeasurementsToJson { get; }
    public RelayCommand SaveMeasurementsPatchToJson { get; }
    public RelayCommand LoadMeasurementsFromJson { get; }
    public RelayCommand SaveRulesToJson { get; }
    public RelayCommand SaveRulesPatchToJson { get; }
    public RelayCommand SaveRulesPatchWithDeletesToJson { get; }
    public RelayCommand LoadRulesFromJson { get; }
    public RelayCommand AddDescriptorCommand { get; }
    public RelayCommand DeleteSelectedTreeNodeCommand { get; }

    public IEnumerable<string> AvailableMeasurementNames => Measurements.Select(m => m.Name).Where(n => !string.IsNullOrEmpty(n));
    public IEnumerable<string> AvailableKeyVertexNames => KeyVertices.Select(k => k.Name).Where(n => !string.IsNullOrEmpty(n));

    public ObservableCollection<string> AvailableBodyTypeNames => _parent.AvailableBodyTypeNames;
    public ObservableCollection<BodyShapeDescriptor.LabelSignature> AvailableDescriptors => _parent.AvailableDescriptors;

    /// <summary>Distinct, alphabetized Category names from <see cref="AvailableDescriptors"/>,
    /// for the Rules tab Descriptor Category dropdown. The dropdown is strict (non-editable),
    /// so this list must include every Category referenced by any current rule — see
    /// Settings_OBody.TemplateDescriptors as the source of truth.</summary>
    public IEnumerable<string> AvailableDescriptorCategories =>
        AvailableDescriptors
            .Select(d => d.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal);

    /// <summary>Values defined for a given Category in <see cref="AvailableDescriptors"/>.
    /// Empty when <paramref name="category"/> is null/blank or unknown. The Rules tab
    /// Descriptor Value dropdown filters via this so the user can't pair a Category with
    /// a Value that doesn't exist as a TemplateDescriptor.</summary>
    public IEnumerable<string> AvailableDescriptorValuesFor(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) return Array.Empty<string>();
        return AvailableDescriptors
            .Where(d => string.Equals(d.Category, category, StringComparison.Ordinal))
            .Select(d => d.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal);
    }

    /// <summary>Returns the subset of <see cref="AvailableDescriptors"/> that the
    /// <c>DescriptorRef</c> condition at <c>(ruleIdx, groupIdx, condIdx)</c> can safely
    /// reference without creating a cycle in the rule-dependency graph. The editing
    /// condition's existing ref is temporarily cleared before the check, so changing the
    /// ref from one valid target to another is always allowed (only the new candidate's
    /// cycle-ness is evaluated). Hand-edited cycles already present in the JSON are
    /// detected during the same scan — those rules drop out of the "safe" set too, so
    /// the user can't add new edges that perpetuate them.</summary>
    public IEnumerable<(string Category, string Value)> GetSafeDescriptorRefsForCondition(
        int ruleIdx, int groupIdx, int condIdx)
    {
        var dumped = Rules.Select(r => r.DumpToModel()).ToList();

        // Clear the editing condition's existing ref so cycle detection treats this slot as
        // "currently empty". Bounds-check at every level — out-of-range indices just skip
        // the clear, which is still correct (the dropdown then sees the existing edge as
        // part of the graph; not ideal but not unsafe).
        if (ruleIdx >= 0 && ruleIdx < dumped.Count)
        {
            var rule = dumped[ruleIdx];
            if (rule?.GroupsORlogic != null
                && groupIdx >= 0 && groupIdx < rule.GroupsORlogic.Count)
            {
                var group = rule.GroupsORlogic[groupIdx];
                if (group?.ConditionsANDlogic != null
                    && condIdx >= 0 && condIdx < group.ConditionsANDlogic.Count)
                {
                    var cond = group.ConditionsANDlogic[condIdx];
                    if (cond != null && cond.Kind == MeasurementConditionKind.DescriptorRef)
                    {
                        cond.RefCategory = "";
                        cond.RefValue = "";
                    }
                }
            }
        }

        var safe = new List<(string Category, string Value)>();
        foreach (var d in AvailableDescriptors)
        {
            if (string.IsNullOrEmpty(d.Category) || string.IsNullOrEmpty(d.Value)) continue;
            if (RuleDependencyOrder.WouldCreateCycle(dumped, ruleIdx, d.Category, d.Value)) continue;
            safe.Add((d.Category, d.Value));
        }
        return safe;
    }

    /// <summary>Index of <paramref name="rule"/> in the editor-side rule collection, or
    /// <c>-1</c> if not found. Used by cycle-safe descriptor-ref enumeration to identify
    /// which rule is being edited.</summary>
    public int IndexOfRule(VM_MeasurementRule rule) => Rules.IndexOf(rule);

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
    /// target with the primary half and appends the partner row. A
    /// <see cref="VM_CharacterViewer.KeyVertexBoxPick.IsDuplicate"/> pick (from "Confirm as
    /// Duplicate") always forks to a new row even with an edit session active, leaving the
    /// edit target intact so a later regular Confirm still updates the original.</para>
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
            pick.Criterion == BoxCriterionSelection.MirrorBulgeX ||
            pick.Criterion == BoxCriterionSelection.MinYMirroredAcrossX ||
            pick.Criterion == BoxCriterionSelection.MaxYMirroredAcrossX ||
            pick.Criterion == BoxCriterionSelection.MinZMirroredAcrossX ||
            pick.Criterion == BoxCriterionSelection.MaxZMirroredAcrossX ||
            pick.Criterion == BoxCriterionSelection.MirrorBoneTransitionX;

        // MirrorPinchX / MirrorBulgeX expand into the *paired* criteria so the two generated rows
        // resolve jointly (same Y-slice) and a PointDistance across them measures true horizontal
        // thickness. Single-side PinchMin/MaxX and BulgeMin/MaxX remain available for manual use
        // and for loading legacy profiles that stored those values directly.
        // Min/Max{Y,Z}MirroredAcrossX expand into LeftOfX/RightOfX rows that share the full box but
        // each scans only its half of X=0 — one anchor per side of the body for paired top/bottom
        // (Y) or front/back (Z) landmarks.
        (BoundingBoxCriterion a, BoundingBoxCriterion b)? mirrorPair = isMirror
            ? pick.Criterion switch
            {
                BoxCriterionSelection.MirrorX              => (BoundingBoxCriterion.MaxX,           BoundingBoxCriterion.MinX),
                BoxCriterionSelection.MirrorY              => (BoundingBoxCriterion.MaxY,           BoundingBoxCriterion.MinY),
                BoxCriterionSelection.MirrorZ              => (BoundingBoxCriterion.MaxZ,           BoundingBoxCriterion.MinZ),
                BoxCriterionSelection.MirrorPinchX         => (BoundingBoxCriterion.PinchPairMaxX,  BoundingBoxCriterion.PinchPairMinX),
                BoxCriterionSelection.MirrorBulgeX         => (BoundingBoxCriterion.BulgePairMaxX,  BoundingBoxCriterion.BulgePairMinX),
                BoxCriterionSelection.MinYMirroredAcrossX  => (BoundingBoxCriterion.MinYRightOfX,   BoundingBoxCriterion.MinYLeftOfX),
                BoxCriterionSelection.MaxYMirroredAcrossX  => (BoundingBoxCriterion.MaxYRightOfX,   BoundingBoxCriterion.MaxYLeftOfX),
                BoxCriterionSelection.MinZMirroredAcrossX  => (BoundingBoxCriterion.MinZRightOfX,   BoundingBoxCriterion.MinZLeftOfX),
                BoxCriterionSelection.MaxZMirroredAcrossX  => (BoundingBoxCriterion.MaxZRightOfX,   BoundingBoxCriterion.MaxZLeftOfX),
                BoxCriterionSelection.MirrorBoneTransitionX => (BoundingBoxCriterion.BoneTransitionPairMaxX, BoundingBoxCriterion.BoneTransitionPairMinX),
                _                                          => (BoundingBoxCriterion.MaxZRightOfX,   BoundingBoxCriterion.MaxZLeftOfX),
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

        // "Confirm as Duplicate" always forks to a new row, even when an edit session is
        // active. The edit-session target is preserved (not cleared) so a follow-up regular
        // Confirm can still update the originally-selected row.
        if (pick.IsDuplicate)
        {
            editTarget = null;
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
    /// Counts of <see cref="VM_NamedKeyVertex"/> rows whose <see cref="VM_NamedKeyVertex.ResolutionState"/>
    /// is not <see cref="KeyVertexResolutionState.Resolved"/>. Updated by
    /// <see cref="RecomputeKeyVertexResolutionStates"/>; drives the unresolved-KV banner above the grid.
    /// <see cref="HasUnresolvedKeyVertices"/> is the bool variant for binding to
    /// <c>BoolToVisibilityConverter</c> (the int variant misbehaves with WPF DataTrigger Value="0"
    /// — see <see cref="HasDuplicateKeyVertexNames"/> for the same reasoning).
    /// </summary>
    public int UnresolvedKeyVertexCount { get; set; }
    public bool HasUnresolvedKeyVertices { get; set; }

    /// <summary>Called from <see cref="CaptureFingerprintFromActiveViewer"/> after the fingerprint
    /// is refreshed. Rewrites <see cref="VM_NamedKeyVertex.ShapeName"/> on any row whose shape no
    /// longer exists in the loaded mesh, using the largest-shape-by-vertex-count heuristic to pick
    /// the new target (the body torso reliably dominates over sub-shapes like
    /// <c>3BA_Vagina</c> / <c>3BA_Anus</c>). Explicit-strategy rows get their <see cref="VM_NamedKeyVertex.NeedsRepick"/>
    /// flag set because their stored index almost certainly points at unrelated anatomy on the
    /// new topology; BoundingBox-strategy rows self-heal on the next <see cref="RefreshBoundingBoxMarkers"/>
    /// pass via the box scanner. Ambiguous cases (two equally-large candidate shapes, or the row
    /// shape already matches another row that resolves correctly) are left alone with a single
    /// log line so the user sees what was skipped.</summary>
    private void AutoRemapKeyVertexShapeNames(IReadOnlyDictionary<string, int> currentShapes)
    {
        if (currentShapes == null || currentShapes.Count == 0) return;
        if (KeyVertices.Count == 0) return;

        var loadedShapeSet = new HashSet<string>(currentShapes.Keys, StringComparer.OrdinalIgnoreCase);

        // Build the set of profile shape names that DO currently resolve. We won't remap onto
        // a target that's already claimed by a resolving row, because that would silently merge
        // two distinct shape references onto one mesh and create cross-contamination.
        var claimedTargets = new HashSet<string>(
            KeyVertices
                .Select(k => k.ShapeName ?? "")
                .Where(s => !string.IsNullOrEmpty(s) && loadedShapeSet.Contains(s)),
            StringComparer.OrdinalIgnoreCase);

        // For each distinct unresolved shape name in the profile, pick at most one remap target.
        var unresolvedSources = KeyVertices
            .Select(k => k.ShapeName ?? "")
            .Where(s => !string.IsNullOrEmpty(s) && !loadedShapeSet.Contains(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unresolvedSources.Count == 0) return;

        // Heuristic: the loaded shape with the largest vertex count is the main body. Reject if
        // two shapes tie for the maximum (ambiguous), or if it's already claimed.
        var ranked = currentShapes
            .OrderByDescending(kvp => kvp.Value)
            .ToList();
        string? primaryTarget = null;
        if (ranked.Count > 0)
        {
            int top = ranked[0].Value;
            int tieCount = ranked.Count(kvp => kvp.Value == top);
            if (tieCount == 1) primaryTarget = ranked[0].Key;
        }

        var remaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var skipped = new List<string>();
        foreach (var src in unresolvedSources)
        {
            // If we can't pick a single primary, or it's already in use by another resolving
            // row, we can't safely remap without prompting — leave it for the user.
            if (primaryTarget == null || claimedTargets.Contains(primaryTarget))
            {
                skipped.Add(src);
                continue;
            }
            remaps[src] = primaryTarget;
            // Claim the target so a second unresolved source can't also collapse onto it.
            claimedTargets.Add(primaryTarget);
            primaryTarget = null;
        }

        if (remaps.Count == 0 && skipped.Count == 0) return;

        int remapped = 0, flaggedForRepick = 0;
        if (remaps.Count > 0)
        {
            foreach (var kv in KeyVertices)
            {
                var src = kv.ShapeName ?? "";
                if (!remaps.TryGetValue(src, out var dst)) continue;
                kv.ShapeName = dst;
                remapped++;
                if (kv.Strategy == KeyVertexStrategy.Explicit)
                {
                    kv.NeedsRepick = true;
                    flaggedForRepick++;
                }
            }
        }

        var logger = _parent?.Logger;
        if (logger != null)
        {
            if (remaps.Count > 0)
            {
                var pairs = string.Join(", ", remaps.Select(kvp => $"'{kvp.Key}' -> '{kvp.Value}'"));
                logger.LogMessage($"BodyTypeProfile: Capture remapped {remapped} key vertex shape name(s): {pairs}. {flaggedForRepick} Explicit row(s) flagged for re-pick.");
            }
            if (skipped.Count > 0)
            {
                logger.LogMessage($"BodyTypeProfile: Capture left {skipped.Count} unresolved shape name(s) alone (ambiguous target on loaded mesh): {string.Join(", ", skipped.Select(s => "'" + s + "'"))}");
            }
        }
    }

    /// <summary>Recomputes <see cref="VM_NamedKeyVertex.ResolutionState"/> for every row in
    /// <see cref="KeyVertices"/> against the active viewer, plus the aggregate
    /// <see cref="UnresolvedKeyVertexCount"/> / <see cref="HasUnresolvedKeyVertices"/> that drive
    /// the editor's banner. Called at the tail of <see cref="RefreshMeasurementValues"/> so the
    /// state tracks every preset/weight/mesh change. O(n) in KV count — negligible at typical
    /// profile sizes (the live shape lookup per row is a single dictionary probe).</summary>
    private void RecomputeKeyVertexResolutionStates()
    {
        var viewer = ActiveViewer;
        if (viewer == null)
        {
            foreach (var kv in KeyVertices)
            {
                kv.ResolutionState = KeyVertexResolutionState.Unknown;
            }
            UnresolvedKeyVertexCount = 0;
            HasUnresolvedKeyVertices = false;
            return;
        }

        var counts = viewer.GetCurrentShapeVertexCounts();
        int unresolved = 0;
        foreach (var kv in KeyVertices)
        {
            KeyVertexResolutionState state;
            if (kv.NeedsRepick && kv.Strategy == KeyVertexStrategy.Explicit)
            {
                state = KeyVertexResolutionState.NeedsRepick;
            }
            else if (string.IsNullOrEmpty(kv.ShapeName))
            {
                state = KeyVertexResolutionState.ShapeNotLoaded;
            }
            else if (!counts.TryGetValue(kv.ShapeName, out int n))
            {
                state = KeyVertexResolutionState.ShapeNotLoaded;
            }
            else if (kv.VertexIndex < 0 || kv.VertexIndex >= n)
            {
                state = KeyVertexResolutionState.IndexOutOfRange;
            }
            else
            {
                state = KeyVertexResolutionState.Resolved;
            }

            kv.ResolutionState = state;
            if (state != KeyVertexResolutionState.Resolved) unresolved++;
        }

        UnresolvedKeyVertexCount = unresolved;
        HasUnresolvedKeyVertices = unresolved > 0;
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
        // Re-push the preview marker for whatever row is currently selected so it tracks
        // mesh deformation (preset/weight changes) and BB re-resolution. RefreshBoundingBoxMarkers
        // has just updated kv.VertexIndex for BB rows, so this picks up the new resolved vertex.
        // No-op for Explicit rows where the index doesn't change, but TryGetCurrentVertex still
        // pulls a fresh CpuPositions slot in case the underlying mesh deformed.
        var selKv = SelectedKeyVertex;
        if (viewer != null && selKv != null
            && !string.IsNullOrEmpty(selKv.ShapeName)
            && selKv.VertexIndex >= 0)
        {
            viewer.SetPreviewKeyVertex(selKv.ShapeName, selKv.VertexIndex);
        }

        if (Measurements.Count == 0)
        {
            RefreshMeasurementHighlight();
            RecomputeKeyVertexResolutionStates();
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
                shape => viewer.GetShapeBoneInfo(shape),
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
        RecomputeKeyVertexResolutionStates();
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

        // Same topo-sort the production evaluator uses, so DescriptorRef-kind conditions
        // see the upstream matches that should already have fired by the time their parent
        // rule's predicate is checked. The preview pane is intentionally not gated on IsDraft
        // (the user wants to see what draft rules would emit), so aggregators referencing
        // draft descriptors light up here.
        var eligibleModels = new List<MeasurementRule>();
        var ruleVMByModel = new Dictionary<MeasurementRule, VM_MeasurementRule>(ReferenceEqualityComparer.Instance);
        foreach (var r in Rules)
        {
            if (r == null) continue;
            if (string.IsNullOrEmpty(r.DescriptorCategory)) continue;
            if (string.IsNullOrEmpty(r.DescriptorValue)) continue;
            var m = r.DumpToModel();
            eligibleModels.Add(m);
            ruleVMByModel[m] = r;
        }
        var orderedModels = RuleDependencyOrder.SortByDescriptorDependencies(eligibleModels, out _);

        var matched = new HashSet<(string Category, string Value)>();
        foreach (var model in orderedModels)
        {
            if (!MeasurementMath.RuleMatches(model, meas, matched)) continue;

            var sourceVm = ruleVMByModel[model];
            matched.Add((model.Descriptor.Category, model.Descriptor.Value));
            PreviewMatches.Add(new VM_PreviewMatch
            {
                Category = sourceVm.DescriptorCategory,
                Value = sourceVm.DescriptorValue,
                IsDraft = sourceVm.IsDraft,
                ConditionTrace = BuildMatchTrace(model, meas, matched),
            });
            if (sourceVm.IsDraft) drafts++; else promoted++;
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

    private static string BuildMatchTrace(
        MeasurementRule rule,
        IReadOnlyDictionary<string, float> meas,
        IReadOnlySet<(string Category, string Value)>? matched = null)
    {
        if (rule.GroupsORlogic == null) return "";
        foreach (var g in rule.GroupsORlogic)
        {
            if (g?.ConditionsANDlogic == null || g.ConditionsANDlogic.Count == 0) continue;
            bool allMatch = true;
            var parts = new List<string>(g.ConditionsANDlogic.Count);
            foreach (var c in g.ConditionsANDlogic)
            {
                if (c == null) { allMatch = false; break; }
                if (c.Kind == MeasurementConditionKind.DescriptorRef)
                {
                    if (string.IsNullOrEmpty(c.RefCategory) || string.IsNullOrEmpty(c.RefValue))
                    {
                        allMatch = false;
                        break;
                    }
                    bool present = matched != null && matched.Contains((c.RefCategory, c.RefValue));
                    bool fires = c.Negate ? !present : present;
                    if (!fires) { allMatch = false; break; }
                    string negPrefix = c.Negate ? "NOT " : "";
                    parts.Add($"{negPrefix}{c.RefCategory}:{c.RefValue}");
                }
                else
                {
                    if (string.IsNullOrEmpty(c.MeasurementName)) { allMatch = false; break; }
                    if (!meas.TryGetValue(c.MeasurementName, out var v)) { allMatch = false; break; }
                    if (!MeasurementMath.Compare(v, c.Comparator, c.Value)) { allMatch = false; break; }
                    parts.Add($"{c.MeasurementName}={v:F3} {ComparatorSymbol(c.Comparator)} {c.Value:F3}");
                }
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

        // Per-shape bone-info cache so a profile with many BoneTransition rows on the same
        // shape pays the lookup once. Most marker refreshes use Pinch/Bulge/AxisExtremum and
        // never trigger the fetch at all.
        var boneCache = new Dictionary<string, (int[]? Indices, float[]? Weights)>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in KeyVertices)
        {
            if (kv.Strategy != KeyVertexStrategy.BoundingBox) continue;
            if (string.IsNullOrEmpty(kv.ShapeName)) continue;

            var positions = viewer.GetShapePositions(kv.ShapeName);
            if (positions == null || positions.Length == 0) continue;

            int[]? rowBoneIndices = null;
            float[]? rowBoneWeights = null;
            if (MeasurementMath.IsBoneTransitionCriterion(kv.Criterion))
            {
                if (!boneCache.TryGetValue(kv.ShapeName, out var cached))
                {
                    cached = viewer.GetShapeBoneInfo(kv.ShapeName);
                    boneCache[kv.ShapeName] = cached;
                }
                rowBoneIndices = cached.Indices;
                rowBoneWeights = cached.Weights;
            }

            var model = bbSnapshots[kv];
            int? idx = MeasurementMath.FindBestInBox(positions, model, kv.Criterion, findSibling, rowBoneIndices, rowBoneWeights);
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
    /// <summary>Builds the per-Y-bin paired-X debug overlay for the currently-edited box
    /// and pushes it through the viewer's measurement-line channel. Targets the pending
    /// box if one is open (so the overlay tracks live coord edits); otherwise targets the
    /// SelectedKeyVertex's stored box. Each occupied bin produces a single horizontal
    /// segment from its min-X vertex to its max-X vertex; the winner (largest Bulge width,
    /// smallest Pinch width) is drawn in cyan, the rest in white. Clears the line channel
    /// when there's no valid target or the criterion isn't a pair criterion. The internal
    /// <see cref="ShowBulgeOverlay"/> guard is intentional: callers from property-change
    /// subscriptions don't have to re-check the toggle.</summary>
    private void RefreshBulgeOverlay()
    {
        var viewer = ActiveViewer;
        if (viewer == null) return;
        if (!ShowBulgeOverlay) return;

        string? shapeName;
        float minX, minY, minZ, maxX, maxY, maxZ;
        BoundingBoxCriterion criterion;

        if (viewer.HasPendingBox && !string.IsNullOrEmpty(viewer.PendingBoxShapeName))
        {
            // Pending box wins because the user is actively editing — the overlay should
            // reflect the in-flight coords, not whatever was last confirmed.
            shapeName = viewer.PendingBoxShapeName;
            minX = viewer.PendingBoxMinX; maxX = viewer.PendingBoxMaxX;
            minY = viewer.PendingBoxMinY; maxY = viewer.PendingBoxMaxY;
            minZ = viewer.PendingBoxMinZ; maxZ = viewer.PendingBoxMaxZ;
            var expanded = ExpandSelectionToPersistedCriteria(viewer.PendingBoxFinalCriterion);
            criterion = expanded.Count > 0 ? expanded[0] : BoundingBoxCriterion.MaxX;
        }
        else
        {
            // Fall back to the selected row's committed box; nothing to draw if no row is
            // selected or it's an Explicit-strategy row.
            var sel = SelectedKeyVertex;
            if (sel == null || sel.Strategy != KeyVertexStrategy.BoundingBox)
            {
                viewer.SetMeasurementLines(null);
                return;
            }
            shapeName = sel.ShapeName;
            minX = sel.BoxMinX; maxX = sel.BoxMaxX;
            minY = sel.BoxMinY; maxY = sel.BoxMaxY;
            minZ = sel.BoxMinZ; maxZ = sel.BoxMaxZ;
            criterion = sel.Criterion;
        }

        if (string.IsNullOrEmpty(shapeName))
        {
            viewer.SetMeasurementLines(null);
            return;
        }

        // Only the four paired Pinch/Bulge criteria use the per-Y-bin pairing this overlay
        // visualizes. For non-pair criteria the user picks a single side per bin and the
        // notion of a "paired width per slice" doesn't apply, so we just clear.
        if (!MeasurementMath.IsPairCriterion(criterion))
        {
            viewer.SetMeasurementLines(null);
            return;
        }

        var positions = viewer.GetShapePositions(shapeName);
        if (positions == null || positions.Length == 0)
        {
            viewer.SetMeasurementLines(null);
            return;
        }

        var stub = new NamedKeyVertex
        {
            ShapeName = shapeName,
            Strategy = KeyVertexStrategy.BoundingBox,
            BoxMinX = minX, BoxMinY = minY, BoxMinZ = minZ,
            BoxMaxX = maxX, BoxMaxY = maxY, BoxMaxZ = maxZ,
            Criterion = criterion,
        };

        bool wantPinch = criterion == BoundingBoxCriterion.PinchPairMinX
                      || criterion == BoundingBoxCriterion.PinchPairMaxX;
        var snapshot = MeasurementMath.GetPairXBinSnapshot(positions, stub, wantPinch);
        if (snapshot == null)
        {
            viewer.SetMeasurementLines(null);
            return;
        }

        // Colors match the user's spec: white for non-winner bins, cyan for the winner.
        // Slightly brighter cyan than the existing ratio-denominator highlight so the
        // winner reads even against the cluster of white slice lines.
        var white = new OpenTK.Mathematics.Vector3(1.0f, 1.0f, 1.0f);
        var cyan = new OpenTK.Mathematics.Vector3(0.0f, 1.0f, 1.0f);

        // Bulge-bin debug lines aren't named measurements (one per Y-bin of the paired
        // criterion algorithm), so they get null labels — the hover hit-test skips them.
        var segments = new List<(OpenTK.Mathematics.Vector3 A, OpenTK.Mathematics.Vector3 B, OpenTK.Mathematics.Vector3 Color, string? Label)>();
        foreach (var bin in snapshot)
        {
            if (!bin.HasMin || !bin.HasMax) continue;
            if (bin.MinVertexIndex < 0 || bin.MinVertexIndex >= positions.Length) continue;
            if (bin.MaxVertexIndex < 0 || bin.MaxVertexIndex >= positions.Length) continue;
            var a = positions[bin.MinVertexIndex];
            var b = positions[bin.MaxVertexIndex];
            segments.Add((a, b, bin.IsWinner ? cyan : white, null));
        }

        viewer.SetMeasurementLines(segments);
    }

    private void RefreshMeasurementHighlight()
    {
        var viewer = ActiveViewer;
        if (viewer == null) return;

        // Effective selection: prefer the multi-selection list when populated (driven by the
        // DataGrid's SelectionChanged path); fall back to the single SelectedMeasurement when
        // no SelectionChanged has fired yet (e.g., callers that arrive via the
        // SelectedMeasurement PropertyChanged hook or a viewer-reload path).
        IReadOnlyList<VM_MeasurementDefinition> sels =
            _selectedMeasurements.Count > 0
                ? _selectedMeasurements
                : (SelectedMeasurement != null ? new[] { SelectedMeasurement } : System.Array.Empty<VM_MeasurementDefinition>());

        if (sels.Count == 0)
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

        // Labeled tuple: each segment carries its parent measurement's name so the hover
        // tooltip in UC_CharacterViewer can identify which line corresponds to which
        // measurement. Especially important when "Show Measurements" or a multi-row
        // Measurements-grid selection puts several lines on screen at once (where the
        // color scheme alone doesn't disambiguate). Per-leg suffixes (e.g. " (numerator)")
        // are appended for ratio / axis decompositions so users can tell which leg of the
        // same measurement they're hovering.
        var segments = new List<(OpenTK.Mathematics.Vector3 A, OpenTK.Mathematics.Vector3 B, OpenTK.Mathematics.Vector3 Color, string? Label)>();

        // Yellow for the primary pair, cyan for the ratio denominator pair. For AxisDistance
        // the three axis-aligned legs use: yellow (the measurement axis), white (the two
        // secondary axes), and grey (the A-B hypotenuse) — white/grey stand in for the
        // originally-planned dashed styling so the renderer can stay on flat-color lines.
        // The same color scheme is reused for every selected measurement; with multi-selection
        // the legend stays "yellow = numerator, cyan = denominator" regardless of which
        // measurement a given leg belongs to.
        var primary = new OpenTK.Mathematics.Vector3(1.0f, 0.85f, 0.1f);
        var secondary = new OpenTK.Mathematics.Vector3(0.1f, 0.85f, 1.0f);
        var axisSecondary = new OpenTK.Mathematics.Vector3(1.0f, 1.0f, 1.0f);
        var axisHypotenuse = new OpenTK.Mathematics.Vector3(0.5f, 0.5f, 0.5f);

        // Axis-aligned leg from a to a + projection of (b - a) onto the named axis. Length
        // equals |b - a| on that axis (matches MeasurementMath.AxisOrLength). Inline so the
        // visualization code can mirror the metric for both AxisDistance and the
        // axis-projected RatioDistance pairs.
        static OpenTK.Mathematics.Vector3 AxisLegEnd(OpenTK.Mathematics.Vector3 av,
            OpenTK.Mathematics.Vector3 bv, MeasurementAxis axis) => axis switch
        {
            MeasurementAxis.X => new OpenTK.Mathematics.Vector3(bv.X, av.Y, av.Z),
            MeasurementAxis.Y => new OpenTK.Mathematics.Vector3(av.X, bv.Y, av.Z),
            MeasurementAxis.Z => new OpenTK.Mathematics.Vector3(av.X, av.Y, bv.Z),
            _ => bv,
        };

        // Pair equality is unordered: a PointDistance/AxisDistance between (X, Y) measures
        // the same scalar as one between (Y, X). The hover-label cross-reference treats
        // pairs as sets so a ratio's numerator (L_HipSide, R_HipSide) finds a sibling
        // hip_width PointDistance regardless of which way that sibling was authored.
        static bool PairsMatch(string a1, string b1, string a2, string b2)
        {
            if (string.IsNullOrEmpty(a1) || string.IsNullOrEmpty(b1)) return false;
            if (string.IsNullOrEmpty(a2) || string.IsNullOrEmpty(b2)) return false;
            if (string.Equals(a1, a2, StringComparison.Ordinal) && string.Equals(b1, b2, StringComparison.Ordinal)) return true;
            if (string.Equals(a1, b2, StringComparison.Ordinal) && string.Equals(b1, a2, StringComparison.Ordinal)) return true;
            return false;
        }

        // Cross-reference helper for RatioDistance hover labels. Given one of the ratio's
        // pair-and-axis combinations, find a separately-defined single-distance measurement
        // (PointDistance for axis==null, AxisDistance with matching Axis otherwise) whose
        // (V1, V2) matches the same physical scalar.
        // <para>RatioDistance entries are not indexed here. A ratio is a composite of two
        // pairs, not a single pair, so cross-referencing one to another would be misleading
        // ("numerator: some_other_ratio" tells the user nothing about what the line is).
        // Returns null when no sibling matches — caller falls back to the explicit-pair form.</para>
        string? FindMatchingSinglePairMeasurement(string vertexRef1, string vertexRef2, MeasurementAxis? axis)
        {
            if (string.IsNullOrEmpty(vertexRef1) || string.IsNullOrEmpty(vertexRef2)) return null;
            foreach (var m in Measurements)
            {
                if (m == null || string.IsNullOrEmpty(m.Name)) continue;
                if (axis == null)
                {
                    if (m.Kind != MeasurementKind.PointDistance) continue;
                }
                else
                {
                    if (m.Kind != MeasurementKind.AxisDistance) continue;
                    if (m.Axis != axis.Value) continue;
                }
                if (PairsMatch(m.VertexRefA, m.VertexRefB, vertexRef1, vertexRef2))
                    return m.Name;
            }
            return null;
        }

        // Resolves the text that fills the colon slot in "{ratio_name} (role: <slot>)".
        // - Match found:    returns the matched sibling measurement's name (e.g. "hip_width").
        // - No match found: returns an explicit "V1 to V2" pair, with a trailing ": AXIS"
        //                   suffix when axis is set — so an unmatched X-axis numerator reads
        //                   "(numerator: L_HipSide to R_HipSide: X)" instead of the
        //                   content-free "(numerator)". The pair-only form for axis==null
        //                   stays "(numerator: L_HipSide to R_HipSide)".
        string PairSlot(string vertexRef1, string vertexRef2, MeasurementAxis? axis)
        {
            string? match = FindMatchingSinglePairMeasurement(vertexRef1, vertexRef2, axis);
            if (!string.IsNullOrEmpty(match)) return match;
            string v1 = string.IsNullOrEmpty(vertexRef1) ? "?" : vertexRef1;
            string v2 = string.IsNullOrEmpty(vertexRef2) ? "?" : vertexRef2;
            string pair = $"{v1} to {v2}";
            return axis.HasValue ? $"{pair}: {axis.Value}" : pair;
        }

        foreach (var sel in sels)
        {
            if (sel == null) continue;

            string name = string.IsNullOrEmpty(sel.Name) ? "(unnamed)" : sel.Name;

            var a = Resolve(sel.VertexRefA);
            var b = Resolve(sel.VertexRefB);
            if (a.HasValue && b.HasValue)
            {
                if (sel.Kind == MeasurementKind.AxisDistance)
                {
                    // Decompose B-A into three axis-aligned legs walking A → P1 → P2 → B along
                    // X, then Y, then Z. The leg matching the measurement axis takes the primary
                    // (yellow) color; the other two take secondary (white). Zero-length legs are
                    // skipped.
                    // <para>The full A-B hypotenuse line is intentionally NOT drawn for
                    // AxisDistance: it represents a 3D length that the measurement doesn't
                    // actually evaluate (only the axis-projected leg matters), so showing it
                    // visually invites confusion about what the threshold tests against. The
                    // colored legs are sufficient to convey both the pair and the axis;
                    // unlike the RatioDistance branches we don't need the grey reference
                    // line as visual context for a non-evaluated denominator pair.</para>
                    var av = a.Value;
                    var bv = b.Value;
                    var p1 = new OpenTK.Mathematics.Vector3(bv.X, av.Y, av.Z); // after X leg
                    var p2 = new OpenTK.Mathematics.Vector3(bv.X, bv.Y, av.Z); // after Y leg

                    var xColor = sel.Axis == MeasurementAxis.X ? primary : axisSecondary;
                    var yColor = sel.Axis == MeasurementAxis.Y ? primary : axisSecondary;
                    var zColor = sel.Axis == MeasurementAxis.Z ? primary : axisSecondary;

                    // Label format: "{name} (role qualifier)". Name leads so the parent
                    // measurement is identifiable at a glance; role describes which leg of
                    // the decomposition the cursor is on. The axis matching sel.Axis is
                    // annotated as "measurement axis" so the user can tell which leg
                    // actually contributes to the threshold. AxisDistance doesn't get a
                    // cross-reference colon because it's a single measurement, not a
                    // composite — the parent name alone identifies it.
                    if (av.X != bv.X) segments.Add((av, p1, xColor, $"{name} (X leg{(sel.Axis == MeasurementAxis.X ? " — measurement axis" : "")})"));
                    if (av.Y != bv.Y) segments.Add((p1, p2, yColor, $"{name} (Y leg{(sel.Axis == MeasurementAxis.Y ? " — measurement axis" : "")})"));
                    if (av.Z != bv.Z) segments.Add((p2, bv, zColor, $"{name} (Z leg{(sel.Axis == MeasurementAxis.Z ? " — measurement axis" : "")})"));
                }
                else if (sel.Kind == MeasurementKind.RatioDistance && sel.NumeratorAxis.HasValue)
                {
                    // RatioDistance numerator pair (A,B) is being reduced along a single axis
                    // via NumeratorAxis. Draw only the axis-projected leg in the primary
                    // color — its length equals the actual scalar being fed into the ratio.
                    // The grey A→B hypotenuse was previously also drawn as a visual reference
                    // for where A and B sit, but per user feedback it's omitted for axis-
                    // locked pairs: the hypotenuse represents a 3D length the ratio doesn't
                    // evaluate, and seeing it invites the same "wait, which one IS the
                    // measurement?" confusion that motivated the original axis-projected
                    // branch in the first place.
                    var legEnd = AxisLegEnd(a.Value, b.Value, sel.NumeratorAxis.Value);
                    segments.Add((a.Value, legEnd, primary,
                        $"{name} (numerator: {PairSlot(sel.VertexRefA, sel.VertexRefB, sel.NumeratorAxis)})"));
                }
                else
                {
                    if (sel.Kind == MeasurementKind.RatioDistance)
                    {
                        // No NumeratorAxis: the ratio uses the full 3D length, so the single
                        // line drawn IS the numerator's contribution. Cross-references a
                        // sibling PointDistance with the same pair (axis=null).
                        segments.Add((a.Value, b.Value, primary,
                            $"{name} (numerator: {PairSlot(sel.VertexRefA, sel.VertexRefB, null)})"));
                    }
                    else
                    {
                        // PointDistance: only one segment per measurement, name alone suffices.
                        segments.Add((a.Value, b.Value, primary, name));
                    }
                }
            }

            if (sel.Kind == MeasurementKind.RatioDistance)
            {
                var c = Resolve(sel.VertexRefC);
                var d = Resolve(sel.VertexRefD);
                if (c.HasValue && d.HasValue)
                {
                    if (sel.DenominatorAxis.HasValue)
                    {
                        // Symmetric treatment to the axis-locked numerator branch above:
                        // only the cyan axis-projected leg is drawn (its length is the actual
                        // denominator scalar). The C→D hypotenuse is suppressed for the same
                        // reason — it represents a 3D length the ratio doesn't evaluate.
                        var legEnd = AxisLegEnd(c.Value, d.Value, sel.DenominatorAxis.Value);
                        segments.Add((c.Value, legEnd, secondary,
                            $"{name} (denominator: {PairSlot(sel.VertexRefC, sel.VertexRefD, sel.DenominatorAxis)})"));
                    }
                    else
                    {
                        segments.Add((c.Value, d.Value, secondary,
                            $"{name} (denominator: {PairSlot(sel.VertexRefC, sel.VertexRefD, null)})"));
                    }
                }
            }
        }

        if (segments.Count == 0)
        {
            viewer.SetMeasurementLines(null);
            return;
        }

        viewer.SetMeasurementLines(segments);
    }

    /// <summary>Dumps the Measurements grid to a CSV file via the standard save dialog.
    /// Includes the LiveValue column so the snapshot captures what's currently evaluated
    /// against the loaded mesh; developer-only convenience, not exposed in the UI beyond
    /// the Ctrl+Shift+S keybinding (bound on the Measurements, Preview, and Match Presets
    /// tabs — all three operate on whichever preset+weight is currently loaded in the
    /// viewer, so they all share this single command).
    /// <para>Default filename is <c>{PresetLabel}_w{Weight}_Measurements.csv</c>,
    /// matching the convention used in the calibration reference set. Falls back to
    /// <c>measurements.csv</c> when no preset is selected (e.g. user invokes the shortcut
    /// before loading anything).</para></summary>
    private void SaveMeasurementsToCsvFile()
    {
        if (Measurements.Count == 0) return;

        string defaultName;
        var preset = _parent?.SelectedPreset;
        if (preset != null && !string.IsNullOrWhiteSpace(preset.Label))
        {
            // Spaces are kept as-is in reference filenames (see Round 2's "Fighter3BA"
            // vs "Different Bodies - Petite to BBW"); only sanitize characters Windows
            // outright bans in filenames. Falling back to '_' for those keeps the result
            // copy-pasteable into a directory listing.
            string label = preset.Label.Trim();
            foreach (char ch in System.IO.Path.GetInvalidFileNameChars())
            {
                label = label.Replace(ch, '_');
            }
            int weight = _parent!.PreviewWeight;
            defaultName = $"{label}_w{weight}_Measurements.csv";
        }
        else
        {
            defaultName = "measurements.csv";
        }

        if (!IO_Aux.SelectFileSave("", "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                ".csv", "Save Measurements as CSV", out string path, defaultName))
        {
            return;
        }
        try
        {
            var sb = new System.Text.StringBuilder();
            AppendMeasurementsTable(sb, ',', quoteCsv: true);
            System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
            _parent?.Logger?.LogMessage("BodyTypeProfile: exported " + Measurements.Count
                + " measurement(s) of profile '" + Name + "' to " + path);
        }
        catch (Exception ex)
        {
            _parent?.Logger?.LogError("SaveMeasurementsToCsvFile failed: " + ex.Message);
        }
    }

    /// <summary>Cumulative variant of <see cref="SaveMeasurementsToCsvFile"/>: drives a
    /// scan across every (preset, gender, weight) target for this profile and writes one
    /// row per slice. Reuses the shared <see cref="MeasurementCache"/>, so when the cache
    /// is already complete + fresh the "scan" is the all-hit fast path (no mesh work).
    /// Columns: PresetLabel, Gender, Weight, TopologyMismatch, one column per measurement
    /// in this profile's definition list (empty when the evaluator failed for that slice).
    /// Bound to Ctrl+Alt+Shift+S on the Measurements tab.</summary>
    private async System.Threading.Tasks.Task SaveAllMeasurementsToCsvAsync()
    {
        if (Measurements.Count == 0) return;
        if (_parent == null || _parent.IsScanning) return;
        if (!ReferenceEquals(_parent.SelectedProfile, this)) return;

        // Sanitize the profile name the same way the per-slice CSV does, then suffix
        // _AllMeasurements so the cumulative export is distinct from the per-slice one.
        string label = (Name ?? "Profile").Trim();
        if (label.Length == 0) label = "Profile";
        foreach (char ch in System.IO.Path.GetInvalidFileNameChars())
        {
            label = label.Replace(ch, '_');
        }
        string defaultName = $"{label}_AllMeasurements.csv";

        if (!IO_Aux.SelectFileSave("", "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                ".csv", "Save Cumulative Measurements as CSV", out string path, defaultName))
        {
            return;
        }

        // Ensure the cache is populated for every (preset, weight) target. RunScanAsync
        // already handles the empty/partial/stale cases and short-circuits when nothing
        // needs to be scanned, so calling it unconditionally is the simplest contract.
        try
        {
            await _parent.RunScanAsync();
        }
        catch (Exception ex)
        {
            _parent?.Logger?.LogError("SaveAllMeasurementsToCsvAsync: scan failed: " + ExceptionLogger.GetExceptionStack(ex));
            return;
        }

        if (MeasurementCache.Count == 0)
        {
            _parent?.Logger?.LogMessage("BodyTypeProfile: cumulative measurements export aborted — no presets matched this profile's body type.");
            return;
        }

        try
        {
            var sb = new System.Text.StringBuilder();
            AppendCumulativeMeasurementsTable(sb, ',', quoteCsv: true);
            System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
            _parent?.Logger?.LogMessage("BodyTypeProfile: exported cumulative measurements ("
                + MeasurementCache.Count + " slice(s), " + Measurements.Count
                + " measurement(s)) of profile '" + Name + "' to " + path);
        }
        catch (Exception ex)
        {
            _parent?.Logger?.LogError("SaveAllMeasurementsToCsvAsync: write failed: " + ExceptionLogger.GetExceptionStack(ex));
        }
    }

    /// <summary>Bulk variant of the per-row "H" histogram export: prompts for a folder,
    /// drives a scan if the cache is stale or empty (mirroring the per-row gate), then
    /// writes one CSV per measurement using the same column layout as the in-window
    /// Save CSV button. Bound to Ctrl+Shift+H on the Measurements tab.
    /// <para>Each per-measurement histogram is computed via <see cref="VM_MeasurementHistogram"/>
    /// so the binning is byte-identical to what the user would see opening the window
    /// individually (including using the persisted bin count when the "Persist" toggle
    /// is on). Measurements that evaluate to null on every cache entry are skipped with
    /// a logged count rather than producing empty CSVs.</para>
    /// <para>Filename convention: <c>{ProfileName}_{MeasurementName}_histogram.csv</c>,
    /// sanitized via <see cref="VM_MeasurementHistogram.SanitizeForFileName"/>. The CSV
    /// format itself (BinIndex, BinStart, BinEnd, Count) is the same single source of
    /// truth used by the per-window save (<see cref="VM_MeasurementHistogram.BuildHistogramCsv"/>).
    /// </para></summary>
    private async System.Threading.Tasks.Task SaveAllMeasurementHistogramsToCsvAsync()
    {
        if (Measurements.Count == 0) return;
        if (_parent == null || _parent.IsScanning) return;
        if (!ReferenceEquals(_parent.SelectedProfile, this)) return;

        // Prompt up front rather than after the scan, so the user can cancel without
        // paying the scan cost when they realize they don't have a target folder ready.
        if (!IO_Aux.SelectFolder("", out string folder))
        {
            return;
        }

        try
        {
            await _parent.RunScanAsync();
        }
        catch (Exception ex)
        {
            _parent?.Logger?.LogError("SaveAllMeasurementHistogramsToCsvAsync: scan failed: " + ExceptionLogger.GetExceptionStack(ex));
            return;
        }

        if (MeasurementCache.Count == 0)
        {
            _parent?.Logger?.LogMessage("BodyTypeProfile: histogram bulk export aborted — no presets matched this profile's body type.");
            return;
        }

        int saved = 0;
        int skipped = 0;
        int writeFailed = 0;
        // Snapshot the Measurements list so a mid-iteration edit (the user clicking X
        // on a row while a long bulk-export runs) doesn't throw a "collection modified"
        // mid-loop. Unlikely with the IsScanning gate keeping the UI quiet, but cheap
        // insurance.
        foreach (var def in Measurements.ToList())
        {
            if (def == null || string.IsNullOrEmpty(def.Name?.Trim()))
            {
                skipped++;
                continue;
            }

            try
            {
                var vm = new VM_MeasurementHistogram(this, def);
                if (vm.TotalSamples == 0 || vm.Bins.Count == 0)
                {
                    // Measurement is defined but cached as null for every (preset, weight)
                    // — probably broken vertex refs. Skip silently; the row's individual
                    // H button surfaces the diagnosis dialog when the user investigates.
                    skipped++;
                    continue;
                }

                string filename = vm.DefaultCsvFileName;
                string path = System.IO.Path.Combine(folder, filename);
                System.IO.File.WriteAllText(path,
                    VM_MeasurementHistogram.BuildHistogramCsv(vm.Bins),
                    new System.Text.UTF8Encoding(false));
                saved++;
            }
            catch (Exception ex)
            {
                _parent?.Logger?.LogError($"SaveAllMeasurementHistogramsToCsvAsync: '{def.Name}' failed: {ExceptionLogger.GetExceptionStack(ex)}");
                writeFailed++;
            }
        }

        _parent?.Logger?.LogMessage($"BodyTypeProfile: exported {saved} histogram CSV(s) to {folder}"
            + (skipped > 0 ? $" (skipped {skipped} with no samples)" : "")
            + (writeFailed > 0 ? $" — {writeFailed} write failure(s), see log" : "")
            + ".");
    }

    /// <summary>Drives a scan to refresh <see cref="ScanResults"/> against the current
    /// rules, then writes one CSV row per (descriptor, preset, gender, weight) match.
    /// Long format keeps the file easy to pivot in Excel: filter by Category/Value to see
    /// which slices fired a given descriptor; group by Preset/Weight to see what each slice
    /// was labeled. Bound to Ctrl+Alt+Shift+S on the Match Presets tab.</summary>
    private async System.Threading.Tasks.Task SaveDescriptorMatchesToCsvAsync()
    {
        if (Rules.Count == 0) return;
        if (_parent == null || _parent.IsScanning) return;
        if (!ReferenceEquals(_parent.SelectedProfile, this)) return;

        string label = (Name ?? "Profile").Trim();
        if (label.Length == 0) label = "Profile";
        foreach (char ch in System.IO.Path.GetInvalidFileNameChars())
        {
            label = label.Replace(ch, '_');
        }
        string defaultName = $"{label}_DescriptorMatches.csv";

        if (!IO_Aux.SelectFileSave("", "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                ".csv", "Save Descriptor Matches as CSV", out string path, defaultName))
        {
            return;
        }

        try
        {
            await _parent.RunScanAsync();
        }
        catch (Exception ex)
        {
            _parent?.Logger?.LogError("SaveDescriptorMatchesToCsvAsync: scan failed: " + ExceptionLogger.GetExceptionStack(ex));
            return;
        }

        if (ScanResults.Count == 0)
        {
            _parent?.Logger?.LogMessage("BodyTypeProfile: descriptor-matches export aborted — no scan results to dump.");
            return;
        }

        try
        {
            var sb = new System.Text.StringBuilder();
            int rowCount = AppendDescriptorMatchesTable(sb, ',', quoteCsv: true);
            System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
            _parent?.Logger?.LogMessage("BodyTypeProfile: exported " + rowCount
                + " descriptor match row(s) for profile '" + Name + "' to " + path);
        }
        catch (Exception ex)
        {
            _parent?.Logger?.LogError("SaveDescriptorMatchesToCsvAsync: write failed: " + ExceptionLogger.GetExceptionStack(ex));
        }
    }

    /// <summary>Builds the cumulative-measurements table into <paramref name="sb"/>.
    /// One header row plus one data row per cache entry. Measurement columns follow
    /// the order in <see cref="Measurements"/> (first occurrence wins on duplicate
    /// names — matches the evaluator's first-wins policy). Missing/failed values
    /// render as empty cells so the CSV remains tabular.</summary>
    private void AppendCumulativeMeasurementsTable(System.Text.StringBuilder sb, char separator, bool quoteCsv)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        var measurementColumns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in Measurements)
        {
            var n = m?.Name?.Trim() ?? "";
            if (string.IsNullOrEmpty(n)) continue;
            if (!seen.Add(n)) continue;
            measurementColumns.Add(n);
        }

        var headers = new List<string> { "PresetLabel", "Gender", "Weight", "TopologyMismatch" };
        headers.AddRange(measurementColumns);
        AppendRow(sb, separator, quoteCsv, headers.ToArray());

        var ordered = MeasurementCache
            .OrderBy(kv => kv.Key.Gender)
            .ThenBy(kv => kv.Key.PresetLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Weight);

        foreach (var kv in ordered)
        {
            var row = new List<string>(headers.Count)
            {
                kv.Key.PresetLabel ?? "",
                kv.Key.Gender.ToString(),
                kv.Key.Weight.ToString(inv),
                kv.Value.TopologyMismatch ? "true" : "false",
            };
            foreach (var name in measurementColumns)
            {
                if (kv.Value.Measurements.TryGetValue(name, out var v) && v.HasValue)
                    row.Add(v.Value.ToString("F4", inv));
                else
                    row.Add("");
            }
            AppendRow(sb, separator, quoteCsv, row.ToArray());
        }
    }

    /// <summary>Builds the descriptor-matches long-format table into <paramref name="sb"/>.
    /// Returns the number of data rows written (excluding the header) so the caller can
    /// log a meaningful count. Sort order keeps related rows together: Category, Value,
    /// then by Gender / Preset / Weight. Slices with zero matches contribute zero rows;
    /// they aren't represented in the file.</summary>
    private int AppendDescriptorMatchesTable(System.Text.StringBuilder sb, char separator, bool quoteCsv)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string[] headers = { "Category", "Value", "PresetLabel", "Gender", "Weight" };
        AppendRow(sb, separator, quoteCsv, headers);

        // Flatten ScanResults → (descriptor, slice) tuples, then sort. Sorting after the
        // flatten (rather than per-slice) puts every preset that matched a given descriptor
        // adjacent in the output, which is the whole point of grouping by descriptor.
        var rows = new List<(string Category, string Value, string PresetLabel, Gender Gender, int Weight)>();
        foreach (var kv in ScanResults)
        {
            if (kv.Value == null) continue;
            foreach (var d in kv.Value)
            {
                if (d == null) continue;
                rows.Add((
                    d.Category ?? "",
                    d.Value ?? "",
                    kv.Key.PresetLabel ?? "",
                    kv.Key.Gender,
                    kv.Key.Weight));
            }
        }

        rows.Sort((a, b) =>
        {
            int c = string.Compare(a.Category, b.Category, StringComparison.Ordinal);
            if (c != 0) return c;
            c = string.Compare(a.Value, b.Value, StringComparison.Ordinal);
            if (c != 0) return c;
            c = a.Gender.CompareTo(b.Gender);
            if (c != 0) return c;
            c = string.Compare(a.PresetLabel, b.PresetLabel, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            return a.Weight.CompareTo(b.Weight);
        });

        foreach (var r in rows)
        {
            string[] row =
            {
                r.Category,
                r.Value,
                r.PresetLabel,
                r.Gender.ToString(),
                r.Weight.ToString(inv),
            };
            AppendRow(sb, separator, quoteCsv, row);
        }
        return rows.Count;
    }

    /// <summary>Copies the Measurements grid as TSV onto the clipboard so it pastes
    /// straight into Excel / Google Sheets without import wizards. Tab-separated avoids
    /// the comma-decimal locale headaches CSV runs into.</summary>
    private void CopyMeasurementsToClipboardTsv()
    {
        if (Measurements.Count == 0) return;
        try
        {
            var sb = new System.Text.StringBuilder();
            AppendMeasurementsTable(sb, '\t', quoteCsv: false);
            System.Windows.Clipboard.SetText(sb.ToString());
            _parent?.Logger?.LogMessage("BodyTypeProfile: copied " + Measurements.Count
                + " measurement(s) to clipboard.");
        }
        catch (Exception ex)
        {
            _parent?.Logger?.LogError("CopyMeasurementsToClipboardTsv failed: " + ex.Message);
        }
    }

    /// <summary>Builds the Name / Kind / A / B / C / D / Axis / NumeratorAxis /
    /// DenominatorAxis / LiveValue table into <paramref name="sb"/> with one row per
    /// measurement. <paramref name="separator"/> is comma for CSV or tab for TSV;
    /// <paramref name="quoteCsv"/> enables RFC 4180 quoting of fields containing the
    /// separator, a double quote, or a newline. Floats and enums format with
    /// <c>CultureInfo.InvariantCulture</c> so the CSV survives locale roundtrips.</summary>
    private void AppendMeasurementsTable(System.Text.StringBuilder sb, char separator, bool quoteCsv)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string[] headers = { "Name", "Kind", "A", "B", "C", "D", "Axis",
                              "NumeratorAxis", "DenominatorAxis", "LiveValue" };
        AppendRow(sb, separator, quoteCsv, headers);

        foreach (var m in Measurements)
        {
            bool isRatio = m.Kind == MeasurementKind.RatioDistance;
            string[] row =
            {
                m.Name ?? "",
                m.Kind.ToString(),
                m.VertexRefA ?? "",
                m.VertexRefB ?? "",
                isRatio ? (m.VertexRefC ?? "") : "",
                isRatio ? (m.VertexRefD ?? "") : "",
                m.Kind == MeasurementKind.AxisDistance ? m.Axis.ToString() : "",
                isRatio && m.NumeratorAxis.HasValue   ? m.NumeratorAxis.Value.ToString()   : "",
                isRatio && m.DenominatorAxis.HasValue ? m.DenominatorAxis.Value.ToString() : "",
                m.LiveValue.HasValue ? m.LiveValue.Value.ToString("F4", inv) : "",
            };
            AppendRow(sb, separator, quoteCsv, row);
        }
    }

    private static void AppendRow(System.Text.StringBuilder sb, char separator, bool quoteCsv, string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(separator);
            sb.Append(quoteCsv ? CsvEscape(fields[i], separator) : fields[i]);
        }
        sb.Append('\n');
    }

    /// <summary>RFC 4180 quoting: wrap in double quotes when the field contains the
    /// separator, a double quote, or a line break; double internal quotes.</summary>
    private static string CsvEscape(string field, char separator)
    {
        if (string.IsNullOrEmpty(field)) return "";
        bool needsQuoting = field.IndexOf(separator) >= 0
                         || field.IndexOf('"') >= 0
                         || field.IndexOf('\n') >= 0
                         || field.IndexOf('\r') >= 0;
        if (!needsQuoting) return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>Envelope classes used by the per-tab Save/Load shortcuts (Ctrl+S / Ctrl+L
    /// on the KeyVertices, Measurements, and Rules tabs). Field shape mirrors the
    /// Revised_BodyTypeProfile_Rules.json reference: a top-level array named after the
    /// collection, with documentation <c>__comment_*</c> siblings ignored by Newtonsoft
    /// on load. One class per tab so the file's top-level key documents the payload.
    /// <para>The top-level <c>IsPatch</c> flag distinguishes a wholesale snapshot (false /
    /// absent) from a partial patch (true). A patch identifies items by their natural key
    /// (Name for KeyVertices / Measurements, Id for Rules) and adds-or-replaces them in
    /// place rather than wiping the target collection. Legacy files without the flag
    /// deserialize as <c>IsPatch == false</c> (C# default), preserving the original
    /// wholesale-replace semantics. <see cref="RulesExportPayload.RulesToDelete"/> is the
    /// patch-only delete list (rule Ids); always serialized (empty by default) since this
    /// project's shared JSON settings don't ignore defaults.</para></summary>
    private class KeyVerticesExportPayload
    {
        public bool IsPatch { get; set; } = false;
        public List<NamedKeyVertex> KeyVertices { get; set; } = new();
    }
    private class MeasurementsExportPayload
    {
        public bool IsPatch { get; set; } = false;
        public List<MeasurementDefinition> Measurements { get; set; } = new();
    }
    private class RulesExportPayload
    {
        public bool IsPatch { get; set; } = false;
        public List<MeasurementRule> Rules { get; set; } = new();
        public List<string> RulesToDelete { get; set; } = new();
    }

    /// <summary>Replaces every item in <paramref name="target"/> with <paramref name="newItems"/>
    /// via per-item Remove + Add (rather than Clear()) so each existing
    /// <c>CollectionChanged</c> handler sees individual Remove/Add events for the affected
    /// rows. Clear() raises a single Reset that this codebase's per-row PropertyChanged
    /// hooks don't iterate, leaving stale subscriptions on the discarded items and stale
    /// entries in the parent's per-row caches (_kvLastNames, _measurementLastNames).
    /// O(n) and n is tiny for typical profiles, so the overhead doesn't matter.</summary>
    private static void ReplaceObservableCollection<T>(ObservableCollection<T> target, IEnumerable<T> newItems)
    {
        while (target.Count > 0) target.RemoveAt(target.Count - 1);
        foreach (var it in newItems)
        {
            if (it != null) target.Add(it);
        }
    }

    /// <summary>Writes KeyVertices to a user-chosen path as { "IsPatch": ..., "KeyVertices": [...] } JSON.
    /// Smart-mode: when 0 rows are selected or every row is selected (DataGrid SelectionMode=Extended
    /// fed by the code-behind into <see cref="_selectedKeyVertices"/>), the full collection is written
    /// with <c>IsPatch: false</c> (a full snapshot, byte-equivalent to the legacy format apart from the
    /// added flag). When a strict subset is selected, only those rows are written with <c>IsPatch: true</c>
    /// and the filename suggestion gains a <c>_patch</c> suffix — the resulting file applies as an
    /// add-or-replace-by-Name patch on load.</summary>
    private void SaveKeyVerticesToJsonFile()
    {
        if (KeyVertices.Count == 0)
        {
            MessageWindow.DisplayNotificationOK("No Key Vertices", "There are no key vertices to save.");
            return;
        }
        // Treat "0 selected" and "all selected" as wholesale export so the user doesn't
        // have to remember to clear the selection. Anything in between → patch.
        int selCount = _selectedKeyVertices.Count;
        bool isPatch = selCount > 0 && selCount < KeyVertices.Count;
        var sourceRows = isPatch ? (IEnumerable<VM_NamedKeyVertex>)_selectedKeyVertices : KeyVertices;
        string baseName = string.IsNullOrWhiteSpace(Name) ? "keyvertices" : Name.Trim().Replace(' ', '_');
        string defaultName = baseName + "_keyvertices" + (isPatch ? "_patch" : "") + ".json";
        if (!IO_Aux.SelectFileSave("", "Key Vertices JSON (*.json)|*.json|All files (*.*)|*.*",
                ".json", isPatch ? "Save Key Vertices Patch" : "Save Key Vertices", out string path, defaultName))
        {
            return;
        }
        var payload = new KeyVerticesExportPayload
        {
            IsPatch = isPatch,
            KeyVertices = sourceRows.Where(k => k != null).Select(k => k.DumpToModel()).ToList(),
        };
        JSONhandler<KeyVerticesExportPayload>.SaveJSONFile(payload, path, out bool success, out string exception);
        if (!success)
        {
            MessageWindow.DisplayNotificationOK("Save Failed", exception);
            return;
        }
        _parent?.Logger?.LogMessage("BodyTypeProfile: saved " + payload.KeyVertices.Count
            + " key vertex/vertices of profile '" + Name + "' to " + path
            + (isPatch ? " (patch)" : " (full)"));
    }

    /// <summary>Parses a Key Vertices JSON file. Branches on the top-level <c>IsPatch</c> flag:
    /// false / absent → legacy wholesale-replace with a confirmation dialog; true → add-or-replace
    /// by Name (case-sensitive, Ordinal) via <see cref="ApplyKeyVerticesPatch"/>. Legacy files
    /// without the flag deserialize as IsPatch=false (C# default) so existing JSONs still load
    /// the same way.</summary>
    private void LoadKeyVerticesFromJsonFile()
    {
        if (!IO_Aux.SelectFile("", "Key Vertices JSON (*.json)|*.json|All files (*.*)|*.*",
                "Load Key Vertices", out string path))
        {
            return;
        }
        KeyVerticesExportPayload? loaded;
        bool success;
        string exception;
        try
        {
            loaded = JSONhandler<KeyVerticesExportPayload>.LoadJSONFile(path, out success, out exception);
        }
        catch (Exception ex)
        {
            loaded = null;
            success = false;
            exception = ex.Message;
        }
        if (!success || loaded == null)
        {
            MessageWindow.DisplayNotificationOK("Load Failed",
                string.IsNullOrEmpty(exception) ? "Could not parse Key Vertices JSON." : exception);
            return;
        }
        var newItems = loaded.KeyVertices ?? new List<NamedKeyVertex>();
        if (newItems.Count == 0)
        {
            MessageWindow.DisplayNotificationOK("No Key Vertices Found",
                "The selected file contains no 'KeyVertices' array (or it is empty).");
            return;
        }
        if (loaded.IsPatch)
        {
            bool confirm = MessageWindow.DisplayNotificationYesNo("Apply Key Vertices Patch?",
                "This patch will add or replace " + newItems.Count + " key vertex/vertices by Name "
                + "(existing rows with the same Name are updated in place; new names are appended).\n\nContinue?");
            if (!confirm) return;
            int beforeCount = KeyVertices.Count;
            ApplyKeyVerticesPatch(newItems);
            _parent?.Logger?.LogMessage("BodyTypeProfile: patched profile '" + Name + "' with "
                + newItems.Count + " key-vertex add/edit entries from " + path
                + " (" + beforeCount + " → " + KeyVertices.Count + ")");
            return;
        }
        if (KeyVertices.Count > 0)
        {
            bool confirm = MessageWindow.DisplayNotificationYesNo("Replace Key Vertices?",
                "This will replace the current " + KeyVertices.Count + " key vertex/vertices with "
                + newItems.Count + " loaded entries.\n\nContinue?");
            if (!confirm) return;
        }
        ReplaceObservableCollection(KeyVertices, newItems.Where(k => k != null).Select(k => new VM_NamedKeyVertex(k, this)));
        _parent?.Logger?.LogMessage("BodyTypeProfile: loaded " + KeyVertices.Count
            + " key vertex/vertices into profile '" + Name + "' from " + path);
    }

    /// <summary>Applies a KeyVertices patch in place: each incoming model adds-or-replaces by
    /// <see cref="NamedKeyVertex.Name"/> (Ordinal, trimmed). Replacement preserves the row's
    /// original index (Remove + Insert at the same slot) so the user perceives an "edit in place".
    /// Never calls <c>Clear()</c>; uses the same per-item Remove/Add discipline as
    /// <see cref="ReplaceObservableCollection{T}"/> so per-row CollectionChanged subscriptions
    /// stay correct. After the pass runs <see cref="RecomputeDuplicateKeyVertexNames"/> once for
    /// a clean aggregate state.</summary>
    private void ApplyKeyVerticesPatch(List<NamedKeyVertex> incoming)
    {
        if (incoming == null) return;
        // First-row-wins on duplicate names within KeyVertices — matches RecomputeDuplicateKeyVertexNames.
        var byName = new Dictionary<string, VM_NamedKeyVertex>(StringComparer.Ordinal);
        foreach (var vm in KeyVertices)
        {
            if (vm == null) continue;
            var n = vm.Name?.Trim() ?? "";
            if (n.Length == 0) continue;
            if (!byName.ContainsKey(n)) byName[n] = vm;
        }
        foreach (var model in incoming)
        {
            if (model == null) continue;
            var n = model.Name?.Trim() ?? "";
            if (n.Length == 0) continue;
            if (byName.TryGetValue(n, out var existing))
            {
                int idx = KeyVertices.IndexOf(existing);
                if (idx < 0)
                {
                    // Drifted dict (shouldn't happen, but be defensive).
                    KeyVertices.Add(new VM_NamedKeyVertex(model, this));
                    continue;
                }
                KeyVertices.RemoveAt(idx);
                var replacement = new VM_NamedKeyVertex(model, this);
                KeyVertices.Insert(idx, replacement);
                byName[n] = replacement;
            }
            else
            {
                var added = new VM_NamedKeyVertex(model, this);
                KeyVertices.Add(added);
                byName[n] = added;
            }
        }
        RecomputeDuplicateKeyVertexNames();
    }

    /// <summary>Writes the current Measurements to a user-chosen path as
    /// { "IsPatch": false, "Measurements": [...] } JSON. The LiveValue column is not persisted —
    /// it's a runtime readout, not a field of <see cref="MeasurementDefinition"/>.</summary>
    private void SaveMeasurementsToJsonFile()
    {
        if (Measurements.Count == 0)
        {
            MessageWindow.DisplayNotificationOK("No Measurements", "There are no measurements to save.");
            return;
        }
        string baseName = string.IsNullOrWhiteSpace(Name) ? "measurements" : Name.Trim().Replace(' ', '_');
        if (!IO_Aux.SelectFileSave("", "Measurements JSON (*.json)|*.json|All files (*.*)|*.*",
                ".json", "Save Measurements", out string path, baseName + "_measurements.json"))
        {
            return;
        }
        var payload = new MeasurementsExportPayload
        {
            IsPatch = false,
            Measurements = Measurements.Select(m => m.DumpToModel()).ToList(),
        };
        JSONhandler<MeasurementsExportPayload>.SaveJSONFile(payload, path, out bool success, out string exception);
        if (!success)
        {
            MessageWindow.DisplayNotificationOK("Save Failed", exception);
            return;
        }
        _parent?.Logger?.LogMessage("BodyTypeProfile: saved " + Measurements.Count
            + " measurement(s) of profile '" + Name + "' to " + path);
    }

    /// <summary>Patch-export the rows currently selected in the Measurements DataGrid (fed by the
    /// code-behind into <see cref="_selectedMeasurements"/>) as { "IsPatch": true,
    /// "Measurements": [...] } JSON. No selection → user-facing OK and bail; we never silently
    /// fall through to a full export because Ctrl+S is already the full-export shortcut and a
    /// "patch with everything" file would be confusing.</summary>
    private void SaveMeasurementsPatchToJsonFile()
    {
        if (_selectedMeasurements.Count == 0)
        {
            MessageWindow.DisplayNotificationOK("No Selection",
                "Select one or more measurement rows in the grid first, then re-trigger the patch export.");
            return;
        }
        string baseName = string.IsNullOrWhiteSpace(Name) ? "measurements" : Name.Trim().Replace(' ', '_');
        if (!IO_Aux.SelectFileSave("", "Measurements JSON (*.json)|*.json|All files (*.*)|*.*",
                ".json", "Save Measurements Patch", out string path, baseName + "_measurements_patch.json"))
        {
            return;
        }
        var payload = new MeasurementsExportPayload
        {
            IsPatch = true,
            Measurements = _selectedMeasurements.Where(m => m != null).Select(m => m.DumpToModel()).ToList(),
        };
        JSONhandler<MeasurementsExportPayload>.SaveJSONFile(payload, path, out bool success, out string exception);
        if (!success)
        {
            MessageWindow.DisplayNotificationOK("Save Failed", exception);
            return;
        }
        _parent?.Logger?.LogMessage("BodyTypeProfile: saved " + payload.Measurements.Count
            + " measurement(s) of profile '" + Name + "' to " + path + " (patch)");
    }

    /// <summary>Parses a Measurements JSON file. Branches on the top-level <c>IsPatch</c> flag:
    /// false / absent → legacy wholesale-replace with a confirmation dialog; true → add-or-replace
    /// by Name via <see cref="ApplyMeasurementsPatch"/>.</summary>
    private void LoadMeasurementsFromJsonFile()
    {
        if (!IO_Aux.SelectFile("", "Measurements JSON (*.json)|*.json|All files (*.*)|*.*",
                "Load Measurements", out string path))
        {
            return;
        }
        MeasurementsExportPayload? loaded;
        bool success;
        string exception;
        try
        {
            loaded = JSONhandler<MeasurementsExportPayload>.LoadJSONFile(path, out success, out exception);
        }
        catch (Exception ex)
        {
            loaded = null;
            success = false;
            exception = ex.Message;
        }
        if (!success || loaded == null)
        {
            MessageWindow.DisplayNotificationOK("Load Failed",
                string.IsNullOrEmpty(exception) ? "Could not parse Measurements JSON." : exception);
            return;
        }
        var newItems = loaded.Measurements ?? new List<MeasurementDefinition>();
        if (newItems.Count == 0)
        {
            MessageWindow.DisplayNotificationOK("No Measurements Found",
                "The selected file contains no 'Measurements' array (or it is empty).");
            return;
        }
        if (loaded.IsPatch)
        {
            bool confirm = MessageWindow.DisplayNotificationYesNo("Apply Measurements Patch?",
                "This patch will add or replace " + newItems.Count + " measurement(s) by Name "
                + "(existing rows with the same Name are updated in place; new names are appended).\n\nContinue?");
            if (!confirm) return;
            int beforeCount = Measurements.Count;
            ApplyMeasurementsPatch(newItems);
            _parent?.Logger?.LogMessage("BodyTypeProfile: patched profile '" + Name + "' with "
                + newItems.Count + " measurement add/edit entries from " + path
                + " (" + beforeCount + " → " + Measurements.Count + ")");
            return;
        }
        if (Measurements.Count > 0)
        {
            bool confirm = MessageWindow.DisplayNotificationYesNo("Replace Measurements?",
                "This will replace the current " + Measurements.Count + " measurement(s) with "
                + newItems.Count + " loaded measurement(s).\n\nContinue?");
            if (!confirm) return;
        }
        ReplaceObservableCollection(Measurements, newItems.Where(m => m != null).Select(m => new VM_MeasurementDefinition(m, this)));
        _parent?.Logger?.LogMessage("BodyTypeProfile: loaded " + Measurements.Count
            + " measurement(s) into profile '" + Name + "' from " + path);
    }

    /// <summary>Applies a Measurements patch in place: each incoming model adds-or-replaces by
    /// <see cref="MeasurementDefinition.Name"/> (Ordinal, trimmed), preserving the original row
    /// index on replacement. Runs <see cref="RecomputeDuplicateMeasurementNames"/> and
    /// <see cref="RecomputeMeasurementRefValidity"/> once at the end so aggregate validation
    /// reflects the final state rather than transient in-flight Remove+Insert pairs.</summary>
    private void ApplyMeasurementsPatch(List<MeasurementDefinition> incoming)
    {
        if (incoming == null) return;
        var byName = new Dictionary<string, VM_MeasurementDefinition>(StringComparer.Ordinal);
        foreach (var vm in Measurements)
        {
            if (vm == null) continue;
            var n = vm.Name?.Trim() ?? "";
            if (n.Length == 0) continue;
            if (!byName.ContainsKey(n)) byName[n] = vm;
        }
        foreach (var model in incoming)
        {
            if (model == null) continue;
            var n = model.Name?.Trim() ?? "";
            if (n.Length == 0) continue;
            if (byName.TryGetValue(n, out var existing))
            {
                int idx = Measurements.IndexOf(existing);
                if (idx < 0)
                {
                    Measurements.Add(new VM_MeasurementDefinition(model, this));
                    continue;
                }
                Measurements.RemoveAt(idx);
                var replacement = new VM_MeasurementDefinition(model, this);
                Measurements.Insert(idx, replacement);
                byName[n] = replacement;
            }
            else
            {
                var added = new VM_MeasurementDefinition(model, this);
                Measurements.Add(added);
                byName[n] = added;
            }
        }
        RecomputeDuplicateMeasurementNames();
        RecomputeMeasurementRefValidity();
    }

    /// <summary>Writes the current Rules collection to a user-chosen path as
    /// { "IsPatch": false, "Rules": [...], "RulesToDelete": [] } JSON. Default filename derives
    /// from the profile name.</summary>
    private void SaveRulesToJsonFile()
    {
        if (Rules.Count == 0)
        {
            MessageWindow.DisplayNotificationOK("No Rules", "There are no rules to save.");
            return;
        }
        string baseName = string.IsNullOrWhiteSpace(Name) ? "rules" : Name.Trim().Replace(' ', '_');
        string defaultName = baseName + "_rules.json";
        if (!IO_Aux.SelectFileSave("", "Rules JSON (*.json)|*.json|All files (*.*)|*.*",
                ".json", "Save Rules", out string path, defaultName))
        {
            return;
        }
        var payload = new RulesExportPayload
        {
            IsPatch = false,
            Rules = Rules.Select(r => r.DumpToModel()).ToList(),
            RulesToDelete = new List<string>(),
        };
        JSONhandler<RulesExportPayload>.SaveJSONFile(payload, path, out bool success, out string exception);
        if (!success)
        {
            MessageWindow.DisplayNotificationOK("Save Failed", exception);
            return;
        }
        _parent?.Logger?.LogMessage("BodyTypeProfile: saved " + Rules.Count
            + " rule(s) of profile '" + Name + "' to " + path);
    }

    /// <summary>Returns the rules that fall under the current <see cref="SelectedRuleTreeNode"/>:
    /// every rule for a Category node, the exact (Category, Value) match for a Value node, empty
    /// for null. Predicate matches <see cref="RefreshFilteredRules"/> exactly so the patch export
    /// and the right-pane filter stay in sync (FilteredRules itself is a view-side rebuild target
    /// and could be stale by the time the command fires; recomputing avoids that hazard).</summary>
    private List<VM_MeasurementRule> GetRulesUnderSelectedTreeNode()
    {
        var result = new List<VM_MeasurementRule>();
        switch (SelectedRuleTreeNode)
        {
            case VM_RuleTreeValueNode v:
                foreach (var r in Rules)
                {
                    if (r == null) continue;
                    if (string.Equals(r.DescriptorCategory, v.Category, StringComparison.Ordinal)
                        && string.Equals(r.DescriptorValue, v.Value, StringComparison.Ordinal))
                    {
                        result.Add(r);
                    }
                }
                break;
            case VM_RuleTreeCategoryNode c:
                foreach (var r in Rules)
                {
                    if (r == null) continue;
                    if (string.Equals(r.DescriptorCategory, c.Category, StringComparison.Ordinal))
                    {
                        result.Add(r);
                    }
                }
                break;
        }
        return result;
    }

    /// <summary>Ctrl+Shift+P on the Rules tab: writes the rules under the currently-selected
    /// TreeView node as { "IsPatch": true, "Rules": [...], "RulesToDelete": [] }. No node
    /// selected, or selected node has no rules → user-facing OK and bail.</summary>
    private void SaveRulesPatchToJsonFile()
    {
        var picked = GetRulesUnderSelectedTreeNode();
        if (picked.Count == 0)
        {
            MessageWindow.DisplayNotificationOK("No Rules Selected",
                "Select a Category or Value node in the Rules tree first. The patch will include "
                + "every rule under that node.");
            return;
        }
        string baseName = string.IsNullOrWhiteSpace(Name) ? "rules" : Name.Trim().Replace(' ', '_');
        if (!IO_Aux.SelectFileSave("", "Rules JSON (*.json)|*.json|All files (*.*)|*.*",
                ".json", "Save Rules Patch", out string path, baseName + "_rules_patch.json"))
        {
            return;
        }
        var payload = new RulesExportPayload
        {
            IsPatch = true,
            Rules = picked.Select(r => r.DumpToModel()).ToList(),
            RulesToDelete = new List<string>(),
        };
        JSONhandler<RulesExportPayload>.SaveJSONFile(payload, path, out bool success, out string exception);
        if (!success)
        {
            MessageWindow.DisplayNotificationOK("Save Failed", exception);
            return;
        }
        _parent?.Logger?.LogMessage("BodyTypeProfile: saved " + payload.Rules.Count
            + " rule(s) of profile '" + Name + "' to " + path + " (patch)");
    }

    /// <summary>Ctrl+Shift+Alt+P on the Rules tab: opens the rule-delete picker window,
    /// pre-seeded with the rules under the currently-selected tree node as the add/edit set.
    /// On OK, writes { "IsPatch": true, "Rules": [...], "RulesToDelete": [...] } with both
    /// the (possibly refined) add/edit list and the user-marked deletion Ids.</summary>
    private void SaveRulesPatchWithDeletesToJsonFile()
    {
        var addEditSeed = GetRulesUnderSelectedTreeNode();
        var picker = new VM_RuleDeleteExportPicker(Rules, addEditSeed);
        var win = new Window_RuleDeleteExportPicker
        {
            DataContext = picker,
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        win.ShowDialog();
        if (!picker.Confirmed)
        {
            return;
        }
        var addEditModels = picker.AddEditModels;
        var deleteIds = picker.DeleteIds;
        if (addEditModels.Count == 0 && deleteIds.Count == 0)
        {
            MessageWindow.DisplayNotificationOK("Empty Patch",
                "The picker produced no add/edit entries and no deletions — nothing to write.");
            return;
        }
        string baseName = string.IsNullOrWhiteSpace(Name) ? "rules" : Name.Trim().Replace(' ', '_');
        if (!IO_Aux.SelectFileSave("", "Rules JSON (*.json)|*.json|All files (*.*)|*.*",
                ".json", "Save Rules Patch (with deletes)", out string path, baseName + "_rules_patch.json"))
        {
            return;
        }
        var payload = new RulesExportPayload
        {
            IsPatch = true,
            Rules = addEditModels.ToList(),
            RulesToDelete = deleteIds.ToList(),
        };
        JSONhandler<RulesExportPayload>.SaveJSONFile(payload, path, out bool success, out string exception);
        if (!success)
        {
            MessageWindow.DisplayNotificationOK("Save Failed", exception);
            return;
        }
        _parent?.Logger?.LogMessage("BodyTypeProfile: saved " + payload.Rules.Count
            + " rule add/edit + " + payload.RulesToDelete.Count
            + " rule delete(s) of profile '" + Name + "' to " + path + " (patch)");
    }

    /// <summary>Parses a Rules JSON file at a user-chosen path. Branches on the top-level
    /// <c>IsPatch</c> flag: false / absent → legacy wholesale-replace with a confirmation
    /// dialog; true → delete-then-add/replace by Id via <see cref="ApplyRulesPatch"/>.
    /// Tolerates the documentation comment fields in the reference format (they deserialize
    /// as unknown properties Newtonsoft ignores).</summary>
    private void LoadRulesFromJsonFile()
    {
        if (!IO_Aux.SelectFile("", "Rules JSON (*.json)|*.json|All files (*.*)|*.*",
                "Load Rules", out string path))
        {
            return;
        }
        RulesExportPayload? loaded;
        bool success;
        string exception;
        try
        {
            loaded = JSONhandler<RulesExportPayload>.LoadJSONFile(path, out success, out exception);
        }
        catch (Exception ex)
        {
            loaded = null;
            success = false;
            exception = ex.Message;
        }
        if (!success || loaded == null)
        {
            MessageWindow.DisplayNotificationOK("Load Failed",
                string.IsNullOrEmpty(exception) ? "Could not parse Rules JSON." : exception);
            return;
        }
        var newRules = loaded.Rules ?? new List<MeasurementRule>();
        var deleteIds = loaded.RulesToDelete ?? new List<string>();
        if (loaded.IsPatch)
        {
            int addEditCount = newRules.Count(r => r != null);
            int deleteCount = deleteIds.Count(s => !string.IsNullOrWhiteSpace(s));
            if (addEditCount == 0 && deleteCount == 0)
            {
                MessageWindow.DisplayNotificationOK("Patch is Empty",
                    "The selected patch contains no rules to add/edit and no rules to delete.");
                return;
            }
            // Defensive: a hand-edited file may omit Id or leave nested collections null.
            // Fix in place before the VM ctor sees them, rather than crashing it.
            foreach (var r in newRules)
            {
                if (r == null) continue;
                if (string.IsNullOrEmpty(r.Id)) r.Id = Guid.NewGuid().ToString("N");
                if (r.Descriptor == null) r.Descriptor = new BodyShapeDescriptor.LabelSignature();
                if (r.GroupsORlogic == null) r.GroupsORlogic = new List<AndGatedMeasurementGroup>();
            }
            bool confirm = MessageWindow.DisplayNotificationYesNo("Apply Rules Patch?",
                "This patch will:\n\n"
                + "  • Add or replace " + addEditCount + " rule(s) by Id\n"
                + "  • Delete " + deleteCount + " rule(s) by Id\n\n"
                + "(Existing rules with the same Id are updated in place. Unknown Ids in the delete "
                + "list are skipped.)\n\nContinue?");
            if (!confirm) return;
            int beforeCount = Rules.Count;
            ApplyRulesPatch(newRules, deleteIds);
            _parent?.Logger?.LogMessage("BodyTypeProfile: patched profile '" + Name + "' with "
                + addEditCount + " rule add/edit + " + deleteCount + " rule delete(s) from " + path
                + " (" + beforeCount + " → " + Rules.Count + ")");
            return;
        }
        if (newRules.Count == 0)
        {
            MessageWindow.DisplayNotificationOK("No Rules Found",
                "The selected file contains no 'Rules' array (or it is empty).");
            return;
        }
        if (Rules.Count > 0)
        {
            bool confirm = MessageWindow.DisplayNotificationYesNo("Replace Rules?",
                "This will replace the current " + Rules.Count + " rule(s) with "
                + newRules.Count + " loaded rule(s).\n\nContinue?");
            if (!confirm) return;
        }
        // Defensive: a hand-edited file may omit Id or leave nested collections null.
        // Fix in place before the VM ctor sees them, rather than crashing it.
        foreach (var r in newRules)
        {
            if (r == null) continue;
            if (string.IsNullOrEmpty(r.Id)) r.Id = Guid.NewGuid().ToString("N");
            if (r.Descriptor == null) r.Descriptor = new BodyShapeDescriptor.LabelSignature();
            if (r.GroupsORlogic == null) r.GroupsORlogic = new List<AndGatedMeasurementGroup>();
        }
        ReplaceObservableCollection(Rules, newRules.Where(r => r != null).Select(r => new VM_MeasurementRule(r, this)));
        _parent?.Logger?.LogMessage("BodyTypeProfile: loaded " + Rules.Count
            + " rule(s) into profile '" + Name + "' from " + path);
    }

    /// <summary>Applies a Rules patch in place: deletes first (skipping any Id that's also in
    /// the add/edit set so the add wins), then adds-or-replaces by Id. Replacement preserves the
    /// row's original index. Calls <see cref="RebuildRuleTree"/> + <see cref="RefreshFilteredRules"/>
    /// once at the end (batch path) rather than per mutation so the tree doesn't churn N times.</summary>
    private void ApplyRulesPatch(List<MeasurementRule> incoming, List<string> deleteIds)
    {
        if (incoming == null) incoming = new List<MeasurementRule>();
        if (deleteIds == null) deleteIds = new List<string>();
        var incomingIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in incoming)
        {
            if (r == null) continue;
            if (!string.IsNullOrWhiteSpace(r.Id)) incomingIds.Add(r.Id);
        }
        var byId = new Dictionary<string, VM_MeasurementRule>(StringComparer.Ordinal);
        foreach (var vm in Rules)
        {
            if (vm == null) continue;
            if (string.IsNullOrEmpty(vm.Id)) continue;
            if (!byId.ContainsKey(vm.Id)) byId[vm.Id] = vm;
        }
        // Delete first: drop rules whose Id appears in deleteIds AND not in incomingIds (a
        // patch that both re-asserts and deletes the same Id resolves to "keep the add/edit").
        int actuallyDeleted = 0, skippedUnknown = 0, skippedOverridden = 0;
        foreach (var rawId in deleteIds)
        {
            if (string.IsNullOrWhiteSpace(rawId)) continue;
            if (incomingIds.Contains(rawId)) { skippedOverridden++; continue; }
            if (!byId.TryGetValue(rawId, out var target)) { skippedUnknown++; continue; }
            Rules.Remove(target);
            byId.Remove(rawId);
            actuallyDeleted++;
        }
        if (skippedUnknown > 0 || skippedOverridden > 0)
        {
            _parent?.Logger?.LogMessage("BodyTypeProfile: rules-patch delete-list skips: "
                + skippedUnknown + " unknown Id(s), " + skippedOverridden
                + " Id(s) also present in the add/edit set");
        }
        // Then add-or-replace.
        foreach (var model in incoming)
        {
            if (model == null) continue;
            var id = model.Id;
            if (!string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var existing))
            {
                int idx = Rules.IndexOf(existing);
                if (idx < 0)
                {
                    Rules.Add(new VM_MeasurementRule(model, this));
                    continue;
                }
                Rules.RemoveAt(idx);
                var replacement = new VM_MeasurementRule(model, this);
                Rules.Insert(idx, replacement);
                byId[id] = replacement;
            }
            else
            {
                var added = new VM_MeasurementRule(model, this);
                Rules.Add(added);
                if (!string.IsNullOrEmpty(id)) byId[id] = added;
            }
        }
        RebuildRuleTree();
        RefreshFilteredRules();
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

    /// <summary>Returns the first <c>{typed}_N</c> (N starting at 2) that doesn't collide
    /// with any name in <paramref name="taken"/>, comparing case-sensitively to match the
    /// downstream lookup dictionaries. Used by the edit-time collision dialog's "Rename"
    /// branch — distinct from <see cref="NextDefaultName"/> which generates fresh sequential
    /// names from a bare prefix. Starts at 2 because the typed name itself is the implied
    /// "_1"; the suffix gives the user's intended name a numeric sibling rather than
    /// shifting them into a generic prefix sequence.</summary>
    public static string GenerateUniqueNameFrom(string typed, IEnumerable<string> taken)
    {
        var typedTrimmed = (typed ?? "").Trim();
        if (string.IsNullOrEmpty(typedTrimmed)) typedTrimmed = "Name";
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (taken != null)
        {
            foreach (var t in taken)
            {
                if (!string.IsNullOrEmpty(t)) set.Add(t.Trim());
            }
        }
        for (int i = 2; i < 1000; i++)
        {
            var candidate = typedTrimmed + "_" + i;
            if (!set.Contains(candidate)) return candidate;
        }
        return typedTrimmed + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
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
    private void OnScanInvalidatingChange(object? sender, PropertyChangedEventArgs e)
    {
        MarkScanResultsStale();
        // Piggyback ref-validity recompute on the existing per-condition subscription. The
        // sender's MeasurementName change is the only condition-side edit that affects
        // validity; gating on the property name keeps the per-keystroke threshold/value
        // edits from re-walking the rule tree. IsMeasurementRefValid is written by the
        // recompute itself — skip to avoid recursion.
        if (sender is VM_MeasurementCondition
            && e.PropertyName != nameof(VM_MeasurementCondition.IsMeasurementRefValid)
            && (e.PropertyName == nameof(VM_MeasurementCondition.MeasurementName)
                || string.IsNullOrEmpty(e.PropertyName)))
        {
            RecomputeConditionRefValidity();
        }
    }

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
    /// invalidate the measurements cache.
    /// <para>
    /// Skips PropertyChanged events that <see cref="RefreshMeasurementValues"/> and the
    /// duplicate / ref-validity recomputes write back into the row themselves:
    /// <see cref="VM_MeasurementDefinition.LiveValue"/> updates on every preview refresh
    /// (clicking a row in Match Presets, navigating between weight slots, applying a new
    /// BodySlide preset), and <see cref="VM_MeasurementDefinition.HasDuplicateName"/> /
    /// <see cref="VM_MeasurementDefinition.IsRefAValid"/> through <c>IsRefDValid</c> get
    /// rewritten by the editor's own recompute methods. Reacting to those would set the
    /// scan-cache-stale flag immediately after a fresh scan, surfacing the spurious
    /// "results stale — re-scan recommended" badge on every row click.
    /// </para>
    /// <para>
    /// Mirrors the exclusion list in <see cref="OnMeasurementRowPropertyChanged"/>; keep
    /// the two in sync if new derived-from-recompute properties are added.
    /// </para></summary>
    private void OnMeasurementCacheInvalidatingChange(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM_MeasurementDefinition.LiveValue)) return;
        if (e.PropertyName == nameof(VM_MeasurementDefinition.HasDuplicateName)) return;
        if (e.PropertyName == nameof(VM_MeasurementDefinition.IsRefAValid)) return;
        if (e.PropertyName == nameof(VM_MeasurementDefinition.IsRefBValid)) return;
        if (e.PropertyName == nameof(VM_MeasurementDefinition.IsRefCValid)) return;
        if (e.PropertyName == nameof(VM_MeasurementDefinition.IsRefDValid)) return;
        MarkMeasurementCacheStale();
    }

    /// <summary>Marks both the measurements cache and the derived descriptor list stale.
    /// Used for KeyVertices and MeasurementDefinition changes — the underlying numbers
    /// will differ, so any cached entry could be wrong.</summary>
    private void MarkMeasurementCacheStale()
    {
        MeasurementCacheStale = true;
        MarkScanResultsStale();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Rules-tab tree state. RuleTreeCategories mirrors AvailableDescriptors
    //  (TemplateDescriptors) grouped by Category; SelectedRuleTreeNode drives
    //  FilteredRules on the right pane. Tree rebuilds on Rules CollectionChanged
    //  or on a per-rule Descriptor PropertyChanged (Category or Value), and on
    //  AvailableDescriptors CollectionChanged.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Drives <see cref="RefreshFilteredRules"/> when the selection changes.
    /// Fody emits PropertyChanged for SelectedRuleTreeNode automatically; this is the
    /// hand-written reaction.</summary>
    private void OnSelectedRuleTreeNodeChanged() => RefreshFilteredRules();

    private void HookRuleForTreeRebuild(VM_MeasurementRule rule)
    {
        if (rule == null) return;
        rule.PropertyChanged += OnRuleDescriptorChanged;
    }

    private void UnhookRuleForTreeRebuild(VM_MeasurementRule rule)
    {
        if (rule == null) return;
        rule.PropertyChanged -= OnRuleDescriptorChanged;
    }

    private void OnRuleDescriptorChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Only DescriptorCategory / DescriptorValue changes reshape the tree (the rule moves
        // to a different branch). Everything else (IsDraft, predicate edits, etc.) leaves
        // the tree structure alone, so skip the rebuild for noise reduction.
        if (e.PropertyName != nameof(VM_MeasurementRule.DescriptorCategory)
            && e.PropertyName != nameof(VM_MeasurementRule.DescriptorValue)) return;
        RebuildRuleTree();
        RefreshFilteredRules();
    }

    /// <summary>Rebuilds <see cref="RuleTreeCategories"/> from
    /// <c>_parent.AvailableDescriptors</c> (TemplateDescriptors) and recomputes per-node
    /// rule counts. Preserves the currently-selected node by (Category, Value) match where
    /// possible so the right pane doesn't jump when an unrelated rule is added/removed.</summary>
    public void RebuildRuleTree()
    {
        // Remember selection so we can restore by (Cat, Val) after rebuild.
        string selCat = "", selVal = "";
        bool selWasValueLevel = false;
        switch (SelectedRuleTreeNode)
        {
            case VM_RuleTreeValueNode v:
                selCat = v.Category; selVal = v.Value; selWasValueLevel = true; break;
            case VM_RuleTreeCategoryNode c:
                selCat = c.Category; break;
        }

        // Count rules per (Cat, Val). Includes Descriptors that aren't in TemplateDescriptors
        // (orphan rules) so we can surface them via TotalRuleCount; the tree itself still only
        // shows TemplateDescriptor-backed nodes.
        var perPairCount = new Dictionary<(string Cat, string Val), int>();
        foreach (var r in Rules)
        {
            if (r == null) continue;
            var key = (r.DescriptorCategory ?? "", r.DescriptorValue ?? "");
            perPairCount.TryGetValue(key, out int cur);
            perPairCount[key] = cur + 1;
        }

        // Group AvailableDescriptors by category, preserving alphabetical order.
        var byCategory = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var d in _parent.AvailableDescriptors)
        {
            if (d == null || string.IsNullOrEmpty(d.Category) || string.IsNullOrEmpty(d.Value)) continue;
            if (!byCategory.TryGetValue(d.Category, out var values))
            {
                values = new List<string>();
                byCategory[d.Category] = values;
            }
            if (!values.Contains(d.Value, StringComparer.Ordinal)) values.Add(d.Value);
        }

        RuleTreeCategories.Clear();
        VM_RuleTreeCategoryNode? restoredCategoryNode = null;
        VM_RuleTreeValueNode? restoredValueNode = null;

        foreach (var kv in byCategory)
        {
            var catNode = new VM_RuleTreeCategoryNode(this, kv.Key);
            int catTotal = 0;
            foreach (var val in kv.Value.OrderBy(v => v, StringComparer.Ordinal))
            {
                int count = perPairCount.TryGetValue((kv.Key, val), out var c) ? c : 0;
                catTotal += count;
                var valNode = new VM_RuleTreeValueNode(this, kv.Key, val) { RuleCount = count };
                catNode.Values.Add(valNode);
                if (selWasValueLevel
                    && string.Equals(selCat, kv.Key, StringComparison.Ordinal)
                    && string.Equals(selVal, val, StringComparison.Ordinal))
                {
                    restoredValueNode = valNode;
                }
            }
            catNode.RuleCount = catTotal;
            RuleTreeCategories.Add(catNode);
            if (!selWasValueLevel && string.Equals(selCat, kv.Key, StringComparison.Ordinal))
            {
                restoredCategoryNode = catNode;
            }
        }

        // Restore selection. Setting SelectedRuleTreeNode fires OnSelectedRuleTreeNodeChanged,
        // which refreshes FilteredRules — caller doesn't need to invoke it again.
        if (restoredValueNode != null) SelectedRuleTreeNode = restoredValueNode;
        else if (restoredCategoryNode != null) SelectedRuleTreeNode = restoredCategoryNode;
        // If the selected (Cat, Val) was removed (e.g. TemplateDescriptor deleted), drop the
        // selection rather than pointing at a stale instance.
        else if (SelectedRuleTreeNode != null) SelectedRuleTreeNode = null;
    }

    /// <summary>Refreshes <see cref="FilteredRules"/> based on the current
    /// <see cref="SelectedRuleTreeNode"/>. Category-level selection includes every rule
    /// whose Descriptor.Category matches; Value-level selection narrows to that
    /// (Category, Value). Null selection clears the list.</summary>
    public void RefreshFilteredRules()
    {
        FilteredRules.Clear();
        switch (SelectedRuleTreeNode)
        {
            case VM_RuleTreeValueNode v:
                foreach (var r in Rules)
                {
                    if (r == null) continue;
                    if (string.Equals(r.DescriptorCategory, v.Category, StringComparison.Ordinal)
                        && string.Equals(r.DescriptorValue, v.Value, StringComparison.Ordinal))
                    {
                        FilteredRules.Add(r);
                    }
                }
                break;
            case VM_RuleTreeCategoryNode c:
                foreach (var r in Rules)
                {
                    if (r == null) continue;
                    if (string.Equals(r.DescriptorCategory, c.Category, StringComparison.Ordinal))
                    {
                        FilteredRules.Add(r);
                    }
                }
                break;
        }
    }

    private bool CanAddDescriptor()
    {
        var cat = NewCategoryInput?.Trim();
        var val = NewValueInput?.Trim();
        return !string.IsNullOrEmpty(cat) && !string.IsNullOrEmpty(val);
    }

    private void AddDescriptorFromInputs()
    {
        var cat = NewCategoryInput?.Trim() ?? "";
        var val = NewValueInput?.Trim() ?? "";
        if (string.IsNullOrEmpty(cat) || string.IsNullOrEmpty(val)) return;

        // Bail if (cat, val) already exists; otherwise we'd add a duplicate that confuses the
        // tree's per-pair count tracking.
        foreach (var d in _parent.AvailableDescriptors)
        {
            if (d == null) continue;
            if (string.Equals(d.Category, cat, StringComparison.Ordinal)
                && string.Equals(d.Value, val, StringComparison.Ordinal))
            {
                return;
            }
        }

        _parent.AvailableDescriptors.Add(new BodyShapeDescriptor.LabelSignature { Category = cat, Value = val });
        // CollectionChanged on AvailableDescriptors triggers RebuildRuleTree automatically.
        NewCategoryInput = "";
        NewValueInput = "";
    }

    private bool CanDeleteSelectedTreeNode()
    {
        // Block delete if any existing rule still produces a descriptor under the node — the
        // user would silently orphan those rules. They can move the rule's Descriptor to a
        // different (Cat, Val) first, then retry the delete.
        switch (SelectedRuleTreeNode)
        {
            case VM_RuleTreeValueNode v:
                foreach (var r in Rules)
                {
                    if (r == null) continue;
                    if (string.Equals(r.DescriptorCategory, v.Category, StringComparison.Ordinal)
                        && string.Equals(r.DescriptorValue, v.Value, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
                return true;
            case VM_RuleTreeCategoryNode c:
                foreach (var r in Rules)
                {
                    if (r == null) continue;
                    if (string.Equals(r.DescriptorCategory, c.Category, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
                return true;
            default:
                return false;
        }
    }

    private void DeleteSelectedTreeNode()
    {
        switch (SelectedRuleTreeNode)
        {
            case VM_RuleTreeValueNode v:
                RemoveDescriptor(v.Category, v.Value);
                break;
            case VM_RuleTreeCategoryNode c:
                // Remove every Value under this Category. Iterate via snapshot so the
                // CollectionChanged callback doesn't trip the foreach.
                var pairs = new List<(string Cat, string Val)>();
                foreach (var d in _parent.AvailableDescriptors)
                {
                    if (d != null
                        && string.Equals(d.Category, c.Category, StringComparison.Ordinal)
                        && !string.IsNullOrEmpty(d.Value))
                    {
                        pairs.Add((d.Category, d.Value));
                    }
                }
                foreach (var (cat, val) in pairs) RemoveDescriptor(cat, val);
                break;
        }
    }

    private void RemoveDescriptor(string category, string value)
    {
        for (int i = _parent.AvailableDescriptors.Count - 1; i >= 0; i--)
        {
            var d = _parent.AvailableDescriptors[i];
            if (d == null) continue;
            if (string.Equals(d.Category, category, StringComparison.Ordinal)
                && string.Equals(d.Value, value, StringComparison.Ordinal))
            {
                _parent.AvailableDescriptors.RemoveAt(i);
            }
        }
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

        // Mirror BodySlideMeasurementEvaluator.Evaluate: filter eligible rules (by gender +
        // draft status + valid descriptor), topo-sort by descriptor dependencies so aggregator
        // rules see the matched set, then iterate. Gender is taken from the cache key — every
        // cached entry was scanned with a known (PresetLabel, Gender, Weight) coordinate.
        var eligible = new List<MeasurementRule>();
        foreach (var rule in profileModel.Rules)
        {
            if (rule == null) continue;
            if (rule.IsDraft && !includeDrafts) continue;
            if (rule.Descriptor == null
                || string.IsNullOrEmpty(rule.Descriptor.Category)
                || string.IsNullOrEmpty(rule.Descriptor.Value)) continue;
            if (!BodySlideMeasurementEvaluator.RuleGenderMatches(rule.Gender, key.Gender)) continue;
            eligible.Add(rule);
        }
        var ordered = RuleDependencyOrder.SortByDescriptorDependencies(eligible, out _);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var matched = new HashSet<(string Category, string Value)>();
        foreach (var rule in ordered)
        {
            if (!MeasurementMath.RuleMatches(rule, floats, matched)) continue;

            string k = rule.Descriptor.Category + "::" + rule.Descriptor.Value;
            if (!seen.Add(k)) continue;

            matched.Add((rule.Descriptor.Category, rule.Descriptor.Value));
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

/// <summary>Per-row diagnostic for whether a <see cref="VM_NamedKeyVertex"/> can be
/// resolved against the currently-loaded mesh in <see cref="VM_BodyTypeProfile.ActiveViewer"/>.
/// Driven by <see cref="VM_BodyTypeProfile.RefreshMeasurementValues"/>; never persisted.
/// Surfaces in the KV grid as a small badge and feeds the top-level unresolved-KV banner.</summary>
public enum KeyVertexResolutionState
{
    /// <summary>No active viewer yet, or no evaluation has run. Treated as "not yet a problem" by the UI.</summary>
    Unknown = 0,
    /// <summary>ShapeName matched a loaded mesh and VertexIndex is in range — overlays will render.</summary>
    Resolved = 1,
    /// <summary>No loaded mesh has a shape with this <see cref="VM_NamedKeyVertex.ShapeName"/>.
    /// Almost always means the profile was authored against a different body (e.g. duplicated
    /// from CBBE then used against 3BA before Capture-from-Active-Viewer remapped names).</summary>
    ShapeNotLoaded = 2,
    /// <summary>ShapeName matched but <see cref="VM_NamedKeyVertex.VertexIndex"/> &gt;= mesh vertex count.</summary>
    IndexOutOfRange = 3,
    /// <summary>Capture-from-Active-Viewer rewrote this Explicit-strategy KV's ShapeName onto a
    /// new body. The old VertexIndex is no longer trustworthy — the user must re-pick on the
    /// new mesh before evaluation can use it. BoundingBox-strategy KVs self-heal and never
    /// receive this state.</summary>
    NeedsRepick = 4,
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

    /// <summary>True when at least one other row in the parent profile's <see cref="VM_BodyTypeProfile.KeyVertices"/>
    /// collection has the same <see cref="Name"/> (Ordinal, trimmed). Driven by
    /// <see cref="VM_BodyTypeProfile.RecomputeDuplicateKeyVertexNames"/>; the row VM never
    /// computes this itself. Surfaces in the editor as a red highlight on the Name cell.</summary>
    public bool HasDuplicateName { get; set; }

    /// <summary>Whether this KV can be resolved on the active mesh. See
    /// <see cref="KeyVertexResolutionState"/> for the states. Recomputed by
    /// <see cref="VM_BodyTypeProfile.RefreshMeasurementValues"/>; never written by the row VM.</summary>
    public KeyVertexResolutionState ResolutionState { get; set; } = KeyVertexResolutionState.Unknown;

    /// <summary>Transient (non-persisted) sticky flag set by Capture-from-Active-Viewer when
    /// it rewrote this Explicit-strategy KV's ShapeName onto a new body. The stored VertexIndex
    /// almost certainly points at unrelated anatomy on the new topology, so we keep the warning
    /// visible until the user either re-picks (which clears via the VertexIndex change handler)
    /// or switches Strategy to BoundingBox (which clears via the Strategy change handler).</summary>
    public bool NeedsRepick { get; set; }

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

        // Opens a separate window plotting this measurement's distribution across every
        // (preset, weight) entry in the profile's MeasurementCache. The execute path is
        // fire-and-forget so the WPF dispatcher returns immediately; the actual await on
        // a possibly-running scan happens inside the profile method. canExecute requires
        // a non-empty Name (the cache is keyed by name, so an empty name has no data).
        OpenHistogramCommand = new RelayCommand(
            canExecute: _ => !string.IsNullOrEmpty(Name),
            execute: _ => _ = _parent.OpenMeasurementHistogramAsync(this));
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

    /// <summary>True when at least one other row in the parent profile's <see cref="VM_BodyTypeProfile.Measurements"/>
    /// collection has the same <see cref="Name"/> (Ordinal, trimmed). Driven by
    /// <see cref="VM_BodyTypeProfile.RecomputeDuplicateMeasurementNames"/>. Surfaces in the
    /// editor as a red highlight on the Name cell.</summary>
    public bool HasDuplicateName { get; set; }

    /// <summary>True when this row's <see cref="VertexRefA"/> (etc.) names an existing
    /// <see cref="VM_NamedKeyVertex"/> in the parent profile, or is empty. False when the
    /// reference is a non-empty string that doesn't match any current key vertex — typically
    /// because the user deleted the referenced KV or typed a stale name. Set by
    /// <see cref="VM_BodyTypeProfile.RecomputeMeasurementRefValidity"/>; defaults to true so
    /// fresh / loading rows don't briefly flash red before the first recompute. Surfaces in
    /// the Measurements grid as a per-cell red wash on the invalid ref column. Cells C and D
    /// only flag invalid when <see cref="ShowSecondPair"/> is true — for non-Ratio kinds the
    /// fields are inert, so a stale value there doesn't represent a broken evaluation.</summary>
    public bool IsRefAValid { get; set; } = true;
    public bool IsRefBValid { get; set; } = true;
    public bool IsRefCValid { get; set; } = true;
    public bool IsRefDValid { get; set; } = true;

    public RelayCommand DeleteCommand { get; }

    /// <summary>Per-row command that opens a <see cref="Window_MeasurementHistogram"/>
    /// showing the distribution of this measurement's cached values across the profile's
    /// (preset, weight) entries. Drives a scan via the parent profile/editor first when
    /// the cache is stale, mirroring the gating used elsewhere for cache-dependent reads.</summary>
    public RelayCommand OpenHistogramCommand { get; }

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
        Gender = source.Gender;

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

    /// <summary>Stable identifier for this rule (Guid string). Set on construction from the
    /// source model's Id (auto-generated if missing) and never mutated; round-trips through
    /// <see cref="DumpToModel"/> so the same rule keeps the same Id across save/load. Used as
    /// the patch-import match key — wholesale-replace doesn't need it, but partial patches
    /// (<c>IsPatch: true</c>) identify rules by Id to add/replace/delete them in place.</summary>
    public string Id => _id;

    public string DescriptorCategory { get; set; }
    public string DescriptorValue { get; set; }
    public bool IsDraft { get; set; }

    /// <summary>Per-rule gender filter (Either / Male / Female). Bound to the Gender ComboBox
    /// in the Rules tab. Round-trips through <see cref="DumpToModel"/> + ctor so it persists
    /// across saves and JSON load/save. Default Either preserves legacy behavior.</summary>
    public RuleGender Gender { get; set; } = RuleGender.Either;

    /// <summary>Bound to the Gender ComboBox.ItemsSource on the Rules tab. Static — the enum
    /// values are fixed at compile time.</summary>
    public static IReadOnlyList<RuleGender> AvailableGenders { get; } = new[]
    {
        RuleGender.Either, RuleGender.Male, RuleGender.Female,
    };

    public ObservableCollection<VM_AndGatedMeasurementGroup> Groups { get; } = new();

    public RelayCommand AddGroup { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand PromoteFromDraft { get; }

    public ObservableCollection<BodyShapeDescriptor.LabelSignature> AvailableDescriptors => _parent.AvailableDescriptors;

    /// <summary>Owning profile — exposed so child conditions can navigate up for cycle
    /// detection and safe-ref enumeration without re-walking the VM tree.</summary>
    public VM_BodyTypeProfile ParentProfile => _parent;

    /// <summary>Bound to the Descriptor Category ComboBox.ItemsSource on the Rules tab.</summary>
    public IEnumerable<string> AvailableDescriptorCategories => _parent.AvailableDescriptorCategories;

    /// <summary>Bound to the Descriptor Value ComboBox.ItemsSource on the Rules tab. Filters
    /// by the rule's currently-selected <see cref="DescriptorCategory"/>; Fody re-evaluates
    /// this property when DescriptorCategory raises PropertyChanged, so the Value dropdown
    /// updates automatically on Category change.</summary>
    public IEnumerable<string> AvailableDescriptorValues => _parent.AvailableDescriptorValuesFor(DescriptorCategory);

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
            Gender = Gender,
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

    /// <summary>Owning rule. Exposed so child conditions can navigate up to the profile
    /// (e.g. to compute cycle-safe descriptor-ref candidates).</summary>
    public VM_MeasurementRule ParentRule => _parent;

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
        Kind = source.Kind;
        MeasurementName = source.MeasurementName ?? "";
        Comparator = source.Comparator;
        Value = source.Value;
        RefCategory = source.RefCategory ?? "";
        RefValue = source.RefValue ?? "";
        Negate = source.Negate;

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => _parent.RemoveCondition(this));
    }

    /// <summary>Selects the row's display + persistence shape. Toggling this between
    /// <see cref="MeasurementConditionKind.Measurement"/> and
    /// <see cref="MeasurementConditionKind.DescriptorRef"/> swaps the visible XAML controls
    /// (via the <see cref="IsMeasurementKind"/> / <see cref="IsDescriptorRefKind"/>
    /// triggers); the underlying fields for the inactive kind stay in memory so toggling
    /// back doesn't lose the user's prior values.</summary>
    public MeasurementConditionKind Kind { get; set; }

    // --- Measurement-kind fields ---
    public string MeasurementName { get; set; }
    public MeasurementComparator Comparator { get; set; }
    public float Value { get; set; }

    // --- DescriptorRef-kind fields ---
    public string RefCategory { get; set; }
    public string RefValue { get; set; }
    public bool Negate { get; set; }

    public RelayCommand DeleteCommand { get; }

    /// <summary>True when <see cref="MeasurementName"/> is empty or matches an existing
    /// <see cref="VM_MeasurementDefinition"/> in the parent profile. False when it names a
    /// measurement that no longer exists (typically because the user deleted it after this
    /// condition was authored). Set by <see cref="VM_BodyTypeProfile.RecomputeConditionRefValidity"/>;
    /// defaults to true so freshly-loaded conditions don't flash red before the first
    /// recompute. Surfaces in the Rules tab as a red wash on the MeasurementName combo.</summary>
    public bool IsMeasurementRefValid { get; set; } = true;

    public IEnumerable<string> AvailableMeasurementNames => _parent.AvailableMeasurementNames;

    /// <summary>Drives XAML visibility for the Measurement-kind controls.</summary>
    public bool IsMeasurementKind => Kind == MeasurementConditionKind.Measurement;

    /// <summary>Drives XAML visibility for the DescriptorRef-kind controls.</summary>
    public bool IsDescriptorRefKind => Kind == MeasurementConditionKind.DescriptorRef;

    /// <summary>Available enum values for the Kind ComboBox in the Rules tab.</summary>
    public static IReadOnlyList<MeasurementConditionKind> KindOptions { get; } = new[]
    {
        MeasurementConditionKind.Measurement,
        MeasurementConditionKind.DescriptorRef,
    };

    /// <summary>Cycle-filtered list of descriptor Categories the user can reference from
    /// this DescriptorRef condition. Computed by enumerating
    /// <see cref="VM_BodyTypeProfile.GetSafeDescriptorRefsForCondition"/> with this row's
    /// (rule, group, condition) coordinates so the editing condition's existing ref is
    /// excluded from the cycle check. Snapshotted at access time; the dropdown picks up
    /// changes the next time the user opens it.</summary>
    public IEnumerable<string> AvailableRefCategories
    {
        get
        {
            var safe = ResolveSafeRefs();
            return safe
                .Select(t => t.Category)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.Ordinal);
        }
    }

    /// <summary>Cycle-filtered Values for the currently-selected <see cref="RefCategory"/>.
    /// Fody re-evaluates this when <see cref="RefCategory"/> changes, so the Value dropdown
    /// refilters automatically on Category change.</summary>
    public IEnumerable<string> AvailableRefValues
    {
        get
        {
            if (string.IsNullOrEmpty(RefCategory)) return Array.Empty<string>();
            var safe = ResolveSafeRefs();
            return safe
                .Where(t => string.Equals(t.Category, RefCategory, StringComparison.Ordinal))
                .Select(t => t.Value)
                .OrderBy(v => v, StringComparer.Ordinal);
        }
    }

    private IEnumerable<(string Category, string Value)> ResolveSafeRefs()
    {
        var rule = _parent.ParentRule;
        var profile = rule.ParentProfile;
        int ruleIdx = profile.IndexOfRule(rule);
        int groupIdx = rule.Groups.IndexOf(_parent);
        int condIdx = _parent.Conditions.IndexOf(this);
        return profile.GetSafeDescriptorRefsForCondition(ruleIdx, groupIdx, condIdx);
    }

    public MeasurementCondition DumpToModel() => new()
    {
        Kind = Kind,
        MeasurementName = MeasurementName?.Trim() ?? "",
        Comparator = Comparator,
        Value = Value,
        RefCategory = RefCategory?.Trim() ?? "",
        RefValue = RefValue?.Trim() ?? "",
        Negate = Negate,
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

    /// <summary>Margin-based "how strongly does this preset match the selected descriptor's
    /// rule" score, populated by <see cref="VM_BodyTypeProfileEditor.RefreshMatchingPresets"/>
    /// when sorting is active. Null when no descriptor is selected, when multiple are selected,
    /// when sorting is off, or when no rule for the selected descriptor exists yet. Higher =
    /// matches more deeply past the threshold. Display in the row template via
    /// <see cref="ScoreDisplay"/> rather than reading this directly — null vs zero is a
    /// meaningful distinction the formatter handles.</summary>
    public double? Score { get; set; }

    /// <summary>Pre-formatted score string for the row template. Empty when <see cref="Score"/>
    /// is null. Unit suffix differs by mode so the user can tell at a glance which metric
    /// produced the number (σ vs %). Always written together with <see cref="Score"/>.</summary>
    public string ScoreDisplay { get; set; } = "";

    /// <summary>Multi-line tooltip showing this row's margin score against every rule whose
    /// Descriptor.Category matches the currently-selected descriptor's Category — i.e., the
    /// selected value plus its siblings. Lets the user see at a glance whether a row that
    /// barely matched the selected value is "almost Rectangle" or "almost Hourglass" without
    /// re-selecting each descriptor in turn. Null when scoring isn't active (so WPF suppresses
    /// the tooltip rather than rendering an empty box). Populated by
    /// <see cref="VM_BodyTypeProfileEditor.RefreshMatchingPresets"/> alongside
    /// <see cref="Score"/>.</summary>
    public string SiblingScoresTooltip { get; set; }
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

/// <summary>Match Presets list sorting mode. Active only when exactly one descriptor value
/// is selected in the filter — that's the rule we score against. With zero or 2+ selected,
/// any value other than <see cref="Off"/> is treated as Off for that refresh (no single rule
/// to project against, so falling back to the default sort is more useful than picking an
/// arbitrary one).</summary>
public enum MarginScoreMode
{
    /// <summary>Default (Gender, PresetLabel, Weight) sort — pre-feature behavior.</summary>
    Off = 0,

    /// <summary>Margin (value − threshold, sign-flipped for &lt; comparators) divided by the
    /// std-dev of that measurement across the cached preset population. Reads as "this row
    /// clears the rule by N standard deviations." Most diagnostic for spotting outliers
    /// when measurements have very different units; falls back to PercentOfThreshold's
    /// formula when std-dev is undefined (≤1 sample) or zero (every preset has the same
    /// value for that measurement).</summary>
    StdDevNormalized = 1,

    /// <summary>Margin divided by |threshold|, so the score reads as a fractional excess
    /// past the line: 0.30 means 30 % past the threshold. Doesn't need population statistics,
    /// so it's stable across partial scans. Falls back to raw margin when |threshold| &lt; 1e-6
    /// (a threshold of exactly zero) since the percentage would otherwise blow up.</summary>
    PercentOfThreshold = 2,

    /// <summary>Same σ-normalized scoring as <see cref="StdDevNormalized"/>, but the row is
    /// scored against the rule for <see cref="VM_BodyTypeProfileEditor.SimilarityTarget"/>
    /// (a sibling descriptor value picked from a second dropdown) rather than the
    /// filter-selected rule. Surfaces how close each filter-matched preset is to *also*
    /// matching a different value in the same category — useful for tuning Pear/Hourglass
    /// or Rectangle/Inverted-Triangle boundaries. Degrades to the default sort when no
    /// target is picked or the target has no rules.</summary>
    SimilarityToStdDevNormalized = 3,

    /// <summary>Same %-of-threshold scoring as <see cref="PercentOfThreshold"/>, scored
    /// against the <see cref="VM_BodyTypeProfileEditor.SimilarityTarget"/> rule instead of
    /// the filter's. Companion to <see cref="SimilarityToStdDevNormalized"/>.</summary>
    SimilarityToPercentOfThreshold = 4,

    /// <summary>Sorts each row by the cached value of the measurement picked in
    /// <see cref="VM_BodyTypeProfileEditor.SelectedMeasurementForSort"/> (largest first).
    /// Independent of the descriptor filter — every (preset, weight) that survives the
    /// filter is scored as long as its cache entry contains a value for the picked
    /// measurement. Rows whose cache entry is missing the measurement fall to the bottom
    /// (unscored). No rule projection is involved, so this works even when the picked
    /// measurement isn't referenced by any rule.</summary>
    MeasurementValue = 5,
}

/// <summary>Convenience predicate kept next to the enum so call sites don't have to
/// enumerate every Similarity* member when they want "is the user comparing against
/// a different descriptor?" semantics. Added separately to avoid leaking implementation
/// details (like the integer values) into other files.</summary>
internal static class MarginScoreModeExtensions
{
    public static bool IsSimilarity(this MarginScoreMode mode)
        => mode == MarginScoreMode.SimilarityToStdDevNormalized
        || mode == MarginScoreMode.SimilarityToPercentOfThreshold;

    /// <summary>Underlying normalization the Similarity variant reuses. Returns
    /// <see cref="MarginScoreMode.Off"/> when called on a non-Similarity mode.</summary>
    public static MarginScoreMode SimilarityBaseMode(this MarginScoreMode mode) => mode switch
    {
        MarginScoreMode.SimilarityToStdDevNormalized => MarginScoreMode.StdDevNormalized,
        MarginScoreMode.SimilarityToPercentOfThreshold => MarginScoreMode.PercentOfThreshold,
        _ => MarginScoreMode.Off,
    };
}

/// <summary>(value, label) pair for the score-mode ComboBox so SelectedValuePath/DisplayMemberPath
/// can keep the enum out of XAML. Owned by <see cref="VM_BodyTypeProfileEditor.ScoreSortModeOptions"/>.</summary>
public sealed class MarginScoreOption
{
    public MarginScoreOption(MarginScoreMode value, string label)
    {
        Value = value;
        Label = label;
    }
    public MarginScoreMode Value { get; }
    public string Label { get; }
}

/// <summary>Top-level node in the Rules-tab tree. One per distinct Descriptor.Category
/// in <see cref="VM_BodyTypeProfile"/>'s <c>_parent.AvailableDescriptors</c>. Holds
/// child <see cref="VM_RuleTreeValueNode"/> instances and a rule-count rollup used in
/// the tree label.</summary>
public class VM_RuleTreeCategoryNode : VM
{
    private readonly VM_BodyTypeProfile _parent;

    public VM_RuleTreeCategoryNode(VM_BodyTypeProfile parent, string category)
    {
        _parent = parent;
        Category = category;
    }

    public string Category { get; }
    public ObservableCollection<VM_RuleTreeValueNode> Values { get; } = new();

    /// <summary>Sum of <see cref="VM_RuleTreeValueNode.RuleCount"/> across this Category's
    /// values. Set by <see cref="VM_BodyTypeProfile.RebuildRuleTree"/>.</summary>
    public int RuleCount { get; set; }

    public string DisplayLabel => RuleCount > 0 ? $"{Category} ({RuleCount})" : Category;

    /// <summary>True when the TreeView expanded this node. Bound TwoWay so the editor can
    /// snap expansion state programmatically when needed (e.g. after add-descriptor it auto-
    /// expands the affected branch).</summary>
    public bool IsExpanded { get; set; } = true;
}

/// <summary>Leaf node in the Rules-tab tree. Represents one TemplateDescriptor (Category,
/// Value) pair. Selecting this node filters <see cref="VM_BodyTypeProfile.FilteredRules"/>
/// to rules whose Descriptor matches exactly.</summary>
public class VM_RuleTreeValueNode : VM
{
    private readonly VM_BodyTypeProfile _parent;

    public VM_RuleTreeValueNode(VM_BodyTypeProfile parent, string category, string value)
    {
        _parent = parent;
        Category = category;
        Value = value;
    }

    public string Category { get; }
    public string Value { get; }

    /// <summary>Number of rules in the parent profile whose Descriptor matches this
    /// (Category, Value) exactly. Recomputed by <see cref="VM_BodyTypeProfile.RebuildRuleTree"/>
    /// — never edited directly here.</summary>
    public int RuleCount { get; set; }

    public string DisplayLabel => RuleCount > 0 ? $"{Value} ({RuleCount})" : Value;
}

