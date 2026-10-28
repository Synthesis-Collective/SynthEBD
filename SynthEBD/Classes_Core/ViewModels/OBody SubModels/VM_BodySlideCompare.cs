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
        Func<VM_SettingsOBody> oBodyVM,
        BodyTypeSourceMeshes sourceMeshes)
    {
        _logger = logger;

        var oBody = oBodyVM();
        var bodySlideMenu = oBody?.BodySlidesUI;
        var bodyTypes = BuildBodyTypeOptions(sourceMeshes);
        var skinMap = new CompareSkinMap(
            oBody?.CompareSkinSourceByBodyType ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            oBody?.CompareSkinModByBodyType ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        PaneA = new VM_BodySlideComparePane("A", logger, patcherState, environmentProvider,
            characterViewerFactory, previewNpcResolver, bodySlideMenu, sourceMeshes, bodyTypes, skinMap);
        PaneB = new VM_BodySlideComparePane("B", logger, patcherState, environmentProvider,
            characterViewerFactory, previewNpcResolver, bodySlideMenu, sourceMeshes, bodyTypes, skinMap);
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
    /// user was already looking at. Pane B is primed to the same body type, NPC and weight but is
    /// left without a preset, so the first thing the user does is pick B's preset (or B's body
    /// type) — the comparison the window exists for. Null/absent values are simply skipped.
    /// <para>The body type is the seed's, else the preset's SliderGroup, else the first one for the
    /// seed's gender. <see cref="BodySlideCompareSeed.ExcludeHiddenAndDisabled"/> drops
    /// hidden-and-disabled presets from both panes' pickers, for launches from the Auto-Annotation
    /// editor.</para>
    /// </summary>
    public void SeedFrom(BodySlideCompareSeed seed)
    {
        var bodyType = PaneA.FindBodyType(seed.BodyType)
            ?? PaneA.FindBodyType(seed.Preset?.SliderGroup)
            ?? PaneA.BodyTypes.FirstOrDefault(b => b.Gender == seed.Gender)
            ?? PaneA.BodyTypes.FirstOrDefault();

        PaneA.SetExcludeHiddenAndDisabled(seed.ExcludeHiddenAndDisabled);
        PaneB.SetExcludeHiddenAndDisabled(seed.ExcludeHiddenAndDisabled);
        PaneA.Seed(bodyType, seed.Weight, seed.PreviewNpc, seed.Preset);
        PaneB.Seed(bodyType, seed.Weight, seed.PreviewNpc, preset: null);
    }

    /// <summary>The Body Type picker's options, shared by both panes: registry entries with a
    /// BodySlide source mesh on disk. With none at all (no BodySlide ShapeData installed) the panes fall
    /// back to one unfiltered option per gender on the installed body, so the window still works
    /// the way it did before body types were selectable.</summary>
    private static IReadOnlyList<CompareBodyTypeOption> BuildBodyTypeOptions(BodyTypeSourceMeshes sourceMeshes)
    {
        var options = sourceMeshes.GetAvailable()
            .Select(e => new CompareBodyTypeOption(e.Name, e.Gender, e))
            .ToList();
        if (options.Count == 0)
        {
            options.Add(new CompareBodyTypeOption("Installed female body", Gender.Female, null));
            options.Add(new CompareBodyTypeOption("Installed male body", Gender.Male, null));
        }
        return options;
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
/// <see cref="ExcludeHiddenAndDisabled"/> hides presets that are both hidden and disabled from the
/// window's pickers (set by the Auto-Annotation hosts). <see cref="BodyType"/> is the Body Type
/// Registry name the panes open on; null = the preset's SliderGroup.
/// </summary>
public readonly record struct BodySlideCompareSeed(
    Gender Gender, int Weight, FormKey PreviewNpc, BodySlideSetting? Preset, bool ExcludeHiddenAndDisabled = false,
    string? BodyType = null);

/// <summary>One entry of the Compare panes' Body Type picker. <see cref="Source"/> is the body type's
/// BodySlide source mesh; null only for the no-ShapeData fallback options, which show the installed
/// body and don't filter presets by SliderGroup.</summary>
public sealed record CompareBodyTypeOption(string Name, Gender Gender, BodyTypeSourceMeshes.Entry? Source)
{
    public bool FiltersBySliderGroup => Source != null;
}

/// <summary>What a Compare pane loads in place of the worn body: the source mesh, the OSDs that
/// deform it, and its skin -- the NPC's own (<see cref="UsesNpcSkin"/>, when the skin body type is the
/// installed one), else textures looked up in <see cref="TextureFolder"/> (null = neutral beige).
/// <see cref="SkinBodyType"/> is the body type the skin comes from after the skin mapping;
/// <see cref="SkinMod"/> the mod (folder name) it was mapped to, if any.</summary>
internal sealed record CompareBodyMesh(string NifPath, BodyMorphFiles Morphs, string? TextureFolder,
    bool UsesNpcSkin, string SkinBodyType, string? SkinMod = null,
    IReadOnlyDictionary<string, string>? PartMeshes = null)
{
    /// <summary>The viewer-side replacement: body NIF, matching hands/feet/tail, and skin source.</summary>
    public BodyReplacement ToReplacement() => new(NifPath, PartMeshes, TextureFolder, UsesNpcSkin);
}

/// <summary>One entry of a Compare pane's Skin picker: <see cref="BodyType"/> null = the pane's own
/// body type's skin, else render with that body type's skin. <see cref="Label"/> starts as the plain
/// name and is then rewritten to say what the choice will actually show (the NPC's skin, a body's own
/// textures, or beige because none are included), once that has been checked off the UI thread.</summary>
public sealed class CompareSkinOption : System.ComponentModel.INotifyPropertyChanged
{
    public CompareSkinOption(string label, string? bodyType, bool isModChoice = false)
    {
        Label = label;
        BaseLabel = label;
        BodyType = bodyType;
        IsModChoice = isModChoice;
    }

    /// <summary>The "From a mod..." entry: the skin comes from an installed mod picked in the Mod picker.</summary>
    public bool IsModChoice { get; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; set; }

    /// <summary>The name the label was built from, before any availability note.</summary>
    public string BaseLabel { get; }

    public string? BodyType { get; }
}

/// <summary>
/// The Compare window's view of the persisted skin mappings -- body type -> skin body type
/// (<see cref="VM_SettingsOBody.CompareSkinSourceByBodyType"/>) and body type -> skin mod
/// (<see cref="VM_SettingsOBody.CompareSkinModByBodyType"/>), both edited in place so the next settings
/// save writes them -- shared by both panes. A body type has at most one of the two: setting either
/// clears the other. <see cref="Changed"/> lets the other pane follow an edit: body mappings chain
/// (UNP -> BHUNP -> ...), and the chain's last body type may carry a mod, so any edit can change what
/// either pane resolves to.
/// </summary>
public sealed class CompareSkinMap
{
    private readonly Dictionary<string, string> _map;
    private readonly Dictionary<string, string> _modMap;

    internal CompareSkinMap(Dictionary<string, string> map, Dictionary<string, string>? modMap = null)
    {
        _map = map;
        _modMap = modMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The skin mod (installed mod folder name) mapped to <paramref name="bodyType"/>, or null.</summary>
    internal string? GetMod(string bodyType)
        => _modMap.TryGetValue(bodyType, out var mod) && !string.IsNullOrWhiteSpace(mod) ? mod : null;

    /// <summary>Maps <paramref name="bodyType"/> to the skin in mod <paramref name="modName"/> (null clears),
    /// replacing any body-type mapping it had.</summary>
    internal void SetMod(string bodyType, string? modName)
    {
        bool changed;
        if (string.IsNullOrWhiteSpace(modName))
        {
            changed = _modMap.Remove(bodyType);
        }
        else
        {
            changed = !string.Equals(GetMod(bodyType), modName, StringComparison.OrdinalIgnoreCase);
            _modMap[bodyType] = modName;
            changed |= _map.Remove(bodyType);
        }
        if (changed) Changed?.Invoke();
    }

    internal event Action? Changed;

    /// <summary>The mapped skin body type of <paramref name="bodyType"/>, or null for its own.</summary>
    internal string? Get(string bodyType)
        => _map.TryGetValue(bodyType, out var target) && !string.IsNullOrWhiteSpace(target) ? target : null;

    /// <summary>Maps <paramref name="bodyType"/> to <paramref name="skinBodyType"/> (null/self = own skin),
    /// replacing any skin mod it had.</summary>
    internal void Set(string bodyType, string? skinBodyType)
    {
        bool own = string.IsNullOrWhiteSpace(skinBodyType)
            || string.Equals(skinBodyType, bodyType, StringComparison.OrdinalIgnoreCase);
        bool changed = _modMap.Remove(bodyType);
        if (own)
        {
            changed |= _map.Remove(bodyType);
        }
        else if (!string.Equals(Get(bodyType), skinBodyType, StringComparison.OrdinalIgnoreCase))
        {
            _map[bodyType] = skinBodyType!;
            changed = true;
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Follows the mapping from <paramref name="bodyType"/> to the body type whose skin is used,
    /// stopping at an unmapped type or before revisiting one (a cycle ends where it closes).</summary>
    internal string Resolve(string bodyType)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { bodyType };
        var current = bodyType;
        while (GetMod(current) == null && Get(current) is { } next && seen.Add(next)) current = next;
        return current;
    }
}

/// <summary>One entry of a Compare pane's preset picker: a real preset, or the synthetic Zeroed
/// entry (no slider values) that heads every list.</summary>
public sealed record ComparePresetOption(string Label, BodySlideSetting Model, bool IsZeroed);

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
            execute: _ => Open(compareFactory, seedProvider, logger));
    }

    /// <summary>
    /// Opens one <see cref="Window_BodySlideCompare"/> seeded from <paramref name="seedProvider"/>.
    /// The body of <see cref="CreateCommand"/>, also called directly by hosts that open Compare
    /// on a specific preset rather than their current preview (the Show Spread / annotation
    /// Panel "3D" buttons, via <see cref="VM_BodyTypeProfileEditor.OpenSliceInCompare"/>).
    /// </summary>
    public static void Open(
        Func<VM_BodySlideCompare> compareFactory,
        Func<BodySlideCompareSeed> seedProvider,
        Logger logger)
    {
        try
        {
            var vm = compareFactory();
            vm.SeedFrom(seedProvider());

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
    }
}

/// <summary>
/// One side of the Compare window: a CharacterViewer plus the inputs that drive it —
/// body type (which also fixes the gender), a filterable BodySlide preset dropdown headed by
/// Zeroed, an integer weight, and the preview NPC (both a weight-filtered candidate list and a
/// free-form picker).
///
/// <para><b>Body mesh.</b> The installed body type renders the NPC's own worn body; any other
/// type renders its BodySlide reference mesh through
/// <see cref="VM_CharacterViewer.BodyReplacement"/>, deformed by that mesh's own OSDs and textured
/// from the mod that ships it, or neutral beige (see <see cref="BodyTypeSourceMeshes"/>).</para>
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
    private readonly BodyTypeSourceMeshes _sourceMeshes;
    private readonly CompareSkinMap _skinMap;

    /// <summary>Non-zero while the Skin picker is being synced from the mapping, so the write isn't
    /// taken as a user edit.</summary>
    private int _skinSyncDepth;

    /// <summary>Guards <see cref="RefreshSkinOptionLabels"/>: only the newest pass writes its labels.</summary>
    private int _skinLabelGeneration;

    /// <summary>Guards <see cref="RefreshSkinModList"/>: only the newest search fills the Mod picker.</summary>
    private int _skinModListGeneration;

    /// <summary>Zeroed + every preset for the current body type, before <see cref="PresetFilterText"/>.</summary>
    private readonly List<ComparePresetOption> _allPresets = new();

    /// <summary>What the last refresh loaded in place of the worn body (null = worn body); handed to a
    /// superimpose host by <see cref="ProjectOnto"/>.</summary>
    private CompareBodyMesh? _bodyMeshOverride;

    /// <summary>When set, <see cref="RebuildPresetList"/> skips hidden-and-disabled presets.</summary>
    private bool _excludeHiddenAndDisabled;

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
        VM_BodySlidesMenu? bodySlideMenu,
        BodyTypeSourceMeshes sourceMeshes,
        IReadOnlyList<CompareBodyTypeOption> bodyTypes,
        CompareSkinMap skinMap)
    {
        PaneLabel = paneLabel;
        _logger = logger;
        _patcherState = patcherState;
        _previewNpcResolver = previewNpcResolver;
        _bodySlideMenu = bodySlideMenu;
        _sourceMeshes = sourceMeshes;
        _skinMap = skinMap;
        BodyTypes = bodyTypes;
        SelectedBodyType = bodyTypes.FirstOrDefault(b => b.Gender == Gender.Female) ?? bodyTypes.FirstOrDefault();
        PreviewGender = SelectedBodyType?.Gender ?? Gender.Female;

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
                case nameof(SelectedBodyType):
                    HandleBodyTypeChange();
                    break;

                case nameof(SelectedSkinSource):
                    HandleSkinSourcePick();
                    break;

                case nameof(SelectedSkinMod):
                    HandleSkinModPick();
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
        RebuildSkinSources();

        // The other pane edited the mapping (it chains, so it may change what this pane resolves to).
        _skinMap.Changed += HandleSkinMapEdit;
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

    /// <summary>The Body Type picker's options (shared with the other pane).</summary>
    public IReadOnlyList<CompareBodyTypeOption> BodyTypes { get; }

    /// <summary>The body this pane shows. Fixes <see cref="PreviewGender"/>, the preset list and
    /// which body mesh loads.</summary>
    public CompareBodyTypeOption? SelectedBodyType { get; set; }

    /// <summary>The selected body type's gender; drives the NPC candidates and the default NPC.</summary>
    public Gender PreviewGender { get; private set; } = Gender.Female;

    /// <summary>Which mesh is on screen for the body type ("installed body" / "BodySlide reference
    /// mesh"), shown beside the picker so a reference-mesh render isn't mistaken for the user's own body.</summary>
    public string BodyMeshNote { get; private set; } = "";

    /// <summary>The Skin picker's options: the body type's own skin, then every other body type of the
    /// same gender -- for bodies sharing a UV layout (CBBE with CBBE 3BA skins, UNP with BHUNP's).</summary>
    public ObservableCollection<CompareSkinOption> SkinSources { get; } = new();

    /// <summary>Whose skin this pane's body type renders with; persisted per body type, shared with the
    /// other pane. Only matters for a body type shown through its source mesh -- the installed body
    /// always wears the NPC's own skin.</summary>
    public CompareSkinOption? SelectedSkinSource { get; set; }

    /// <summary>True while "From a mod..." is the Skin choice: shows the Mod picker.</summary>
    public bool IsSkinModPickerVisible => SelectedSkinSource?.IsModChoice == true;

    /// <summary>Installed mods (folder names) that contain the skin texture this body type's mesh asks
    /// for -- the Mod picker's options. Filled off the UI thread when the picker opens.</summary>
    public ObservableCollection<string> SkinMods { get; } = new();

    /// <summary>The mod whose skin this body type renders with (persisted per body type).</summary>
    public string? SelectedSkinMod { get; set; }

    /// <summary>True while <see cref="SkinMods"/> is being searched; drives the "searching" hint.</summary>
    public bool IsFindingSkinMods { get; private set; }

    /// <summary>The Mod picker's status line ("3 mods contain femalebody_1.dds", "none found").</summary>
    public string SkinModStatus { get; private set; } = "";

    public string PresetFilterText { get; set; } = "";

    public ObservableCollection<ComparePresetOption> FilteredPresets { get; } = new();

    public ComparePresetOption? SelectedPreset { get; set; }

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

    /// <summary>The <see cref="BodyTypes"/> option named <paramref name="name"/> (case-insensitive), or null.</summary>
    public CompareBodyTypeOption? FindBodyType(string? name)
        => string.IsNullOrWhiteSpace(name) ? null
            : BodyTypes.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Applies the opening state handed down by <see cref="VM_BodySlideCompare.SeedFrom"/>.
    /// Writes the inputs under the interlock guard and refreshes once at the end, so seeding
    /// costs one load rather than one per property.
    /// </summary>
    public void Seed(CompareBodyTypeOption? bodyType, int weight, FormKey previewNpc, BodySlideSetting? preset)
    {
        _interlockDepth++;
        try
        {
            if (bodyType != null) SelectedBodyType = bodyType;
            Weight = Math.Clamp(weight, 0, 100);
            PreviewNpc = previewNpc;
            // No RebuildPresetList() here: setting SelectedBodyType above already rebuilt via
            // PropertyChanged if it differed, and the constructor built the list for the default
            // body type if it didn't. Rebuilding again would re-sort thousands of presets for nothing.
            if (preset != null)
            {
                SelectedPreset = _allPresets.FirstOrDefault(p => ReferenceEquals(p.Model, preset));
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

        _ = host.LoadGuestNpcAsync(npc, lk, SelectedPreset?.Model, Weight,
            _bodyMeshOverride?.ToReplacement(), _bodyMeshOverride?.Morphs);
    }

    /// <summary>
    /// A new body type: take its gender, rebuild the preset list for it (keeping Zeroed selected if
    /// it was, so zeroed bodies can be flipped through type by type), and reload. A gender change
    /// also drops the picked NPC, who would be the wrong sex now -- the default NPC for the weight
    /// takes over. Seeding sets the NPC itself right after, so the reset is skipped there.
    /// </summary>
    private void HandleBodyTypeChange()
    {
        var gender = SelectedBodyType?.Gender ?? PreviewGender;
        if (gender != PreviewGender)
        {
            PreviewGender = gender;
            if (_interlockDepth == 0 && !PreviewNpc.IsNull)
            {
                _interlockDepth++;
                try { PreviewNpc = FormKey.Null; }
                finally { _interlockDepth--; }
            }
        }

        // Guarded so the rebuild's selection churn (cleared, then possibly Zeroed again) doesn't
        // queue a reload per write; the one refresh below covers it.
        _interlockDepth++;
        try { RebuildPresetList(); }
        finally { _interlockDepth--; }
        RebuildSkinSources();

        RefreshUnlessSeeding();
        RaisePreviewInputsChanged();
    }

    /// <summary>Refills <see cref="SkinSources"/> for the current body type and selects its mapped entry
    /// (a mapping to a body type not offered here -- other gender, no source mesh -- still shows).</summary>
    private void RebuildSkinSources()
    {
        _skinSyncDepth++;
        try
        {
            SkinSources.Clear();
            var bodyType = SelectedBodyType;
            if (bodyType == null) { SelectedSkinSource = null; return; }

            var own = new CompareSkinOption("Own skin", null);
            SkinSources.Add(own);
            foreach (var other in BodyTypes.Where(b => b.Gender == bodyType.Gender
                         && !string.Equals(b.Name, bodyType.Name, StringComparison.OrdinalIgnoreCase)))
            {
                SkinSources.Add(new CompareSkinOption(other.Name, other.Name));
            }
            CompareSkinOption? modChoice = null;
            if (bodyType.Source != null && _sourceMeshes.CanListMods)
            {
                modChoice = new CompareSkinOption("From a mod...", null, isModChoice: true);
                SkinSources.Add(modChoice);
            }

            var mapped = _skinMap.Get(bodyType.Name);
            var selected = _skinMap.GetMod(bodyType.Name) != null && modChoice != null ? modChoice
                : mapped == null ? own
                : SkinSources.FirstOrDefault(o => string.Equals(o.BodyType, mapped, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
            {
                selected = new CompareSkinOption(mapped!, mapped);
                SkinSources.Add(selected);
            }
            SelectedSkinSource = selected;
            SelectedSkinMod = _skinMap.GetMod(bodyType.Name);
        }
        finally
        {
            _skinSyncDepth--;
        }
        RefreshSkinOptionLabels();
        if (IsSkinModPickerVisible) RefreshSkinModList();
    }

    /// <summary>
    /// Fills the Mod picker with the installed mods (deployed or not) that contain the skin texture this
    /// body type's mesh asks for (<see cref="BodyTypeSourceMeshes.FindModsWithSkin"/>), off the UI thread.
    /// The current mapping stays listed even if its mod no longer has the file (or is gone), so the
    /// picker never silently changes the saved choice.
    /// </summary>
    private async void RefreshSkinModList()
    {
        int generation = ++_skinModListGeneration;
        var source = SelectedBodyType?.Source;
        var bodyTypeName = SelectedBodyType?.Name;
        if (source == null || bodyTypeName == null) return;

        IsFindingSkinMods = true;
        SkinModStatus = "searching your mods...";
        try
        {
            var (mods, diffuse) = await Task.Run(() =>
                (_sourceMeshes.FindModsWithSkin(source).Select(f => System.IO.Path.GetFileName(f)!).ToList(),
                 _sourceMeshes.MainSkinDiffuse(source)));
            if (generation != _skinModListGeneration) return;

            var current = _skinMap.GetMod(bodyTypeName);
            _skinSyncDepth++;
            try
            {
                SkinMods.Clear();
                foreach (var mod in mods) SkinMods.Add(mod);
                if (current != null && !SkinMods.Contains(current, StringComparer.OrdinalIgnoreCase)) SkinMods.Insert(0, current);
                SelectedSkinMod = current == null ? null
                    : SkinMods.First(m => string.Equals(m, current, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _skinSyncDepth--;
            }

            string file = System.IO.Path.GetFileName(diffuse ?? "") is { Length: > 0 } f ? f : "its skin texture";
            SkinModStatus = mods.Count == 0 ? "no installed mod contains " + file
                : mods.Count + (mods.Count == 1 ? " mod contains " : " mods contain ") + file;
        }
        catch (Exception ex)
        {
            _logger.LogError("BodySlide Compare pane " + PaneLabel + ": skin mod search failed: "
                + ExceptionLogger.GetExceptionStack(ex));
            SkinModStatus = "mod search failed (see log)";
        }
        finally
        {
            if (generation == _skinModListGeneration) IsFindingSkinMods = false;
        }
    }

    /// <summary>A user pick in the Mod picker: store it for the body type and reload.</summary>
    private void HandleSkinModPick()
    {
        if (_skinSyncDepth > 0 || SelectedBodyType == null || string.IsNullOrWhiteSpace(SelectedSkinMod)) return;
        _skinMap.SetMod(SelectedBodyType.Name, SelectedSkinMod);
        RefreshUnlessSeeding();
    }

    /// <summary>
    /// Rewrites each Skin option's label to say what picking it shows, so "Own skin" on a body that ships
    /// no textures doesn't promise a skin and then render beige. Off the UI thread: it reads NIFs
    /// (cached). Each option is judged by where its mapping ends: the installed body type -> the NPC's
    /// own skin; a body type that ships its main skin texture -> that skin; anything else -> beige.
    /// </summary>
    private async void RefreshSkinOptionLabels()
    {
        int generation = ++_skinLabelGeneration;
        var bodyType = SelectedBodyType;
        if (bodyType?.Source == null) return; // fallback options: no source mesh, nothing to predict
        var options = SkinSources.ToList();
        var effective = options.Select(o => o.BodyType == null ? bodyType.Name : _skinMap.Resolve(o.BodyType)).ToList();
        // Own skin means "no mapping", so its chain stops at this body type and its own mod is ignored;
        // the mod choice is labelled with this body type's mod.
        var effectiveMods = options.Select((o, i) => o.IsModChoice ? _skinMap.GetMod(bodyType.Name)
            : o.BodyType == null ? null : _skinMap.GetMod(effective[i])).ToList();

        try
        {
            var labels = await Task.Run(() =>
            {
                var installed = _sourceMeshes.FindInstalledBodyType(bodyType.Gender);
                return options.Select((o, i) => DescribeSkinChoice(o, bodyType.Name, effective[i], effectiveMods[i], installed)).ToList();
            });
            if (generation != _skinLabelGeneration) return;
            for (int i = 0; i < options.Count; i++) options[i].Label = labels[i];
        }
        catch (Exception ex)
        {
            _logger.LogError("BodySlide Compare pane " + PaneLabel + ": skin availability check failed: "
                + ExceptionLogger.GetExceptionStack(ex));
        }
    }

    /// <summary>The label for <paramref name="option"/> on a pane showing <paramref name="paneBodyType"/>,
    /// whose mapping ends at <paramref name="effective"/> (and its mod <paramref name="effectiveMod"/>, if
    /// mapped to one). Runs off the UI thread.</summary>
    private string DescribeSkinChoice(CompareSkinOption option, string paneBodyType, string effective,
        string? effectiveMod, string? installed)
    {
        if (option.IsModChoice)
            return effectiveMod == null ? option.BaseLabel : "Mod: " + effectiveMod;
        if (effectiveMod != null)
            return option.BaseLabel + " (mod " + effectiveMod + ")";

        string via = string.Equals(effective, option.BodyType ?? paneBodyType, StringComparison.OrdinalIgnoreCase)
            ? "" : " via " + effective;
        if (string.Equals(effective, installed, StringComparison.OrdinalIgnoreCase))
            return option.BaseLabel + " (your NPC's skin" + via + ")";

        var entry = BodyTypes.FirstOrDefault(b => string.Equals(b.Name, effective, StringComparison.OrdinalIgnoreCase))?.Source;
        if (entry != null && _sourceMeshes.HasOwnSkin(entry))
            return via.Length == 0 ? option.BaseLabel : option.BaseLabel + " (" + via.Trim() + ")";
        return option.BaseLabel + " (none included" + via + " - beige)";
    }

    /// <summary>A user pick in the Skin picker: store it for the body type and reload.</summary>
    private void HandleSkinSourcePick()
    {
        if (_skinSyncDepth > 0 || SelectedBodyType == null || SelectedSkinSource == null) return;
        if (SelectedSkinSource.IsModChoice)
        {
            // Nothing is stored until a mod is picked; until then the previous skin stays on screen.
            RefreshSkinModList();
            return;
        }
        _skinMap.Set(SelectedBodyType.Name, SelectedSkinSource.BodyType);
        RefreshUnlessSeeding();
    }

    private void HandleSkinMapEdit()
    {
        RebuildSkinSources();
        RefreshUnlessSeeding();
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

    /// <summary>Sets whether hidden-and-disabled presets are left out of this pane's picker; rebuilds the list on change.</summary>
    public void SetExcludeHiddenAndDisabled(bool exclude)
    {
        if (_excludeHiddenAndDisabled == exclude) return;
        _excludeHiddenAndDisabled = exclude;
        RebuildPresetList();
    }

    /// <summary>Label of the synthetic all-sliders-zero entry heading every preset list.</summary>
    internal const string ZeroedLabel = "Zeroed";

    /// <summary>Rebuilds the body type's preset list (Zeroed first, then its gender's presets whose
    /// SliderGroup is the body type) and re-applies the text filter. A Zeroed selection carries over
    /// to the new body type's Zeroed entry.</summary>
    private void RebuildPresetList()
    {
        bool wasZeroed = SelectedPreset?.IsZeroed == true;
        var bodyType = SelectedBodyType;
        _allPresets.Clear();

        // Zeroed, as BodySlide's own "Zeroed Sliders": every visible slider of the body type's slider set
        // explicitly 0, hidden ones left to their defaults (a source mesh gets BodySlide's value rules
        // applied, so an unlisted visible slider would otherwise take its default, not 0). For the worn
        // body the zeros are a no-op on its .tri. Its model label names the body type so the superimpose
        // overlay's cache, keyed on the label, tells two body types' Zeroed entries apart.
        var zeroedModel = new BodySlideSetting { Label = ZeroedLabel + " (" + (bodyType?.Name ?? "") + ")", SliderGroup = bodyType?.Name ?? "" };
        foreach (var info in bodyType?.Source?.Mesh.Morphs.Sliders ?? Array.Empty<BodySlideSliderInfo>())
        {
            if (info.Hidden || !info.IsShapeDelta) continue;
            zeroedModel.SliderValues[info.Name] = new BodySlideSlider { SliderName = info.Name, Big = 0, Small = 0 };
        }
        var zeroed = new ComparePresetOption(ZeroedLabel, zeroedModel, IsZeroed: true);
        _allPresets.Add(zeroed);

        var source = PreviewGender == Gender.Male
            ? _bodySlideMenu?.BodySlidesMale
            : _bodySlideMenu?.BodySlidesFemale;

        if (source != null)
        {
            foreach (var placeHolder in source.OrderBy(p => p?.Label ?? "", StringComparer.OrdinalIgnoreCase))
            {
                var model = placeHolder?.AssociatedModel;
                if (model == null) continue;
                if (_excludeHiddenAndDisabled && placeHolder!.IsHiddenAndDisabled) continue;
                if (bodyType?.FiltersBySliderGroup == true
                    && !string.Equals(model.SliderGroup, bodyType.Name, StringComparison.OrdinalIgnoreCase)) continue;
                _allPresets.Add(new ComparePresetOption(placeHolder!.Label ?? model.Label, model, IsZeroed: false));
            }
        }

        // An empty dropdown is indistinguishable from a populated one when closed, so say which
        // it is. The interesting failure is _bodySlideMenu being null — that means the OBody
        // settings VM wasn't resolvable when the window opened, and every pane would silently
        // offer nothing at all rather than "no presets for this body type".
        _logger.LogMessage("BodySlide Compare pane " + PaneLabel + ": " + (_allPresets.Count - 1)
            + " preset(s) available for " + (bodyType?.Name ?? PreviewGender.ToString())
            + (_bodySlideMenu == null ? " (BodySlides menu VM unavailable)" : ""));

        RebuildFilteredPresets();
        if (wasZeroed) SelectedPreset = zeroed;
    }

    /// <summary>Re-applies <see cref="PresetFilterText"/> (Zeroed always stays), preserving the
    /// current selection when it survives the filter so typing doesn't blank the viewer.</summary>
    private void RebuildFilteredPresets()
    {
        var previous = SelectedPreset;

        FilteredPresets.Clear();
        string filter = PresetFilterText?.Trim() ?? "";
        foreach (var preset in _allPresets)
        {
            if (!preset.IsZeroed && filter.Length > 0
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

        foreach (var slotWeight in PreviewNpcResolver.OrderWeightKeysByProximity(table.Keys, Weight))
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
        var snapshotPreset = SelectedPreset?.Model;
        int snapshotWeight = Weight;
        var snapshotBodyType = SelectedBodyType;

        try
        {
            if (lk == null) return;

            // Worn body or source mesh. Off-thread: the first call per gender parses NIFs.
            string? skinBodyType = snapshotBodyType != null ? _skinMap.Resolve(snapshotBodyType.Name) : null;
            string? skinMod = skinBodyType != null ? _skinMap.GetMod(skinBodyType) : null;
            var bodyOverride = await Task.Run(() => ResolveBodyMeshOverride(snapshotBodyType, skinBodyType, skinMod));
            if (myGen != _refreshGeneration) return;
            ApplyBodyMeshOverride(snapshotBodyType, bodyOverride);

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

    /// <summary>What to load for <paramref name="bodyType"/> in place of the worn body: null for the
    /// installed type (the NPC's own worn body, with its .tri and skin) and for the no-ShapeData fallback
    /// options, else the type's source mesh. Its skin follows <paramref name="skinBodyType"/> (the skin
    /// mapping, resolved): a skin mod mapped there (<paramref name="skinMod"/>) wins -- its folder is
    /// where textures are looked up; else the installed type's skin is the NPC's own, and any other body
    /// type's is looked up in the mod that ships that body type. Runs off the UI thread.</summary>
    private CompareBodyMesh? ResolveBodyMeshOverride(CompareBodyTypeOption? bodyType, string? skinBodyType, string? skinMod)
    {
        var source = bodyType?.Source;
        if (source == null) return null;
        var installed = _sourceMeshes.FindInstalledBodyType(bodyType!.Gender);
        if (string.Equals(installed, bodyType.Name, StringComparison.OrdinalIgnoreCase)) return null;

        var skin = skinBodyType ?? bodyType.Name;
        if (skinMod != null)
            return new CompareBodyMesh(source.Mesh.NifPath, source.Mesh.Morphs, _sourceMeshes.FindModFolderByName(skinMod),
                UsesNpcSkin: false, skin, skinMod, source.PartMeshes);
        if (string.Equals(installed, skin, StringComparison.OrdinalIgnoreCase))
            return new CompareBodyMesh(source.Mesh.NifPath, source.Mesh.Morphs, null, UsesNpcSkin: true, skin,
                PartMeshes: source.PartMeshes);

        var skinEntry = string.Equals(skin, bodyType.Name, StringComparison.OrdinalIgnoreCase) ? source
            : BodyTypes.FirstOrDefault(b => string.Equals(b.Name, skin, StringComparison.OrdinalIgnoreCase))?.Source;
        var folder = skinEntry != null ? _sourceMeshes.FindTextureFolder(skinEntry) : null;
        return new CompareBodyMesh(source.Mesh.NifPath, source.Mesh.Morphs, folder, UsesNpcSkin: false, skin,
            PartMeshes: source.PartMeshes);
    }

    /// <summary>Points the viewer at <paramref name="bodyOverride"/> (mesh, morph OSDs, texture folder)
    /// for the coming load and updates <see cref="BodyMeshNote"/>. On a change, logs it and tells the
    /// superimpose host, whose overlay of this pane must switch body too.</summary>
    private void ApplyBodyMeshOverride(CompareBodyTypeOption? bodyType, CompareBodyMesh? bodyOverride)
    {
        string skinNote = bodyOverride == null ? ""
            : bodyOverride.SkinMod != null ? (bodyOverride.TextureFolder == null
                ? "mod " + bodyOverride.SkinMod + " not found, neutral skin"
                : "skin from mod " + bodyOverride.SkinMod)
            : bodyOverride.UsesNpcSkin ? "NPC skin (" + bodyOverride.SkinBodyType + ")"
            : bodyOverride.TextureFolder == null ? "neutral skin"
            : string.Equals(bodyOverride.SkinBodyType, bodyType?.Name, StringComparison.OrdinalIgnoreCase) ? "own skin"
            : bodyOverride.SkinBodyType + " skin";
        BodyMeshNote = bodyType?.Source == null ? ""
            : bodyOverride == null ? "installed body"
            : "BodySlide source mesh, " + skinNote;

        if (Equals(bodyOverride, _bodyMeshOverride)) return;
        _bodyMeshOverride = bodyOverride;
        CharacterViewer.BodyReplacement = bodyOverride?.ToReplacement();
        CharacterViewer.SetBodyMorphOsdOverride(bodyOverride?.Morphs);
        _logger.LogMessage("BodySlide Compare pane " + PaneLabel + ": " + (bodyType?.Name ?? "?") + " renders "
            + (bodyOverride == null ? "the installed worn body"
                : "its source mesh '" + bodyOverride.NifPath + "' (" + bodyOverride.Morphs.Paths.Count + " slider data file(s); "
                  + (bodyOverride.UsesNpcSkin ? "the NPC's own skin, via " + bodyOverride.SkinBodyType
                      : bodyOverride.SkinMod != null ? "skin from mod " + bodyOverride.SkinMod + " at " + (bodyOverride.TextureFolder ?? "(not found - neutral)")
                      : bodyOverride.SkinBodyType + " skin from " + (bodyOverride.TextureFolder ?? "nowhere - neutral")) + ")"));
        RaisePreviewInputsChanged();
    }
}
