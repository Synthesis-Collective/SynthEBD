using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;
using MediaColor = System.Windows.Media.Color;

namespace SynthEBD;

/// <summary>
/// Controls which editing features are available in the character viewer.
/// </summary>
[Flags]
public enum ViewerMode
{
    ReadOnly = 0,
    TextureEdit = 1,
    BodySlideEdit = 2,
    Full = TextureEdit | BodySlideEdit
}

/// <summary>
/// ViewModel for the 3D character viewer. Manages the OpenGL rendering scene,
/// loaded mesh data, textures, and user interaction.
/// </summary>
public class VM_CharacterViewer : VM
{
    private readonly NifMeshBuilder _meshBuilder;
    private readonly NpcMeshResolver _npcMeshResolver;
    private readonly BodySlideDeformer _bodySlideDeformer;
    private readonly BsdFileParser _bsdFileParser;
    private readonly BodyTriFileParser _bodyTriFileParser;
    private readonly GameAssetResolver _assetResolver;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly CharacterViewerLogGate _logGate;

    private CancellationTokenSource? _loadCts;

    // GL rendering objects — created once, reused across NPC loads
    public GlRenderer Renderer { get; } = new();
    public GlTextureManager? TextureManager { get; private set; }
    public OrbitCamera Camera { get; } = new();

    /// <summary>Tracks which GlMesh corresponds to which body part for texture overrides.</summary>
    private readonly Dictionary<string, GlMesh> _meshesByBodyPart = new();

    /// <summary>Tracks BuiltMesh per body part for BodySlide and normal override resampling.</summary>
    private readonly Dictionary<string, NifMeshBuilder.BuiltMesh> _builtMeshesByBodyPart = new();

    /// <summary>Cached built meshes for reapplying BodySlide without reloading.</summary>
    private readonly Dictionary<string, NifMeshBuilder.BuiltMesh> _cachedBodyMeshes = new();

    private List<OsdFile>? _cachedOsdFiles;

    /// <summary>Disk path of the currently-loaded body NIF, used to locate the
    /// sibling .tri for topology-matched morphing (BodySlide "Build Morphs" output).</summary>
    private string? _cachedBodyNifDiskPath;

    /// <summary>Parsed sibling .tri for the current body NIF, cached across preset/weight
    /// changes so we don't re-parse on every RefreshPreview. Null when no .tri is
    /// present next to the NIF -- in that case we fall back to the OSD path.</summary>
    private BodyTriFile? _cachedBodyTri;

    private NpcMeshResolver.NpcMeshPaths? _cachedMeshPaths;

    /// <summary>NPC's HairColor record (HCLR) resolved from HeadData.HairColor FormLink,
    /// in 0..1 linear floats. Null if the NPC has no HairColor set or it fails to resolve.
    /// In-game Skyrim uses this to override the NIF's baked BSLSP hairTintColor.</summary>
    private (float R, float G, float B)? _npcHairColorFromRecord;

    /// <summary>Cached texture info per mesh for ReapplyAllTextures.</summary>
    private readonly Dictionary<GlMesh, TextureApplyInfo> _textureApplyInfoByMesh = new();

    private record TextureApplyInfo(
        Dictionary<int, string> EffectiveTextures,
        bool IsHairTint, float HairTintR, float HairTintG, float HairTintB,
        bool IsFaceTint, string? FaceTintPath);

    private readonly VM_Settings_General _generalSettings;
    private readonly FaceGenPreviewService _faceGenPreviewService;

    /// <summary>True when the GL context has been initialized.</summary>
    public bool IsGlInitialized { get; private set; }

    /// <summary>Pending scene data waiting for GL context to become available.</summary>
    private (List<(string BodyPart, AssetSource? MeshSource, List<NifMeshBuilder.BuiltMesh> Meshes)> LoadResults,
             NpcMeshResolver.NpcMeshPaths MeshPaths)? _pendingScene;

    /// <summary>True from the moment a new NPC load starts until the render callback
    /// has rebuilt the scene. Routes texture overrides to the pending queue so they
    /// aren't applied to meshes that are about to be destroyed by ClearScene().</summary>
    private bool _sceneRebuildPending;

    /// <summary>Pending texture overrides to apply after scene setup.</summary>
    private List<FilePathReplacement>? _pendingTextureOverrides;

    /// <summary>Pending BodySlide to apply after scene setup.</summary>
    private (BodySlideSetting Preset, int Weight)? _pendingBodySlide;

    /// <summary>FormKey of the NPC whose scene is currently installed in the renderer.
    /// Captured at the end of ProcessPendingScene; cleared by ClearScene. Used by
    /// LoadNpcAsync to short-circuit reloads of the same NPC when narrow editors
    /// (BodySlide preset change, AssetPack subgroup flip) call LoadNpcAsync
    /// defensively even though only a narrow downstream update is needed.</summary>
    private FormKey _currentLoadedNpc = FormKey.Null;

    /// <summary>Head-mesh override path baked into the currently-installed scene
    /// (absolute path from ApplyHeadPartsAsync's FaceGen preview output), or null
    /// if the scene used the NPC's resolved head mesh. Compared case-insensitively
    /// as part of the same-NPC short-circuit in LoadNpcAsync.</summary>
    private string? _currentHeadMeshOverride;

    /// <summary>FormKey of the load whose results are queued in _pendingScene.
    /// Promoted to _currentLoadedNpc by ProcessPendingScene once the scene commits.</summary>
    private FormKey _pendingLoadNpcKey = FormKey.Null;

    /// <summary>Head-override path of the load whose results are queued in _pendingScene.
    /// Promoted to _currentHeadMeshOverride by ProcessPendingScene once the scene commits.</summary>
    private string? _pendingLoadHeadMeshOverride;

    /// <summary>Stopwatch started at LoadNpcAsync entry and threaded through to
    /// ProcessPendingScene so the GL-upload checkpoint can report elapsed-from-initiation.
    /// Set under Application.Current.Dispatcher when the scene is queued; consumed
    /// (and cleared) on the render thread when the scene is installed.</summary>
    private System.Diagnostics.Stopwatch? _pendingLoadStopwatch;

    /// <summary>Pending head-only rebuild (P2). Set by RebuildHeadOnlyAsync after the
    /// new head NIF is parsed off-thread; drained by ProcessPendingScene where the
    /// GL context is current. Replaces the current Head shape(s) in place, leaving
    /// Body/Hands/Feet and their texture/morph state untouched.</summary>
    private (string HeadNifPath, List<NifMeshBuilder.BuiltMesh> Meshes)? _pendingHeadReplace;

    /// <summary>Counterpart of _pendingLoadStopwatch for the head-only fast path.</summary>
    private System.Diagnostics.Stopwatch? _pendingHeadReplaceStopwatch;

    private readonly CharacterPreviewCache _previewCache;

    public VM_CharacterViewer(
        NpcMeshResolver npcMeshResolver,
        BodySlideDeformer bodySlideDeformer,
        BsdFileParser bsdFileParser,
        BodyTriFileParser bodyTriFileParser,
        GameAssetResolver assetResolver,
        IEnvironmentStateProvider environmentProvider,
        PatcherState patcherState,
        VM_Settings_General generalSettings,
        FaceGenPreviewService faceGenPreviewService,
        CharacterPreviewCache previewCache,
        CharacterViewerLogGate logGate,
        Logger logger)
    {
        _logGate = logGate;
        _previewCache = previewCache;
        // Mesh parser is shared via the preview cache so its parsed-NIF LRU
        // survives across viewer instances (the BodySlide menu disposes the
        // previous viewer on every preset switch).
        _meshBuilder = previewCache.MeshBuilder;
        _npcMeshResolver = npcMeshResolver;
        _bodySlideDeformer = bodySlideDeformer;
        _bsdFileParser = bsdFileParser;
        _bodyTriFileParser = bodyTriFileParser;
        _assetResolver = assetResolver;
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _generalSettings = generalSettings;
        _faceGenPreviewService = faceGenPreviewService;
        _logger = logger;

        // Verbose-log toggle persists in Settings_General so it survives the
        // viewer-disposal/recreate cycle that happens on every BodySlide preset
        // switch. Without this hop a per-instance default would silently flip
        // verbose logging back off whenever a new viewer was constructed.
        VerboseLog = _generalSettings.CharacterViewerVerboseLog;
        // Push VerboseLog -> shared gate so helper classes (BsdFileParser, GameAssetResolver,
        // NpcMeshResolver, BodySlideDeformer, NifMeshBuilder) can consult the same flag.
        _logGate.Verbose = VerboseLog;
        this.WhenAnyValue(x => x.VerboseLog).Skip(1).Subscribe(v =>
        {
            _logGate.Verbose = v;
            _generalSettings.CharacterViewerVerboseLog = v;
        }).DisposeWith(this);

        // Load persisted lighting state *before* XAML binds. If we defer this to
        // InitializeGl (which runs from the first GL render callback), the
        // ComboBox's two-way binding fires first and overwrites the persisted
        // selection with the field's default — that bug caused selections to
        // appear not to persist across sessions.
        InitializeLightingState();

        // Push height changes through to the renderer's model matrix. Either
        // source (NPC record default or per-assignment override) triggers a
        // recompute, and the owning VM only needs to set HeightOverride.
        this.WhenAnyValue(x => x.NpcBaseHeight, x => x.HeightOverride)
            .Subscribe(_ => ApplyCharacterScale())
            .DisposeWith(this);

        // Per-axis symmetry mirror: when the user edits one side of a locked axis, mirror
        // the opposite side about 0 so the box stays centered on the symmetry plane. Guarded
        // by _applyingSymmetry so the mirror write doesn't re-enter the handler. We subscribe
        // to the raw PropertyChanged event (rather than six WhenAnyValue chains) so the write
        // ordering stays deterministic inside the guard window.
        PropertyChanged += OnPendingBoxPropertyChanged;
    }

    private void OnPendingBoxPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_applyingSymmetry || !HasPendingBox) return;
        if (PendingBoxSymmetry == SymmetryAxes.None) return;

        _applyingSymmetry = true;
        try
        {
            switch (e.PropertyName)
            {
                case nameof(PendingBoxMinX) when (PendingBoxSymmetry & SymmetryAxes.X) != 0:
                    PendingBoxMaxX = -PendingBoxMinX; break;
                case nameof(PendingBoxMaxX) when (PendingBoxSymmetry & SymmetryAxes.X) != 0:
                    PendingBoxMinX = -PendingBoxMaxX; break;
                case nameof(PendingBoxMinY) when (PendingBoxSymmetry & SymmetryAxes.Y) != 0:
                    PendingBoxMaxY = -PendingBoxMinY; break;
                case nameof(PendingBoxMaxY) when (PendingBoxSymmetry & SymmetryAxes.Y) != 0:
                    PendingBoxMinY = -PendingBoxMaxY; break;
                case nameof(PendingBoxMinZ) when (PendingBoxSymmetry & SymmetryAxes.Z) != 0:
                    PendingBoxMaxZ = -PendingBoxMinZ; break;
                case nameof(PendingBoxMaxZ) when (PendingBoxSymmetry & SymmetryAxes.Z) != 0:
                    PendingBoxMinZ = -PendingBoxMaxZ; break;
                case nameof(PendingBoxSymmetry):
                    // When user changes the symmetry flags, immediately enforce them by
                    // re-centering each locked axis about 0 using the larger-magnitude side.
                    EnforceSymmetryOnAllLockedAxes();
                    break;
            }
        }
        finally
        {
            _applyingSymmetry = false;
        }
    }

    /// <summary>Re-centers each axis with its symmetry flag set so |min| == |max|, using the
    /// larger of the two magnitudes. Called when the user changes the symmetry ComboBox so
    /// existing box values are corrected to match the newly-selected lock instead of waiting
    /// for the next side-edit to propagate.</summary>
    private void EnforceSymmetryOnAllLockedAxes()
    {
        if ((PendingBoxSymmetry & SymmetryAxes.X) != 0)
        {
            float half = MathF.Max(MathF.Abs(PendingBoxMinX), MathF.Abs(PendingBoxMaxX));
            PendingBoxMinX = -half;
            PendingBoxMaxX = half;
        }
        if ((PendingBoxSymmetry & SymmetryAxes.Y) != 0)
        {
            float half = MathF.Max(MathF.Abs(PendingBoxMinY), MathF.Abs(PendingBoxMaxY));
            PendingBoxMinY = -half;
            PendingBoxMaxY = half;
        }
        if ((PendingBoxSymmetry & SymmetryAxes.Z) != 0)
        {
            float half = MathF.Max(MathF.Abs(PendingBoxMinZ), MathF.Abs(PendingBoxMaxZ));
            PendingBoxMinZ = -half;
            PendingBoxMaxZ = half;
        }
    }

    /// <summary>Pushes the effective NPC-height scale to the renderer. Override
    /// wins when set; otherwise the NPC record's Height is used.</summary>
    private void ApplyCharacterScale()
    {
        float scale = HeightOverride ?? NpcBaseHeight;
        if (!float.IsFinite(scale) || scale <= 0f) scale = 1.0f;
        Renderer.ModelScale = scale;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  VIEWER STATE
    // ═══════════════════════════════════════════════════════════════════════

    public ViewerMode Mode { get; set; } = ViewerMode.ReadOnly;
    public string StatusText { get; set; } = "No mesh loaded";
    public bool IsLoading { get; set; }
    public int NpcWeight { get; set; } = 50;

    /// <summary>True when the last BodySlide/BodyGen deformation attempt couldn't find
    /// a sibling .tri for the worn body NIF. Bound to a red banner above the viewport
    /// so the user gets a visible cue to run BodySlide's "Build Morphs" (i.e. generate
    /// a Zeroed Sliders body with morphs). Reset on scene clear and on a successful
    /// .tri load.</summary>
    public bool BodyTriMissing { get; set; } = false;

    /// <summary>The NPC record's Height field, a uniform full-model scale
    /// multiplier (1.0 = default). Refreshed whenever a new NPC is loaded;
    /// acts as the fallback when no per-assignment override is set.</summary>
    public float NpcBaseHeight { get; set; } = 1.0f;

    /// <summary>Per-assignment Height override (Consistency / Specific NPC
    /// Assignment Height field). When non-null, replaces <see cref="NpcBaseHeight"/>
    /// as the applied scale. Null means "fall back to the NPC record".</summary>
    public float? HeightOverride { get; set; }

    /// <summary>Viewport background color, bound to XAML.</summary>
    public MediaColor BackgroundColor { get; set; } = MediaColor.FromRgb(105, 105, 105);

    // ═══════════════════════════════════════════════════════════════════════
    //  LIGHTING CONTROLS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Suppresses renderer push-backs while a preset or constructor is applying values,
    /// so each property setter doesn't independently stomp on sibling lights.</summary>
    private bool _applyingPreset;

    // Intensity is expressed in percent (100 = 1.0 multiplier). Upper bound is
    // deliberately loose — the shader does not clamp, so values above 100 are
    // useful for heavily-attenuating materials.
    private const double IntensityMax = 500;

    public double AmbientIntensity { get; set; } = CharacterViewerLightingPresets.DefaultLayout.Ambient;

    public double KeyLightIntensity   { get; set; } = CharacterViewerLightingPresets.DefaultLayout.KeyIntensity;
    public double KeyLightAzimuth     { get; set; } = CharacterViewerLightingPresets.DefaultLayout.KeyAzimuth;
    public double KeyLightElevation   { get; set; } = CharacterViewerLightingPresets.DefaultLayout.KeyElevation;
    public MediaColor KeyLightColor   { get; set; } = MediaColor.FromRgb(255, 245, 224); // warm key
    public bool KeyLightEnabled       { get; set; } = true;

    public double FillLightIntensity  { get; set; } = CharacterViewerLightingPresets.DefaultLayout.FillIntensity;
    public double FillLightAzimuth    { get; set; } = CharacterViewerLightingPresets.DefaultLayout.FillAzimuth;
    public double FillLightElevation  { get; set; } = CharacterViewerLightingPresets.DefaultLayout.FillElevation;
    public MediaColor FillLightColor  { get; set; } = MediaColor.FromRgb(230, 237, 255);
    public bool FillLightEnabled      { get; set; } = true;

    public double RimLightIntensity   { get; set; } = CharacterViewerLightingPresets.DefaultLayout.RimIntensity;
    public double RimLightAzimuth     { get; set; } = CharacterViewerLightingPresets.DefaultLayout.RimAzimuth;
    public double RimLightElevation   { get; set; } = CharacterViewerLightingPresets.DefaultLayout.RimElevation;
    public MediaColor RimLightColor   { get; set; } = MediaColor.FromRgb(255, 243, 217);
    public bool RimLightEnabled       { get; set; } = true;

    /// <summary>Master toggle for the per-light control UI and the 3D arrow gizmos.
    /// When false, the lighting controls are hidden and arrows are not drawn.</summary>
    public bool ShowLightControls { get; set; } = false;

    /// <summary>When true, every currently-loaded mesh draws its triangle edges
    /// as a wireframe overlay. Used by the BodySlide classifier workflow; toggled
    /// from the viewer toolbar.</summary>
    public bool ShowWireframe { get; set; } = false;

    /// <summary>Master toggle for the BodySlide classifier toolbar cluster
    /// (Wireframe / Pick Vertex / Select Mirror / Clear Picks). Hidden by default
    /// and flipped on only when the viewer is embedded inside the OBody Body
    /// Type Profiles editor, where key-vertex assignment is the whole point.</summary>
    public bool ShowClassifierControls { get; set; } = false;

    /// <summary>One-line summary of the most recent vertex pick for the classifier
    /// pick-info panel. Empty when no picks in the current session.</summary>
    public string LastPickSummary { get; set; } = "";

    /// <summary>Axis selection for <see cref="ProjectLastPickAcrossAxis"/>: 0=X, 1=Y, 2=Z.
    /// Bound to the "Axis" ComboBox in the classifier toolbar; defaults to Z (the most
    /// common use case — pairing front/back midline anchors like NippleFront → UpperSpineBack).</summary>
    public int ProjectAcrossAxisIndex { get; set; } = 2;

    /// <summary>Bound to the pick-info panel's ItemsControl. One row per pick in
    /// the current session, in pick order. Cleared by <see cref="ClearKeyVertexMarkers"/>.</summary>
    public ObservableCollection<PickRow> Picks { get; } = new();

    public RelayCommand CopyPicksToClipboardCommand { get; private set; } = null!;
    public RelayCommand ConfirmPendingBoxCommand     { get; private set; } = null!;
    public RelayCommand CancelPendingBoxCommand      { get; private set; } = null!;
    public RelayCommand ShrinkAlongViewAxisCommand   { get; private set; } = null!;

    /// <summary>
    /// Gates the viewer's informational log output. Errors (<c>LogError</c>) are never gated --
    /// only the noisy per-frame / per-load diagnostics flowing through <see cref="LogVerbose"/>.
    /// Off by default so selecting a preset doesn't bury the classifier's diagnostic log in
    /// viewer chatter. Toggle from the viewer toolbar when debugging mesh/NPC/lighting issues.
    /// </summary>
    public bool VerboseLog { get; set; } = false;

    /// <summary>Routes an informational line to <see cref="_logger"/> only when
    /// <see cref="VerboseLog"/> is on. Keeps error paths (which call <c>_logger.LogError</c>
    /// directly) visible at all times.</summary>
    private void LogVerbose(string message)
    {
        if (VerboseLog) _logger?.LogMessage(message);
    }

    /// <summary>Verbose checkpoint formatter for the NPC-load pipeline. Prefixes the
    /// message with elapsed-from-LoadNpcAsync-entry so timings can be eyeballed across
    /// the parse/skin/dispatch/GL-upload handoff. No-op when the stopwatch is null
    /// (e.g. cancelled load drained nothing) or VerboseLog is off.</summary>
    private void LogLoadCheckpoint(System.Diagnostics.Stopwatch? sw, string checkpoint)
    {
        if (!VerboseLog || sw == null) return;
        _logger?.LogMessage("CharacterViewer: [t+" + sw.ElapsedMilliseconds.ToString().PadLeft(5) + "ms] " + checkpoint);
    }

    /// <summary>0 = none, 1 = key, 2 = fill, 3 = rim. Set when the user clicks
    /// an arrow in the 3D view (or from the UI). Controls which light the
    /// per-light editor panel edits.</summary>
    public int SelectedLightIndex { get; set; } = 0;

    public IReadOnlyList<CharacterViewerLightingLayout> LightingLayouts { get; private set; } =
        CharacterViewerLightingPresets.BuiltInLayouts;

    public IReadOnlyList<CharacterViewerLightingColorScheme> LightingColorSchemes { get; private set; } =
        CharacterViewerLightingPresets.BuiltInColorSchemes;

    public CharacterViewerLightingLayout SelectedLightingLayout { get; set; } =
        CharacterViewerLightingPresets.DefaultLayout;

    public CharacterViewerLightingColorScheme SelectedLightingColorScheme { get; set; } =
        CharacterViewerLightingPresets.DefaultColorScheme;

    public RelayCommand SaveLayoutPresetCommand { get; private set; } = null!;
    public RelayCommand SaveColorSchemePresetCommand { get; private set; } = null!;
    public RelayCommand DeleteSelectedLayoutCommand { get; private set; } = null!;
    public RelayCommand DeleteSelectedColorSchemeCommand { get; private set; } = null!;

    private void InitializeLightingState()
    {
        // Rebuild the combined (built-in + user) preset lists.
        var layouts = new List<CharacterViewerLightingLayout>(CharacterViewerLightingPresets.BuiltInLayouts);
        layouts.AddRange(_generalSettings.UserLightingLayouts);
        LightingLayouts = layouts;

        var schemes = new List<CharacterViewerLightingColorScheme>(CharacterViewerLightingPresets.BuiltInColorSchemes);
        schemes.AddRange(_generalSettings.UserLightingColorSchemes);
        LightingColorSchemes = schemes;

        // Resolve persisted selections (empty / unknown names fall back to defaults).
        SelectedLightingLayout = CharacterViewerLightingPresets.FindLayoutOrDefault(
            _generalSettings.CharacterViewerLightingLayout, _generalSettings.UserLightingLayouts);
        SelectedLightingColorScheme = CharacterViewerLightingPresets.FindColorSchemeOrDefault(
            _generalSettings.CharacterViewerLightingColorScheme, _generalSettings.UserLightingColorSchemes);

        // Seed per-light fields from the resolved layout/scheme before any UI binding
        // runs, so the editor panel opens with values that match the picked preset.
        ApplyPresetToFields(SelectedLightingLayout, SelectedLightingColorScheme);

        // Watch selection changes and push them to the renderer + settings.
        this.WhenAnyValue(x => x.SelectedLightingLayout).Skip(1).Subscribe(layout =>
        {
            if (layout == null) return;
            _generalSettings.CharacterViewerLightingLayout = layout.Name;
            ApplyPresetToFields(layout, SelectedLightingColorScheme);
            PushAllLightsToRenderer();
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.SelectedLightingColorScheme).Skip(1).Subscribe(scheme =>
        {
            if (scheme == null) return;
            _generalSettings.CharacterViewerLightingColorScheme = scheme.Name;
            KeyLightColor = MediaFromVec3(scheme.KeyColor);
            FillLightColor = MediaFromVec3(scheme.FillColor);
            RimLightColor = MediaFromVec3(scheme.RimColor);
            PushAllLightsToRenderer();
        }).DisposeWith(this);

        // Any per-light edit (or enable toggle) re-pushes to the renderer.
        // Skip(1) suppresses the initial value emission so we don't push during
        // construction when the renderer isn't yet initialized. Split per-light
        // because ReactiveUI's WhenAnyValue overloads cap out at a modest arity.
        this.WhenAnyValue(x => x.AmbientIntensity)
            .Skip(1).Subscribe(_ => PushAllLightsToRenderer()).DisposeWith(this);

        this.WhenAnyValue(
            x => x.KeyLightIntensity, x => x.KeyLightAzimuth, x => x.KeyLightElevation,
            x => x.KeyLightColor, x => x.KeyLightEnabled)
            .Skip(1).Subscribe(_ => PushAllLightsToRenderer()).DisposeWith(this);

        this.WhenAnyValue(
            x => x.FillLightIntensity, x => x.FillLightAzimuth, x => x.FillLightElevation,
            x => x.FillLightColor, x => x.FillLightEnabled)
            .Skip(1).Subscribe(_ => PushAllLightsToRenderer()).DisposeWith(this);

        this.WhenAnyValue(
            x => x.RimLightIntensity, x => x.RimLightAzimuth, x => x.RimLightElevation,
            x => x.RimLightColor, x => x.RimLightEnabled)
            .Skip(1).Subscribe(_ => PushAllLightsToRenderer()).DisposeWith(this);

        this.WhenAnyValue(x => x.SelectedLightIndex)
            .Skip(1).Subscribe(_ => PushAllLightsToRenderer()).DisposeWith(this);

        this.WhenAnyValue(x => x.ShowLightControls).Subscribe(v =>
        {
            Renderer.ShowKeyLightVisualization = v;
            if (!v) SelectedLightIndex = 0;
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.ShowWireframe).Subscribe(v =>
        {
            foreach (var mesh in Renderer.Meshes)
                mesh.ShowWireframe = v;
        }).DisposeWith(this);

        // Commands
        SaveLayoutPresetCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => SaveCurrentAsLayoutPreset());
        SaveColorSchemePresetCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => SaveCurrentAsColorScheme());
        DeleteSelectedLayoutCommand = new RelayCommand(
            canExecute: _ => SelectedLightingLayout != null && !SelectedLightingLayout.IsBuiltIn,
            execute: _ => DeleteSelectedLayout());
        DeleteSelectedColorSchemeCommand = new RelayCommand(
            canExecute: _ => SelectedLightingColorScheme != null && !SelectedLightingColorScheme.IsBuiltIn,
            execute: _ => DeleteSelectedColorScheme());
        CopyPicksToClipboardCommand = new RelayCommand(
            canExecute: _ => Picks.Count > 0,
            execute: _ => CopyPicksToClipboard());
        ConfirmPendingBoxCommand = new RelayCommand(
            canExecute: _ => HasPendingBox,
            execute: _ => ConfirmPendingBox());
        CancelPendingBoxCommand = new RelayCommand(
            canExecute: _ => HasPendingBox,
            execute: _ => CancelPendingBox());
        ShrinkAlongViewAxisCommand = new RelayCommand(
            canExecute: _ => HasPendingBox,
            execute: _ => ShrinkPendingBoxAlongViewAxis());
    }

    /// <summary>Normalizes the per-light fields from a layout+scheme, suppressing
    /// intermediate renderer pushes so the combined state is sent once at the end.</summary>
    private void ApplyPresetToFields(CharacterViewerLightingLayout layout,
        CharacterViewerLightingColorScheme colors)
    {
        _applyingPreset = true;
        try
        {
            AmbientIntensity   = layout.Ambient;
            KeyLightIntensity  = layout.KeyIntensity;
            KeyLightAzimuth    = layout.KeyAzimuth;
            KeyLightElevation  = layout.KeyElevation;
            KeyLightColor      = MediaFromVec3(colors.KeyColor);

            FillLightIntensity = layout.FillIntensity;
            FillLightAzimuth   = layout.FillAzimuth;
            FillLightElevation = layout.FillElevation;
            FillLightColor     = MediaFromVec3(colors.FillColor);

            RimLightIntensity  = layout.RimIntensity;
            RimLightAzimuth    = layout.RimAzimuth;
            RimLightElevation  = layout.RimElevation;
            RimLightColor      = MediaFromVec3(colors.RimColor);
        }
        finally
        {
            _applyingPreset = false;
        }
    }

    private void PushAllLightsToRenderer()
    {
        if (_applyingPreset) return;

        // Clamp & push. Intensity is divided by 100 since the shader expects a multiplier.
        var amb = Math.Clamp(AmbientIntensity, 0, IntensityMax) / 100.0;
        var keyI = Math.Clamp(KeyLightIntensity, 0, IntensityMax) / 100.0;
        var fillI = Math.Clamp(FillLightIntensity, 0, IntensityMax) / 100.0;
        var rimI = Math.Clamp(RimLightIntensity, 0, IntensityMax) / 100.0;

        Renderer.SetAmbientIntensity((float)amb);
        Renderer.SetKeyLight ((float)KeyLightAzimuth,  (float)KeyLightElevation,  (float)keyI,  Vec3FromMedia(KeyLightColor));
        Renderer.SetFillLight((float)FillLightAzimuth, (float)FillLightElevation, (float)fillI, Vec3FromMedia(FillLightColor));
        Renderer.SetRimLight ((float)RimLightAzimuth,  (float)RimLightElevation,  (float)rimI,  Vec3FromMedia(RimLightColor));

        Renderer.Lights[1].Type = KeyLightEnabled  ? 2 : 0;
        Renderer.Lights[2].Type = FillLightEnabled ? 2 : 0;
        Renderer.Lights[3].Type = RimLightEnabled  ? 2 : 0;

        Renderer.SelectedLightIndex = SelectedLightIndex;
    }

    private void SaveCurrentAsLayoutPreset()
    {
        string? name = PromptForName("Save Lighting Layout",
            "Enter a name for this lighting layout preset:",
            SelectedLightingLayout?.Name ?? "My Layout");
        if (string.IsNullOrWhiteSpace(name)) return;

        // Prevent clobbering a built-in name, which would be unreachable after save
        // (FindByName returns the built-in first).
        foreach (var b in CharacterViewerLightingPresets.BuiltInLayouts)
        {
            if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                MessageWindow.DisplayNotificationOK("Name in use",
                    $"'{name}' is a built-in preset name. Please choose a different name.");
                return;
            }
        }

        var existing = _generalSettings.UserLightingLayouts.FirstOrDefault(l =>
            string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null &&
            !MessageWindow.DisplayNotificationYesNo("Overwrite preset?",
                $"A user preset named '{name}' already exists. Overwrite it?"))
        {
            return;
        }
        if (existing != null) _generalSettings.UserLightingLayouts.Remove(existing);

        var preset = new CharacterViewerLightingLayout
        {
            Name = name, IsBuiltIn = false,
            Ambient = AmbientIntensity,
            KeyAzimuth = KeyLightAzimuth, KeyElevation = KeyLightElevation, KeyIntensity = KeyLightIntensity,
            FillAzimuth = FillLightAzimuth, FillElevation = FillLightElevation, FillIntensity = FillLightIntensity,
            RimAzimuth = RimLightAzimuth, RimElevation = RimLightElevation, RimIntensity = RimLightIntensity,
        };
        _generalSettings.UserLightingLayouts.Add(preset);
        RebuildLayoutList();
        SelectedLightingLayout = preset;
    }

    private void SaveCurrentAsColorScheme()
    {
        string? name = PromptForName("Save Color Scheme",
            "Enter a name for this color scheme preset:",
            SelectedLightingColorScheme?.Name ?? "My Colors");
        if (string.IsNullOrWhiteSpace(name)) return;

        foreach (var b in CharacterViewerLightingPresets.BuiltInColorSchemes)
        {
            if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                MessageWindow.DisplayNotificationOK("Name in use",
                    $"'{name}' is a built-in preset name. Please choose a different name.");
                return;
            }
        }

        var existing = _generalSettings.UserLightingColorSchemes.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null &&
            !MessageWindow.DisplayNotificationYesNo("Overwrite preset?",
                $"A user color scheme named '{name}' already exists. Overwrite it?"))
        {
            return;
        }
        if (existing != null) _generalSettings.UserLightingColorSchemes.Remove(existing);

        var scheme = new CharacterViewerLightingColorScheme
        {
            Name = name, IsBuiltIn = false,
            KeyColor = Vec3FromMedia(KeyLightColor),
            FillColor = Vec3FromMedia(FillLightColor),
            RimColor = Vec3FromMedia(RimLightColor),
        };
        _generalSettings.UserLightingColorSchemes.Add(scheme);
        RebuildColorSchemeList();
        SelectedLightingColorScheme = scheme;
    }

    private void DeleteSelectedLayout()
    {
        var sel = SelectedLightingLayout;
        if (sel == null || sel.IsBuiltIn) return;
        if (!MessageWindow.DisplayNotificationYesNo("Delete preset?",
                $"Delete the user lighting layout '{sel.Name}'?")) return;

        _generalSettings.UserLightingLayouts.Remove(sel);
        RebuildLayoutList();
        SelectedLightingLayout = CharacterViewerLightingPresets.DefaultLayout;
    }

    private void DeleteSelectedColorScheme()
    {
        var sel = SelectedLightingColorScheme;
        if (sel == null || sel.IsBuiltIn) return;
        if (!MessageWindow.DisplayNotificationYesNo("Delete preset?",
                $"Delete the user color scheme '{sel.Name}'?")) return;

        _generalSettings.UserLightingColorSchemes.Remove(sel);
        RebuildColorSchemeList();
        SelectedLightingColorScheme = CharacterViewerLightingPresets.DefaultColorScheme;
    }

    private void RebuildLayoutList()
    {
        var layouts = new List<CharacterViewerLightingLayout>(CharacterViewerLightingPresets.BuiltInLayouts);
        layouts.AddRange(_generalSettings.UserLightingLayouts);
        LightingLayouts = layouts;
    }

    private void RebuildColorSchemeList()
    {
        var schemes = new List<CharacterViewerLightingColorScheme>(CharacterViewerLightingPresets.BuiltInColorSchemes);
        schemes.AddRange(_generalSettings.UserLightingColorSchemes);
        LightingColorSchemes = schemes;
    }

    private static string? PromptForName(string title, string message, string defaultValue)
    {
        string? result = null;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var window = new Window
            {
                Title = title,
                Width = 380,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
                ResizeMode = ResizeMode.NoResize,
                Background = System.Windows.Media.Brushes.DimGray,
            };

            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var label = new System.Windows.Controls.TextBlock
            {
                Text = message,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap,
            };
            System.Windows.Controls.Grid.SetRow(label, 0);
            grid.Children.Add(label);

            var textBox = new System.Windows.Controls.TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 13,
            };
            textBox.SelectAll();
            textBox.Focus();
            System.Windows.Controls.Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var okButton = new System.Windows.Controls.Button
            {
                Content = "OK", Width = 70, Margin = new Thickness(0, 0, 6, 0), IsDefault = true,
            };
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "Cancel", Width = 70, IsCancel = true,
            };
            okButton.Click += (_, _) => { result = textBox.Text; window.Close(); };
            cancelButton.Click += (_, _) => { result = null; window.Close(); };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            System.Windows.Controls.Grid.SetRow(buttonPanel, 3);
            grid.Children.Add(buttonPanel);

            window.Content = grid;
            window.Loaded += (_, _) => textBox.Focus();
            window.ShowDialog();
        });
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static MediaColor MediaFromVec3(OpenTK.Mathematics.Vector3 v) =>
        MediaColor.FromRgb(
            (byte)Math.Clamp((int)Math.Round(v.X * 255f), 0, 255),
            (byte)Math.Clamp((int)Math.Round(v.Y * 255f), 0, 255),
            (byte)Math.Clamp((int)Math.Round(v.Z * 255f), 0, 255));

    private static OpenTK.Mathematics.Vector3 Vec3FromMedia(MediaColor c) =>
        new(c.R / 255f, c.G / 255f, c.B / 255f);

    public void LogLightingSettings()
    {
        LogVerbose($"CharacterViewer: LIGHTING — Layout='{SelectedLightingLayout?.Name}', " +
            $"Colors='{SelectedLightingColorScheme?.Name}', Ambient={AmbientIntensity:F0}%, " +
            $"Key={KeyLightIntensity:F0}%@({KeyLightAzimuth:F0}°,{KeyLightElevation:F0}°), " +
            $"Fill={FillLightIntensity:F0}%@({FillLightAzimuth:F0}°,{FillLightElevation:F0}°), " +
            $"Rim={RimLightIntensity:F0}%@({RimLightAzimuth:F0}°,{RimLightElevation:F0}°)");
    }

    /// <summary>
    /// Tests whether a screen-space click lies on one of the light-direction
    /// arrow gizmos. Returns the light index (1=key, 2=fill, 3=rim) or 0 for miss.
    /// Uses ray vs. capsule distance along the arrow's shaft — sufficient for
    /// gizmo picking since the arrows are drawn thick enough.
    /// </summary>
    public int HitTestLightArrow(float mouseX, float mouseY, float viewportWidth, float viewportHeight)
    {
        if (!ShowLightControls) return 0;

        var (origin, rayDir) = Camera.ScreenPointToRay(mouseX, mouseY, viewportWidth, viewportHeight);

        int best = 0;
        float bestDepth = float.MaxValue;
        for (int slot = 1; slot <= 3; slot++)
        {
            if (!Renderer.TryGetArrowSegment(slot, out var tail, out var tip, out var radius)) continue;
            if (RayCapsuleHit(origin, rayDir, tail, tip, radius, out float tAlongRay))
            {
                if (tAlongRay < bestDepth)
                {
                    bestDepth = tAlongRay;
                    best = slot;
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Closest-distance ray vs. capsule test. Returns true if the perpendicular
    /// distance from the ray to the segment (tail, tip) is ≤ radius, and
    /// outputs the parametric ray depth at the closest point.
    /// </summary>
    private static bool RayCapsuleHit(
        OpenTK.Mathematics.Vector3 rayOrigin, OpenTK.Mathematics.Vector3 rayDir,
        OpenTK.Mathematics.Vector3 tail, OpenTK.Mathematics.Vector3 tip,
        float radius, out float tAlongRay)
    {
        tAlongRay = 0f;
        var d1 = rayDir; // assume ~unit length
        var d2 = tip - tail;
        float len2 = d2.LengthSquared;
        if (len2 < 1e-6f) return false;

        var r = rayOrigin - tail;
        float a = OpenTK.Mathematics.Vector3.Dot(d1, d1);
        float e = OpenTK.Mathematics.Vector3.Dot(d2, d2);
        float f = OpenTK.Mathematics.Vector3.Dot(d2, r);
        float c = OpenTK.Mathematics.Vector3.Dot(d1, r);
        float b = OpenTK.Mathematics.Vector3.Dot(d1, d2);
        float denom = a * e - b * b;

        float s, t;
        if (denom != 0f) s = Math.Clamp((b * f - c * e) / denom, 0f, float.MaxValue);
        else s = 0f;
        t = (b * s + f) / e;

        if (t < 0f) { t = 0f; s = Math.Clamp(-c / a, 0f, float.MaxValue); }
        else if (t > 1f) { t = 1f; s = Math.Clamp((b - c) / a, 0f, float.MaxValue); }

        var closestOnRay = rayOrigin + d1 * s;
        var closestOnSeg = tail + d2 * t;
        float distSq = (closestOnRay - closestOnSeg).LengthSquared;

        if (distSq > radius * radius) return false;
        tAlongRay = s;
        return s >= 0f;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HIT TESTING
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Performs a ray cast from screen coordinates and returns the closest hit mesh, or null.
    /// Uses Möller-Trumbore ray-triangle intersection against CPU-side geometry.
    /// </summary>
    public GlMesh? HitTest(float mouseX, float mouseY, float viewportWidth, float viewportHeight)
    {
        var (origin, direction) = Camera.ScreenPointToRay(mouseX, mouseY, viewportWidth, viewportHeight);

        GlMesh? closestMesh = null;
        float closestDist = float.MaxValue;

        foreach (var mesh in Renderer.Meshes)
        {
            if (!mesh.IsRendering) continue;
            if (mesh.CpuPositions == null || mesh.CpuIndices == null) continue;

            if (RayIntersectsMesh(origin, direction, mesh, out float dist) && dist < closestDist)
            {
                closestDist = dist;
                closestMesh = mesh;
            }
        }

        return closestMesh;
    }

    /// <summary>
    /// Möller-Trumbore ray-triangle intersection test against a mesh's CPU-side geometry.
    /// </summary>
    private static bool RayIntersectsMesh(
        OpenTK.Mathematics.Vector3 rayOrigin,
        OpenTK.Mathematics.Vector3 rayDir,
        GlMesh mesh,
        out float hitDistance)
    {
        hitDistance = float.MaxValue;
        bool anyHit = false;

        var positions = mesh.CpuPositions!;
        var indices = mesh.CpuIndices!;
        const float epsilon = 1e-6f;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            var v0Sys = positions[indices[i]];
            var v1Sys = positions[indices[i + 1]];
            var v2Sys = positions[indices[i + 2]];

            // Convert System.Numerics.Vector3 to OpenTK.Mathematics.Vector3
            var v0 = new OpenTK.Mathematics.Vector3(v0Sys.X, v0Sys.Y, v0Sys.Z);
            var v1 = new OpenTK.Mathematics.Vector3(v1Sys.X, v1Sys.Y, v1Sys.Z);
            var v2 = new OpenTK.Mathematics.Vector3(v2Sys.X, v2Sys.Y, v2Sys.Z);

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var h = OpenTK.Mathematics.Vector3.Cross(rayDir, edge2);
            float a = OpenTK.Mathematics.Vector3.Dot(edge1, h);

            if (a > -epsilon && a < epsilon) continue; // parallel

            float f = 1f / a;
            var s = rayOrigin - v0;
            float u = f * OpenTK.Mathematics.Vector3.Dot(s, h);
            if (u < 0f || u > 1f) continue;

            var q = OpenTK.Mathematics.Vector3.Cross(s, edge1);
            float v = f * OpenTK.Mathematics.Vector3.Dot(rayDir, q);
            if (v < 0f || u + v > 1f) continue;

            float t = f * OpenTK.Mathematics.Vector3.Dot(edge2, q);
            if (t > epsilon && t < hitDistance)
            {
                hitDistance = t;
                anyHit = true;
            }
        }

        return anyHit;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  KEY-VERTEX PICKING (Phase 2 — BodySlide classifier)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When true, a left-click in the viewport picks the nearest triangle
    /// vertex under the cursor instead of orbiting the camera. Phase 2
    /// scaffolding for the BodySlide classifier workflow; Phase 4 will build
    /// the proper profile-editor UI on top of this.
    /// </summary>
    public bool IsKeyVertexPickMode { get; set; } = false;

    /// <summary>
    /// When true, a left-mouse drag in the viewport paints a 2D screen rectangle whose
    /// contents are projected back onto the starting mesh to form a mesh-local AABB; the
    /// rectangle + selected <see cref="PendingBoxCriterion"/> are then fired through
    /// <see cref="KeyVertexBoxPicked"/>. Mutually exclusive with orbit — both modes share
    /// left-drag, but BB pick mode suppresses camera orbit while engaged.
    /// </summary>
    public bool IsBoundingBoxPickMode { get; set; } = false;

    /// <summary>
    /// Current <see cref="BoxCriterionSelection"/> chosen in the viewer combo. Read at the
    /// moment the user releases the mouse so the emitted pick carries the criterion the
    /// user meant. Default <see cref="BoxCriterionSelection.MaxX"/>.
    /// </summary>
    public BoxCriterionSelection PendingBoxCriterion { get; set; } = BoxCriterionSelection.MaxX;

    // ──────────────── Pending (pre-commit) box state ────────────────
    // After a BB drag the resulting AABB is parked here instead of firing immediately, so the
    // user can adjust per-axis min/max (most importantly the depth axis, to exclude things like
    // an overhanging belly from the hip box) before confirming. The wireframe overlay reads
    // these fields each GL frame; Confirm fires AnyKeyVertexBoxPicked with the final values.

    /// <summary>True while a box is drawn but not yet confirmed. Shows the wireframe + edit panel.</summary>
    public bool HasPendingBox { get; set; }

    public string PendingBoxShapeName { get; set; } = "";
    public float PendingBoxMinX { get; set; }
    public float PendingBoxMaxX { get; set; }
    public float PendingBoxMinY { get; set; }
    public float PendingBoxMaxY { get; set; }
    public float PendingBoxMinZ { get; set; }
    public float PendingBoxMaxZ { get; set; }

    /// <summary>Criterion for the pending pick. Seeded at drag time from the toolbar combo; the
    /// user can still change it via the pending-box panel combo before confirming.</summary>
    public BoxCriterionSelection PendingBoxFinalCriterion { get; set; }

    /// <summary>Per-axis symmetry lock applied to the pending box. When a flag is set, editing
    /// one side of that axis (e.g., PendingBoxMinX) auto-mirrors the opposite side about 0 so
    /// the box stays centered on the world-space symmetry plane. Auto-seeded in
    /// <see cref="BeginPendingBox"/> when the captured box already straddles an axis within
    /// 10%; user can also set it manually via the pending-box panel combo.</summary>
    public SymmetryAxes PendingBoxSymmetry { get; set; } = SymmetryAxes.None;

    /// <summary>The eight valid <see cref="SymmetryAxes"/> flag combinations in display order,
    /// used as the ItemsSource for the pending-box symmetry ComboBox. Enum.GetValues returns
    /// only the single-flag members (None, X, Y, Z) for a [Flags] enum, so the combinations
    /// are enumerated explicitly.</summary>
    public static IReadOnlyList<SymmetryAxes> SymmetryAxesChoices { get; } = new[]
    {
        SymmetryAxes.None,
        SymmetryAxes.X,
        SymmetryAxes.Y,
        SymmetryAxes.Z,
        SymmetryAxes.X | SymmetryAxes.Y,
        SymmetryAxes.X | SymmetryAxes.Z,
        SymmetryAxes.Y | SymmetryAxes.Z,
        SymmetryAxes.X | SymmetryAxes.Y | SymmetryAxes.Z,
    };

    // Guard so the auto-mirror side-effects don't recurse (PropertyChanged fires from the
    // mirror write, which would then re-enter the same handler).
    private bool _applyingSymmetry;

    /// <summary>
    /// Fired once per successful key-vertex pick (after the marker has been added). Phase 4's
    /// BodyTypeProfile editor subscribes when active so picks route into the selected profile.
    /// Carries the same payload as <see cref="NotifyKeyVertexPicked"/>.
    /// </summary>
    public event Action<KeyVertexPick>? KeyVertexPicked;

    /// <summary>
    /// Process-wide pick fan-out. Fires for every successful pick from any viewer instance.
    /// Lets long-lived UIs (the BodyTypeProfile editor) subscribe once instead of attaching
    /// to whichever viewer happens to be visible. Sender is the originating viewer.
    /// </summary>
    public static event Action<VM_CharacterViewer, KeyVertexPick>? AnyKeyVertexPicked;

    /// <summary>Per-viewer fan-out for bounding-box picks (parallel to <see cref="KeyVertexPicked"/>).</summary>
    public event Action<KeyVertexBoxPick>? KeyVertexBoxPicked;

    /// <summary>Process-wide fan-out for BB picks (parallel to <see cref="AnyKeyVertexPicked"/>).</summary>
    public static event Action<VM_CharacterViewer, KeyVertexBoxPick>? AnyKeyVertexBoxPicked;

    /// <summary>
    /// Fires at the end of ApplyBodySlide, after CpuPositions have been refreshed. The
    /// BodyTypeProfile editor subscribes so live measurement readouts recompute after the
    /// deferred-drain path (ApplyBodySlide called before ProcessPendingScene has built the
    /// meshes — direct-path measurements would otherwise see the pre-deform geometry).
    /// </summary>
    public event Action? BodySlideApplied;

    /// <summary>Result of a key-vertex pick: the hit mesh, the index into its
    /// CpuPositions array, and the vertex position in that same (pre-ModelScale)
    /// space so the renderer can re-project it as ModelScale changes.</summary>
    public readonly struct KeyVertexPick
    {
        public KeyVertexPick(GlMesh mesh, int vertexIndex, OpenTK.Mathematics.Vector3 localPos)
        {
            Mesh = mesh;
            VertexIndex = vertexIndex;
            LocalPos = localPos;
        }
        public GlMesh Mesh { get; }
        public int VertexIndex { get; }
        public OpenTK.Mathematics.Vector3 LocalPos { get; }
    }

    /// <summary>Result of a bounding-box authoring drag: the target mesh's shape name, the
    /// mesh-local AABB (pre-ModelScale) computed by projecting that mesh's CpuPositions into
    /// screen space and keeping the ones inside the drag rect, and the UX-level criterion the
    /// user had selected when releasing the mouse. The editor expands <c>Mirror*</c> values
    /// into two paired <see cref="NamedKeyVertex"/> rows; persistence only stores single-axis
    /// <see cref="BoundingBoxCriterion"/>.</summary>
    public readonly struct KeyVertexBoxPick
    {
        public KeyVertexBoxPick(
            string shapeName,
            OpenTK.Mathematics.Vector3 boxMin,
            OpenTK.Mathematics.Vector3 boxMax,
            BoxCriterionSelection criterion)
        {
            ShapeName = shapeName ?? "";
            BoxMin = boxMin;
            BoxMax = boxMax;
            Criterion = criterion;
        }
        public string ShapeName { get; }
        public OpenTK.Mathematics.Vector3 BoxMin { get; }
        public OpenTK.Mathematics.Vector3 BoxMax { get; }
        public BoxCriterionSelection Criterion { get; }
    }

    /// <summary>
    /// Ray-casts against every renderable mesh and returns the vertex closest
    /// (by barycentric weight) to the hit point on the nearest triangle, or
    /// null if the cursor missed all meshes.
    /// </summary>
    public KeyVertexPick? HitTestKeyVertex(
        float mouseX, float mouseY, float viewportWidth, float viewportHeight)
    {
        var (origin, direction) = Camera.ScreenPointToRay(
            mouseX, mouseY, viewportWidth, viewportHeight);

        GlMesh? closestMesh = null;
        int closestVertexIndex = -1;
        OpenTK.Mathematics.Vector3 closestLocal = default;
        float closestDist = float.MaxValue;

        foreach (var mesh in Renderer.Meshes)
        {
            if (!mesh.IsRendering) continue;
            if (mesh.CpuPositions == null || mesh.CpuIndices == null) continue;

            if (RayIntersectsMeshPickVertex(origin, direction, mesh,
                out float dist, out int vertexIndex, out var localPos)
                && dist < closestDist)
            {
                closestDist = dist;
                closestMesh = mesh;
                closestVertexIndex = vertexIndex;
                closestLocal = localPos;
            }
        }

        if (closestMesh == null || closestVertexIndex < 0) return null;
        return new KeyVertexPick(closestMesh, closestVertexIndex, closestLocal);
    }

    /// <summary>
    /// Same Möller-Trumbore intersection as <see cref="RayIntersectsMesh"/>,
    /// but at the closest hit it resolves the nearest of the triangle's three
    /// vertices by barycentric weight and returns that vertex's index and
    /// position (in mesh-local / pre-ModelScale space).
    /// </summary>
    private static bool RayIntersectsMeshPickVertex(
        OpenTK.Mathematics.Vector3 rayOrigin,
        OpenTK.Mathematics.Vector3 rayDir,
        GlMesh mesh,
        out float hitDistance,
        out int hitVertexIndex,
        out OpenTK.Mathematics.Vector3 hitVertexLocal)
    {
        hitDistance = float.MaxValue;
        hitVertexIndex = -1;
        hitVertexLocal = default;
        bool anyHit = false;

        var positions = mesh.CpuPositions!;
        var indices = mesh.CpuIndices!;
        const float epsilon = 1e-6f;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];

            var v0Sys = positions[i0];
            var v1Sys = positions[i1];
            var v2Sys = positions[i2];

            var v0 = new OpenTK.Mathematics.Vector3(v0Sys.X, v0Sys.Y, v0Sys.Z);
            var v1 = new OpenTK.Mathematics.Vector3(v1Sys.X, v1Sys.Y, v1Sys.Z);
            var v2 = new OpenTK.Mathematics.Vector3(v2Sys.X, v2Sys.Y, v2Sys.Z);

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var h = OpenTK.Mathematics.Vector3.Cross(rayDir, edge2);
            float a = OpenTK.Mathematics.Vector3.Dot(edge1, h);
            if (a > -epsilon && a < epsilon) continue;

            float f = 1f / a;
            var s = rayOrigin - v0;
            float u = f * OpenTK.Mathematics.Vector3.Dot(s, h);
            if (u < 0f || u > 1f) continue;

            var q = OpenTK.Mathematics.Vector3.Cross(s, edge1);
            float v = f * OpenTK.Mathematics.Vector3.Dot(rayDir, q);
            if (v < 0f || u + v > 1f) continue;

            float t = f * OpenTK.Mathematics.Vector3.Dot(edge2, q);
            if (t > epsilon && t < hitDistance)
            {
                hitDistance = t;

                // Barycentric weights: w0 belongs to v0, w1 to v1, w2 to v2.
                // The vertex with the largest weight is the one closest to
                // the hit point on the triangle's plane.
                float w0 = 1f - u - v;
                float w1 = u;
                float w2 = v;

                if (w0 >= w1 && w0 >= w2) { hitVertexIndex = i0; hitVertexLocal = v0; }
                else if (w1 >= w2)        { hitVertexIndex = i1; hitVertexLocal = v1; }
                else                       { hitVertexIndex = i2; hitVertexLocal = v2; }

                anyHit = true;
            }
        }

        return anyHit;
    }

    /// <summary>
    /// Called by the view when the user clicks a vertex in key-vertex pick
    /// mode. Phase 2 behavior: drop a visible marker at the picked position
    /// and log the hit. Phase 3 will route this into a BodyTypeProfile.
    /// </summary>
    public void NotifyKeyVertexPicked(KeyVertexPick pick)
    {
        // Dedup: a vertex already captured on the same shape is a no-op. Keeps
        // SelectMirrorPicks from re-adding pre-existing partners and keeps the
        // picks panel list clean when mirror targets overlap prior picks.
        var shapeName = pick.Mesh?.ShapeName ?? "";
        for (int i = 0; i < _keyVertexPicks.Count; i++)
        {
            var existing = _keyVertexPicks[i];
            if (existing.VertexIndex == pick.VertexIndex
                && string.Equals(existing.Mesh?.ShapeName ?? "", shapeName, StringComparison.OrdinalIgnoreCase))
            {
                LogVerbose("CharacterViewer: skip duplicate pick #" + pick.VertexIndex
                    + " on '" + shapeName + "'.");
                return;
            }
        }

        Renderer.KeyVertexMarkers.Add(pick.LocalPos);
        // Parallel bookkeeping so SelectMirrorPicks can resolve each marker back
        // to its (mesh, vertexIndex) without having to guess from position alone.
        _keyVertexPicks.Add(pick);

        var row = new PickRow
        {
            ShapeName = shapeName,
            VertexIndex = pick.VertexIndex,
        };
        row.UpdatePosition(pick.LocalPos.X, pick.LocalPos.Y, pick.LocalPos.Z);
        Picks.Add(row);
        LastPickSummary = row.Display;

        LogVerbose(
            "CharacterViewer: picked vertex #" + pick.VertexIndex
            + " on '" + pick.Mesh.ShapeName + "'"
            + " at (" + pick.LocalPos.X.ToString("F2") + ", "
                     + pick.LocalPos.Y.ToString("F2") + ", "
                     + pick.LocalPos.Z.ToString("F2") + ")");

        KeyVertexPicked?.Invoke(pick);
        AnyKeyVertexPicked?.Invoke(this, pick);
    }

    /// <summary>
    /// Resolves a 2D drag rectangle (in <see cref="GlControl"/> logical units) into a
    /// mesh-local AABB by projecting every vertex of every rendered mesh into screen space,
    /// keeping those inside the rect, and bucketing them by mesh. The bucket with the most
    /// hits wins — this is more robust than requiring the start-click to land on the mesh,
    /// since users often begin the drag in empty space. Returns null when no vertex of any
    /// visible mesh fell inside the rect. Positions returned in the same pre-ModelScale
    /// mesh-local space as <see cref="GetShapePositions"/> / <see cref="TryGetCurrentVertex"/>.
    /// </summary>
    public KeyVertexBoxPick? ComputeBoxFromScreenRect(
        float x0, float y0, float x1, float y1,
        float viewportWidth, float viewportHeight,
        BoxCriterionSelection criterion)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0) return null;

        float rectMinX = MathF.Min(x0, x1);
        float rectMaxX = MathF.Max(x0, x1);
        float rectMinY = MathF.Min(y0, y1);
        float rectMaxY = MathF.Max(y0, y1);
        // Guard against accidental tiny drags — treat as a click, not a box selection.
        if ((rectMaxX - rectMinX) < 4f || (rectMaxY - rectMinY) < 4f) return null;

        float aspect = viewportWidth / viewportHeight;
        var viewProj = Camera.GetViewMatrix() * Camera.GetProjectionMatrix(aspect);
        float modelScale = Renderer.ModelScale;

        GlMesh? bestMesh = null;
        int bestHitCount = 0;
        OpenTK.Mathematics.Vector3 bestMin = default;
        OpenTK.Mathematics.Vector3 bestMax = default;

        foreach (var mesh in Renderer.Meshes)
        {
            if (!mesh.IsRendering) continue;
            if (mesh.CpuPositions == null || mesh.CpuPositions.Length == 0) continue;

            int hits = 0;
            var localMin = new OpenTK.Mathematics.Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var localMax = new OpenTK.Mathematics.Vector3(float.MinValue, float.MinValue, float.MinValue);

            var positions = mesh.CpuPositions;
            for (int i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                // Apply the renderer's model scale so projection matches what the user sees.
                var world = new OpenTK.Mathematics.Vector4(
                    p.X * modelScale, p.Y * modelScale, p.Z * modelScale, 1f);
                // Row-vector convention (matches Camera.ScreenPointToRay's inverse path).
                var clip = world * viewProj;
                if (clip.W <= 0f) continue; // behind the near plane
                float ndcX = clip.X / clip.W;
                float ndcY = clip.Y / clip.W;
                // OpenGL NDC y-up -> WPF window y-down
                float screenX = (ndcX * 0.5f + 0.5f) * viewportWidth;
                float screenY = (1f - (ndcY * 0.5f + 0.5f)) * viewportHeight;

                if (screenX < rectMinX || screenX > rectMaxX) continue;
                if (screenY < rectMinY || screenY > rectMaxY) continue;

                hits++;
                if (p.X < localMin.X) localMin.X = p.X;
                if (p.Y < localMin.Y) localMin.Y = p.Y;
                if (p.Z < localMin.Z) localMin.Z = p.Z;
                if (p.X > localMax.X) localMax.X = p.X;
                if (p.Y > localMax.Y) localMax.Y = p.Y;
                if (p.Z > localMax.Z) localMax.Z = p.Z;
            }

            if (hits > bestHitCount)
            {
                bestHitCount = hits;
                bestMesh = mesh;
                bestMin = localMin;
                bestMax = localMax;
            }
        }

        if (bestMesh == null || bestHitCount == 0) return null;

        LogVerbose(
            "CharacterViewer: BB drag captured " + bestHitCount
            + " vertices on '" + bestMesh.ShapeName + "'"
            + " -> mesh-local AABB min(" + bestMin.X.ToString("F2") + ","
                + bestMin.Y.ToString("F2") + "," + bestMin.Z.ToString("F2") + ")"
            + " max(" + bestMax.X.ToString("F2") + ","
                + bestMax.Y.ToString("F2") + "," + bestMax.Z.ToString("F2") + ")"
            + " criterion=" + criterion);

        return new KeyVertexBoxPick(bestMesh.ShapeName ?? "", bestMin, bestMax, criterion);
    }

    /// <summary>Entry point for the codebehind drag handler — fans the completed BB pick
    /// out to per-viewer and process-wide subscribers (parallels
    /// <see cref="NotifyKeyVertexPicked"/>).</summary>
    public void NotifyKeyVertexBoxPicked(KeyVertexBoxPick pick)
    {
        KeyVertexBoxPicked?.Invoke(pick);
        AnyKeyVertexBoxPicked?.Invoke(this, pick);
    }

    /// <summary>Parks a freshly-captured rectangle pick in the pending-box editor state
    /// instead of firing it. The wireframe overlay + edit panel become visible, the user
    /// tweaks the six min/max values (and optionally the criterion), then
    /// <see cref="ConfirmPendingBox"/> or <see cref="CancelPendingBox"/> finalizes.</summary>
    public void BeginPendingBox(KeyVertexBoxPick initial)
    {
        // Suppress mirror handler while seeding all six min/max values; otherwise the first
        // write under an auto-detected symmetry flag would overwrite the opposite side before
        // the other setters had a chance to run, corrupting the captured box.
        _applyingSymmetry = true;
        try
        {
            PendingBoxShapeName = initial.ShapeName ?? "";
            PendingBoxMinX = initial.BoxMin.X;
            PendingBoxMinY = initial.BoxMin.Y;
            PendingBoxMinZ = initial.BoxMin.Z;
            PendingBoxMaxX = initial.BoxMax.X;
            PendingBoxMaxY = initial.BoxMax.Y;
            PendingBoxMaxZ = initial.BoxMax.Z;
            PendingBoxFinalCriterion = initial.Criterion;

            // Auto-detect per-axis symmetry: a box that straddles 0 with near-equal reach on
            // both sides strongly implies the user meant to frame a symmetric feature
            // (hips/waist/shoulders). Threshold: |min|/|max| must agree to within 10%.
            PendingBoxSymmetry = DetectSymmetryAxes(initial.BoxMin, initial.BoxMax, 0.10f);
        }
        finally
        {
            _applyingSymmetry = false;
        }

        HasPendingBox = true;
        // Auto-disable pick mode so the user can rotate the view with left-drag without
        // accidentally drawing a second box over the one they just captured. They can
        // re-enable the toggle to draw a new box.
        IsBoundingBoxPickMode = false;
    }

    /// <summary>Tests whether the captured box straddles each world axis (min < 0 < max) with
    /// sides within <paramref name="tolerance"/> of equal magnitude (e.g., 0.10 = 10%). Returns
    /// the flag-set of axes that qualify. Used to auto-seed <see cref="PendingBoxSymmetry"/>
    /// so the mirror-mode ComboBox reflects what the user most likely intended.</summary>
    private static SymmetryAxes DetectSymmetryAxes(OpenTK.Mathematics.Vector3 min, OpenTK.Mathematics.Vector3 max, float tolerance)
    {
        var result = SymmetryAxes.None;
        if (AxisStraddlesAndIsSymmetric(min.X, max.X, tolerance)) result |= SymmetryAxes.X;
        if (AxisStraddlesAndIsSymmetric(min.Y, max.Y, tolerance)) result |= SymmetryAxes.Y;
        if (AxisStraddlesAndIsSymmetric(min.Z, max.Z, tolerance)) result |= SymmetryAxes.Z;
        return result;
    }

    private static bool AxisStraddlesAndIsSymmetric(float lo, float hi, float tolerance)
    {
        if (!(lo < 0f && hi > 0f)) return false;
        float absLo = MathF.Abs(lo);
        float absHi = MathF.Abs(hi);
        float larger = MathF.Max(absLo, absHi);
        if (larger <= 1e-5f) return false;
        float asymmetry = MathF.Abs(absLo - absHi) / larger;
        return asymmetry <= tolerance;
    }

    /// <summary>Emits the current pending box as a <see cref="KeyVertexBoxPick"/> and clears
    /// the pending state. Downstream subscribers (BodyTypeProfile editor) receive this via
    /// <see cref="AnyKeyVertexBoxPicked"/> exactly as if the drag had fired directly.</summary>
    public void ConfirmPendingBox()
    {
        if (!HasPendingBox) return;
        var pick = new KeyVertexBoxPick(
            PendingBoxShapeName,
            new OpenTK.Mathematics.Vector3(PendingBoxMinX, PendingBoxMinY, PendingBoxMinZ),
            new OpenTK.Mathematics.Vector3(PendingBoxMaxX, PendingBoxMaxY, PendingBoxMaxZ),
            PendingBoxFinalCriterion);
        HasPendingBox = false;
        NotifyKeyVertexBoxPicked(pick);
    }

    public void CancelPendingBox() => HasPendingBox = false;

    /// <summary>Shrinks the pending box in half along whichever mesh-local axis is most aligned
    /// with the camera's view direction, keeping the half on the camera side. Lets the user
    /// carve off an overhanging belly from a hip-pinch box with one click. Subsequent clicks
    /// keep halving that axis's camera-side range.</summary>
    public void ShrinkPendingBoxAlongViewAxis()
    {
        if (!HasPendingBox) return;

        var eye = Camera.GetEyePosition();
        var viewDir = Camera.Target - eye;
        float ax = MathF.Abs(viewDir.X);
        float ay = MathF.Abs(viewDir.Y);
        float az = MathF.Abs(viewDir.Z);

        float centerX = (PendingBoxMinX + PendingBoxMaxX) * 0.5f;
        float centerY = (PendingBoxMinY + PendingBoxMaxY) * 0.5f;
        float centerZ = (PendingBoxMinZ + PendingBoxMaxZ) * 0.5f;

        if (ax >= ay && ax >= az)
        {
            if (eye.X > centerX) PendingBoxMinX = centerX; else PendingBoxMaxX = centerX;
        }
        else if (ay >= az)
        {
            if (eye.Y > centerY) PendingBoxMinY = centerY; else PendingBoxMaxY = centerY;
        }
        else
        {
            if (eye.Z > centerZ) PendingBoxMinZ = centerZ; else PendingBoxMaxZ = centerZ;
        }
    }

    /// <summary>Row VM for the pick-info panel. Mirrors a single <see cref="KeyVertexPick"/>
    /// as display-formatted primitives so the XAML can bind without converters.
    /// Mutable (rather than <c>init</c>-only) so <see cref="RefreshKeyVertexMarkerPositions"/>
    /// can update coords in place — replacing the row object would drop any user selection
    /// in the picks ListBox because WPF tracks selection by reference. Display / Tsv are
    /// stored (not expression-bodied) so Fody's PropertyChanged weaver raises change
    /// notifications on them directly without relying on computed-property dependency
    /// inference.</summary>
    public sealed class PickRow : VM
    {
        public string ShapeName { get; set; } = "";
        public int VertexIndex { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public string Display { get; set; } = "";
        public string Tsv { get; set; } = "";

        public void UpdatePosition(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
            Display =
                ShapeName + "[" + VertexIndex + "]  "
                + x.ToString("F2") + ", "
                + y.ToString("F2") + ", "
                + z.ToString("F2");
            Tsv =
                ShapeName + "\t" + VertexIndex + "\t"
                + x.ToString("F4") + "\t"
                + y.ToString("F4") + "\t"
                + z.ToString("F4");
        }
    }

    private void CopyPicksToClipboard()
    {
        if (Picks.Count == 0) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("shape\tindex\tx\ty\tz");
        foreach (var p in Picks) sb.AppendLine(p.Tsv);
        try { Clipboard.SetText(sb.ToString()); }
        catch (Exception ex) { _logger?.LogError("CopyPicksToClipboard failed: " + ex.Message); }
    }

    /// <summary>
    /// Parallel to <see cref="GlRenderer.KeyVertexMarkers"/>: holds full pick metadata
    /// (mesh + vertex index) so SelectMirrorPicks can map each marker back to its mesh.
    /// Kept in sync via NotifyKeyVertexPicked / ClearKeyVertexMarkers.
    /// </summary>
    private readonly List<KeyVertexPick> _keyVertexPicks = new();

    /// <summary>
    /// Picks currently highlighted in the pick-info panel ListBox. The view pushes
    /// updates here on SelectionChanged so <see cref="SelectMirrorPicks"/> can act
    /// on just the highlighted row(s) rather than every pick in the session.
    /// </summary>
    private readonly List<PickRow> _selectedPicks = new();

    /// <summary>Called by the view (<see cref="UC_CharacterViewer"/>) whenever the
    /// picks ListBox selection changes. Replaces the cached selection wholesale and
    /// mirrors it into <see cref="GlRenderer.SelectedKeyVertexMarkerIndices"/> so the
    /// corresponding marker spheres render in the "selected" color.</summary>
    public void SetSelectedPicks(IEnumerable<PickRow> selection)
    {
        _selectedPicks.Clear();
        Renderer.SelectedKeyVertexMarkerIndices.Clear();
        if (selection == null) return;
        foreach (var p in selection)
        {
            _selectedPicks.Add(p);
            // Picks and Renderer.KeyVertexMarkers are parallel — the row's position
            // in Picks is also its marker index.
            int idx = Picks.IndexOf(p);
            if (idx >= 0) Renderer.SelectedKeyVertexMarkerIndices.Add(idx);
        }
    }

    /// <summary>Fired when the VM wants the view's pick-info ListBox to update its
    /// selection (e.g., the BodyTypeProfile editor highlighted a KeyVertex whose
    /// (shape, index) matches an existing pick row). View subscribes and flips
    /// <c>PicksList.SelectedItems</c> accordingly; SetSelectedPicks then runs through
    /// the normal PicksList_SelectionChanged path and updates the green highlight.</summary>
    public event Action<IReadOnlyList<PickRow>>? RequestPickSelection;

    /// <summary>Asks the view to select the pick row (if any) matching the supplied
    /// (shape, vertex index). Silent no-op when no row matches. Used by the
    /// BodyTypeProfile editor to cross-link its KeyVertex grid selection with the
    /// viewer's Picks list.</summary>
    public void RequestSelectPickByShapeAndIndex(string shapeName, int vertexIndex)
    {
        if (string.IsNullOrEmpty(shapeName) || vertexIndex < 0) return;
        PickRow? match = null;
        foreach (var row in Picks)
        {
            if (row.VertexIndex == vertexIndex
                && string.Equals(row.ShapeName, shapeName, StringComparison.OrdinalIgnoreCase))
            {
                match = row;
                break;
            }
        }
        if (match == null) return;
        RequestPickSelection?.Invoke(new[] { match });
    }

    /// <summary>Multi-selection variant: selects every pick row that matches one of the
    /// supplied (shape, vertex index) pairs. Fires a single <see cref="RequestPickSelection"/>
    /// event so the view replaces its whole selection in one pass. Empty/unmatched inputs
    /// clear the viewer's pick selection.</summary>
    public void RequestSelectPicksByShapeAndIndices(IEnumerable<(string ShapeName, int VertexIndex)> keys)
    {
        var matches = new List<PickRow>();
        if (keys != null)
        {
            foreach (var (shape, idx) in keys)
            {
                if (string.IsNullOrEmpty(shape) || idx < 0) continue;
                foreach (var row in Picks)
                {
                    if (row.VertexIndex == idx
                        && string.Equals(row.ShapeName, shape, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!matches.Contains(row)) matches.Add(row);
                        break;
                    }
                }
            }
        }
        RequestPickSelection?.Invoke(matches);
    }

    /// <summary>Rewrites the (shape, oldIdx) pick entry — if present — to point at
    /// <paramref name="newVertexIndex"/> instead, refreshing its marker position, Display/Tsv
    /// strings, and backing <see cref="KeyVertexPick"/>. Called by the BodyTypeProfile editor
    /// after a BoundingBox key vertex re-resolves to a different index on preset/weight switch,
    /// so the orange pick tracks the live BB resolution (and stays match-able by the editor's
    /// cross-link into <see cref="RequestSelectPickByShapeAndIndex"/>). Silent no-op when no
    /// matching row exists or when the new index cannot be resolved on the current mesh.
    /// Returns true on successful migration.</summary>
    public bool MigrateKeyVertexPick(string shapeName, int oldVertexIndex, int newVertexIndex)
    {
        if (string.IsNullOrEmpty(shapeName) || oldVertexIndex < 0 || newVertexIndex < 0) return false;
        if (oldVertexIndex == newVertexIndex) return false;

        int rowIdx = -1;
        for (int i = 0; i < Picks.Count; i++)
        {
            if (Picks[i].VertexIndex == oldVertexIndex
                && string.Equals(Picks[i].ShapeName, shapeName, StringComparison.OrdinalIgnoreCase))
            {
                rowIdx = i;
                break;
            }
        }
        if (rowIdx < 0) return false;

        // New vertex must be in range on the current mesh; bail (keeping old pick) otherwise.
        if (!TryGetCurrentVertex(shapeName, newVertexIndex, out var newPos)) return false;

        // Mutate PickRow in place — replacing the row object would drop WPF ListBox
        // selection (tracked by reference).
        Picks[rowIdx].VertexIndex = newVertexIndex;
        Picks[rowIdx].UpdatePosition(newPos.X, newPos.Y, newPos.Z);

        // Parallel _keyVertexPicks list. Entries are immutable records, so replace.
        if (rowIdx < _keyVertexPicks.Count)
        {
            var existing = _keyVertexPicks[rowIdx];
            if (existing.Mesh != null)
            {
                _keyVertexPicks[rowIdx] = new KeyVertexPick(existing.Mesh, newVertexIndex, newPos);
            }
        }

        // Renderer marker at the same slot (orange sphere list is parallel to _keyVertexPicks).
        if (rowIdx < Renderer.KeyVertexMarkers.Count)
        {
            Renderer.KeyVertexMarkers[rowIdx] = newPos;
        }

        if (rowIdx == Picks.Count - 1) LastPickSummary = Picks[rowIdx].Display;
        return true;
    }

    /// <summary>
    /// Look up the current (post-deformation, pre-ModelScale) position of a vertex on a body
    /// mesh by shape name and index. Used by the BodyTypeProfile editor to compute live
    /// measurement readouts and by the Phase 5 evaluator. Returns false when the shape isn't
    /// present in the current viewer state or the index is out of range.
    /// </summary>
    public bool TryGetCurrentVertex(string shapeName, int vertexIndex, out OpenTK.Mathematics.Vector3 localPos)
    {
        localPos = default;
        if (string.IsNullOrEmpty(shapeName) || vertexIndex < 0) return false;

        var mesh = Renderer.Meshes.FirstOrDefault(m =>
            string.Equals(m.ShapeName, shapeName, StringComparison.OrdinalIgnoreCase));
        if (mesh?.CpuPositions == null) return false;
        if (vertexIndex >= mesh.CpuPositions.Length) return false;

        var p = mesh.CpuPositions[vertexIndex];
        localPos = new OpenTK.Mathematics.Vector3(p.X, p.Y, p.Z);
        return true;
    }

    /// <summary>
    /// Replaces the renderer's BB-resolved marker set with <paramref name="positions"/>.
    /// Intended for the BodyTypeProfile editor, which resolves every <c>BoundingBox</c>
    /// key-vertex to a mesh-local position whenever <c>RefreshMeasurementValues</c> runs and
    /// pushes them here so the yellow markers track preset/weight change. Explicit picks
    /// continue to live in <see cref="GlRenderer.KeyVertexMarkers"/> and aren't touched.
    /// </summary>
    public void SetBoxResolvedMarkers(IEnumerable<OpenTK.Mathematics.Vector3> positions)
    {
        Renderer.BoxResolvedMarkers.Clear();
        if (positions == null) return;
        foreach (var p in positions) Renderer.BoxResolvedMarkers.Add(p);
    }

    /// <summary>
    /// Returns every vertex position for a shape in the same pre-ModelScale local space as
    /// <see cref="TryGetCurrentVertex"/>, or null when the shape isn't currently loaded.
    /// Used by <see cref="MeasurementMath"/> to resolve <see cref="KeyVertexStrategy.BoundingBox"/>
    /// key-vertex entries — the scanner needs the full positions array to find the extremum
    /// inside the AABB. Allocates a fresh array per call (positions are converted from
    /// System.Numerics.Vector3 to OpenTK.Mathematics.Vector3); called infrequently enough
    /// that this hasn't been worth caching.
    /// </summary>
    public OpenTK.Mathematics.Vector3[]? GetShapePositions(string shapeName)
    {
        if (string.IsNullOrEmpty(shapeName)) return null;
        var mesh = Renderer.Meshes.FirstOrDefault(m =>
            string.Equals(m.ShapeName, shapeName, StringComparison.OrdinalIgnoreCase));
        if (mesh?.CpuPositions == null || mesh.CpuPositions.Length == 0) return null;

        var src = mesh.CpuPositions;
        var dst = new OpenTK.Mathematics.Vector3[src.Length];
        for (int i = 0; i < src.Length; i++)
            dst[i] = new OpenTK.Mathematics.Vector3(src[i].X, src[i].Y, src[i].Z);
        return dst;
    }

    /// <summary>
    /// Returns the current per-shape vertex counts for every renderable mesh that has CPU-side
    /// positions. Used by the BodyTypeProfile editor to capture/refresh the
    /// <see cref="TopologyFingerprint"/> for the active profile.
    /// </summary>
    public Dictionary<string, int> GetCurrentShapeVertexCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var mesh in Renderer.Meshes)
        {
            if (mesh?.CpuPositions == null || string.IsNullOrEmpty(mesh.ShapeName)) continue;
            counts[mesh.ShapeName] = mesh.CpuPositions.Length;
        }
        return counts;
    }

    /// <summary>Removes every marker gizmo. Bound to the toolbar "Clear Picks"
    /// button. Phase 3 will replace this with per-profile clearing.</summary>
    public void ClearKeyVertexMarkers()
    {
        Renderer.KeyVertexMarkers.Clear();
        Renderer.SelectedKeyVertexMarkerIndices.Clear();
        _keyVertexPicks.Clear();
        _selectedPicks.Clear();
        Picks.Clear();
        LastPickSummary = "";
    }

    /// <summary>
    /// Re-resolves every existing key-vertex pick against the current post-deformation
    /// <see cref="GlMesh.CpuPositions"/> and updates the on-screen markers + picks panel
    /// rows in place. Called at the end of <see cref="ApplyBodySlide"/> so markers track
    /// the body as the user switches presets or weight instead of staying stuck in the
    /// world at the position they were picked at. Picks whose mesh+index no longer
    /// resolve (e.g., after an NPC swap) are skipped silently — their Picks row keeps
    /// its last-known coordinates so the (shape, index) is still recoverable.
    /// </summary>
    private void RefreshKeyVertexMarkerPositions()
    {
        if (_keyVertexPicks.Count == 0) return;

        Renderer.KeyVertexMarkers.Clear();
        // Indices may shift if any pick fails to resolve mid-list below — clear and
        // re-derive at the end from the still-valid _selectedPicks row references.
        Renderer.SelectedKeyVertexMarkerIndices.Clear();

        for (int i = 0; i < _keyVertexPicks.Count; i++)
        {
            var pick = _keyVertexPicks[i];
            var shapeName = pick.Mesh?.ShapeName ?? "";
            if (string.IsNullOrEmpty(shapeName)
                || !TryGetCurrentVertex(shapeName, pick.VertexIndex, out var pos))
            {
                continue;
            }

            _keyVertexPicks[i] = new KeyVertexPick(pick.Mesh!, pick.VertexIndex, pos);
            Renderer.KeyVertexMarkers.Add(pos);

            if (i < Picks.Count)
            {
                // Mutate in place — replacing the row object would clear the user's
                // ListBox selection because WPF tracks selection by reference.
                Picks[i].UpdatePosition(pos.X, pos.Y, pos.Z);
            }
        }

        if (Picks.Count > 0) LastPickSummary = Picks[^1].Display;

        // Re-derive selection indices after the marker list was rebuilt (indices may
        // have shifted if some picks failed to resolve). PickRow references in
        // _selectedPicks still point at live rows in Picks because UpdatePosition
        // mutates in place rather than replacing them.
        foreach (var row in _selectedPicks)
        {
            int idx = Picks.IndexOf(row);
            if (idx >= 0) Renderer.SelectedKeyVertexMarkerIndices.Add(idx);
        }
    }

    /// <summary>
    /// Resolves a list of (shape, vertex index) references against the current mesh state
    /// and displays each as a pick marker in the viewer. Used by the BodyTypeProfile
    /// editor's "Show picks in viewer" button to re-visualize a profile's persisted
    /// <c>KeyVertices</c> after closing and reopening the program. Picks already present
    /// are skipped by the dedup in <see cref="NotifyKeyVertexPicked"/>; references that
    /// don't resolve on the current mesh (wrong shape or out-of-range index) are silently
    /// skipped and logged.
    /// </summary>
    public int ShowKeyVerticesInViewer(IEnumerable<(string ShapeName, int VertexIndex)> entries)
    {
        if (entries == null) return 0;
        int added = 0;
        int skipped = 0;
        foreach (var (shapeName, vertexIndex) in entries)
        {
            if (string.IsNullOrEmpty(shapeName) || vertexIndex < 0) { skipped++; continue; }
            var mesh = Renderer.Meshes.FirstOrDefault(m =>
                string.Equals(m.ShapeName, shapeName, StringComparison.OrdinalIgnoreCase));
            if (mesh?.CpuPositions == null || vertexIndex >= mesh.CpuPositions.Length)
            {
                skipped++;
                continue;
            }

            int before = _keyVertexPicks.Count;
            var p = mesh.CpuPositions[vertexIndex];
            NotifyKeyVertexPicked(new KeyVertexPick(mesh, vertexIndex,
                new OpenTK.Mathematics.Vector3(p.X, p.Y, p.Z)));
            if (_keyVertexPicks.Count > before) added++;
        }
        LogVerbose("CharacterViewer: ShowKeyVerticesInViewer added " + added
            + " marker(s); " + skipped + " reference(s) unresolved.");
        return added;
    }

    /// <summary>
    /// For every existing pick marker, adds a new marker at the vertex whose *current*
    /// (post-deformation) position is closest to the X-mirror of the original pick's
    /// current position, searching the same mesh as the source pick. Intended for
    /// bilaterally symmetric key-vertex assignment: pick once on the left, press the
    /// button to fill in the right-side counterparts. Fires the same pick events as
    /// a manual click so the BodyTypeProfile editor captures the mirrors too.
    /// </summary>
    public void SelectMirrorPicks()
    {
        // Scope: if the user has selected rows in the picks panel, mirror only those;
        // otherwise fall back to the most recent pick. This matches the UX where a
        // fresh pick auto-selects itself in the ListBox, so an un-interacted panel
        // still does the intuitive thing (mirror the last pick).
        KeyVertexPick[] sourcePicks;
        if (_selectedPicks.Count > 0)
        {
            var list = new List<KeyVertexPick>(_selectedPicks.Count);
            foreach (var row in _selectedPicks)
            {
                foreach (var pick in _keyVertexPicks)
                {
                    if (pick.VertexIndex == row.VertexIndex
                        && string.Equals(pick.Mesh?.ShapeName ?? "", row.ShapeName, StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(pick);
                        break;
                    }
                }
            }
            sourcePicks = list.ToArray();
        }
        else if (_keyVertexPicks.Count > 0)
        {
            sourcePicks = new[] { _keyVertexPicks[^1] };
        }
        else
        {
            sourcePicks = Array.Empty<KeyVertexPick>();
        }

        if (sourcePicks.Length == 0)
        {
            LogVerbose("CharacterViewer: SelectMirrorPicks — no source picks; nothing to do.");
            return;
        }

        int added = 0;
        foreach (var src in sourcePicks)
        {
            if (src.Mesh == null || src.Mesh.CpuPositions == null) continue;

            // Use the current (deformed) position, not the captured LocalPos, so the
            // mirror is correct even if the body was re-shaped after the original pick.
            if (!TryGetCurrentVertex(src.Mesh.ShapeName, src.VertexIndex, out var livePos))
                continue;

            var mirrorTarget = new OpenTK.Mathematics.Vector3(-livePos.X, livePos.Y, livePos.Z);

            // Find the vertex on the same mesh closest to the mirror target.
            var positions = src.Mesh.CpuPositions;
            int bestIdx = -1;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < positions.Length; i++)
            {
                float dx = positions[i].X - mirrorTarget.X;
                float dy = positions[i].Y - mirrorTarget.Y;
                float dz = positions[i].Z - mirrorTarget.Z;
                float d2 = dx * dx + dy * dy + dz * dz;
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    bestIdx = i;
                }
            }
            if (bestIdx < 0) continue;

            // Skip if the mirror resolves to the same vertex (happens for vertices on
            // the X=0 midline — no symmetric counterpart to add).
            if (bestIdx == src.VertexIndex) continue;

            var bestPos = positions[bestIdx];
            var mirrorPick = new KeyVertexPick(src.Mesh, bestIdx,
                new OpenTK.Mathematics.Vector3(bestPos.X, bestPos.Y, bestPos.Z));
            NotifyKeyVertexPicked(mirrorPick);
            added++;
        }

        LogVerbose("CharacterViewer: SelectMirrorPicks added " + added
            + " mirror pick(s) from " + sourcePicks.Length + " source(s).");
    }

    /// <summary>
    /// Starts from the most recent key-vertex pick, casts a ray along the selected axis
    /// (<see cref="ProjectAcrossAxisIndex"/>) through the body, and adds a new pick marker
    /// at the vertex closest to where the ray exits the opposite-side surface. Intended for
    /// pairing midline anchors like NippleFront_L ↔ UpperSpineBack or NavelFront ↔
    /// MidSpineBack without having to eyeball a matching cross-axis coordinate.
    ///
    /// The method casts in both +axis and -axis polarities (offsetting the origin a hair
    /// along the ray so the source's own surface triangles aren't flagged as self-hits),
    /// then keeps the farther hit — whichever direction genuinely points inward through
    /// the body. Fires the same pick events as a manual click so the BodyTypeProfile
    /// editor picks up the new vertex.
    /// </summary>
    public void ProjectLastPickAcrossAxis()
    {
        if (_keyVertexPicks.Count == 0)
        {
            LogVerbose("CharacterViewer: ProjectLastPickAcrossAxis — no source pick.");
            return;
        }

        var src = _keyVertexPicks[^1];
        if (src.Mesh == null || src.Mesh.CpuPositions == null || src.Mesh.CpuIndices == null)
            return;

        if (!TryGetCurrentVertex(src.Mesh.ShapeName, src.VertexIndex, out var originPos))
            return;

        // Offset the origin a small amount along the ray so the start-surface triangles
        // aren't picked up as near-zero-distance self hits (the inner epsilon in
        // RayIntersectsMeshPickVertex is 1e-6 which is tight for floating-point edges).
        const float originOffset = 0.01f;

        float bestDist = -1f;
        int bestVertexIndex = -1;
        OpenTK.Mathematics.Vector3 bestLocal = default;

        for (int sign = +1; sign >= -1; sign -= 2)
        {
            var dir = AxisUnit(ProjectAcrossAxisIndex, sign);
            var origin = originPos + dir * originOffset;
            if (RayIntersectsMeshPickVertex(origin, dir, src.Mesh,
                    out float t, out int vi, out var local)
                && vi != src.VertexIndex
                && t > bestDist)
            {
                bestDist = t;
                bestVertexIndex = vi;
                bestLocal = local;
            }
        }

        if (bestVertexIndex < 0)
        {
            LogVerbose("CharacterViewer: ProjectLastPickAcrossAxis — no opposite-surface hit.");
            return;
        }

        NotifyKeyVertexPicked(new KeyVertexPick(src.Mesh, bestVertexIndex, bestLocal));
    }

    private static OpenTK.Mathematics.Vector3 AxisUnit(int axisIndex, float sign)
    {
        return axisIndex switch
        {
            0 => new OpenTK.Mathematics.Vector3(sign, 0f, 0f),
            1 => new OpenTK.Mathematics.Vector3(0f, sign, 0f),
            _ => new OpenTK.Mathematics.Vector3(0f, 0f, sign),
        };
    }

    /// <summary>
    /// Replaces the renderer's measurement-line overlay with <paramref name="segments"/>.
    /// Positions are in the same pre-ModelScale mesh-local space as
    /// <see cref="GlRenderer.KeyVertexMarkers"/>; pass <c>null</c> or an empty sequence
    /// to clear the overlay. Used by the BodyTypeProfile editor to visualize the
    /// currently-selected measurement.
    /// </summary>
    public void SetMeasurementLines(IEnumerable<(OpenTK.Mathematics.Vector3 A, OpenTK.Mathematics.Vector3 B, OpenTK.Mathematics.Vector3 Color)> segments)
    {
        Renderer.MeasurementLines.Clear();
        if (segments == null) return;
        foreach (var s in segments)
        {
            Renderer.MeasurementLines.Add(new GlRenderer.MeasurementLineSegment
            {
                A = s.A,
                B = s.B,
                Color = s.Color,
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  GL INITIALIZATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called once after the GL context is ready (from the view's OnRender or Loaded event).
    /// </summary>
    public void InitializeGl(string shaderDirectory)
    {
        if (IsGlInitialized) return;

        TextureManager = new GlTextureManager(_previewCache, _logger);
        TextureManager.Initialize();
        Renderer.Initialize(shaderDirectory);

        // Selections were resolved from settings in the ctor; push the current
        // field values to the renderer now that it's initialized.
        PushAllLightsToRenderer();
        Renderer.ShowKeyLightVisualization = ShowLightControls;

        IsGlInitialized = true;

        LogVerbose("CharacterViewer: GL initialized");
    }

    /// <summary>
    /// Called from the GL render callback to process any pending scene setup.
    /// All GL calls (mesh upload, texture loading) happen here where the
    /// GL context is guaranteed to be current.
    /// </summary>
    public void ProcessPendingScene()
    {
        // Head-only rebuild (P2) is independent of full-scene setup and runs
        // without touching Body/Hands/Feet. Drain it here so the render callback
        // owns all GL-side scene mutations.
        if (_pendingHeadReplace != null && IsGlInitialized)
        {
            InstallReplacedHead();
        }

        if (_pendingScene == null || !IsGlInitialized) return;

        var (loadResults, meshPaths) = _pendingScene.Value;
        _pendingScene = null;
        // Hand the timeline off the field so a re-entrant LoadNpcAsync queued
        // mid-install starts its own clock cleanly.
        var loadStopwatch = _pendingLoadStopwatch;
        _pendingLoadStopwatch = null;
        LogLoadCheckpoint(loadStopwatch, "ProcessPendingScene start (GL upload begin)");

        ClearScene();
        _cachedMeshPaths = meshPaths;

        int totalShapes = 0;
        foreach (var (bodyPart, meshSource, meshes) in loadResults)
        {
            Dictionary<int, string>? txstOverrides = null;
            if (bodyPart != "Head" && meshPaths.TxstTextures.TryGetValue(bodyPart, out var txst))
                txstOverrides = txst;

            foreach (var built in meshes)
            {
                var glMesh = CreateGlMesh(built);
                glMesh.MeshSource = meshSource;

                var effectiveTextures = new Dictionary<int, string>(built.TexturePaths);
                if (txstOverrides != null)
                    foreach (var (slot, path) in txstOverrides)
                        effectiveTextures[slot] = path;

                bool isHairTint = false;
                float hairR = 0, hairG = 0, hairB = 0;
                bool isFaceTint = false;
                string? faceTintPath = null;

                ApplyTexturesToGlMesh(glMesh, built, effectiveTextures, meshPaths,
                    ref isHairTint, ref hairR, ref hairG, ref hairB,
                    ref isFaceTint, ref faceTintPath);

                _textureApplyInfoByMesh[glMesh] = new TextureApplyInfo(
                    new Dictionary<int, string>(effectiveTextures),
                    isHairTint, hairR, hairG, hairB, isFaceTint, faceTintPath);

                glMesh.BodyPart = bodyPart;
                glMesh.ShowWireframe = ShowWireframe;
                Renderer.AddMesh(glMesh);

                if (bodyPart == "Head")
                {
                    if (built.IsPrimaryHeadShape || !_meshesByBodyPart.ContainsKey(bodyPart))
                        _meshesByBodyPart[bodyPart] = glMesh;
                    if (built.IsPrimaryHeadShape || !_builtMeshesByBodyPart.ContainsKey(bodyPart))
                        _builtMeshesByBodyPart[bodyPart] = built;
                }
                else
                {
                    if (!_meshesByBodyPart.ContainsKey(bodyPart))
                        _meshesByBodyPart[bodyPart] = glMesh;
                    if (!_builtMeshesByBodyPart.ContainsKey(bodyPart))
                        _builtMeshesByBodyPart[bodyPart] = built;
                }

                if (bodyPart == "Body")
                {
                    _cachedBodyMeshes[built.ShapeName] = built;

                    // Cache the body NIF's disk path once per scene so ApplyBodySlide
                    // can probe for a sibling .tri (BodySlide's "Build Morphs" output).
                    // The .tri is topology-matched to this NIF, so it avoids the OSD
                    // path's reference-mesh mismatch.
                    if (_cachedBodyNifDiskPath == null && meshSource?.ResolvedDiskPath != null)
                    {
                        _cachedBodyNifDiskPath = meshSource.ResolvedDiskPath;
                    }
                }

                totalShapes++;
            }
        }

        StatusText = totalShapes > 0
            ? $"Loaded {totalShapes} shape(s) for NPC"
            : "No renderable shapes found for NPC";
        IsLoading = false;

        LogLoadCheckpoint(loadStopwatch, "Scene committed (" + totalShapes +
            " shapes -> " + Renderer.Meshes.Count + " GL meshes) — load complete");

        // Record the identity of the scene we just committed so LoadNpcAsync
        // can short-circuit same-NPC re-invocations. Must be done before clearing
        // _sceneRebuildPending so any re-entrant LoadNpcAsync from the drain below
        // sees the correct identity.
        _currentLoadedNpc = _pendingLoadNpcKey;
        _currentHeadMeshOverride = _pendingLoadHeadMeshOverride;
        _pendingLoadNpcKey = FormKey.Null;
        _pendingLoadHeadMeshOverride = null;

        // Scene is now rebuilt — clear the rebuild flag before draining the
        // pending-override queue so ApplyTextureOverrides takes the direct path.
        _sceneRebuildPending = false;

        // Process any pending overrides that were queued before the scene was ready
        if (_pendingTextureOverrides != null)
        {
            var overrides = _pendingTextureOverrides;
            _pendingTextureOverrides = null;
            ApplyTextureOverrides(overrides);
        }

        if (_pendingBodySlide != null)
        {
            var (preset, weight) = _pendingBodySlide.Value;
            _pendingBodySlide = null;
            ApplyBodySlide(preset, weight);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LOADING — Full NPC
    // ═══════════════════════════════════════════════════════════════════════

    public async Task LoadNpcAsync(FormKey npcFormKey, ILinkCache linkCache, string? overrideHeadMeshAbsolutePath = null)
    {
        // Same-NPC short-circuit. Narrow editors (BodySlide preset change, BodyGen
        // spec edit, AssetPack subgroup flip) call LoadNpcAsync defensively before
        // their narrow update method, but when the NPC hasn't changed the rebuild
        // is pure waste: full NIF re-parse, CPU re-skinning, DDS re-decode.
        //
        // Guard at the top of the single VM choke point so every current and future
        // caller benefits without per-site tracking.
        //
        // Why the override path check is exclusion-only (both sides null), not
        // equality: when overrideHeadMeshAbsolutePath is non-null it refers to a
        // FaceGen preview NIF that is rewritten in place on every headpart change.
        // Path equality would incorrectly skip the reload. ApplyHeadPartsAsync
        // owns its own fast path (P2); LoadNpcAsync just needs to always rebuild
        // when an override is involved.
        if (!_sceneRebuildPending
            && _meshesByBodyPart.Count > 0
            && npcFormKey == _currentLoadedNpc
            && overrideHeadMeshAbsolutePath == null
            && _currentHeadMeshOverride == null)
        {
            LogVerbose("CharacterViewer: LoadNpcAsync same-NPC short-circuit (" +
                npcFormKey + ")");
            return;
        }

        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        // Mark a rebuild as in-flight so any ApplyTextureOverrides calls arriving
        // between now and when ProcessPendingScene finishes are queued rather than
        // applied to the soon-to-be-destroyed current meshes.
        _sceneRebuildPending = true;

        IsLoading = true;
        StatusText = "Resolving NPC meshes...";

        var loadStopwatch = System.Diagnostics.Stopwatch.StartNew();
        LogLoadCheckpoint(loadStopwatch, "LoadNpcAsync begin (NPC=" + npcFormKey + ")");

        try
        {
            _npcHairColorFromRecord = null;
            if (linkCache.TryResolve<Mutagen.Bethesda.Skyrim.INpcGetter>(npcFormKey, out var npcGetter))
            {
                NpcWeight = Math.Clamp((int)npcGetter.Weight, 0, 100);
                LogVerbose("CharacterViewer: NPC weight = " + NpcWeight +
                    " (raw " + npcGetter.Weight.ToString("F2") + ")");

                // NPC.Height is a full-model uniform scale multiplier (1.0 default).
                // Guard against zero/negative values from malformed records to avoid
                // a collapsed or mirrored render.
                float recordHeight = npcGetter.Height;
                NpcBaseHeight = (float.IsFinite(recordHeight) && recordHeight > 0f) ? recordHeight : 1.0f;
                LogVerbose("CharacterViewer: NPC height = " + NpcBaseHeight.ToString("F3") +
                    " (raw " + recordHeight.ToString("F3") + ")");

                // Resolve the NPC's HairColor FormLink (HCLR record) — in-game, this
                // overrides the default hairTintColor baked into the hair NIF's BSLSP.
                // Logging both lets us diagnose mismatches between reference images
                // (which show the NPC's HCLR color) and the viewer (which currently
                // uses only the NIF's baked tint).
                if (npcGetter.HairColor.IsNull)
                {
                    LogVerbose("CharacterViewer: NPC.HairColor FormLink is null — " +
                        "no HCLR override available; viewer will use NIF's baked BSLSP tint.");
                }
                else
                {
                    var hclr = npcGetter.HairColor.TryResolve(linkCache);
                    if (hclr != null)
                    {
                        var c = hclr.Color;
                        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
                        _npcHairColorFromRecord = (r, g, b);
                        string hex = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                        LogVerbose("CharacterViewer: NPC.HairColor HCLR=" +
                            npcGetter.HairColor.FormKey.ToString() +
                            " name='" + (hclr.Name?.String ?? "?") + "'" +
                            " RGB=(" + c.R + "," + c.G + "," + c.B + ")" +
                            " float=(" + r.ToString("F3") + "," + g.ToString("F3") + "," + b.ToString("F3") + ")" +
                            " hex=" + hex);
                    }
                    else
                    {
                        LogVerbose("CharacterViewer: NPC.HairColor FormLink " +
                            npcGetter.HairColor.FormKey.ToString() + " failed to resolve.");
                    }
                }
            }
            LogLoadCheckpoint(loadStopwatch, "NPC record resolved");

            var meshPaths = await Task.Run(() => _previewCache.GetOrResolveMeshPaths(npcFormKey, linkCache), cts.Token);
            if (meshPaths == null)
            {
                StatusText = "Could not resolve NPC mesh paths";
                // No scene will be queued for ProcessPendingScene to rebuild, so
                // clear the flag now — otherwise future ApplyTextureOverrides
                // calls would be queued forever.
                if (_loadCts == cts) _sceneRebuildPending = false;
                return;
            }
            LogLoadCheckpoint(loadStopwatch, "Mesh paths resolved");

            if (!string.IsNullOrWhiteSpace(overrideHeadMeshAbsolutePath))
            {
                meshPaths = meshPaths.WithHeadMeshPath(overrideHeadMeshAbsolutePath);
                LogVerbose("CharacterViewer: head mesh path overridden -> " + overrideHeadMeshAbsolutePath);
            }

            _cachedMeshPaths = meshPaths;
            cts.Token.ThrowIfCancellationRequested();

            StatusText = "Loading meshes...";
            var loadResults = await Task.Run(() => LoadAllMeshParts(meshPaths), cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            // Store pending scene data — GL work is deferred to the render callback
            // where the GL context is guaranteed to be current.
            int totalShapes = loadResults.Sum(r => r.Meshes.Count);
            LogLoadCheckpoint(loadStopwatch, "NIFs parsed + skinned (" + totalShapes +
                " shapes across " + loadResults.Count + " parts)");
            Application.Current.Dispatcher.Invoke(() =>
            {
                _pendingScene = (loadResults, meshPaths);
                _pendingLoadNpcKey = npcFormKey;
                _pendingLoadHeadMeshOverride = overrideHeadMeshAbsolutePath;
                // Hand the clock to the render thread — ProcessPendingScene will
                // consume it on the next frame tick and log the GL-upload span.
                _pendingLoadStopwatch = loadStopwatch;
                StatusText = totalShapes > 0
                    ? $"Loaded {totalShapes} shape(s), setting up scene..."
                    : "No renderable shapes found for NPC";
            });
            LogLoadCheckpoint(loadStopwatch, "Scene queued for GL upload");
        }
        catch (OperationCanceledException)
        {
            LogVerbose("CharacterViewer: NPC load cancelled");
            // If a newer load took over, _loadCts != cts and that newer load owns
            // the flag. Only clear the flag if we're still the current (unreplaced)
            // load — meaning cancellation came from outside, not from a new load.
            if (_loadCts == cts) _sceneRebuildPending = false;
        }
        catch (Exception ex)
        {
            // Many exceptions (NullReferenceException, IndexOutOfRangeException, …) have an
            // unhelpful or empty Message. Log the full chained exception so the Status Log
            // actually reveals the failure instead of silently switching tabs.
            StatusText = $"Error: {ex.Message}";
            _logger.LogError("CharacterViewer: Failed to load NPC " + npcFormKey + Environment.NewLine
                + ExceptionLogger.GetExceptionStack(ex));
            if (_loadCts == cts) _sceneRebuildPending = false;
        }
        finally
        {
            if (_loadCts == cts) IsLoading = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TEXTURE APPLICATION
    // ═══════════════════════════════════════════════════════════════════════

    private void ApplyTexturesToGlMesh(GlMesh glMesh, NifMeshBuilder.BuiltMesh built,
        Dictionary<int, string> effectiveTextures, NpcMeshResolver.NpcMeshPaths meshPaths,
        ref bool isHairTint, ref float hairR, ref float hairG, ref float hairB,
        ref bool isFaceTint, ref string? faceTintPath)
    {
        if (TextureManager == null) return;

        // Diffuse (slot 0) — with special handling for hair tint and face tint.
        // Two tinting modes match the Skyrim engine (and NPC Portrait Creator):
        //   1. SLSF1_Greyscale_To_Palette_Color flag set:
        //      Texture is greyscale; shader: baseColor.rrr * tint_color * greyscaleToPaletteScale
        //   2. BSLSP_HAIRTINT shader type only (flag NOT set):
        //      Texture is full RGB; shader: baseColor.rgb *= tint_color (simple multiply)
        if (built.IsHairTintShader && built.HairTintColor.HasValue &&
            effectiveTextures.TryGetValue(0, out string? hairDiffuse))
        {
            var (tR, tG, tB) = built.HairTintColor.Value;
            isHairTint = true; hairR = tR; hairG = tG; hairB = tB;
            glMesh.DiffuseTexture = TextureManager.LoadTexture(hairDiffuse);
            glMesh.TintColor = new System.Numerics.Vector3(tR, tG, tB);

            if (built.HasGreyscaleToPaletteFlag)
            {
                glMesh.HasGreyscaleToPalette = true;
                glMesh.GreyscaleToPaletteScale = built.GreyscaleToPaletteScale;
                RecordTextureSource(glMesh, "Diffuse (hair tint, greyscale-to-palette)", hairDiffuse);
                LogVerbose("CharacterViewer: Hair tint (greyscale-to-palette): " +
                    "tint=(" + tR.ToString("F3") + "," + tG.ToString("F3") + "," + tB.ToString("F3") + ")" +
                    " scale=" + built.GreyscaleToPaletteScale.ToString("F2") +
                    " -> baseColor.rrr * tint * scale" +
                    " | diffuse=" + System.IO.Path.GetFileName(hairDiffuse));
            }
            else
            {
                glMesh.HasTintColor = true;
                RecordTextureSource(glMesh, "Diffuse (hair tint, RGB multiply)", hairDiffuse);
                LogVerbose("CharacterViewer: Hair tint (simple RGB multiply): " +
                    "tint=(" + tR.ToString("F3") + "," + tG.ToString("F3") + "," + tB.ToString("F3") + ")" +
                    " -> baseColor.rgb *= tint" +
                    " | diffuse=" + System.IO.Path.GetFileName(hairDiffuse));
            }
        }
        else if (built.IsPrimaryHeadShape && effectiveTextures.TryGetValue(0, out string? headDiffuse) &&
                 meshPaths.FaceTintPath != null)
        {
            isFaceTint = true;
            faceTintPath = meshPaths.FaceTintPath;
            // Load diffuse and face tint as separate textures so they can be
            // toggled independently. The shader blends them via has_face_tint_map.
            glMesh.DiffuseTexture = TextureManager.LoadTexture(headDiffuse);
            glMesh.FaceTintTexture = TextureManager.LoadTexture(meshPaths.FaceTintPath);
            glMesh.HasFaceTintMap = true;
            RecordTextureSource(glMesh, "Diffuse", headDiffuse);
            RecordTextureSource(glMesh, "Face Tint", meshPaths.FaceTintPath);
        }
        else if (effectiveTextures.TryGetValue(0, out string? diffusePath))
        {
            glMesh.DiffuseTexture = TextureManager.LoadTexture(diffusePath);
            RecordTextureSource(glMesh, "Diffuse", diffusePath);
        }
        else
        {
            glMesh.DiffuseTexture = TextureManager.WhiteTexture;
        }

        // Normal map (slot 1)
        if (effectiveTextures.TryGetValue(1, out string? normalPath))
        {
            glMesh.NormalTexture = TextureManager.LoadTexture(normalPath);
            glMesh.HasNormalMap = true;
            glMesh.IsModelSpace = built.IsModelSpaceNormals;
            RecordTextureSource(glMesh, "Normal Map", normalPath);
        }
        else
        {
            glMesh.NormalTexture = TextureManager.WhiteTexture;
        }

        // Skin/subsurface map (slot 2) — only meaningful for skin/face shader types.
        // For other shader types (eye, hair, default, etc.) slot 2 has a different meaning
        // (glow, environment, etc.) and applying it as a skin map would add incorrect red SSS tinting.
        bool isSkinShader = built.ShaderType == 4  // BSLSP_FACE
                         || built.ShaderType == 5; // BSLSP_SKINTINT
        if (isSkinShader && effectiveTextures.TryGetValue(2, out string? skinPath))
        {
            glMesh.SkinTexture = TextureManager.LoadTexture(skinPath);
            glMesh.HasSkinMap = true;
            RecordTextureSource(glMesh, "Skin/SSS", skinPath);
        }
        else
        {
            glMesh.SkinTexture = TextureManager.WhiteTexture;
        }

        // Specular map (slot 7)
        if (effectiveTextures.TryGetValue(7, out string? specPath))
        {
            glMesh.SpecularTexture = TextureManager.LoadTexture(specPath);
            glMesh.HasSpecularMap = true;
            glMesh.HasSpecular = true;
            RecordTextureSource(glMesh, "Specular", specPath);
        }
        else
        {
            glMesh.SpecularTexture = TextureManager.WhiteTexture;
            // Check shader flags for specular enable even without a map
            glMesh.HasSpecular = (built.ShaderFlags1 & (1u << 0)) != 0;
        }

        if (!glMesh.HasFaceTintMap)
            glMesh.FaceTintTexture = TextureManager.WhiteTexture;

        // Material properties
        glMesh.MaterialGlossiness = built.Glossiness;
        glMesh.MaterialSpecularStrength = built.SpecularStrength;
        glMesh.SpecularColor = built.SpecularColor;
        glMesh.SubsurfaceRolloff = built.SubsurfaceRolloff;
        glMesh.RimlightPower = built.RimlightPower;
        glMesh.HasVertexColors = built.HasVertexColors;
        glMesh.UvScale = built.UvScale;
        glMesh.UvOffset = built.UvOffset;

        // Emissive (SLSF1_OwnEmit, bit 22)
        if ((built.ShaderFlags1 & (1u << 22)) != 0)
        {
            glMesh.HasEmissive = true;
            glMesh.EmissiveColor = built.EmissiveColor;
            glMesh.EmissiveMultiple = built.EmissiveMultiple;
        }

        // Shader type flags (bit positions per Nifskope/Bethesda spec)
        glMesh.HasHairSoftLighting = (built.ShaderFlags1 & (1u << 18)) != 0; // SLSF1_Hair_Soft_Lighting
        glMesh.HasSoftLighting = (built.ShaderFlags2 & (1u << 25)) != 0; // SLSF2_Soft_Lighting
        glMesh.HasRimLighting = (built.ShaderFlags2 & (1u << 26)) != 0; // SLSF2_Rim_Lighting

        // Skin tint (shader type 5 = ST_SkinTint): apply NPC's QNAM TextureLighting color
        if (built.ShaderType == 5 && meshPaths.TextureLightingColor.HasValue)
        {
            var (r, g, b) = meshPaths.TextureLightingColor.Value;
            glMesh.HasTintColor = true;
            glMesh.TintColor = new System.Numerics.Vector3(r, g, b);
        }

        // Eye shader (shader type 16 = ST_EyeEnvmap)
        if (built.ShaderType == 16)
        {
            glMesh.IsEye = true;
        }

        // Environment mapping (SLSF1_Environment_Mapping bit 7, or SLSF1_Eye_Environment_Mapping bit 17)
        bool hasEnvMap = (built.ShaderFlags1 & (1u << 7)) != 0;
        bool hasEyeEnvMap = (built.ShaderFlags1 & (1u << 17)) != 0;
        if ((hasEnvMap || hasEyeEnvMap) && effectiveTextures.TryGetValue(4, out string? envMapPath))
        {
            var envTex = TextureManager.LoadCubemap(envMapPath);
            if (envTex != 0)
            {
                glMesh.EnvMapTexture = envTex;
                glMesh.HasEnvironmentMap = true;
                glMesh.EnvMapScale = built.EnvironmentMapScale;
                glMesh.EyeCubemapScale = built.EyeCubemapScale;
                RecordTextureSource(glMesh, "Environment Cubemap", envMapPath);
            }

            if (effectiveTextures.TryGetValue(5, out string? envMaskPath))
            {
                glMesh.EnvMaskTexture = TextureManager.LoadTexture(envMaskPath);
                glMesh.HasEnvMask = true;
                RecordTextureSource(glMesh, "Environment Mask", envMaskPath);
            }
        }

        // Detail map (SLSF1_Facegen_Detail_Map, bit 10)
        if ((built.ShaderFlags1 & (1u << 10)) != 0 && effectiveTextures.TryGetValue(3, out string? detailPath))
        {
            glMesh.DetailTexture = TextureManager.LoadTexture(detailPath);
            glMesh.HasDetailMap = true;
            RecordTextureSource(glMesh, "Detail Map", detailPath);
        }

        // Double-sided (brow, eyelash, hair — thin geometry visible from both sides)
        glMesh.IsDoubleSided = built.IsDoubleSided;

        // Alpha test / blend
        if (built.HasAlphaTest || built.HasAlphaBlend)
        {
            if (glMesh.DiffuseTexture != TextureManager.WhiteTexture)
            {
                glMesh.UseAlphaTest = built.HasAlphaTest;
                glMesh.HasAlphaBlend = built.HasAlphaBlend;
                glMesh.AlphaThreshold = built.AlphaThreshold;
            }
            else
            {
                glMesh.IsRendering = false;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TEXTURE OVERRIDES
    // ═══════════════════════════════════════════════════════════════════════

    public void ApplyTextureOverrides(IEnumerable<FilePathReplacement> overrides)
    {
        var overrideList = overrides.ToList();

        // Queue when the scene is empty, the texture manager isn't ready, OR a
        // rebuild is in-flight. The rebuild check is what catches the subgroup
        // re-selection case: between LoadNpcAsync queueing _pendingScene and the
        // render callback running ClearScene()+rebuild, _meshesByBodyPart still
        // holds the previous meshes and without this flag we'd apply overrides
        // to meshes that are about to be destroyed.
        if (_meshesByBodyPart.Count == 0 || TextureManager == null || _sceneRebuildPending)
        {
            LogVerbose("CharacterViewer: ApplyTextureOverrides queuing " + overrideList.Count +
                " override(s); meshes=" + _meshesByBodyPart.Count +
                ", texMgr=" + (TextureManager != null) +
                ", rebuildPending=" + _sceneRebuildPending);
            _pendingTextureOverrides = overrideList;
            return;
        }

        LogVerbose("CharacterViewer: ApplyTextureOverrides applying " + overrideList.Count +
            " override(s); tracked body parts: [" + string.Join(", ", _meshesByBodyPart.Keys) + "]");

        foreach (var replacement in overrideList)
        {
            string dest = replacement.Destination;
            if (string.IsNullOrWhiteSpace(dest) || string.IsNullOrWhiteSpace(replacement.Source))
                continue;

            string? bodyPart = ParseBodyPart(dest);
            int? slot = ParseTextureSlot(dest);
            if (bodyPart == null || slot == null)
            {
                LogVerbose("CharacterViewer: Override unparseable — dest='" + dest + "'");
                continue;
            }

            // For Head, target only the primary head shape (the face — face/hair/eyes
            // are separate shapes with different meaning for each slot). For non-head
            // body parts, apply to every shape in that NIF: some NIFs contain multiple
            // body-part shapes (e.g. hands + fingernails, body + belt) and previously
            // only the first-registered shape got the override, leaving the hovered
            // shape showing the original texture.
            List<GlMesh> targets;
            if (bodyPart == "Head")
            {
                if (!_meshesByBodyPart.TryGetValue(bodyPart, out var headMesh))
                {
                    LogVerbose("CharacterViewer: No Head mesh tracked for override — dest='" + dest + "'");
                    continue;
                }
                targets = new List<GlMesh> { headMesh };
            }
            else
            {
                targets = Renderer.Meshes.Where(m => m.BodyPart == bodyPart).ToList();
                if (targets.Count == 0)
                {
                    LogVerbose("CharacterViewer: No meshes with BodyPart='" + bodyPart +
                        "' (slot " + slot + ") — dest='" + dest + "'");
                    continue;
                }
            }

            foreach (var mesh in targets)
            {
                if (slot.Value == 0)
                {
                    mesh.DiffuseTexture = TextureManager.LoadTexture(replacement.Source);
                    RecordTextureSource(mesh, "Diffuse", replacement.Source);
                }
                else if (slot.Value == 1)
                {
                    // Shader handles MSN natively; no CPU resampling needed.
                    mesh.NormalTexture = TextureManager.LoadTexture(replacement.Source);
                    mesh.HasNormalMap = true;
                    RecordTextureSource(mesh, "Normal Map", replacement.Source);
                }
                else if (slot.Value == 2)
                {
                    // Skin/SSS — only meaningful on skin-shader meshes; harmless on others
                    // since HasSkinMap gates shader sampling.
                    mesh.SkinTexture = TextureManager.LoadTexture(replacement.Source);
                    mesh.HasSkinMap = true;
                    RecordTextureSource(mesh, "Skin/SSS", replacement.Source);
                }
                else if (slot.Value == 7)
                {
                    mesh.SpecularTexture = TextureManager.LoadTexture(replacement.Source);
                    mesh.HasSpecularMap = true;
                    mesh.HasSpecular = true;
                    RecordTextureSource(mesh, "Specular", replacement.Source);
                }
            }

            LogVerbose("CharacterViewer: Slot " + slot + " override '" + replacement.Source +
                "' → " + bodyPart + " (" + targets.Count + " shape(s))");
        }
    }

    /// <summary>
    /// Resolves a texture's origin via the asset resolver and records it on
    /// the mesh for display in the hover tooltip. If the slot already has an
    /// entry, replaces it (used when texture overrides update a slot).
    /// </summary>
    private void RecordTextureSource(GlMesh mesh, string slotLabel, string? gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath)) return;

        var source = _assetResolver.ResolveAssetSource(gamePath);

        for (int i = 0; i < mesh.TextureSources.Count; i++)
        {
            if (mesh.TextureSources[i].SlotLabel == slotLabel)
            {
                mesh.TextureSources[i] = (slotLabel, source);
                return;
            }
        }
        mesh.TextureSources.Add((slotLabel, source));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BODYSLIDE
    // ═══════════════════════════════════════════════════════════════════════

    // Kill-switch for the BodySlide deformation path. Originally set true while the
    // neck seam was under investigation; flipped back to false once the viewer was
    // integrated into the OBody editor. Kept as a mutable static (not const) so the
    // branch remains live code and this can be toggled again without code changes
    // if the seam or another deformation artifact returns.
    private static bool _bodySlideDisabled = false;

    public void ApplyBodySlide(BodySlideSetting preset, int weight)
    {
        if (_bodySlideDisabled)
        {
            NpcWeight = Math.Clamp(weight, 0, 100);
            LogVerbose("CharacterViewer: [BodySlideDisabled] ApplyBodySlide bypassed" +
                " (preset='" + (preset?.Label ?? "?") + "', weight=" + NpcWeight + ")");
            return;
        }

        // If scene isn't set up yet (pending GL work), queue for later.
        // Checking Count == 0 alone isn't enough: during a scene rebuild the previous
        // scene's meshes linger in _cachedBodyMeshes until the new load overwrites them,
        // so an ApplyBodySlide fired mid-rebuild would deform the OLD meshes and the
        // replacement load would then discard the deformation. Also gate on
        // _sceneRebuildPending so we always queue until the rebuild has committed.
        if (_cachedBodyMeshes.Count == 0 || _sceneRebuildPending)
        {
            _pendingBodySlide = (preset, weight);
            return;
        }

        NpcWeight = Math.Clamp(weight, 0, 100);

        try
        {
            // Preferred path: if a sibling .tri exists next to the worn body NIF
            // (BodySlide's "Build Morphs" output), use it. Its sparse vertex deltas
            // are authored against this exact NIF's topology, so we sidestep the
            // OSD path's reference-mesh mismatch (chopped deformation bands).
            TryLoadSiblingBodyTri();

            // OSD fallback: only load the slider-group OSD catalog when we don't
            // have a .tri to use. Avoids a wasted ShapeData scan on every
            // preset/weight change for the common case.
            if (_cachedBodyTri == null && preset.SliderGroup != null)
                LoadOsdFilesForGroup(preset.SliderGroup);

            bool haveDeltas = _cachedBodyTri != null
                           || (_cachedOsdFiles != null && _cachedOsdFiles.Count > 0);
            if (!haveDeltas) return;

            foreach (var kvp in _cachedBodyMeshes)
            {
                string shapeName = kvp.Key;
                var originalMesh = kvp.Value;

                // Find the GL mesh for this shape
                var glMesh = Renderer.Meshes.FirstOrDefault(m => m.ShapeName == shapeName);
                if (glMesh == null) continue;

                // Start from bind-pose positions
                var sourcePositions = originalMesh.BindPosePositions ?? originalMesh.Positions;
                var positions = new Vector3[sourcePositions.Length];
                Array.Copy(sourcePositions, positions, sourcePositions.Length);

                // Apply deformation -- prefer .tri (topology-matched, no LCP stripping),
                // fall back to OSD for meshes without "Build Morphs" output.
                if (_cachedBodyTri != null)
                {
                    _bodySlideDeformer.ApplyDeformationFromTri(positions, preset, NpcWeight, _cachedBodyTri, shapeName);
                }
                else
                {
                    _bodySlideDeformer.ApplyDeformation(positions, preset, NpcWeight, _cachedOsdFiles!, shapeName);
                }

                // Recalculate normals
                var sourceNormals = originalMesh.BindPoseNormals ?? originalMesh.Normals;
                var normals = new Vector3[sourceNormals.Length];
                Array.Copy(sourceNormals, normals, sourceNormals.Length);
                BodySlideDeformer.RecalculateNormals(positions, originalMesh.Indices, normals);

                // Re-apply skinning
                if (originalMesh.Skinning != null)
                    NifMeshBuilder.ApplySkinning(positions, normals, originalMesh.Skinning, positions, normals);

                // Re-upload vertex data to GPU
                var vertexData = BuildInterleavedVertexData(positions, normals,
                    originalMesh.TextureCoordinates, originalMesh.Tangents, originalMesh.Bitangents,
                    originalMesh.VertexColors);
                glMesh.UpdateVertexData(vertexData);

                // Update CPU-side positions for hit testing
                glMesh.CpuPositions = positions;
            }
        }
        catch (Exception ex)
        {
            // Surface deformer / skinning / GPU-upload failures with a full stack so the
            // Status Log actually shows what went wrong. Without this catch the exception
            // bubbles up to VM_BodySlideSetting.RefreshPreview, which used to swallow it
            // silently via LogMessage and the user only saw the tab-switch with no detail.
            _logger.LogError("CharacterViewer: ApplyBodySlide failed for preset '"
                + (preset?.Label ?? "?") + "' at weight " + NpcWeight + Environment.NewLine
                + ExceptionLogger.GetExceptionStack(ex));
        }

        // Fire regardless of deformation outcome so subscribers can refresh readouts;
        // a failed deformation leaves CpuPositions in a valid (undeformed) state that
        // is still meaningful to measure.
        RefreshKeyVertexMarkerPositions();
        BodySlideApplied?.Invoke();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BODYGEN OVERRIDES
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies a stack of BodyGen templates by parsing and summing their Specs into
    /// a virtual BodySlideSetting, then routing through the existing ApplyBodySlide
    /// path. Matches BodyGen runtime behavior where templates stack additively on
    /// the same NPC.
    /// </summary>
    public void ApplyBodyGen(IEnumerable<BodyGenConfig.BodyGenTemplate> templates, string sliderGroup, int weight)
    {
        var list = templates?.Where(t => t != null).ToList() ?? new List<BodyGenConfig.BodyGenTemplate>();
        if (list.Count == 0) return;

        var merged = BodyGenSpecsParser.ParseAndMerge(
            list.Select(t => t.Specs ?? string.Empty),
            sliderGroup,
            out var errors);

        if (errors.Count > 0)
        {
            LogVerbose("CharacterViewer.ApplyBodyGen parse warnings: " + string.Join("; ", errors));
        }

        if (merged.SliderValues.Count == 0) return;
        ApplyBodySlide(merged, weight);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HEADPART OVERRIDES
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reloads <paramref name="npcFormKey"/> with <paramref name="assignments"/> applied
    /// as head-part overrides. Generates a preview FaceGen NIF via FaceGenPatcher and
    /// hands its path to <see cref="LoadNpcAsync"/> as the head-mesh override, matching
    /// the flow used by the Headparts editor (single-type) but supporting a full
    /// multi-type dictionary.
    /// </summary>
    public async Task ApplyHeadPartsAsync(FormKey npcFormKey, ILinkCache linkCache, IReadOnlyDictionary<HeadPart.TypeEnum, FormKey> assignments, CancellationToken ct = default)
    {
        if (npcFormKey.IsNull || linkCache == null) return;

        var validAssignments = assignments?
            .Where(kv => !kv.Value.IsNull)
            .ToDictionary(kv => kv.Key, kv => kv.Value) ?? new();

        if (validAssignments.Count == 0)
        {
            await LoadNpcAsync(npcFormKey, linkCache);
            return;
        }

        string? nifPath;
        try
        {
            nifPath = await _faceGenPreviewService.GeneratePreviewFaceGenAsync(npcFormKey, validAssignments, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer.ApplyHeadPartsAsync: preview FaceGen generation failed: " + ex.Message);
            nifPath = null;
        }

        ct.ThrowIfCancellationRequested();

        // P2 fast path: if the NPC is already loaded and the scene is committed,
        // rebuild only the Head shape(s). Body/Hands/Feet keep their current
        // textures and any in-progress BodySlide deformation — a full LoadNpcAsync
        // would re-parse all four NIFs and re-decode all their DDS textures just
        // to swap the head. Fall back to the full-reload branch when any of the
        // preconditions fail (scene not committed, different NPC, GL not ready,
        // or no FaceGen NIF was produced).
        if (nifPath != null
            && !_sceneRebuildPending
            && _meshesByBodyPart.Count > 0
            && npcFormKey == _currentLoadedNpc
            && _cachedMeshPaths != null
            && TextureManager != null)
        {
            await RebuildHeadOnlyAsync(nifPath, ct);
            return;
        }

        await LoadNpcAsync(npcFormKey, linkCache, overrideHeadMeshAbsolutePath: nifPath);
    }

    /// <summary>
    /// Parses <paramref name="headNifPath"/> off-thread and queues the result for
    /// installation on the render thread via <see cref="ProcessPendingScene"/>.
    /// The install step removes current Head shape(s), creates new GlMesh(es),
    /// applies textures using the cached <see cref="_cachedMeshPaths"/> (face tint),
    /// and preserves all Body/Hands/Feet state untouched.
    /// </summary>
    private async Task RebuildHeadOnlyAsync(string headNifPath, CancellationToken ct)
    {
        var headStopwatch = System.Diagnostics.Stopwatch.StartNew();
        LogLoadCheckpoint(headStopwatch, "RebuildHeadOnlyAsync begin (" +
            System.IO.Path.GetFileName(headNifPath) + ")");

        // Parse with no skeleton: FaceGen head NIFs are rigid / self-skinned and
        // the body skeleton is not needed to produce correct vertex positions.
        // This matches how LoadAllMeshParts invokes BuildFromFile for the Head
        // when skeletonNif is null.
        List<NifMeshBuilder.BuiltMesh> meshes;
        try
        {
            meshes = await Task.Run(() => _meshBuilder.BuildFromFile(headNifPath, skeletonNif: null), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer.RebuildHeadOnlyAsync: head NIF parse failed: " +
                ExceptionLogger.GetExceptionStack(ex));
            return;
        }

        if (meshes.Count == 0)
        {
            LogVerbose("CharacterViewer.RebuildHeadOnlyAsync: no renderable shapes in " + headNifPath);
            return;
        }

        ct.ThrowIfCancellationRequested();

        LogLoadCheckpoint(headStopwatch, "Head NIF parsed (" + meshes.Count + " shape(s))");

        Application.Current.Dispatcher.Invoke(() =>
        {
            _pendingHeadReplace = (headNifPath, meshes);
            _pendingHeadReplaceStopwatch = headStopwatch;
        });
        LogLoadCheckpoint(headStopwatch, "Head replace queued for GL install");
    }

    /// <summary>
    /// Installs the queued head replacement. Runs on the render thread from
    /// <see cref="ProcessPendingScene"/> where the GL context is current.
    /// </summary>
    private void InstallReplacedHead()
    {
        if (_pendingHeadReplace == null || TextureManager == null || _cachedMeshPaths == null)
        {
            _pendingHeadReplace = null;
            _pendingHeadReplaceStopwatch = null;
            return;
        }

        var (headNifPath, meshes) = _pendingHeadReplace.Value;
        _pendingHeadReplace = null;
        var headStopwatch = _pendingHeadReplaceStopwatch;
        _pendingHeadReplaceStopwatch = null;
        LogLoadCheckpoint(headStopwatch, "InstallReplacedHead start (GL install begin)");

        // Tear down existing Head shape(s). A FaceGen NIF may contain multiple
        // shapes (face, eyes, hair, ...), all tagged with BodyPart = "Head" when
        // added to the renderer — remove every one of them.
        var oldHeads = Renderer.Meshes.Where(m => m.BodyPart == "Head").ToList();
        foreach (var m in oldHeads)
        {
            Renderer.RemoveMesh(m);
            _textureApplyInfoByMesh.Remove(m);
            m.Dispose();
        }
        _meshesByBodyPart.Remove("Head");
        _builtMeshesByBodyPart.Remove("Head");

        // Install fresh head shape(s). Mirrors the Head branch of ProcessPendingScene.
        foreach (var built in meshes)
        {
            var glMesh = CreateGlMesh(built);

            var effectiveTextures = new Dictionary<int, string>(built.TexturePaths);
            // Head never has TxstTextures overrides (see ProcessPendingScene's
            // bodyPart != "Head" guard) — nothing to merge in.

            bool isHairTint = false;
            float hairR = 0, hairG = 0, hairB = 0;
            bool isFaceTint = false;
            string? faceTintPath = null;

            ApplyTexturesToGlMesh(glMesh, built, effectiveTextures, _cachedMeshPaths,
                ref isHairTint, ref hairR, ref hairG, ref hairB,
                ref isFaceTint, ref faceTintPath);

            _textureApplyInfoByMesh[glMesh] = new TextureApplyInfo(
                new Dictionary<int, string>(effectiveTextures),
                isHairTint, hairR, hairG, hairB, isFaceTint, faceTintPath);

            glMesh.BodyPart = "Head";
            glMesh.ShowWireframe = ShowWireframe;
            Renderer.AddMesh(glMesh);

            if (built.IsPrimaryHeadShape || !_meshesByBodyPart.ContainsKey("Head"))
                _meshesByBodyPart["Head"] = glMesh;
            if (built.IsPrimaryHeadShape || !_builtMeshesByBodyPart.ContainsKey("Head"))
                _builtMeshesByBodyPart["Head"] = built;
        }

        // Keep the short-circuit identity in sync: a subsequent LoadNpcAsync call
        // with this same override path must still rebuild (file contents may
        // change), which is exactly what the "overrideHeadMeshAbsolutePath == null"
        // half of the short-circuit already enforces. We record the current
        // override so other inspection / future logic can read it.
        _currentHeadMeshOverride = headNifPath;

        LogLoadCheckpoint(headStopwatch, "Head install complete (" + meshes.Count +
            " shape(s) from " + System.IO.Path.GetFileName(headNifPath) + ")");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SCENE MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════════

    public void ClearScene()
    {
        Renderer.ClearMeshes();
        _meshesByBodyPart.Clear();
        _builtMeshesByBodyPart.Clear();
        _cachedBodyMeshes.Clear();
        _textureApplyInfoByMesh.Clear();
        _cachedOsdFiles = null;
        _cachedBodyNifDiskPath = null;
        _cachedBodyTri = null;
        BodyTriMissing = false;
        _currentLoadedNpc = FormKey.Null;
        _currentHeadMeshOverride = null;
    }

    private bool _disposed;

    /// <summary>
    /// Releases GL resources (shaders, VBO/VAO, loaded textures), cancels any in-flight
    /// NPC-load async work, and drops scene caches. Called when the owning parent VM
    /// (e.g. VM_BodyGenTemplateMenu, VM_BodySlideSetting) is itself disposed — which
    /// in turn happens when its grandparent (e.g. a BodyGen config being swapped) is
    /// torn down.
    ///
    /// GL delete calls must ideally run while the GL context is current. When the
    /// owning UserControl has already been unloaded, the context may no longer be
    /// current on this thread; in that case GL.DeleteBuffer / DeleteTexture on most
    /// drivers are silent no-ops (the resources are reclaimed when the context itself
    /// is destroyed). We wrap in try/catch so a stray driver throw doesn't propagate
    /// out of the dispose chain and bring down the settings load.
    /// </summary>
    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Cancel any in-flight NPC load so its continuation doesn't race with
        // the scene being torn down.
        try
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
        }
        catch { /* best-effort */ }
        _loadCts = null;

        // Drop every scene-level cache; _pending* holders would otherwise pin
        // BuiltMesh data (with its vertex/index buffers) until GC.
        _pendingScene = null;
        _pendingTextureOverrides = null;
        _pendingBodySlide = null;
        _pendingHeadReplace = null;
        _meshesByBodyPart.Clear();
        _builtMeshesByBodyPart.Clear();
        _cachedBodyMeshes.Clear();
        _textureApplyInfoByMesh.Clear();
        _cachedOsdFiles = null;
        _cachedBodyNifDiskPath = null;
        _cachedBodyTri = null;
        _cachedMeshPaths = null;

        try
        {
            if (IsGlInitialized)
            {
                TextureManager?.Dispose();
                TextureManager = null;
                Renderer.Dispose();
                IsGlInitialized = false;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("VM_CharacterViewer.Dispose: GL cleanup threw: "
                + ExceptionLogger.GetExceptionStack(ex));
        }

        // Tears down reactive subscriptions added via DisposeWith(this).
        base.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    private List<(string BodyPart, AssetSource? MeshSource, List<NifMeshBuilder.BuiltMesh> Meshes)> LoadAllMeshParts(
        NpcMeshResolver.NpcMeshPaths meshPaths)
    {
        var results = new List<(string, AssetSource?, List<NifMeshBuilder.BuiltMesh>)>();

        nifly.NifFile? skeletonNif = null;
        string? skelDiskPath = null;
        if (!string.IsNullOrWhiteSpace(meshPaths.SkeletonPath))
        {
            skelDiskPath = _assetResolver.ResolveAssetPath(meshPaths.SkeletonPath);
            if (skelDiskPath != null)
            {
                skeletonNif = new nifly.NifFile();
                if (skeletonNif.Load(skelDiskPath) != 0)
                {
                    skeletonNif.Dispose();
                    skeletonNif = null;
                    skelDiskPath = null;
                }
            }
        }

        try
        {
            void TryLoad(string bodyPart, string? gamePath)
            {
                if (string.IsNullOrWhiteSpace(gamePath)) return;
                var source = _assetResolver.ResolveAssetSource(gamePath);
                if (source.ResolvedDiskPath == null) return;
                var meshes = _meshBuilder.BuildFromFile(source.ResolvedDiskPath, skeletonNif, skelDiskPath);
                if (meshes.Count == 0) return;

                // Weight morph: armor meshes ship as _0/_1 pairs that the game engine
                // linearly interpolates by NpcWeight (0..100). The FaceGen head is already
                // baked at the NPC's weight so it needs no morph. Skinning is linear in
                // vertex position, so blending the already-skinned world-space positions
                // is equivalent to blending bind-pose and re-skinning (both _0 and _1
                // share the same skeleton and skinToBone transforms).
                if (bodyPart != "Head" && NpcWeight < 100)
                {
                    string? weight0Path = TryGetWeightZeroPath(gamePath);
                    if (weight0Path != null)
                    {
                        var weight0Source = _assetResolver.ResolveAssetSource(weight0Path);
                        if (weight0Source.ResolvedDiskPath != null)
                        {
                            var meshes0 = _meshBuilder.BuildFromFile(weight0Source.ResolvedDiskPath, skeletonNif, skelDiskPath);
                            float t = NpcWeight / 100f;
                            BlendWeightMorph(meshes0, meshes, t, bodyPart);
                        }
                        else
                        {
                            LogVerbose("CharacterViewer: [WeightMorph] '" + bodyPart +
                                "' weight-0 '" + weight0Path + "' not found — using _1.nif unmorphed");
                        }
                    }
                    else
                    {
                        LogVerbose("CharacterViewer: [WeightMorph] '" + bodyPart +
                            "' path '" + gamePath + "' does not end in _1.nif — skipping weight morph");
                    }
                }

                results.Add((bodyPart, source, meshes));
            }

            TryLoad("Body", meshPaths.BodyMeshPath);
            TryLoad("Hands", meshPaths.HandsMeshPath);
            TryLoad("Feet", meshPaths.FeetMeshPath);
            TryLoad("Head", meshPaths.HeadMeshPath);
        }
        finally
        {
            skeletonNif?.Dispose();
        }

        return results;
    }

    /// <summary>
    /// Creates a GlMesh from a BuiltMesh, uploading interleaved vertex data and indices.
    /// </summary>
    private GlMesh CreateGlMesh(NifMeshBuilder.BuiltMesh built)
    {
        var vertexData = BuildInterleavedVertexData(
            built.Positions, built.Normals, built.TextureCoordinates,
            built.Tangents, built.Bitangents, built.VertexColors);

        var glMesh = new GlMesh();
        glMesh.Upload(vertexData, built.Indices);
        glMesh.ShapeName = built.ShapeName;
        glMesh.IsPrimaryHeadShape = built.IsPrimaryHeadShape;

        // Store CPU-side geometry for ray-based hit testing
        glMesh.CpuPositions = (Vector3[])built.Positions.Clone();
        glMesh.CpuIndices = (int[])built.Indices.Clone();

        return glMesh;
    }

    /// <summary>
    /// Builds interleaved vertex data array for the GL mesh.
    /// Layout per vertex: position(3) + normal(3) + texcoord(2) + color(4) + tangent(3) + bitangent(3) = 18 floats
    /// </summary>
    private static float[] BuildInterleavedVertexData(
        Vector3[] positions, Vector3[] normals, Vector2[] uvs,
        Vector3[] tangents, Vector3[] bitangents, Vector4[]? vertexColors = null)
    {
        int vertCount = positions.Length;
        var data = new float[vertCount * 18];

        for (int i = 0; i < vertCount; i++)
        {
            int offset = i * 18;
            var p = positions[i];
            var n = i < normals.Length ? normals[i] : Vector3.UnitY;
            var uv = i < uvs.Length ? uvs[i] : Vector2.Zero;
            var t = i < tangents.Length ? tangents[i] : Vector3.Zero;
            var b = i < bitangents.Length ? bitangents[i] : Vector3.Zero;

            // Position
            data[offset]     = p.X;
            data[offset + 1] = p.Y;
            data[offset + 2] = p.Z;
            // Normal
            data[offset + 3] = n.X;
            data[offset + 4] = n.Y;
            data[offset + 5] = n.Z;
            // UV
            data[offset + 6] = uv.X;
            data[offset + 7] = uv.Y;
            // Vertex color
            if (vertexColors != null && i < vertexColors.Length)
            {
                var vc = vertexColors[i];
                data[offset + 8]  = vc.X;
                data[offset + 9]  = vc.Y;
                data[offset + 10] = vc.Z;
                data[offset + 11] = vc.W;
            }
            else
            {
                data[offset + 8]  = 1f;
                data[offset + 9]  = 1f;
                data[offset + 10] = 1f;
                data[offset + 11] = 1f;
            }
            // Tangent
            data[offset + 12] = t.X;
            data[offset + 13] = t.Y;
            data[offset + 14] = t.Z;
            // Bitangent
            data[offset + 15] = b.X;
            data[offset + 16] = b.Y;
            data[offset + 17] = b.Z;
        }

        return data;
    }

    /// <summary>
    /// Looks for a sibling .tri next to the currently-loaded body NIF and parses it
    /// on first use. Result (including parse failure → null) is cached per scene so
    /// this is a no-op on subsequent preset/weight changes. Silently does nothing
    /// if no body NIF path has been captured yet.
    ///
    /// Probes both naming conventions Skyrim uses: the .tri may share the NIF's
    /// stem verbatim (e.g. `custombody.nif` → `custombody.tri`) or may be the
    /// weight-stripped form (e.g. `FemaleBody_1.nif` → `femalebody.tri`), since
    /// vanilla / BodySlide-built bodies ship paired `_0`/`_1` NIFs but a single
    /// shared .tri whose morph deltas are identical between weight variants.
    /// </summary>
    private void TryLoadSiblingBodyTri()
    {
        if (_cachedBodyTri != null) return;
        if (_cachedBodyNifDiskPath == null) return;

        string? triPath = ProbeSiblingTriPath(_cachedBodyNifDiskPath);
        if (triPath == null)
        {
            LogVerbose("CharacterViewer: No sibling .tri found for '" + _cachedBodyNifDiskPath +
                "' -- falling back to OSD path (chopping bug possible if topology mismatches reference).");
            _cachedBodyNifDiskPath = null; // don't re-probe
            BodyTriMissing = true;
            return;
        }

        _cachedBodyTri = _bodyTriFileParser.Parse(triPath);
        if (_cachedBodyTri == null)
        {
            LogVerbose("CharacterViewer: Sibling .tri at '" + triPath +
                "' failed to parse -- falling back to OSD path.");
            _cachedBodyNifDiskPath = null;
            return;
        }

        BodyTriMissing = false;
        int totalMorphs = 0;
        foreach (var shape in _cachedBodyTri.Shapes) totalMorphs += shape.Morphs.Count;
        LogVerbose("CharacterViewer: Using sibling .tri '" + triPath + "' (" +
            _cachedBodyTri.Shapes.Count + " shape(s), " + totalMorphs + " total morph(s))");
    }

    /// <summary>
    /// Returns the first existing .tri sibling for <paramref name="nifDiskPath"/>,
    /// trying the stripped-weight-suffix form first (matches vanilla/BodySlide
    /// convention). Null if neither exists.
    /// </summary>
    private static string? ProbeSiblingTriPath(string nifDiskPath)
    {
        string dir = Path.GetDirectoryName(nifDiskPath) ?? "";
        string stem = Path.GetFileNameWithoutExtension(nifDiskPath);

        // Strip trailing _0 or _1 weight suffix if present.
        string strippedStem = stem;
        if (stem.Length > 2 && stem[^2] == '_' && (stem[^1] == '0' || stem[^1] == '1'))
        {
            strippedStem = stem.Substring(0, stem.Length - 2);
        }

        if (!string.Equals(strippedStem, stem, StringComparison.Ordinal))
        {
            string strippedPath = Path.Combine(dir, strippedStem + ".tri");
            if (File.Exists(strippedPath)) return strippedPath;
        }

        string samePath = Path.Combine(dir, stem + ".tri");
        if (File.Exists(samePath)) return samePath;

        return null;
    }

    private void LoadOsdFilesForGroup(string sliderGroup)
    {
        string dataFolder = _environmentProvider.DataFolderPath;
        string shapeDataRoot = Path.Combine(dataFolder, "CalienteTools", "BodySlide", "ShapeData");
        if (!Directory.Exists(shapeDataRoot))
        {
            _cachedOsdFiles = new List<OsdFile>();
            return;
        }

        // Primary path: look up the body type in the registry and parse the OSD/BSD files in
        // the entry's declared ShapeDataFolders. If the entry is a superset of another body
        // (e.g. CBBE 3BA ⊃ CBBE), include the parent's folders too -- a 3BA preset may move
        // CBBE-shared sliders whose deltas live only in the CBBE shape data.
        var registry = _patcherState?.OBodySettings?.BodyTypeRegistry;
        var entry = FindRegistryEntry(registry, sliderGroup);
        if (entry != null)
        {
            var folders = new List<string>();
            CollectShapeDataFolders(entry, registry, folders, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var registryOsd = new List<OsdFile>();
            foreach (var rawPath in folders)
            {
                var sub = rawPath.Replace('/', Path.DirectorySeparatorChar)
                                 .Replace('\\', Path.DirectorySeparatorChar)
                                 .TrimStart(Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(shapeDataRoot, sub);
                if (File.Exists(fullPath))
                {
                    var osd = string.Equals(Path.GetExtension(fullPath), ".bsd", StringComparison.OrdinalIgnoreCase)
                        ? _bsdFileParser.ParseBsdFile(fullPath)
                        : _bsdFileParser.ParseOsdFile(fullPath);
                    if (osd != null && seen.Add(osd.ShapeName)) registryOsd.Add(osd);
                }
                else if (Directory.Exists(fullPath))
                {
                    foreach (var osd in _bsdFileParser.ParseAllOsdInDirectory(fullPath, recursive: false))
                    {
                        if (osd != null && seen.Add(osd.ShapeName)) registryOsd.Add(osd);
                    }
                }
            }
            _cachedOsdFiles = registryOsd;
            return;
        }

        // Fallback (registry miss / "Unknown" preset): legacy substring scan over every direct
        // child of ShapeData, then full-tree scan if no name contained the group string.
        var matchingDirs = Directory.GetDirectories(shapeDataRoot)
            .Where(d => Path.GetFileName(d).Contains(sliderGroup, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matchingDirs.Length == 0)
            matchingDirs = Directory.GetDirectories(shapeDataRoot);

        var allOsd = new List<OsdFile>();
        foreach (var dir in matchingDirs)
            allOsd.AddRange(_bsdFileParser.ParseAllOsdInDirectory(dir));

        _cachedOsdFiles = allOsd;
    }

    private static BodyTypeRegistryEntry FindRegistryEntry(List<BodyTypeRegistryEntry> registry, string name)
    {
        if (registry == null || string.IsNullOrWhiteSpace(name)) return null;
        foreach (var e in registry)
        {
            if (e != null && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) return e;
        }
        return null;
    }

    private static void CollectShapeDataFolders(BodyTypeRegistryEntry entry, List<BodyTypeRegistryEntry> registry, List<string> folders, HashSet<string> visited)
    {
        if (entry == null || !visited.Add(entry.Name)) return;
        if (entry.ShapeDataFolders != null)
        {
            foreach (var f in entry.ShapeDataFolders)
            {
                if (!string.IsNullOrWhiteSpace(f)) folders.Add(f);
            }
        }
        if (!string.IsNullOrWhiteSpace(entry.SupersetOfBodyType))
        {
            var parent = FindRegistryEntry(registry, entry.SupersetOfBodyType);
            CollectShapeDataFolders(parent, registry, folders, visited);
        }
    }

    // Derives the weight-0 counterpart path for a NIF path that ends in "_1.nif".
    // Returns null if the input doesn't follow the standard BodySlide weight-pair naming.
    private static string? TryGetWeightZeroPath(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath)) return null;
        const string suffix = "_1.nif";
        if (gamePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return gamePath.Substring(0, gamePath.Length - suffix.Length) + "_0.nif";
        return null;
    }

    // Linearly interpolates weight-0 geometry into the weight-1 meshes by factor t,
    // where t = NpcWeight / 100 (t=0 → all weight-0, t=1 → all weight-1). Matches the
    // game engine's body-weight morph. Operates in-place on meshes1 arrays.
    // Shape matching is by ShapeName; mismatched shapes or vertex counts are skipped.
    private void BlendWeightMorph(List<NifMeshBuilder.BuiltMesh> meshes0,
        List<NifMeshBuilder.BuiltMesh> meshes1, float t, string bodyPart)
    {
        foreach (var m1 in meshes1)
        {
            var m0 = meshes0.FirstOrDefault(m => m.ShapeName == m1.ShapeName);
            if (m0 == null)
            {
                LogVerbose("CharacterViewer: [WeightMorph] '" + bodyPart + "' shape '" +
                    m1.ShapeName + "' has no match in weight-0 NIF — skipping");
                continue;
            }
            if (m0.Positions.Length != m1.Positions.Length)
            {
                LogVerbose("CharacterViewer: [WeightMorph] '" + bodyPart + "' shape '" +
                    m1.ShapeName + "' vertex count mismatch (_0=" + m0.Positions.Length +
                    ", _1=" + m1.Positions.Length + ") — skipping");
                continue;
            }

            int n = m1.Positions.Length;
            for (int i = 0; i < n; i++)
                m1.Positions[i] = Vector3.Lerp(m0.Positions[i], m1.Positions[i], t);

            BlendAndRenormalize(m0.Normals, m1.Normals, t, n);
            BlendAndRenormalize(m0.Tangents, m1.Tangents, t, n);
            BlendAndRenormalize(m0.Bitangents, m1.Bitangents, t, n);

            if (m0.BindPosePositions != null && m1.BindPosePositions != null &&
                m0.BindPosePositions.Length == n && m1.BindPosePositions.Length == n)
            {
                for (int i = 0; i < n; i++)
                    m1.BindPosePositions[i] = Vector3.Lerp(m0.BindPosePositions[i], m1.BindPosePositions[i], t);
            }
            if (m0.BindPoseNormals != null && m1.BindPoseNormals != null &&
                m0.BindPoseNormals.Length == n && m1.BindPoseNormals.Length == n)
            {
                BlendAndRenormalize(m0.BindPoseNormals, m1.BindPoseNormals, t, n);
            }

            LogVerbose("CharacterViewer: [WeightMorph] '" + bodyPart + "' shape '" +
                m1.ShapeName + "' blended " + n + " verts at t=" + t.ToString("F2"));
        }
    }

    private static void BlendAndRenormalize(Vector3[] src0, Vector3[] src1, float t, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var v = Vector3.Lerp(src0[i], src1[i], t);
            float len = v.Length();
            src1[i] = len > 1e-6f ? v / len : src1[i];
        }
    }

    public static string? ParseBodyPart(string destination)
    {
        if (destination.StartsWith("HeadTexture", StringComparison.OrdinalIgnoreCase))
            return "Head";
        if (destination.Contains("SkinTexture", StringComparison.OrdinalIgnoreCase) ||
            destination.Contains("WorldModel", StringComparison.OrdinalIgnoreCase))
        {
            if (destination.Contains("BipedObjectFlag.Body", StringComparison.OrdinalIgnoreCase)) return "Body";
            if (destination.Contains("BipedObjectFlag.Hands", StringComparison.OrdinalIgnoreCase)) return "Hands";
            if (destination.Contains("BipedObjectFlag.Feet", StringComparison.OrdinalIgnoreCase)) return "Feet";
        }
        return null;
    }

    public static int? ParseTextureSlot(string destination)
    {
        if (destination.Contains("BacklightMaskOrSpecular", StringComparison.OrdinalIgnoreCase)) return 7;
        if (destination.Contains("NormalOrGloss", StringComparison.OrdinalIgnoreCase)) return 1;
        if (destination.Contains("GlowOrDetailMap", StringComparison.OrdinalIgnoreCase)) return 2;
        if (destination.Contains("Diffuse", StringComparison.OrdinalIgnoreCase)) return 0;
        if (destination.Contains("Height", StringComparison.OrdinalIgnoreCase)) return 3;
        return null;
    }
}
