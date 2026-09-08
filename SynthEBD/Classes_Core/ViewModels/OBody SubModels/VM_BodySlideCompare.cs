using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// View model for <see cref="Window_BodySlideCompare"/>: two independent BodySlide preview
/// panes side by side, plus the cross-pane controls (lock camera, superimpose).
///
/// <para>Opened from the "Compare" button on any of the three OBody-menu CharacterViewer
/// hosts (<see cref="VM_BodySlideSetting"/>, <see cref="VM_BodyTypeProfileEditor"/>,
/// <see cref="VM_SliderAnnotatorPreviewPanel"/>) via <see cref="SeedFrom"/>, which primes
/// pane A with whatever that menu is currently previewing so the window opens on something
/// meaningful rather than blank.</para>
///
/// <para><b>Concurrency note.</b> This window makes SynthEBD a multi-viewer host: its two
/// panes plus the still-live embedded viewer behind it are three GL contexts at once. That
/// is only safe because <c>UC_CharacterViewer</c> installs a
/// <see cref="GlControlPinningMarshaller"/> per control — see Part 5 of
/// <c>RENDERING_PIPELINE.md</c>. Do not host concurrent viewers without it.</para>
/// </summary>
public class VM_BodySlideCompare : VM
{
    private readonly Logger _logger;

    public delegate VM_BodySlideCompare Factory();

    public VM_BodySlideCompare(
        Logger logger,
        PatcherState patcherState,
        IEnvironmentStateProvider environmentProvider,
        Func<VM_CharacterViewer> characterViewerFactory,
        PreviewNpcResolver previewNpcResolver,
        Func<VM_SettingsOBody> oBodyVM)
    {
        _logger = logger;

        var bodySlideMenu = oBodyVM()?.BodySlidesUI;

        PaneA = new VM_BodySlideComparePane("A", logger, patcherState, environmentProvider,
            characterViewerFactory, previewNpcResolver, bodySlideMenu);
        PaneB = new VM_BodySlideComparePane("B", logger, patcherState, environmentProvider,
            characterViewerFactory, previewNpcResolver, bodySlideMenu);
        PaneA.DisposeWith(this);
        PaneB.DisposeWith(this);

        // Camera lock. Both directions are wired unconditionally and the handlers gate on
        // LockCamera, so ticking the box mid-session needs no re-subscription. OrbitCamera
        // raises ViewChanged from its property setters (not its mouse handlers), so a
        // programmatic reframe after an NPC load propagates too — which is what a locked
        // pair should do. CopyViewFrom suppresses the receiving camera's event, without
        // which the two would echo forever.
        PaneA.CharacterViewer.Camera.ViewChanged += () => MirrorCamera(PaneA, PaneB);
        PaneB.CharacterViewer.Camera.ViewChanged += () => MirrorCamera(PaneB, PaneA);

        // Ticking Lock mid-session should visibly snap the panes together rather than wait
        // for the next mouse move; A is treated as the authority.
        this.WhenAnyValue(x => x.LockCamera)
            .Where(locked => locked)
            .Subscribe(_ => MirrorCamera(PaneA, PaneB, force: true))
            .DisposeWith(this);

        // Superimpose state. Every input rebuilds the overlay: the toggle itself, the
        // fill style, the body-only scope, and — via the panes' own change signal — the
        // guest pane's preset / NPC / weight.
        this.WhenAnyValue(x => x.Superimpose, x => x.SuperimposeStyle, x => x.SuperimposeBodyOnly)
            .Subscribe(_ => RefreshSuperimpose())
            .DisposeWith(this);

        PaneB.PreviewInputsChanged += OnGuestPaneInputsChanged;
    }

    public VM_BodySlideComparePane PaneA { get; }
    public VM_BodySlideComparePane PaneB { get; }

    /// <summary>When on, orbiting/zooming/panning either pane drives the other to the same view.</summary>
    public bool LockCamera { get; set; } = true;

    /// <summary>
    /// When on, pane B's model is also drawn inside pane A's viewport, overlaid on pane A's
    /// own model at the same origin. Pane B keeps rendering its own view independently — the
    /// overlay is additive, not a takeover, so the user can still see B on its own.
    /// </summary>
    public bool Superimpose { get; set; } = false;

    /// <summary>How the superimposed (pane B) model is drawn inside pane A. See <see cref="GuestOverlayStyle"/>.</summary>
    public GuestOverlayStyle SuperimposeStyle { get; set; } = GuestOverlayStyle.Translucent;

    /// <summary>
    /// When on, only the body mesh of pane B is superimposed — head, hair and worn gear are
    /// left out. Usually what you want for shape comparison: two full models at the same
    /// origin interpenetrate at the head and read as noise. Independent of
    /// <see cref="SuperimposeStyle"/>, so all six combinations are reachable.
    /// </summary>
    public bool SuperimposeBodyOnly { get; set; } = true;

    /// <summary>Enum values for the fill-style picker, bound by the view.</summary>
    public IReadOnlyList<GuestOverlayStyle> SuperimposeStyles { get; } =
        Enum.GetValues<GuestOverlayStyle>();

    /// <summary>
    /// Primes pane A from the menu that opened the window, so Compare opens showing what the
    /// user was already looking at. Pane B is primed to the same NPC/weight but is left
    /// without a preset, so the first thing the user does is pick B's preset — the comparison
    /// the window exists for. Null/absent values are simply skipped.
    /// </summary>
    public void SeedFrom(Gender gender, int weight, FormKey previewNpc, BodySlideSetting? preset)
    {
        PaneA.Seed(gender, weight, previewNpc, preset);
        PaneB.Seed(gender, weight, previewNpc, preset: null);
    }

    private void MirrorCamera(VM_BodySlideComparePane source, VM_BodySlideComparePane target, bool force = false)
    {
        if (!force && !LockCamera) return;
        target.CharacterViewer.Camera.CopyViewFrom(source.CharacterViewer.Camera);
    }

    private void OnGuestPaneInputsChanged() => RefreshSuperimpose();

    /// <summary>
    /// Pushes the current superimpose state into pane A's viewer. Called on every input that
    /// can change what the overlay should show. Turning the toggle off (or pane B having
    /// nothing loaded) clears the guest scene rather than leaving a stale overlay behind.
    /// </summary>
    private void RefreshSuperimpose()
    {
        try
        {
            var host = PaneA.CharacterViewer;

            if (!Superimpose)
            {
                host.ClearGuestScene();
                return;
            }

            host.GuestStyle = SuperimposeStyle;
            host.GuestBodyOnly = SuperimposeBodyOnly;
            PaneB.ProjectOnto(host);
        }
        catch (Exception ex)
        {
            _logger?.LogError("VM_BodySlideCompare.RefreshSuperimpose failed: "
                + ExceptionLogger.GetExceptionStack(ex));
        }
    }

    /// <summary>Detaches the cross-pane wiring, then disposes both panes (and with them both
    /// GL scenes) via the base composite. Called from the window's Closed handler.</summary>
    public override void Dispose()
    {
        PaneB.PreviewInputsChanged -= OnGuestPaneInputsChanged;
        base.Dispose();
    }
}

/// <summary>
/// The opening state a menu hands to the Compare window: what that menu is previewing right
/// now. <see cref="Preset"/> may be null (nothing selected yet) and <see cref="PreviewNpc"/>
/// may be <see cref="FormKey.Null"/> (the menu is relying on the per-weight default table).
/// </summary>
public readonly record struct BodySlideCompareSeed(
    Gender Gender, int Weight, FormKey PreviewNpc, BodySlideSetting? Preset);

/// <summary>
/// Builds the "Compare" command shared by the three OBody-menu CharacterViewer hosts.
/// Factored out so each host contributes only its own seed — the window construction,
/// ownership and failure handling are identical everywhere and shouldn't be triplicated.
/// </summary>
public static class BodySlideCompareLauncher
{
    /// <summary>
    /// Returns a command that opens a fresh <see cref="Window_BodySlideCompare"/>, seeded by
    /// invoking <paramref name="seedProvider"/> at click time (not at wire-up time, so the
    /// seed reflects the menu's state when the user actually clicks).
    ///
    /// <para>The window is shown non-modally and owns its view model: nothing here keeps a
    /// reference, and the window's Closed handler disposes the VM along with both panes' GL
    /// scenes. Opening several Compare windows is therefore allowed and each is independent —
    /// though every open window is another pair of live GL contexts.</para>
    /// </summary>
    public static RelayCommand CreateCommand(
        Func<VM_BodySlideCompare> compareFactory,
        Func<BodySlideCompareSeed> seedProvider,
        Logger logger)
    {
        return new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                try
                {
                    var vm = compareFactory();
                    var seed = seedProvider();
                    vm.SeedFrom(seed.Gender, seed.Weight, seed.PreviewNpc, seed.Preset);

                    var window = new Window_BodySlideCompare { DataContext = vm };
                    // Owner keeps Compare above the main window and closes it with the app,
                    // instead of stranding an orphan window with two GL contexts.
                    window.Owner = System.Windows.Application.Current?.MainWindow;
                    window.Show();
                }
                catch (Exception ex)
                {
                    logger?.LogError("Failed to open the BodySlide Compare window: "
                        + ExceptionLogger.GetExceptionStack(ex));
                }
            });
    }
}

/// <summary>
/// One side of the Compare window: a CharacterViewer plus the inputs that drive it —
/// gender, a filterable BodySlide preset dropdown, an integer weight, and the preview NPC
/// (both a weight-filtered candidate list and a free-form picker).
///
/// <para><b>Weight ↔ NPC interlock.</b> The two inputs drive each other in opposite
/// directions and the guard fields below keep that from becoming a loop: changing
/// <see cref="Weight"/> re-scans <see cref="WeightFilteredNpcs"/>, which is what the NPC
/// picker offers; choosing an NPC snaps <see cref="Weight"/> to that NPC's own record weight.
/// The second direction is only observable for an NPC named from outside the filtered set
/// (the picker still accepts a typed or pasted FormKey) — anything picked from the list
/// already sits at the current weight, so the snap is a no-op there.</para>
/// </summary>
public class VM_BodySlideComparePane : VM
{
    private readonly Logger _logger;
    private readonly PatcherState _patcherState;
    private readonly PreviewNpcResolver _previewNpcResolver;
    private readonly VM_BodySlidesMenu? _bodySlideMenu;

    /// <summary>All presets for the current gender, before <see cref="PresetFilterText"/>.</summary>
    private readonly List<VM_BodySlidePlaceHolder> _allPresets = new();

    /// <summary>Guards overlapping <see cref="RefreshPreviewAsync"/> calls: only the newest
    /// generation applies its BodySlide, so rapid input changes can't land a stale preset
    /// over a newer one. Same pattern as VM_SliderAnnotatorPreviewPanel.</summary>
    private int _refreshGeneration;

    /// <summary>Non-zero while the weight↔NPC interlock is writing the other side of the
    /// pair, so the resulting PropertyChanged doesn't bounce back and undo the write.</summary>
    private int _interlockDepth;

    public VM_BodySlideComparePane(
        string paneLabel,
        Logger logger,
        PatcherState patcherState,
        IEnvironmentStateProvider environmentProvider,
        Func<VM_CharacterViewer> characterViewerFactory,
        PreviewNpcResolver previewNpcResolver,
        VM_BodySlidesMenu? bodySlideMenu)
    {
        PaneLabel = paneLabel;
        _logger = logger;
        _patcherState = patcherState;
        _previewNpcResolver = previewNpcResolver;
        _bodySlideMenu = bodySlideMenu;

        CharacterViewer = characterViewerFactory();
        CharacterViewer.Mode = ViewerMode.ReadOnly;
        // Lock the model scale exactly as the Body Type Profile editor and the annotator rail
        // do: this window exists to compare body SHAPES, so the preview NPC's record Height
        // would only add scale noise between NPC swaps — and worse here, where two panes with
        // different NPCs would render at different sizes and defeat the comparison outright.
        CharacterViewer.HeightOverride = 1.0f;
        CharacterViewer.DisposeWith(this);

        // The window's GL contexts are destroyed and rebuilt if WPF recreates the controls
        // (e.g. the window is minimized/restored on some drivers); re-issue the last preview
        // so the pane doesn't come back grey.
        CharacterViewer.GlContextReset += () =>
        {
            if (SelectedPreset != null || !PreviewNpc.IsNull) _ = RefreshPreviewAsync();
        };

        environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        // Weight/gender drive the NPC picker's candidate list directly — no button. The scan
        // walks every NPC record in the load order, so it is throttled: dragging the weight
        // spinner through a dozen values costs one scan, not a dozen. Fires once on subscribe
        // so a freshly-opened pane already offers the right NPCs.
        this.WhenAnyValue(x => x.Weight, x => x.PreviewGender)
            .Throttle(TimeSpan.FromMilliseconds(400), RxApp.MainThreadScheduler)
            .Subscribe(inputs => { _ = RefreshNpcCandidatesAsync(); })
            .DisposeWith(this);

        PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(PreviewGender):
                    RebuildPresetList();
                    RefreshUnlessSeeding();
                    break;

                case nameof(PresetFilterText):
                    RebuildFilteredPresets();
                    break;

                case nameof(SelectedPreset):
                    RefreshUnlessSeeding();
                    RaisePreviewInputsChanged();
                    break;

                case nameof(Weight):
                    RefreshUnlessSeeding();
                    RaisePreviewInputsChanged();
                    break;

                case nameof(PreviewNpc):
                    SnapWeightToNpc();
                    RefreshUnlessSeeding();
                    RaisePreviewInputsChanged();
                    break;
            }
        };

        RebuildPresetList();
    }

    /// <summary>"A" or "B" — shown in the pane header and used in log lines.</summary>
    public string PaneLabel { get; }

    public VM_CharacterViewer CharacterViewer { get; }

    /// <summary>
    /// Raised whenever an input that changes what this pane renders settles (preset, weight,
    /// NPC). <see cref="VM_BodySlideCompare"/> listens on pane B to keep a live superimpose
    /// overlay in sync with B's own view.
    /// </summary>
    public event Action? PreviewInputsChanged;

    public Gender PreviewGender { get; set; } = Gender.Female;

    public string PresetFilterText { get; set; } = "";

    public ObservableCollection<VM_BodySlidePlaceHolder> FilteredPresets { get; } = new();

    public VM_BodySlidePlaceHolder? SelectedPreset { get; set; }

    /// <summary>Preview weight, 0-100. Restricts <see cref="WeightFilteredNpcs"/> and drives the morph.</summary>
    public int Weight { get; set; } = 50;

    /// <summary>The NPC being previewed. Settable from the candidate list or the free-form
    /// picker; setting it snaps <see cref="Weight"/> to the NPC's own record weight.</summary>
    public FormKey PreviewNpc { get; set; } = FormKey.Null;

    /// <summary>
    /// The NPCs the preview-NPC picker offers: those eligible at <see cref="Weight"/> for
    /// <see cref="PreviewGender"/>. Bound to the picker's CandidateFormKeys, which narrows its
    /// suggestion list without preventing a typed/pasted FormKey from outside the set — that
    /// escape hatch is what still makes the NPC-to-weight half of the interlock reachable.
    /// </summary>
    public ObservableCollection<FormKey> WeightFilteredNpcs { get; } = new();

    /// <summary>True while the load-order scan behind <see cref="WeightFilteredNpcs"/> runs. Drives the picker's "searching" hint.</summary>
    public bool IsFindingNpcs { get; private set; }

    /// <summary>Scoped-types filter for the free-form NPC picker.</summary>
    public IEnumerable<Type> NPCPickerFormKeys { get; } = typeof(INpcGetter).AsEnumerable();

    /// <summary>Current environment link cache, bound by the NPC picker.</summary>
    public ILinkCache? lk { get; private set; }

    /// <summary>Gender values for the pane's gender dropdown.</summary>
    public IReadOnlyList<Gender> Genders { get; } = Enum.GetValues<Gender>();

    /// <summary>
    /// Applies the opening state handed down by <see cref="VM_BodySlideCompare.SeedFrom"/>.
    /// Writes the inputs under the interlock guard and refreshes once at the end, so seeding
    /// costs one load rather than one per property.
    /// </summary>
    public void Seed(Gender gender, int weight, FormKey previewNpc, BodySlideSetting? preset)
    {
        _interlockDepth++;
        try
        {
            PreviewGender = gender;
            Weight = Math.Clamp(weight, 0, 100);
            PreviewNpc = previewNpc;
            // No RebuildPresetList() here: setting PreviewGender above already rebuilt via
            // PropertyChanged if the gender differed, and the constructor built the list for
            // the default gender if it didn't. Rebuilding again would re-sort thousands of
            // presets for nothing.
            if (preset != null)
            {
                SelectedPreset = _allPresets.FirstOrDefault(p =>
                    ReferenceEquals(p.AssociatedModel, preset));
            }
        }
        finally
        {
            _interlockDepth--;
        }

        _ = RefreshPreviewAsync();
        RaisePreviewInputsChanged();
    }

    /// <summary>
    /// Asks <paramref name="host"/> to draw this pane's current model as a guest overlay.
    /// Resolves the same NPC + preset + weight this pane is showing, so the overlay always
    /// matches what the user sees on this side.
    /// </summary>
    public void ProjectOnto(VM_CharacterViewer host)
    {
        if (host == null) return;

        var npc = ResolvePreviewNpc();
        if (npc.IsNull || lk == null)
        {
            host.ClearGuestScene();
            return;
        }

        _ = host.LoadGuestNpcAsync(npc, lk, SelectedPreset?.AssociatedModel, Weight);
    }

    private void RaisePreviewInputsChanged() => PreviewInputsChanged?.Invoke();

    /// <summary>
    /// Reloads the preview unless <see cref="Seed"/> is mid-write. Seeding sets gender, weight,
    /// NPC and preset in sequence, and each write raises PropertyChanged; without this gate the
    /// window would kick off four NPC loads per pane on open. Seed issues the single refresh
    /// itself once every input has landed.
    /// </summary>
    private void RefreshUnlessSeeding()
    {
        if (_interlockDepth > 0) return;
        _ = RefreshPreviewAsync();
    }

    /// <summary>
    /// Snaps <see cref="Weight"/> to the chosen NPC's record weight. This is the half of the
    /// interlock that lets the free-form picker reach NPCs outside the current weight — the
    /// candidate list alone could never trigger it, since every entry already matches.
    /// Rounds to nearest because the record weight is a float and the input is an integer.
    /// </summary>
    private void SnapWeightToNpc()
    {
        if (_interlockDepth > 0 || PreviewNpc.IsNull) return;

        var actual = _previewNpcResolver.GetNpcWeight(PreviewNpc);
        if (!actual.HasValue) return;

        int snapped = Math.Clamp((int)Math.Round(actual.Value), 0, 100);
        if (snapped == Weight) return;

        _interlockDepth++;
        try
        {
            Weight = snapped;
        }
        finally
        {
            _interlockDepth--;
        }
    }

    /// <summary>Rebuilds the gendered preset list and re-applies the text filter.</summary>
    private void RebuildPresetList()
    {
        _allPresets.Clear();

        var source = PreviewGender == Gender.Male
            ? _bodySlideMenu?.BodySlidesMale
            : _bodySlideMenu?.BodySlidesFemale;

        if (source != null)
        {
            foreach (var placeHolder in source.OrderBy(p => p?.Label ?? "", StringComparer.OrdinalIgnoreCase))
            {
                if (placeHolder?.AssociatedModel == null) continue;
                _allPresets.Add(placeHolder);
            }
        }

        // An empty dropdown is indistinguishable from a populated one when closed, so say which
        // it is. The interesting failure is _bodySlideMenu being null — that means the OBody
        // settings VM wasn't resolvable when the window opened, and every pane would silently
        // offer nothing at all rather than "no presets for this gender".
        _logger.LogMessage("BodySlide Compare pane " + PaneLabel + ": " + _allPresets.Count
            + " preset(s) available for " + PreviewGender
            + (_bodySlideMenu == null ? " (BodySlides menu VM unavailable)" : ""));

        RebuildFilteredPresets();
    }

    /// <summary>Re-applies <see cref="PresetFilterText"/>, preserving the current selection
    /// when it survives the filter so typing doesn't blank the viewer.</summary>
    private void RebuildFilteredPresets()
    {
        var previous = SelectedPreset;

        FilteredPresets.Clear();
        string filter = PresetFilterText?.Trim() ?? "";
        foreach (var preset in _allPresets)
        {
            if (filter.Length > 0
                && (preset.Label ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            FilteredPresets.Add(preset);
        }

        SelectedPreset = previous != null && FilteredPresets.Contains(previous) ? previous : null;
    }

    /// <summary>
    /// Rebuilds <see cref="WeightFilteredNpcs"/> for the current weight + gender, scanning the
    /// load order off-thread with the same eligibility rules as the OBody Misc auto-pick
    /// (unique, Skyrim.esm race carrying ActorTypeNPC, vanilla body ARMA and skeleton).
    /// Called from a throttled subscription rather than a button, so the picker's options
    /// simply track the weight box.
    /// </summary>
    private async Task RefreshNpcCandidatesAsync()
    {
        var gender = PreviewGender;
        int weight = Weight;
        IsFindingNpcs = true;
        try
        {
            var candidates = await Task.Run(() => _previewNpcResolver.FindNpcsAtWeight(gender, weight));

            // Discard a scan the user invalidated while it ran; otherwise the picker would
            // offer "NPCs at weight N" while the box reads a different N.
            if (gender != PreviewGender || weight != Weight) return;

            WeightFilteredNpcs.Clear();
            foreach (var candidate in candidates)
            {
                WeightFilteredNpcs.Add(candidate.NpcFormKey);
            }

            // The picker gives no visible sign of an empty allow-list beyond offering nothing,
            // so record what the scan actually found. PreviewNpcResolver logs its own
            // per-criterion breakdown when the result is empty.
            _logger.LogMessage("BodySlide Compare pane " + PaneLabel + ": "
                + WeightFilteredNpcs.Count + " NPC(s) at weight " + weight + " (" + gender + ")");
        }
        catch (Exception ex)
        {
            _logger.LogError("BodySlide Compare pane " + PaneLabel + ": NPC search failed: "
                + ExceptionLogger.GetExceptionStack(ex));
        }
        finally
        {
            IsFindingNpcs = false;
        }
    }

    /// <summary>
    /// Preview NPC policy, matching the other OBody preview hosts: the explicit pick wins,
    /// otherwise the OBody Misc "Preview NPC by Weight" table supplies one. The table is read
    /// at the nearest configured slot rather than by exact key because this pane's weight is
    /// continuous (0-100) while the table's slots are sparse — same call the annotator rail makes.
    /// </summary>
    private FormKey ResolvePreviewNpc()
    {
        if (!PreviewNpc.IsNull) return PreviewNpc;

        var table = _patcherState?.OBodySettings?.PreviewNpcs?.WeightPreviewNpcs;
        if (table == null || table.Count == 0) return FormKey.Null;

        foreach (var slotWeight in VM_SliderAnnotatorPreviewPanel.OrderWeightKeysByProximity(table.Keys, Weight))
        {
            var pair = table[slotWeight];
            if (pair == null) continue;
            var npc = PreviewGender == Gender.Female ? pair.FemaleNpc : pair.MaleNpc;
            if (!npc.IsNull) return npc;
        }
        return FormKey.Null;
    }

    /// <summary>
    /// Loads the resolved preview NPC and applies the selected preset at the current weight.
    /// Generation-guarded so rapid input changes can't apply a stale preset over a newer one.
    /// The NPC loads even with no preset selected, so a freshly-opened pane shows a body
    /// rather than nothing.
    /// </summary>
    private async Task RefreshPreviewAsync()
    {
        int myGen = ++_refreshGeneration;
        var snapshotPreset = SelectedPreset?.AssociatedModel;
        int snapshotWeight = Weight;

        try
        {
            if (lk == null) return;

            var npc = ResolvePreviewNpc();

            // Reflect a defaulted NPC back into the picker so it names whoever is actually on
            // screen rather than sitting empty. Written under the interlock guard: this is a
            // display sync, so it must not snap the weight (the default came FROM the weight)
            // and must not trigger another reload of the NPC we are already loading.
            if (!npc.IsNull && PreviewNpc.IsNull)
            {
                _interlockDepth++;
                try { PreviewNpc = npc; }
                finally { _interlockDepth--; }
            }

            if (npc.IsNull)
            {
                _logger.LogMessage("BodySlide Compare pane " + PaneLabel
                    + ": no preview NPC configured near weight " + snapshotWeight + " (" + PreviewGender
                    + ") - pick one, or set a default in OBody Misc Settings.");
                return;
            }

            await CharacterViewer.LoadNpcAsync(npc, lk);

            if (myGen != _refreshGeneration) return;

            if (snapshotPreset != null)
            {
                CharacterViewer.ApplyBodySlide(snapshotPreset, snapshotWeight);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("BodySlide Compare pane " + PaneLabel + " refresh failed: "
                + ExceptionLogger.GetExceptionStack(ex));
        }
    }
}
