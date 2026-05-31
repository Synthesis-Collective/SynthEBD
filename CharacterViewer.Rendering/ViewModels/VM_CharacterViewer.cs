using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ReactiveUI;
using MediaColor = System.Windows.Media.Color;

namespace CharacterViewer.Rendering;

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
public class VM_CharacterViewer : ViewerVm
{
    /// <summary>Composite for reactive subscriptions added via
    /// <see cref="System.Reactive.Disposables.DisposableMixins.DisposeWith{T}"/>.
    /// Disposed in <see cref="Dispose"/>. Replaces the previous
    /// <c>SynthEBD.VM</c> base-class IDisposableDropoff plumbing.</summary>
    private readonly CompositeDisposable _disposables = new();

    private readonly NifMeshBuilder _meshBuilder;
    private readonly BodySlideDeformer _bodySlideDeformer;
    private readonly BsdFileParser _bsdFileParser;
    private readonly BodyTriFileParser _bodyTriFileParser;
    private readonly GameAssetResolver _assetResolver;
    private readonly ICharacterViewerLogger _logger;
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

    /// <summary>Cached built meshes for reapplying BodySlide without reloading.
    /// <see cref="NifMeshBuilder.BuiltMesh.BindPosePositions"/> here holds the
    /// load-time blend (so head/body neck alignment is preserved when nothing else
    /// has been applied), and the per-mesh
    /// <see cref="NifMeshBuilder.BuiltMesh.Weight0BindPosePositions"/> /
    /// <see cref="NifMeshBuilder.BuiltMesh.Weight1BindPosePositions"/> snapshots
    /// taken before <c>BlendWeightMorph</c> let <see cref="ApplyMorphSet"/> redo the
    /// engine-equivalent _0/_1 lerp at any NpcWeight on demand.</summary>
    private readonly Dictionary<string, NifMeshBuilder.BuiltMesh> _cachedBodyMeshes = new();

    private List<OsdFile>? _cachedOsdFiles;

    /// <summary>Disk path of the currently-loaded body NIF, used to locate the
    /// sibling .tri for topology-matched morphing (BodySlide "Build Morphs" output).</summary>
    private string? _cachedBodyNifDiskPath;

    /// <summary>Parsed sibling .tri for the current body NIF, cached across preset/weight
    /// changes so we don't re-parse on every RefreshPreview. Null when no .tri is
    /// present next to the NIF -- in that case we fall back to the OSD path.</summary>
    private BodyTriFile? _cachedBodyTri;

    private ResolvedNpcMeshPaths? _cachedMeshPaths;

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

    private readonly ICharacterViewerSettings _generalSettings;

    /// <summary>True when the GL context has been initialized.</summary>
    public bool IsGlInitialized { get; private set; }

    /// <summary>Pending scene data waiting for GL context to become available.</summary>
    private (List<(string BodyPart, AssetSource? MeshSource, List<NifMeshBuilder.BuiltMesh> Meshes)> LoadResults,
             ResolvedNpcMeshPaths MeshPaths)? _pendingScene;

    /// <summary>
    /// In-flight install state for the sliced GL upload. Non-null between the
    /// render tick that consumes <see cref="_pendingScene"/> and the tick where
    /// the queue drains, so the GL upload phase spans multiple frames instead
    /// of stalling for the whole ~20-texture × N-shape mipmap-gen pass in one.
    ///
    /// A new <see cref="_pendingScene"/> arriving mid-install causes the
    /// in-progress install to be abandoned (its partially-uploaded GL meshes
    /// are torn down by <see cref="ClearScene"/>) and a fresh install to start.
    /// </summary>
    private SceneInstallState? _sceneInstall;

    /// <summary>Per-render-tick GL upload budget for the sliced install. The
    /// install loop pops shapes from the queue until either it's empty or this
    /// budget is exceeded; the next render tick continues. 6 ms keeps 60 FPS
    /// smooth even mid-install. Set to 0 for strict one-shape-per-tick mode.</summary>
    private const double GlInstallBudgetMs = 6.0;

    private sealed record SceneInstallState(
        ResolvedNpcMeshPaths MeshPaths,
        Queue<PendingShape> Pending,
        string LoadIdentityKey,
        string? HeadMeshOverride,
        Stopwatch? LoadStopwatch,
        int TotalShapes)
    {
        public int Installed { get; set; }
    }

    private readonly record struct PendingShape(
        string BodyPart,
        AssetSource? MeshSource,
        Dictionary<int, string>? TxstOverrides,
        NifMeshBuilder.BuiltMesh Built);

    /// <summary>True from the moment a new NPC load starts until the render callback
    /// has rebuilt the scene. Routes texture overrides to the pending queue so they
    /// aren't applied to meshes that are about to be destroyed by ClearScene().</summary>
    private bool _sceneRebuildPending;

    /// <summary>Pending texture overrides (neutral form) to apply after scene
    /// setup. The host-coupled <see cref="ApplyTextureOverrides(IEnumerable{FilePathReplacement})"/>
    /// converts to <see cref="TextureOverride"/> before queueing.</summary>
    private List<TextureOverride>? _pendingTextureOverrides;

    /// <summary>Pending neutral morph application to apply after scene setup.
    /// Set by direct callers of <see cref="ApplyMorphSet"/>; assumes the host
    /// has already called <see cref="SetMorphContext"/> (or that a sibling .tri
    /// will be auto-loaded by the apply path).</summary>
    private (MorphSet Morphs, int Weight)? _pendingMorphSet;

    /// <summary>NpcIdentity.CacheKey of the NPC whose scene is currently installed
    /// in the renderer. Captured at the end of ProcessPendingScene; cleared by
    /// ClearScene. Used by LoadAsync to short-circuit reloads of the same NPC
    /// when narrow editors (BodySlide preset change, AssetPack subgroup flip)
    /// call LoadAsync defensively even though only a narrow downstream update
    /// is needed. Empty string when no NPC is loaded.</summary>
    private string _currentLoadedIdentityKey = "";

    /// <summary>Head-mesh override path baked into the currently-installed scene
    /// (absolute path from the host's FaceGen preview output), or null if the
    /// scene used the NPC's resolved head mesh. Compared case-insensitively as
    /// part of the same-NPC short-circuit in LoadAsync.</summary>
    private string? _currentHeadMeshOverride;

    /// <summary>NpcIdentity.CacheKey of the load whose results are queued in
    /// _pendingScene. Promoted to _currentLoadedIdentityKey by
    /// ProcessPendingScene once the scene commits. Empty when no load pending.</summary>
    private string _pendingLoadIdentityKey = "";

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

    // ─── Per-load resolver scope snapshot ───────────────────────────────────
    // The resolver's scope state is AsyncLocal-backed, so values pushed inside
    // LoadAsync's flow naturally reach off-thread NIF parsing via Task.Run.
    // BUT the install side runs on the WPF render callback (CompositionTarget.
    // Rendering → ProcessPendingScene), which has its own ExecutionContext and
    // does NOT carry LoadAsync's AsyncLocal value. We snapshot here so the
    // install ticks can re-push the same values onto the resolver each tick.
    //
    // Lifecycle: populated at the top of LoadAsync (and RebuildHeadOnlyAsync),
    // cleared at the end of ProcessPendingScene's finalize block (success) or
    // in LoadAsync's catch/finally when SceneCommitted won't fire (cancel /
    // error before commit). A re-entrant LoadAsync overwrites with its own
    // snapshot before the previous install finishes — same behavior the prior
    // singleton-field design had.
    private IReadOnlyList<RenderScope>? _currentSceneScopes;
    private IReadOnlyList<string>? _currentSceneFolders;
    private bool _currentSceneVanillaLooseOverridesBsa = true;
    private bool _currentSceneVanillaLooseOverridesModLoose;

    private readonly CharacterPreviewCache _previewCache;
    private readonly IRenderThreadMarshaller _renderThread;

    // ═══════════════════════════════════════════════════════════════════════
    //  PUBLIC HOST-EXTENSION SURFACE
    //  These members exist for SynthEBD-side (and future NPC2-side) extension
    //  methods that wrap the neutral viewer with their own host-coupled
    //  workflows. The VM itself doesn't reference SynthEBD types.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Fired at the end of <see cref="ProcessPendingScene"/> after
    /// the new scene is installed and any pending neutral overrides/morphs
    /// have drained. Hosts subscribe to drain queues that depend on
    /// scene-ready state — SynthEbdViewerHostState uses this to apply a
    /// queued ApplyBodySlide once the body NIF disk path is cached.</summary>
    public event Action? SceneCommitted;

    /// <summary>Fired when a property that affects how the camera should
    /// frame the scene changes — currently <see cref="FieldOfView"/>. Hosts
    /// in mesh-aware framing modes should subscribe and re-apply their
    /// framing (typically via <c>MeshAwareCameraFitter.ApplyTo</c>) so the
    /// character stays the same size on-screen across FOV changes; manual
    /// orbit hosts can ignore this and let the slider act as a pure zoom.</summary>
    public event Action? ReframeRequested;

    /// <summary>Fired when a property that requires re-running the load
    /// pipeline (NIF parse + texture re-resolve + mesh upload) changes -
    /// currently <see cref="RenderMissingTextureAsWireframe"/>, since it is
    /// consumed during ApplyMaterial at upload time and can't take effect
    /// on already-loaded meshes. Hosts subscribe and call their own
    /// reload-current-NPC routine. Decoupled from the actual reload
    /// mechanism (which depends on the host's identity / scope state) so
    /// the lib doesn't have to know how a host loads NPCs.</summary>
    public event Action? ReloadRequested;

    /// <summary>True when meshes are uploaded and the viewer is ready for
    /// narrow-update operations (texture overrides, morph application,
    /// head-only rebuild). Equivalent to the gate the internal apply paths
    /// use before entering their work loops.</summary>
    public bool IsSceneReady => !_sceneRebuildPending && _meshesByBodyPart.Count > 0;

    /// <summary>NpcIdentity.CacheKey of the currently-loaded scene; empty
    /// before the first load completes. Hosts read this to short-circuit
    /// narrow updates that target the already-loaded NPC, or to gate
    /// fast-path rebuilds (e.g. head-only swap) on identity equality.</summary>
    public string CurrentLoadedIdentityKey => _currentLoadedIdentityKey;

    /// <summary>Resolved disk path of the currently-loaded body NIF, or null
    /// before the scene commits. Phase B2c-era helpers that load sibling
    /// files (BodySlide .tri, OSD catalogs) read this to know where to look.</summary>
    public string? CurrentBodyNifDiskPath => _cachedBodyNifDiskPath;

    /// <summary>Game-paths from the most recent <see cref="LoadAsync"/> whose
    /// asset-resolution returned no on-disk file (loose fallback, BSA, scoped
    /// chains all missed). Cleared at the start of each load and populated as
    /// each body part / skeleton path is attempted. A non-empty list after
    /// load completes means the rendered scene is missing one or more shapes
    /// the host expected to be there — hosts surface this as a UI hint
    /// (e.g. an "incomplete mugshot" tile overlay) and / or write it to a
    /// log. Skeleton, body, hands, feet, head, hair, and tail paths each
    /// contribute when their gamePath was non-empty but unresolvable.</summary>
    public IReadOnlyList<string> MissingMeshPaths => _missingMeshPaths;
    private readonly List<string> _missingMeshPaths = new();

    /// <summary>Texture game-paths from the most recent load that the host
    /// asked the texture manager to load but couldn't decode (resolver miss
    /// or DDS load failure). Each affected shape is rendered as wireframe
    /// in <see cref="GlRenderer.MissingTextureWireframeColor"/> instead of
    /// a flat-white billboard, and hosts surface this list as a tooltip
    /// alongside a "missing texture" tile overlay. Empty paths (a shape
    /// not using a particular slot) are NOT counted.</summary>
    public IReadOnlyCollection<string> MissingTexturePaths =>
        TextureManager?.MissingTexturePaths ?? (IReadOnlyCollection<string>)Array.Empty<string>();

    /// <summary>Controls how an alpha-tested / alpha-blended shape with no
    /// resolvable diffuse is handled during scene build. <c>true</c>
    /// (default): the shape is rendered as a wireframe placeholder in the
    /// renderer's <see cref="GlRenderer.MissingTextureWireframeColor"/> so
    /// the missing-texture state is visible. <c>false</c>: the shape is
    /// culled entirely (<c>GlMesh.IsRendering = false</c>) — the previous
    /// behavior, useful when wireframes cluttering the preview is more
    /// distracting than the silent omission.
    /// <para>Read by <see cref="ApplyMaterial"/>; hosts mutate it
    /// before <see cref="LoadAsync"/> / <see cref="LoadByIdentityAsync"/>
    /// so the next load picks it up. Existing meshes are not retroactively
    /// reclassified — toggle the setting then re-load.</para></summary>
    public bool RenderMissingTextureAsWireframe { get; set; } = true;

    /// <summary>Portrait-quality tone-mapping toggle (2.5.9+). When true,
    /// <see cref="GlRenderer"/> applies an ACES filmic tone-mapper plus
    /// sRGB framebuffer encoding plus a mild saturation boost at the end
    /// of the fragment shader. Hosts mutate before
    /// <see cref="LoadAsync"/> (or any subsequent render) — the value is
    /// read each frame, so toggling at runtime is effective on the next
    /// render without requiring a reload.</summary>
    public bool EnableToneMapping { get; set; } = false;

    /// <summary>Shadow-map toggle (2.5.10+). When true,
    /// <see cref="GlRenderer"/> runs an extra depth-only pass from the
    /// key light's POV and samples the resulting shadow map with PCF
    /// in <c>basic.frag</c>. Off: legacy occlusion-free lighting.</summary>
    public bool EnableShadows { get; set; } = false;

    /// <summary>Screen-space ambient occlusion toggle (2.5.11+). When
    /// true, <see cref="GlRenderer"/> runs a depth pre-pass + SSAO
    /// post-process before the main passes and samples the AO texture
    /// in <c>basic.frag</c> to darken crevices.</summary>
    public bool EnableAmbientOcclusion { get; set; } = false;

    /// <summary>SSAO radius in world units (2.5.12+).</summary>
    public float SsaoRadius { get; set; } = 4.0f;
    /// <summary>SSAO depth-comparison bias in world units (2.5.12+).</summary>
    public float SsaoBias { get; set; } = 0.05f;
    /// <summary>SSAO power-curve exponent (2.5.12+).</summary>
    public float SsaoIntensity { get; set; } = 1.5f;

    /// <summary>Eye catch-light toggle (2.5.13+).</summary>
    public bool EnableEyeCatchlight { get; set; } = false;

    /// <summary>Subsurface scattering strength multiplier (2.5.14+). 0
    /// disables SSS contribution from the corrected pipeline; 1.0 is
    /// honest source-value SSS; higher boosts the warm-flesh look.</summary>
    public float SubsurfaceStrength { get; set; } = 0f;

    /// <summary>Skin-only saturation multiplier applied post-tint,
    /// pre-lighting. 1.0 is no-op (default). &gt;1 boosts chroma, restoring
    /// race-distinguishing skin character that the downstream pipeline
    /// (ACES tonemap + lighting + post-process) tends to compress toward
    /// neutral — Orc green, Redguard brown, and Imperial warmth all
    /// recover at the same setting. Gated on IsSkinShape, so hair, eyes,
    /// and brows are excluded.</summary>
    public float SkinSaturationBoost { get; set; } = 1.0f;

    /// <summary>Vignette inner radius in NDC units (2.5.15+). The
    /// circular zone within this distance of screen center is
    /// unaffected; falloff smoothsteps from here out to the corner.
    /// Folded under <see cref="EnableToneMapping"/> in basic.frag.</summary>
    public float VignetteRadius { get; set; } = 0.7f;

    /// <summary>Vignette darkening strength (2.5.15+). 0 = off,
    /// 1.0 = corners to black.</summary>
    public float VignetteIntensity { get; set; } = 0f;

    // ── Skin-tint debug toggles ──────────────────────────────────────
    /// <summary>Debug: when true, the QNAM tint that body shapes receive
    /// is also applied to ShaderType==4 face shapes. See
    /// <see cref="GlRenderer.SkinTintApplyToFace"/> for context.</summary>
    public bool SkinTintApplyToFace { get; set; } = false;

    /// <summary>Debug operator selector for the QNAM tint blend.
    /// 0 multiply / 1 overlay / 2 linear-space multiply / 3 gamma-aware /
    /// 4 lerp(strength) / 5 lerp weighted by NIF skinTintAlpha /
    /// 6 Pegtop soft-light + body color-shift constant. Default 6:
    /// matches the engine's GetFacegenRGBTintBaseColor per Community
    /// Shaders' Lighting.hlsl replacement shader and reproduces the engine's
    /// face/body skin-tone composition across the test NPC set
    /// (vanilla Addvar, Nordic Faces, Aia, Angeline, Bjartur, UBE Lydia).</summary>
    public int SkinTintOperator { get; set; } = 6;

    /// <summary>Strength used by SkinTintOperator==4 (lerp).</summary>
    public float SkinTintLerpStrength { get; set; } = 0.5f;

    /// <summary>Debug override for vertex-color multiply.
    /// 0 = auto (production), 1 = force on (visually inert for shapes
    /// without VC data — those upload (1,1,1,1)), 2 = force off.</summary>
    public int VertexColorMultiplyMode { get; set; } = 0;

    /// <summary>FaceTint blend mode (runtime selectable).
    /// 0 = Auto (MSN-&gt;overlay, non-MSN-&gt;multiply), 1 = Always overlay,
    /// 2 = Always multiply, 3 = Always skip, 4 = Pegtop soft-light.
    /// Default 4 (Pegtop): the engine's actual FaceTint operator per
    /// Community Shaders' GetFacegenBaseColor source. Pair with
    /// <see cref="UseEngineStyleDetailMap"/> = true for engine-faithful
    /// face composition.</summary>
    public int FaceTintMode { get; set; } = 4;

    /// <summary>Experimental: force the FaceTint blend to multiply on
    /// face shapes whose NIF has <c>SLSF1_Facegen_Detail_Map</c> set but
    /// slot 3 of the texture set is empty. See
    /// <see cref="GlRenderer.FaceTintMultiplyOnEmptyDetail"/>.</summary>
    public bool FaceTintMultiplyOnEmptyDetail { get; set; } = false;

    /// <summary>Apply the slot-3 detail map the way the Skyrim engine
    /// actually does -- multiplicative post-FaceTint blend with a
    /// specific scale/offset transform. See
    /// <see cref="GlRenderer.UseEngineStyleDetailMap"/>. Default true:
    /// pairs with FaceTintMode=4 (Pegtop) and SkinTintOperator=6
    /// (Pegtop) to reproduce the engine's GetFacegenBaseColor pipeline.</summary>
    public bool UseEngineStyleDetailMap { get; set; } = true;

    /// <summary>Experimental: substitute
    /// <c>textures\actors\character\male\BlankDetailmap.dds</c> when slot 3
    /// is empty on a face shape. Triggers a scene reload when toggled.</summary>
    public bool UseBlankDetailFallback { get; set; } = false;

    /// <summary>Whether the head-only rebuild fast path is callable: scene
    /// committed, mesh paths cached, and the GL texture manager initialized.
    /// SynthEBD's ApplyHeadPartsAsync reads this to decide between full
    /// reload vs. <see cref="RebuildHeadOnlyAsync"/>.</summary>
    public bool CanRebuildHeadOnly =>
        IsSceneReady && _cachedMeshPaths != null && TextureManager != null;

    /// <summary>
    /// Priority-ordered loose-file search paths consulted BEFORE the host's
    /// <see cref="IDataFolderProvider.DataFolderPath"/> by this VM's
    /// <see cref="GameAssetResolver"/> calls. Mutate via assignment
    /// (full-list replacement) before calling <see cref="LoadByIdentityAsync"/>
    /// or <see cref="LoadAsync"/> when the host wants to scope the preview to
    /// a specific mod's folders. The list is captured on each <c>LoadAsync</c>
    /// entry so changes mid-load don't race the in-flight resolution; the
    /// resolver field is cleared after the scene commits (or on cancel/error)
    /// so the next load starts cleanly.
    ///
    /// <para>Last entry wins (MO2-style convention). Set to <c>null</c> or an
    /// empty list to use vanilla-only resolution.</para>
    /// </summary>
    public IReadOnlyList<string>? AdditionalDataFolders { get; set; }

    /// <summary>
    /// Strict two-phase asset-resolution chain. Counterpart to
    /// <see cref="Offscreen.OffscreenRenderRequest.AdditionalScopes"/> for the
    /// interactive viewer path. When non-null, OVERRIDES
    /// <see cref="AdditionalDataFolders"/> + the host's
    /// <see cref="IDataFolderProvider"/> /
    /// <see cref="IBsaArchiveProvider.TryLocateInBsa"/> broadcast — the
    /// resolver follows ONLY the scope chain (loose phase last-to-first,
    /// then scoped-BSA phase last-to-first), with no implicit vanilla
    /// fallback. Hosts that want vanilla as a fallback include it as the
    /// first scope.
    ///
    /// <para>Snapshotted by <see cref="LoadAsync"/> at entry and pushed to
    /// the resolver before the off-thread NIF parse + scene queue;
    /// <c>ProcessPendingScene</c> clears after <c>SceneCommitted</c>;
    /// cancel/error paths clear in <c>LoadAsync</c>'s finally guarded by
    /// <c>_loadCts == cts</c> so newer in-flight loads aren't clobbered.</para>
    /// </summary>
    public IReadOnlyList<RenderScope>? AdditionalScopes { get; set; }

    /// <summary>
    /// Counterpart to <see cref="Offscreen.OffscreenRenderRequest.VanillaLooseOverridesBsa"/>
    /// for the live preview path. When true (default), vanilla data folder
    /// loose files override BSA copies. Snapshotted at <see cref="LoadAsync"/>
    /// entry and pushed to the resolver alongside <see cref="AdditionalScopes"/>.
    /// </summary>
    public bool VanillaLooseOverridesBsa { get; set; } = true;

    /// <summary>
    /// Counterpart to <see cref="Offscreen.OffscreenRenderRequest.VanillaLooseOverridesModLoose"/>.
    /// When true, vanilla loose files preempt mod-folder loose files for
    /// non-FaceGen paths. Default false.
    /// </summary>
    public bool VanillaLooseOverridesModLoose { get; set; } = false;

    public VM_CharacterViewer(
        BodySlideDeformer bodySlideDeformer,
        BsdFileParser bsdFileParser,
        BodyTriFileParser bodyTriFileParser,
        GameAssetResolver assetResolver,
        ICharacterViewerSettings generalSettings,
        CharacterPreviewCache previewCache,
        CharacterViewerLogGate logGate,
        ICharacterViewerLogger logger,
        IRenderThreadMarshaller? renderThread = null)
    {
        _logGate = logGate;
        _previewCache = previewCache;
        // Mesh parser is shared via the preview cache so its parsed-NIF LRU
        // survives across viewer instances (the BodySlide menu disposes the
        // previous viewer on every preset switch).
        _meshBuilder = previewCache.MeshBuilder;
        _bodySlideDeformer = bodySlideDeformer;
        _bsdFileParser = bsdFileParser;
        _bodyTriFileParser = bodyTriFileParser;
        _assetResolver = assetResolver;
        _generalSettings = generalSettings;
        _logger = logger;
        _renderThread = renderThread ?? new InlineRenderThreadMarshaller();

        // Forward the renderer's one-shot GL-state dump (FRAMEBUFFER_SRGB,
        // viewport, color-attachment encoding, ...) to the same logger pipeline
        // the rest of the VM uses. The renderer itself has no logger reference,
        // so we wire its DiagnosticLog delegate to _logger here. NpcChooserViewerLoggerAdapter's
        // AsyncLocal sink routes the resulting line into the active capture file
        // (mugshot or live-preview), matching how Built shape / [Skinning] / etc.
        // already reach the same file from NifMeshBuilder and friends.
        Renderer.DiagnosticLog = msg => _logger.LogMessage(msg);

        // Verbose-log state lives in Settings_General as the single source of truth.
        // Every live VM_CharacterViewer instance reactively syncs its local VerboseLog
        // property (for the toolbar checkbox) and the shared _logGate (which helpers
        // BsdFileParser / GameAssetResolver / NpcMeshResolver / BodySlideDeformer /
        // NifMeshBuilder / CharacterPreviewCache consult) from the settings value.
        //
        // Previously each viewer read settings once at ctor and owned a stale local
        // copy. With multiple simultaneous viewer instances (main BodySlides menu +
        // editor's embedded viewer + per-preset VM_BodySlideSetting viewers), a toggle
        // on one viewer would correctly push false through to the gate, but any
        // subsequent construction-time push from a viewer whose local was still true
        // could silently overwrite the gate back to true. Settings is now the
        // authoritative fan-out — every viewer stays in lockstep with it.
        _generalSettings.WhenAnyValue(x => x.CharacterViewerVerboseLog)
            .Subscribe(v =>
            {
                if (VerboseLog != v) VerboseLog = v;
                if (_logGate != null && _logGate.Verbose != v) _logGate.Verbose = v;
            })
            .DisposeWith(_disposables);

        // Feed local-property changes (toolbar checkbox toggles) back to settings. The
        // settings-side subscription above then fans the new value to every other viewer.
        this.WhenAnyValue(x => x.VerboseLog).Skip(1).Subscribe(v =>
        {
            if (_generalSettings.CharacterViewerVerboseLog != v)
                _generalSettings.CharacterViewerVerboseLog = v;
        }).DisposeWith(_disposables);

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
            .DisposeWith(_disposables);

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

    /// <summary>Vertical field of view in degrees, source-of-truth for the
    /// camera's perspective projection. Setter pushes through to
    /// <see cref="OrbitCamera.FieldOfView"/> and raises
    /// <see cref="ReframeRequested"/> so mesh-aware hosts can recompute
    /// distance and keep the character the same on-screen size (matching
    /// NPC Portrait Creator's slider behavior). 25° is the default; the
    /// 10–90° range covers everything from ultra-tight portrait flatness
    /// to wide gameplay-style perspective.</summary>
    public double FieldOfView { get; set; } = 25.0;

    private const double FieldOfViewMin = 10.0;
    private const double FieldOfViewMax = 90.0;

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

    public RelayCommand CopyPicksToClipboardCommand        { get; private set; } = null!;
    public RelayCommand ConfirmPendingBoxCommand           { get; private set; } = null!;
    public RelayCommand ConfirmPendingBoxAsDuplicateCommand{ get; private set; } = null!;
    public RelayCommand CancelPendingBoxCommand            { get; private set; } = null!;
    public RelayCommand ShrinkAlongViewAxisCommand         { get; private set; } = null!;

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

    /// <summary>Public verbose-gated log used by <see cref="UC_CharacterViewer"/> for
    /// view-lifecycle diagnostics (instance-ids, Loaded/Unloaded, first-render details).
    /// Kept on the VM so multiple UC instances sharing this VM all route through the
    /// same <see cref="VerboseLog"/> toggle. Does nothing when <see cref="VerboseLog"/>
    /// is off, so the messages are free for end users.</summary>
    public void LogViewerDiagnostic(string message)
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
        }).DisposeWith(_disposables);

        this.WhenAnyValue(x => x.SelectedLightingColorScheme).Skip(1).Subscribe(scheme =>
        {
            if (scheme == null) return;
            _generalSettings.CharacterViewerLightingColorScheme = scheme.Name;
            KeyLightColor = MediaFromVec3(scheme.KeyColor);
            FillLightColor = MediaFromVec3(scheme.FillColor);
            RimLightColor = MediaFromVec3(scheme.RimColor);
            PushAllLightsToRenderer();
        }).DisposeWith(_disposables);

        // Any per-light edit (or enable toggle) re-pushes to the renderer.
        // Skip(1) suppresses the initial value emission so we don't push during
        // construction when the renderer isn't yet initialized. Split per-light
        // because ReactiveUI's WhenAnyValue overloads cap out at a modest arity.
        this.WhenAnyValue(x => x.AmbientIntensity)
            .Skip(1).Subscribe(_ => PushAllLightsToRenderer()).DisposeWith(_disposables);

        // FOV: clamp, push to OrbitCamera, then ask hosts to re-apply their
        // framing. Skip(1) so the initial value emission doesn't fire a reframe
        // before the host has wired its handler / loaded a scene.
        this.WhenAnyValue(x => x.FieldOfView)
            .Skip(1)
            .Subscribe(v =>
            {
                var clamped = Math.Clamp(v, FieldOfViewMin, FieldOfViewMax);
                Camera.FieldOfView = (float)clamped;
                ReframeRequested?.Invoke();
            })
            .DisposeWith(_disposables);

        // Tone-mapping toggle mirrors directly to the renderer; GlRenderer
        // reads its own field on each Render() so toggling at runtime is
        // effective on the next frame.
        this.WhenAnyValue(x => x.EnableToneMapping)
            .Subscribe(v => Renderer.EnableToneMapping = v)
            .DisposeWith(_disposables);
        this.WhenAnyValue(x => x.EnableShadows)
            .Subscribe(v => Renderer.EnableShadows = v)
            .DisposeWith(_disposables);
        this.WhenAnyValue(x => x.EnableAmbientOcclusion)
            .Subscribe(v => Renderer.EnableAmbientOcclusion = v)
            .DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SsaoRadius)
            .Subscribe(v => Renderer.SsaoRadius = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SsaoBias)
            .Subscribe(v => Renderer.SsaoBias = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SsaoIntensity)
            .Subscribe(v => Renderer.SsaoIntensity = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.EnableEyeCatchlight)
            .Subscribe(v => Renderer.EnableEyeCatchlight = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SubsurfaceStrength)
            .Subscribe(v => Renderer.SubsurfaceStrength = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SkinSaturationBoost)
            .Subscribe(v => Renderer.SkinSaturationBoost = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.VignetteRadius)
            .Subscribe(v => Renderer.VignetteRadius = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.VignetteIntensity)
            .Subscribe(v => Renderer.VignetteIntensity = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SkinTintApplyToFace)
            .Subscribe(v => Renderer.SkinTintApplyToFace = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SkinTintOperator)
            .Subscribe(v => Renderer.SkinTintOperator = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SkinTintLerpStrength)
            .Subscribe(v => Renderer.SkinTintLerpStrength = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.VertexColorMultiplyMode)
            .Subscribe(v => Renderer.VertexColorMultiplyMode = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.FaceTintMode)
            .Subscribe(v => Renderer.FaceTintMode = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.FaceTintMultiplyOnEmptyDetail)
            .Subscribe(v => Renderer.FaceTintMultiplyOnEmptyDetail = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.UseEngineStyleDetailMap)
            .Subscribe(v => Renderer.UseEngineStyleDetailMap = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.UseBlankDetailFallback)
            .Subscribe(v => Renderer.UseBlankDetailFallback = v).DisposeWith(_disposables);
        // Texture substitution is decided at scene-build time, so toggling
        // requires a reload. Skip(1) so the initial value emission doesn't
        // fire a reload before any host has wired the handler.
        this.WhenAnyValue(x => x.UseBlankDetailFallback)
            .Skip(1)
            .Subscribe(_ => ReloadRequested?.Invoke())
            .DisposeWith(_disposables);

        // RenderMissingTextureAsWireframe is consumed during ApplyMaterial
        // (mesh-upload time), so toggling it at runtime needs the host to
        // re-load the current NPC for the change to take effect on
        // already-loaded shapes. Skip(1) so the initial value emission
        // doesn't fire a reload before any host has wired the handler.
        this.WhenAnyValue(x => x.RenderMissingTextureAsWireframe)
            .Skip(1)
            .Subscribe(_ => ReloadRequested?.Invoke())
            .DisposeWith(_disposables);

        // BackgroundColor (System.Windows.Media.Color, 0..255 channels) ->
        // Renderer.ClearColor (Vector3, 0..1 floats). Without this wire the
        // renderer keeps its default DimGray ClearColor for the whole session
        // regardless of host edits, and even hosts that set GL.ClearColor on
        // their FBO before calling Render() see it stomped by the Render()
        // body's own ClearColor reset. Eager subscription (no Skip) so the
        // initial value lands on the renderer at construction.
        this.WhenAnyValue(x => x.BackgroundColor)
            .Subscribe(c => Renderer.ClearColor = new OpenTK.Mathematics.Vector3(
                c.R / 255f, c.G / 255f, c.B / 255f))
            .DisposeWith(_disposables);

        this.WhenAnyValue(
            x => x.KeyLightIntensity, x => x.KeyLightAzimuth, x => x.KeyLightElevation,
            x => x.KeyLightColor, x => x.KeyLightEnabled)
            .Skip(1).Subscribe(_ => PushAllLightsToRenderer()).DisposeWith(_disposables);

        this.WhenAnyValue(
            x => x.FillLightIntensity, x => x.FillLightAzimuth, x => x.FillLightElevation,
            x => x.FillLightColor, x => x.FillLightEnabled)
            .Skip(1).Subscribe(_ => PushAllLightsToRenderer()).DisposeWith(_disposables);

        this.WhenAnyValue(
            x => x.RimLightIntensity, x => x.RimLightAzimuth, x => x.RimLightElevation,
            x => x.RimLightColor, x => x.RimLightEnabled)
            .Skip(1).Subscribe(_ => PushAllLightsToRenderer()).DisposeWith(_disposables);

        this.WhenAnyValue(x => x.SelectedLightIndex)
            .Skip(1).Subscribe(_ => PushAllLightsToRenderer()).DisposeWith(_disposables);

        this.WhenAnyValue(x => x.ShowLightControls).Subscribe(v =>
        {
            Renderer.ShowKeyLightVisualization = v;
            if (!v) SelectedLightIndex = 0;
        }).DisposeWith(_disposables);

        this.WhenAnyValue(x => x.ShowWireframe).Subscribe(v =>
        {
            foreach (var mesh in Renderer.Meshes)
                mesh.ShowWireframe = v;
        }).DisposeWith(_disposables);

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
        ConfirmPendingBoxAsDuplicateCommand = new RelayCommand(
            canExecute: _ => HasPendingBox,
            execute: _ => ConfirmPendingBoxAsDuplicate());
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
                System.Windows.MessageBox.Show(
                    $"'{name}' is a built-in preset name. Please choose a different name.",
                    "Name in use",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }
        }

        var existing = _generalSettings.UserLightingLayouts.FirstOrDefault(l =>
            string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null &&
            System.Windows.MessageBox.Show(
                $"A user preset named '{name}' already exists. Overwrite it?",
                "Overwrite preset?",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
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
                System.Windows.MessageBox.Show(
                    $"'{name}' is a built-in preset name. Please choose a different name.",
                    "Name in use",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }
        }

        var existing = _generalSettings.UserLightingColorSchemes.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null &&
            System.Windows.MessageBox.Show(
                $"A user color scheme named '{name}' already exists. Overwrite it?",
                "Overwrite preset?",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
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
        if (System.Windows.MessageBox.Show(
                $"Delete the user lighting layout '{sel.Name}'?",
                "Delete preset?",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes) return;

        _generalSettings.UserLightingLayouts.Remove(sel);
        RebuildLayoutList();
        SelectedLightingLayout = CharacterViewerLightingPresets.DefaultLayout;
    }

    private void DeleteSelectedColorScheme()
    {
        var sel = SelectedLightingColorScheme;
        if (sel == null || sel.IsBuiltIn) return;
        if (System.Windows.MessageBox.Show(
                $"Delete the user color scheme '{sel.Name}'?",
                "Delete preset?",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes) return;

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
        // Save/delete-preset commands route here from the WPF toolbar buttons,
        // so they always run with Application.Current set. Offscreen renderers
        // never trigger this path; the null guard makes that explicit and
        // keeps the method offscreen-safe in case a future host wires the
        // commands somewhere unusual.
        if (System.Windows.Application.Current?.Dispatcher == null) return null;

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
            $"Key={KeyLightIntensity:F0}%@({KeyLightAzimuth:F0}°,{KeyLightElevation:F0}°)En={KeyLightEnabled}, " +
            $"Fill={FillLightIntensity:F0}%@({FillLightAzimuth:F0}°,{FillLightElevation:F0}°)En={FillLightEnabled}, " +
            $"Rim={RimLightIntensity:F0}%@({RimLightAzimuth:F0}°,{RimLightElevation:F0}°)En={RimLightEnabled}");
        LogVerbose($"CharacterViewer: LIGHT-COLORS — " +
            $"Key=({KeyLightColor.R},{KeyLightColor.G},{KeyLightColor.B}), " +
            $"Fill=({FillLightColor.R},{FillLightColor.G},{FillLightColor.B}), " +
            $"Rim=({RimLightColor.R},{RimLightColor.G},{RimLightColor.B})");
    }

    /// <summary>One-shot render-state snapshot for diagnostic capture. Emits a
    /// compact multi-line dump of host-visible state that participates in
    /// final pixel brightness: lighting (layout + per-light intensity/angle/
    /// enable/color), the QNAM tint operator + face tint mode + detail-map
    /// mode + vertex-color mode (all uniforms that route through basic.frag's
    /// diffuse-composition chain), the post-shading render-quality toggles,
    /// camera framing, and background color. Useful for side-by-side
    /// comparison of two render pathways' state at a single point in time.
    /// Note that this captures host VM state only; per-fragment effects (PNG
    /// alpha-channel write-through, MSAA-resolve behavior, driver-side
    /// gamma handling) won't show up here and need separate instrumentation
    /// (see <see cref="GlRenderer.EmitGlStateDiagnostic"/> for the GL-side
    /// view, and the per-render mugshot capture file for downstream encoding).
    /// <para>Emits via <see cref="_logger"/> directly (NOT through
    /// <see cref="LogVerbose"/>) so it's not gated by the VM's
    /// <see cref="VerboseLog"/> property, which defaults false and is
    /// independent of NPC2's per-capture <c>LogRenderLogic</c> toggle.
    /// The logger adapter's thread-local sink routes the lines to the active
    /// capture file when a capture session is open; outside a capture they
    /// land in Debug output only. Cost is six lines per scene load, which
    /// is negligible.</para></summary>
    public void LogRenderStateSnapshot()
    {
        if (_logger == null) return;
        _logger.LogMessage($"CharacterViewer: LIGHTING — Layout='{SelectedLightingLayout?.Name}', " +
            $"Colors='{SelectedLightingColorScheme?.Name}', Ambient={AmbientIntensity:F0}%, " +
            $"Key={KeyLightIntensity:F0}%@({KeyLightAzimuth:F0}°,{KeyLightElevation:F0}°)En={KeyLightEnabled}, " +
            $"Fill={FillLightIntensity:F0}%@({FillLightAzimuth:F0}°,{FillLightElevation:F0}°)En={FillLightEnabled}, " +
            $"Rim={RimLightIntensity:F0}%@({RimLightAzimuth:F0}°,{RimLightElevation:F0}°)En={RimLightEnabled}");
        _logger.LogMessage($"CharacterViewer: LIGHT-COLORS — " +
            $"Key=({KeyLightColor.R},{KeyLightColor.G},{KeyLightColor.B}), " +
            $"Fill=({FillLightColor.R},{FillLightColor.G},{FillLightColor.B}), " +
            $"Rim=({RimLightColor.R},{RimLightColor.G},{RimLightColor.B})");
        _logger.LogMessage($"CharacterViewer: TINT-UNIFORMS — " +
            $"SkinTintApplyToFace={SkinTintApplyToFace}, " +
            $"SkinTintOperator={SkinTintOperator}, " +
            $"SkinTintLerpStrength={SkinTintLerpStrength:F3}, " +
            $"FaceTintMode={FaceTintMode}, " +
            $"FaceTintMultiplyOnEmptyDetail={FaceTintMultiplyOnEmptyDetail}, " +
            $"UseEngineStyleDetailMap={UseEngineStyleDetailMap}, " +
            $"UseBlankDetailFallback={UseBlankDetailFallback}, " +
            $"VertexColorMultiplyMode={VertexColorMultiplyMode}");
        _logger.LogMessage($"CharacterViewer: RENDER-QUALITY — " +
            $"ToneMapping={EnableToneMapping}, " +
            $"Shadows={EnableShadows}, " +
            $"AO={EnableAmbientOcclusion} (R={SsaoRadius:F2} B={SsaoBias:F3} I={SsaoIntensity:F2}), " +
            $"EyeCatchlight={EnableEyeCatchlight}, " +
            $"SSS={SubsurfaceStrength:F3}, " +
            $"SkinSat={SkinSaturationBoost:F3}, " +
            $"Vignette(R={VignetteRadius:F2} I={VignetteIntensity:F2})");
        _logger.LogMessage($"CharacterViewer: CAMERA — FOV={FieldOfView:F1}°, " +
            $"Distance={Camera.Distance:F2}, " +
            $"Az={Camera.Azimuth:F1}°, El={Camera.Elevation:F1}°, " +
            $"Target=({Camera.Target.X:F2},{Camera.Target.Y:F2},{Camera.Target.Z:F2})");
        _logger.LogMessage($"CharacterViewer: BACKGROUND — RGB=({BackgroundColor.R},{BackgroundColor.G},{BackgroundColor.B})");
    }

    /// <summary>Per-mesh GlMesh state at scene-install time. The on-NIF
    /// shader properties (specularColor, glossiness, alpha flags, ...) were
    /// already verified to match between offscreen and live-preview pathways
    /// via the dense verbose dumps, but the host-side GlMesh fields that
    /// drive the actual per-shape shader uniforms (HasTintColor / TintColor /
    /// IsFaceShape / IsSkinShape / ...) are set during InstallOneShape and
    /// only become observable after the scene-install queue drains in
    /// ProcessPendingScene. Calling this immediately after
    /// <see cref="SceneCommitted"/> guarantees the meshes are populated.
    /// Emits via <see cref="_logger.LogMessage"/> directly so the AsyncLocal
    /// sink routes the lines to the active capture file.
    /// <para>Only the offscreen mugshot pathway reliably catches these
    /// lines, because the live-preview host scopes its
    /// <c>RenderLogCapture</c> to the span of <c>await Viewer.LoadAsync(...)</c>
    /// and the render tick that drains the scene fires after LoadAsync
    /// returns. The mugshot's offscreen pathway holds its capture open
    /// across the whole render request and drains the scene synchronously
    /// inside that window, so it captures both.</para></summary>
    public void LogMeshStateSnapshot()
    {
        if (_logger == null) return;
        try
        {
            for (int i = 0; i < Renderer.Meshes.Count; i++)
            {
                var m = Renderer.Meshes[i];
                _logger.LogMessage($"CharacterViewer: GLMESH[{i}] '{m.ShapeName}' BodyPart={m.BodyPart} " +
                    $"IsFaceShape={m.IsFaceShape}, IsSkinShape={m.IsSkinShape}, " +
                    $"IsHairTintShader={m.IsHairTintShader}, IsEye={m.IsEye}, " +
                    $"HasTintColor={m.HasTintColor}, " +
                    $"TintColor=({m.TintColor.X:F3},{m.TintColor.Y:F3},{m.TintColor.Z:F3}), " +
                    $"TintColorEnabled={m.TintColorEnabled}, " +
                    $"FaceTintEnabled={m.FaceTintEnabled}, " +
                    $"DiffuseEnabled={m.DiffuseEnabled}, " +
                    $"NormalEnabled={m.NormalEnabled}, " +
                    $"SpecularEnabled={m.SpecularEnabled}, " +
                    $"DetailEnabled={m.DetailEnabled}, " +
                    $"EmissiveEnabled={m.EmissiveEnabled}, " +
                    $"EnvMapEnabled={m.EnvMapEnabled}, " +
                    $"UseAlphaTest={m.UseAlphaTest}, AlphaThreshold={m.AlphaThreshold:F3}, " +
                    $"HasAlphaBlend={m.HasAlphaBlend}, " +
                    $"HasDetailMap={m.HasDetailMap}, IsFaceWithEmptyDetailSlot={m.IsFaceWithEmptyDetailSlot}, " +
                    $"HasFaceTintMap={m.HasFaceTintMap}, " +
                    $"HasGreyscaleToPalette={m.HasGreyscaleToPalette}, " +
                    $"SpecularColor=({m.SpecularColor.X:F3},{m.SpecularColor.Y:F3},{m.SpecularColor.Z:F3}), " +
                    $"MaterialGlossiness={m.MaterialGlossiness:F2}, " +
                    $"MaterialSpecularStrength={m.MaterialSpecularStrength:F3}, " +
                    $"RimlightPower={m.RimlightPower:F2}, " +
                    $"IsDoubleSided={m.IsDoubleSided}, " +
                    $"IsRendering={m.IsRendering}, " +
                    $"RenderAsWireframeFallback={m.RenderAsWireframeFallback}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogMessage($"CharacterViewer: GLMESH dump failed: {ex.Message}");
        }
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

    /// <summary>Toolbar toggle for the BodyTypeProfile editor's per-Y-bin paired-X debug
    /// overlay. The viewer doesn't act on this directly — the active profile subscribes via
    /// <c>VM_BodyTypeProfile.AttachViewer</c> and drives its own line-channel refresh. Lives
    /// on the viewer VM so the checkbox can bind to it from <c>UC_CharacterViewer.xaml</c>
    /// without reaching across to the editor VM. Gated by <see cref="ShowClassifierControls"/>
    /// in XAML so non-classifier viewer usages don't see the option.</summary>
    public bool ShowBulgeBinOverlay { get; set; } = false;

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
            BoxCriterionSelection criterion,
            bool isDuplicate = false)
        {
            ShapeName = shapeName ?? "";
            BoxMin = boxMin;
            BoxMax = boxMax;
            Criterion = criterion;
            IsDuplicate = isDuplicate;
        }
        public string ShapeName { get; }
        public OpenTK.Mathematics.Vector3 BoxMin { get; }
        public OpenTK.Mathematics.Vector3 BoxMax { get; }
        public BoxCriterionSelection Criterion { get; }
        /// <summary>True when the pick originated from "Confirm as Duplicate" — signals the
        /// consumer to always create a new row even if an edit session is active, so the box
        /// can be reused with a different criterion alongside the row being edited.</summary>
        public bool IsDuplicate { get; }
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
        // Fire the pick event before clearing HasPendingBox so subscribers watching the
        // flag (e.g., the profile editor's row-edit session) can distinguish a confirm —
        // pick event THEN flag flip — from a user-cancel, which flips the flag with no
        // accompanying event.
        NotifyKeyVertexBoxPicked(pick);
        HasPendingBox = false;
    }

    /// <summary>Same as <see cref="ConfirmPendingBox"/> but leaves the pending box on screen so
    /// the user can pick a different <see cref="PendingBoxFinalCriterion"/> and confirm again,
    /// re-using the exact same AABB. Lets the author capture multiple measurements (e.g. MaxX
    /// and BulgePairMinX) from one drawn box without re-drawing.</summary>
    public void ConfirmPendingBoxAsDuplicate()
    {
        if (!HasPendingBox) return;
        var pick = new KeyVertexBoxPick(
            PendingBoxShapeName,
            new OpenTK.Mathematics.Vector3(PendingBoxMinX, PendingBoxMinY, PendingBoxMinZ),
            new OpenTK.Mathematics.Vector3(PendingBoxMaxX, PendingBoxMaxY, PendingBoxMaxZ),
            PendingBoxFinalCriterion,
            isDuplicate: true);
        NotifyKeyVertexBoxPicked(pick);
        // Intentionally leave HasPendingBox = true so the user can change criterion and
        // duplicate again. The IsDuplicate flag tells downstream consumers to force the
        // AddBoxRow path without consuming any active edit-session target, so a subsequent
        // regular Confirm can still update the originally-edited row.
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
    public sealed class PickRow : ViewerVm
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

    /// <summary>Read-only view of the picks currently highlighted in the pick-info ListBox.
    /// Snapshotted each <see cref="SetSelectedPicks"/> call. Consumed by external tools that
    /// want to act on the user's current marker selection (e.g. the BodyTypeProfile editor's
    /// "Capture Selected Picks" button, which imports them as KeyVertex rows).</summary>
    public IReadOnlyList<PickRow> SelectedPicks => _selectedPicks;

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

    /// <summary>Replaces the single preview marker (see
    /// <see cref="GlRenderer.PreviewKeyVertexMarkers"/>) with the resolved position of
    /// <paramref name="shapeName"/>[<paramref name="vertexIndex"/>]. Pass null/empty shape or
    /// negative index to clear. Used by the BodyTypeProfile editor to render a transient
    /// "you-are-here" sphere when the user selects a KeyVertex row, without populating the
    /// picks ListBox. Silently no-ops when the (shape, index) can't be resolved on the
    /// current mesh state.</summary>
    public void SetPreviewKeyVertex(string? shapeName, int vertexIndex)
    {
        Renderer.PreviewKeyVertexMarkers.Clear();
        if (string.IsNullOrEmpty(shapeName) || vertexIndex < 0) return;
        if (TryGetCurrentVertex(shapeName, vertexIndex, out var pos))
        {
            Renderer.PreviewKeyVertexMarkers.Add(pos);
        }
    }

    /// <summary>Replaces the pending-box pick preview markers (see
    /// <see cref="GlRenderer.PreviewPickMarkers"/>) with the supplied world-space positions.
    /// Pass null or an empty enumerable to clear. Driven by the BodyTypeProfile editor while
    /// a pending box is being authored — it re-runs FindBestInBox on every box-coord /
    /// criterion change and pushes the resolved vertex (or pair) here so the user sees
    /// exactly what Confirm would commit.</summary>
    public void SetPreviewPickMarkers(IEnumerable<OpenTK.Mathematics.Vector3>? positions)
    {
        Renderer.PreviewPickMarkers.Clear();
        if (positions == null) return;
        foreach (var p in positions) Renderer.PreviewPickMarkers.Add(p);
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

    /// <summary>Companion to <see cref="GetShapePositions"/> that surfaces a shape's triangle
    /// index buffer (flat triplets [i0,i1,i2, i3,i4,i5, ...], local to the shape's own
    /// <see cref="GetShapePositions"/> array), or null when the shape isn't loaded or has no
    /// CPU-side geometry. Used by <c>RegionVolumeEvaluator</c> to walk the shape's triangles for
    /// region-volume measurements (clip to box, extract boundary loops, integrate). The array is
    /// not cloned — callers must treat it as read-only, matching <see cref="GetShapeBoneInfo"/>.</summary>
    public int[]? GetShapeIndices(string shapeName)
    {
        if (string.IsNullOrEmpty(shapeName)) return null;
        var mesh = Renderer.Meshes.FirstOrDefault(m =>
            string.Equals(m.ShapeName, shapeName, StringComparison.OrdinalIgnoreCase));
        if (mesh?.CpuIndices == null || mesh.CpuIndices.Length == 0) return null;
        return mesh.CpuIndices;
    }

    /// <summary>Companion to <see cref="GetShapePositions"/> that surfaces the per-vertex
    /// bone indices + weights (4 entries each per vertex, flat-packed) for the bone-transition
    /// criterion in <c>MeasurementMath.FindBestInBox</c>. Returns <c>(null, null)</c> when the
    /// shape isn't loaded, has no CPU-side geometry, or wasn't skinned (e.g. static accessories).
    /// The arrays are not cloned — callers must treat them as read-only.</summary>
    public (int[]? BoneIndices, float[]? BoneWeights) GetShapeBoneInfo(string shapeName)
    {
        if (string.IsNullOrEmpty(shapeName)) return (null, null);
        var mesh = Renderer.Meshes.FirstOrDefault(m =>
            string.Equals(m.ShapeName, shapeName, StringComparison.OrdinalIgnoreCase));
        if (mesh?.CpuBoneIndices == null || mesh.CpuBoneWeights == null) return (null, null);
        if (mesh.CpuBoneIndices.Length == 0 || mesh.CpuBoneWeights.Length == 0) return (null, null);
        return (mesh.CpuBoneIndices, mesh.CpuBoneWeights);
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
        if (added == 0 && skipped > 0)
        {
            var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (shapeName, _) in entries)
            {
                if (!string.IsNullOrEmpty(shapeName)) requested.Add(shapeName);
            }
            var available = GetCurrentShapeVertexCounts();
            LogVerbose("CharacterViewer: ShowKeyVerticesInViewer requested shapes ["
                + string.Join(", ", requested)
                + "]; viewer currently has [" + string.Join(", ",
                    available.Select(kv => kv.Key + ":" + kv.Value)) + "].");
        }
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
    /// <summary>
    /// Single canonical entry point. Each tuple's <c>Label</c> populates
    /// <see cref="GlRenderer.MeasurementLineSegment.Label"/> for the hover-tooltip path;
    /// pass null/empty when a segment shouldn't surface a tooltip (e.g. the bulge-bin
    /// debug overlay where individual lines don't map to a named measurement). The
    /// renderer ignores Label; it's purely transit data for
    /// <see cref="HitTestMeasurementLine"/>.
    /// <para>Callers that don't need labels pass null in the fourth tuple slot — the
    /// previous unlabeled overload was removed because <c>null</c> arguments couldn't
    /// disambiguate between the two signatures (CS0121).</para>
    /// </summary>
    public void SetMeasurementLines(IEnumerable<(OpenTK.Mathematics.Vector3 A, OpenTK.Mathematics.Vector3 B, OpenTK.Mathematics.Vector3 Color, string? Label)> segments)
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
                Label = s.Label,
            });
        }
    }

    /// <summary>
    /// Returns the label of the closest labeled measurement-line segment within
    /// <paramref name="thresholdPixels"/> screen-space distance of the cursor, or null when
    /// no labeled segment is close enough. Unlabeled segments are skipped — they still
    /// render through <see cref="GlRenderer.MeasurementLines"/> but don't surface a
    /// tooltip. Used by <c>UC_CharacterViewer.HoverTimer_Tick</c> as a labeled-overlay
    /// hit-test that takes priority over the mesh hover-tooltip because the lines render
    /// on top of the body with depth test disabled (so they're visually in front
    /// regardless of true depth).
    /// <para>Math: both segment endpoints are scaled by <see cref="GlRenderer.ModelScale"/>
    /// (matching <c>DrawMeasurementLines</c>) and projected through <c>view * projection</c>
    /// to NDC, then to WPF logical pixels. The 2D point-to-segment distance from the cursor
    /// to the projected line picks the closest within the threshold; ties (two segments at
    /// the same pixel distance) are broken by camera-space depth, preferring the segment
    /// closer to the camera so the visually-front line wins.</para>
    /// </summary>
    public string? HitTestMeasurementLine(
        float mouseX, float mouseY,
        float viewportWidth, float viewportHeight,
        float thresholdPixels)
    {
        if (Renderer.MeasurementLines.Count == 0) return null;
        if (viewportWidth <= 0f || viewportHeight <= 0f) return null;

        float aspect = viewportWidth / viewportHeight;
        var vp = Camera.GetViewMatrix() * Camera.GetProjectionMatrix(aspect);
        float modelScale = Renderer.ModelScale;
        float thresholdSq = thresholdPixels * thresholdPixels;

        string? best = null;
        float bestDistSq = float.MaxValue;
        float bestDepth = float.MaxValue;
        var mouse = new OpenTK.Mathematics.Vector2(mouseX, mouseY);

        for (int i = 0; i < Renderer.MeasurementLines.Count; i++)
        {
            var seg = Renderer.MeasurementLines[i];
            if (string.IsNullOrEmpty(seg.Label)) continue;

            if (!TryProjectToPixel(seg.A * modelScale, vp, viewportWidth, viewportHeight, out var pa, out float depthA))
                continue;
            if (!TryProjectToPixel(seg.B * modelScale, vp, viewportWidth, viewportHeight, out var pb, out float depthB))
                continue;

            float distSq = PixelDistanceToSegmentSquared(mouse, pa, pb);
            if (distSq > thresholdSq) continue;

            // Prefer the closer segment when two land within the threshold at the same
            // pixel distance — matches the "topmost visible line" intuition since the
            // overlay disables depth test and stacks in draw order.
            float depth = Math.Min(depthA, depthB);
            if (distSq < bestDistSq || (Math.Abs(distSq - bestDistSq) < 1e-3f && depth < bestDepth))
            {
                best = seg.Label;
                bestDistSq = distSq;
                bestDepth = depth;
            }
        }
        return best;
    }

    /// <summary>Projects a world-space point to WPF-logical pixel coordinates via
    /// <paramref name="vp"/> (view * projection). Returns false when the point is behind
    /// the camera (w &lt;= 0) or clipped on Z (outside the [-1, 1] NDC depth range),
    /// matching the GL clip behavior so out-of-frustum endpoints don't generate false
    /// hover hits at wrapped screen positions.</summary>
    private static bool TryProjectToPixel(
        OpenTK.Mathematics.Vector3 world,
        OpenTK.Mathematics.Matrix4 vp,
        float viewportWidth, float viewportHeight,
        out OpenTK.Mathematics.Vector2 pixel,
        out float ndcZ)
    {
        var clip = new OpenTK.Mathematics.Vector4(world, 1f) * vp;
        if (clip.W <= 1e-6f) { pixel = default; ndcZ = 0f; return false; }
        var ndc = clip.Xyz / clip.W;
        if (ndc.Z < -1f || ndc.Z > 1f) { pixel = default; ndcZ = ndc.Z; return false; }
        float px = (ndc.X * 0.5f + 0.5f) * viewportWidth;
        float py = (1f - (ndc.Y * 0.5f + 0.5f)) * viewportHeight;
        pixel = new OpenTK.Mathematics.Vector2(px, py);
        ndcZ = ndc.Z;
        return true;
    }

    /// <summary>2D squared distance from <paramref name="p"/> to the segment
    /// (<paramref name="a"/>, <paramref name="b"/>). Caller compares against
    /// thresholdPixels² to avoid a sqrt per segment in the hover hit-test.</summary>
    private static float PixelDistanceToSegmentSquared(
        OpenTK.Mathematics.Vector2 p,
        OpenTK.Mathematics.Vector2 a,
        OpenTK.Mathematics.Vector2 b)
    {
        var ab = b - a;
        float len2 = ab.LengthSquared;
        if (len2 < 1e-6f) return (p - a).LengthSquared;
        float t = OpenTK.Mathematics.Vector2.Dot(p - a, ab) / len2;
        t = Math.Clamp(t, 0f, 1f);
        var closest = a + ab * t;
        return (p - closest).LengthSquared;
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
    /// Drops all GL-bound state when the owning UC_CharacterViewer is recreated during
    /// WPF navigation. GLWpfControl 4.x creates a new GL context per control instance,
    /// so shader programs, VAOs, VBOs, and texture IDs minted by a previous UC's context
    /// are invalid in the new one — leaving them in place produces 10 "rendered" meshes
    /// and zero visible pixels (grey screen). Must not issue any GL calls: the old
    /// context is already destroyed, and the new one isn't necessarily current on this
    /// thread when Unloaded fires. Forces the next <see cref="LoadAsync"/> to rebuild
    /// by clearing <see cref="_currentLoadedIdentityKey"/> and setting
    /// <see cref="_sceneRebuildPending"/> so the same-NPC short-circuit skips.
    /// </summary>
    public void HandleGlContextLoss()
    {
        LogVerbose("CharacterViewer: HandleGlContextLoss — dropping GL state for new context");

        Renderer.ForgetResourcesFromDeadContext();
        TextureManager?.ForgetResourcesFromDeadContext();
        TextureManager = null;

        _meshesByBodyPart.Clear();
        _builtMeshesByBodyPart.Clear();
        _cachedBodyMeshes.Clear();
        _textureApplyInfoByMesh.Clear();
        _cachedOsdFiles = null;
        _cachedBodyNifDiskPath = null;
        _cachedBodyTri = null;
        _cachedMeshPaths = null;

        // Any pending buffers were captured against the dead context — drop them so
        // ProcessPendingScene doesn't try to upload stale BuiltMesh data as if it were
        // fresh. LoadNpcAsync will re-populate on the next preview request. A partial
        // sliced install also dies with the dead context — its in-flight queue and the
        // already-uploaded GL handles are equally invalid.
        _pendingScene = null;
        _sceneInstall = null;
        _pendingTextureOverrides = null;
        _pendingMorphSet = null;
        _pendingHeadReplace = null;

        _currentLoadedIdentityKey = "";
        _currentHeadMeshOverride = null;
        _sceneRebuildPending = true;

        IsGlInitialized = false;

        // Host VMs (editor, annotator, etc.) subscribe to re-issue their last preview
        // request so the user sees their previously-loaded character without having to
        // re-click a preset. Raised after state is reset so handlers see a clean VM.
        GlContextReset?.Invoke();
    }

    /// <summary>Fires after <see cref="HandleGlContextLoss"/> finishes resetting VM state.
    /// Host VMs subscribe to re-trigger their last preview (e.g.
    /// <see cref="VM_BodyTypeProfileEditor.RefreshPreviewAsync"/>) so the viewer isn't
    /// grey until the user re-clicks a preset. Called on the UI thread from the GL
    /// render callback, so handlers can safely touch WPF-bound properties.</summary>
    public event Action? GlContextReset;

    /// <summary>True while a load is queued or actively installing — covers
    /// all three pending-work fields. Hosts that drive the scene install
    /// from outside a per-frame render callback (notably the offscreen
    /// renderer) loop on this until the scene is fully committed.</summary>
    public bool HasPendingSceneWork =>
        _pendingScene != null || _sceneInstall != null || _pendingHeadReplace != null;

    /// <summary>Drains <see cref="ProcessPendingScene"/> repeatedly until the
    /// scene is fully installed or <paramref name="maxIterations"/> is
    /// exhausted. Used by the offscreen renderer where there's no per-frame
    /// callback budget — we want the entire scene installed before reading
    /// the framebuffer back.
    ///
    /// In WPF interactive use this would be wrong (it'd block the UI thread
    /// for the full install span), which is why this is a separate entry
    /// point rather than the default behavior.</summary>
    public void ProcessPendingSceneToCompletion(int maxIterations = 200)
    {
        for (int i = 0; i < maxIterations && HasPendingSceneWork; i++)
        {
            ProcessPendingScene();
        }
    }

    /// <summary>
    /// Called from the GL render callback to process any pending scene setup.
    /// All GL calls (mesh upload, texture loading) happen here where the
    /// GL context is guaranteed to be current.
    /// </summary>
    public void ProcessPendingScene()
    {
        // Re-push the per-load resolution snapshot onto the resolver for the
        // duration of this tick's install work. The render callback that
        // invoked us is on a different ExecutionContext than LoadAsync, so
        // resolver AsyncLocal values set inside LoadAsync are NOT visible
        // here — this push is what makes texture/mesh resolves during
        // InstallOneShape see the right scope chain. Pushing nulls when no
        // load is pending is a harmless no-op (PushScopes writes null to
        // the AsyncLocals, then the using restores the prior null).
        using var __scopes = _assetResolver.PushScopes(
            _currentSceneScopes, _currentSceneFolders,
            _currentSceneVanillaLooseOverridesBsa,
            _currentSceneVanillaLooseOverridesModLoose);

        // Head-only rebuild (P2) is independent of full-scene setup and runs
        // without touching Body/Hands/Feet. Drain it here so the render callback
        // owns all GL-side scene mutations.
        if (_pendingHeadReplace != null && IsGlInitialized)
        {
            InstallReplacedHead();
        }

        if (!IsGlInitialized) return;

        // ── 1. First tick of a new scene: drain _pendingScene into a per-shape
        //       install queue. If a previous install is still in flight, abandon
        //       it — ClearScene tears down the partially-uploaded GL meshes so
        //       the new scene starts from a clean renderer.
        if (_pendingScene is { } incoming)
        {
            _pendingScene = null;

            if (_sceneInstall != null)
            {
                LogLoadCheckpoint(_sceneInstall.LoadStopwatch,
                    "ProcessPendingScene: superseded mid-install (" +
                    _sceneInstall.Installed + "/" + _sceneInstall.TotalShapes +
                    " shapes uploaded before abandon)");
                _sceneInstall = null;
            }

            var (loadResults, meshPaths) = incoming;
            // Hand the timeline off the field so a re-entrant LoadNpcAsync queued
            // mid-install starts its own clock cleanly.
            var loadStopwatch = _pendingLoadStopwatch;
            _pendingLoadStopwatch = null;

            // Body parts install in a fixed visual order so the progressive reveal
            // looks coherent (body and accessories first, head/hair last) regardless
            // of the order NpcMeshResolver returned them in.
            var queue = new Queue<PendingShape>();
            foreach (var (bodyPart, meshSource, meshes) in loadResults
                         .OrderBy(r => InstallOrderRank(r.BodyPart)))
            {
                Dictionary<int, string>? txstOverrides = null;
                if (bodyPart != "Head" && meshPaths.TxstTextures.TryGetValue(bodyPart, out var txst))
                    txstOverrides = txst;
                foreach (var built in meshes)
                    queue.Enqueue(new PendingShape(bodyPart, meshSource, txstOverrides, built));
            }

            ClearScene();
            _cachedMeshPaths = meshPaths;

            _sceneInstall = new SceneInstallState(
                MeshPaths: meshPaths,
                Pending: queue,
                LoadIdentityKey: _pendingLoadIdentityKey,
                HeadMeshOverride: _pendingLoadHeadMeshOverride,
                LoadStopwatch: loadStopwatch,
                TotalShapes: queue.Count);
            _pendingLoadIdentityKey = "";
            _pendingLoadHeadMeshOverride = null;

            // LoadNpcAsync's success-path finally leaves IsLoading true so the
            // spinner stays up across the install. Reaffirm here in case any
            // earlier path dropped it.
            IsLoading = true;
            LogLoadCheckpoint(_sceneInstall.LoadStopwatch,
                "ProcessPendingScene start (sliced GL upload, " +
                _sceneInstall.TotalShapes + " shapes)");
        }

        // ── 2. Per-tick install loop: pop shapes until the budget is spent or
        //       the queue is empty. Each shape upload is the same work the
        //       single-frame install used to do inline.
        if (_sceneInstall == null) return;
        var install = _sceneInstall;

        long frameStart = Stopwatch.GetTimestamp();
        while (install.Pending.Count > 0)
        {
            InstallOneShape(install, install.Pending.Dequeue());
            install.Installed++;

            if (GlInstallBudgetMs <= 0) break; // strict one-shape-per-tick mode
            double elapsedMs = (Stopwatch.GetTimestamp() - frameStart) * 1000.0
                               / Stopwatch.Frequency;
            if (elapsedMs >= GlInstallBudgetMs) break;
        }

        if (install.Pending.Count > 0)
        {
            StatusText = $"Installing scene... {install.Installed}/{install.TotalShapes}";
            return; // resume next render tick
        }

        // ── 3. Queue drained: finalize. Promote pending identity into the
        //       currently-loaded fields, clear the rebuild gate, and drain any
        //       texture/BodySlide overrides that arrived during the install.
        LogLoadCheckpoint(install.LoadStopwatch,
            "Scene committed (" + install.TotalShapes +
            " shapes -> " + Renderer.Meshes.Count + " GL meshes) — load complete");

        StatusText = install.TotalShapes > 0
            ? $"Loaded {install.TotalShapes} shape(s) for NPC"
            : "No renderable shapes found for NPC";

        _currentLoadedIdentityKey = install.LoadIdentityKey;
        _currentHeadMeshOverride = install.HeadMeshOverride;
        _sceneInstall = null;

        // Scene is now rebuilt — clear the rebuild flag before draining the
        // pending-override queue so ApplyTextureOverrides takes the direct path.
        _sceneRebuildPending = false;
        IsLoading = false;

        if (_pendingTextureOverrides != null)
        {
            var overrides = _pendingTextureOverrides;
            _pendingTextureOverrides = null;
            ApplyTextureOverrides(overrides);
        }

        if (_pendingMorphSet != null)
        {
            var (morphs, weight) = _pendingMorphSet.Value;
            _pendingMorphSet = null;
            ApplyMorphSet(morphs, weight);
        }

        // Notify host-side queues (e.g. SynthEbdViewerHostState's pending
        // BodySlide preset) that the scene is now ready for narrow updates.
        // Fired after the neutral drains above so subscribers see a fully-committed
        // scene, including any pending texture/morph state from the previous scene.
        SceneCommitted?.Invoke();

        // Per-mesh state dump for diagnostic capture. Lives here (not at end
        // of LoadAsync) because the GlMesh array isn't populated until
        // InstallOneShape runs on the render thread — which only happens
        // after LoadAsync queues _pendingScene and the render thread ticks.
        LogMeshStateSnapshot();

        // Per-load asset-resolution snapshot ends here — the multi-tick sliced
        // install is finished, so clear the per-VM snapshot. Subsequent narrow
        // updates (texture overrides, morphs) that need scoping require the
        // host to re-set AdditionalScopes / AdditionalDataFolders and re-trigger
        // LoadAsync.
        _currentSceneScopes = null;
        _currentSceneFolders = null;
    }

    /// <summary>Body and accessories upload before head/hair so the progressive
    /// reveal during a sliced install never shows a floating head. Anything
    /// unrecognized lands at the end.</summary>
    private static int InstallOrderRank(string bodyPart) => bodyPart switch
    {
        "Body" => 0,
        "Hands" => 1,
        "Feet" => 2,
        "Head" => 3,
        "Hair" => 4,
        _ => 10,
    };

    /// <summary>Uploads one shape's GL mesh + textures and registers it in the
    /// per-body-part dictionaries. Mirrors the inner loop body the single-frame
    /// install used; called once per shape from the sliced install loop.</summary>
    private void InstallOneShape(SceneInstallState install, PendingShape shape)
    {
        var glMesh = CreateGlMesh(shape.Built);
        glMesh.MeshSource = shape.MeshSource;

        var effectiveTextures = new Dictionary<int, string>(shape.Built.TexturePaths);
        // ARMA TXST overrides target the body part's *skin* (e.g. ARMA[Body] → FemaleBody_1.dds).
        // Body/Hands/Feet NIFs can contain non-skin shapes (FemaleUnderwear, fingernails,
        // attached armor) that share the NIF but ship their own diffuse/normal. Gate on
        // BSLSP shader type 5 (ST_SkinTint) so the body skin texture doesn't bleed onto
        // those shapes — engine behavior, and matches IsSkinShape elsewhere.
        if (shape.TxstOverrides != null && shape.Built.ShaderType == 5)
            foreach (var (slot, path) in shape.TxstOverrides)
                effectiveTextures[slot] = path;

        bool isHairTint = false;
        float hairR = 0, hairG = 0, hairB = 0;
        bool isFaceTint = false;
        string? faceTintPath = null;

        ApplyTexturesToGlMesh(glMesh, shape.Built, effectiveTextures, install.MeshPaths,
            ref isHairTint, ref hairR, ref hairG, ref hairB,
            ref isFaceTint, ref faceTintPath);

        _textureApplyInfoByMesh[glMesh] = new TextureApplyInfo(
            new Dictionary<int, string>(effectiveTextures),
            isHairTint, hairR, hairG, hairB, isFaceTint, faceTintPath);

        glMesh.BodyPart = shape.BodyPart;
        glMesh.ShowWireframe = ShowWireframe;
        Renderer.AddMesh(glMesh);

        var bodyPart = shape.BodyPart;
        if (bodyPart == "Head")
        {
            if (shape.Built.IsPrimaryHeadShape || !_meshesByBodyPart.ContainsKey(bodyPart))
                _meshesByBodyPart[bodyPart] = glMesh;
            if (shape.Built.IsPrimaryHeadShape || !_builtMeshesByBodyPart.ContainsKey(bodyPart))
                _builtMeshesByBodyPart[bodyPart] = shape.Built;
        }
        else
        {
            if (!_meshesByBodyPart.ContainsKey(bodyPart))
                _meshesByBodyPart[bodyPart] = glMesh;
            if (!_builtMeshesByBodyPart.ContainsKey(bodyPart))
                _builtMeshesByBodyPart[bodyPart] = shape.Built;
        }

        if (bodyPart == "Body")
        {
            _cachedBodyMeshes[shape.Built.ShapeName] = shape.Built;

            // Cache the body NIF's disk path once per scene so ApplyBodySlide
            // can probe for a sibling .tri (BodySlide's "Build Morphs" output).
            // The .tri is topology-matched to this NIF, so it avoids the OSD
            // path's reference-mesh mismatch.
            if (_cachedBodyNifDiskPath == null && shape.MeshSource?.ResolvedDiskPath != null)
            {
                _cachedBodyNifDiskPath = shape.MeshSource.ResolvedDiskPath;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LOADING — Full NPC
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Neutral cache-driven load entry. Resolves <paramref name="identity"/>
    /// to a <see cref="ResolvedNpcMeshPaths"/> through the preview cache
    /// (whose <see cref="INpcMeshDataSource"/> adapter does the host-specific
    /// resolution — Mutagen for SynthEBD, NPC2's own scheme for NPC2), then
    /// hands off to <see cref="LoadAsync"/>. NPC Plugin Chooser 2 (and any
    /// other host) calls this directly with their own <see cref="NpcIdentity"/>.
    /// </summary>
    public async Task LoadByIdentityAsync(NpcIdentity identity, string? overrideHeadMeshAbsolutePath = null)
    {
        ResolvedNpcMeshPaths? meshPaths = null;
        try
        {
            meshPaths = await Task.Run(() => _previewCache.GetOrResolveMeshPaths(identity));
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer: Failed to resolve NPC " + identity.CacheKey + ": " +
                ex.ToString());
        }

        if (meshPaths == null)
        {
            StatusText = "Could not resolve NPC mesh paths";
            IsLoading = false;
            return;
        }

        await LoadAsync(identity, meshPaths, overrideHeadMeshAbsolutePath);
    }

    /// <summary>
    /// Neutral rendering-tier entry point. Loads <paramref name="paths"/> into
    /// the GL scene with the given <paramref name="identity"/> as the cache /
    /// short-circuit key. The host (SynthEBD or NPC2) is responsible for
    /// producing <see cref="ResolvedNpcMeshPaths"/> beforehand — this method
    /// has no knowledge of Mutagen, FormKeys, or any host-specific NPC model.
    ///
    /// Same-identity short-circuit: narrow editors (BodySlide preset change,
    /// AssetPack subgroup flip) call this defensively before their narrow
    /// update method. When the identity hasn't changed and no head override
    /// is involved, the rebuild is pure waste (full NIF re-parse, CPU
    /// re-skinning, DDS re-decode) and is skipped at this top-level guard.
    ///
    /// Override path check is exclusion-only (both sides null), not equality:
    /// when <paramref name="overrideHeadMeshAbsolutePath"/> is non-null it
    /// refers to a temp NIF (e.g. FaceGen preview) that is rewritten in place
    /// on every change. Path equality would incorrectly skip the reload.
    /// </summary>
    public async Task LoadAsync(NpcIdentity identity, ResolvedNpcMeshPaths paths,
        string? overrideHeadMeshAbsolutePath = null, CancellationToken externalCt = default)
    {
        if (!_sceneRebuildPending
            && _meshesByBodyPart.Count > 0
            && identity.CacheKey == _currentLoadedIdentityKey
            && overrideHeadMeshAbsolutePath == null
            && _currentHeadMeshOverride == null)
        {
            LogVerbose("CharacterViewer: LoadAsync same-identity short-circuit (" +
                identity.CacheKey + ")");
            return;
        }

        _loadCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        _loadCts = cts;

        // Reset the per-load missing-mesh list. LoadAllMeshParts appends to it
        // as each TryLoad attempt encounters a gamePath that no scope could
        // resolve to disk; the renderer / host reads it after LoadAsync
        // completes to surface incomplete-render warnings.
        _missingMeshPaths.Clear();
        TextureManager?.ClearMissingTexturePaths();

        // Mark a rebuild as in-flight so any ApplyTextureOverrides calls arriving
        // between now and when ProcessPendingScene finishes are queued rather than
        // applied to the soon-to-be-destroyed current meshes.
        _sceneRebuildPending = true;

        // Snapshot the host's resolution scoping into per-VM fields. Two
        // consumers of these snapshots:
        //   (a) The PushScopes bracket below covers off-thread NIF parsing
        //       inside Task.Run — AsyncLocal flows through ExecutionContext.
        //   (b) ProcessPendingScene re-pushes from the snapshot fields on
        //       every install tick because the WPF render callback that
        //       fires it is on a different ExecutionContext that does NOT
        //       inherit this method's AsyncLocal value.
        // AdditionalScopes (1.2.0+) wins over AdditionalDataFolders (1.1.0)
        // when both are provided.
        var additionalScopes = AdditionalScopes;
        var additionalFolders = AdditionalDataFolders;
        _currentSceneScopes = additionalScopes;
        _currentSceneFolders = additionalFolders;
        _currentSceneVanillaLooseOverridesBsa = VanillaLooseOverridesBsa;
        _currentSceneVanillaLooseOverridesModLoose = VanillaLooseOverridesModLoose;

        IsLoading = true;
        StatusText = "Loading meshes...";

        var loadStopwatch = System.Diagnostics.Stopwatch.StartNew();
        LogLoadCheckpoint(loadStopwatch, "LoadAsync begin (id=" + identity.CacheKey + ")");

        try
        {
            // Pull NPC-record values straight off the resolved POCO. The host's
            // adapter populates weight/height/hair-color when it builds the POCO;
            // unset fields fall back to the defaults baked into ResolvedNpcMeshPaths.
            NpcWeight = paths.NpcWeight;
            NpcBaseHeight = paths.NpcBaseHeight;
            _npcHairColorFromRecord = paths.HairColorRgb;
            LogVerbose("CharacterViewer: NPC weight=" + NpcWeight +
                ", height=" + NpcBaseHeight.ToString("F3") +
                ", hairRgb=" + (paths.HairColorRgb?.ToString() ?? "null"));

            if (!string.IsNullOrWhiteSpace(overrideHeadMeshAbsolutePath))
            {
                paths = paths.WithHeadMeshPath(overrideHeadMeshAbsolutePath);
                LogVerbose("CharacterViewer: head mesh path overridden -> " + overrideHeadMeshAbsolutePath);
            }

            _cachedMeshPaths = paths;
            cts.Token.ThrowIfCancellationRequested();

            // Push the snapshot onto the resolver's AsyncLocal stack for the
            // duration of the off-thread parse. The using bracket covers only
            // the Task.Run — ExecutionContext flows into the pool worker so
            // LoadAllMeshParts → ResolveAssetSource sees these scopes — and
            // pops on return. The install side later in ProcessPendingScene
            // re-pushes from _currentScene* fields on its own callback flow.
            List<(string BodyPart, AssetSource? MeshSource, List<NifMeshBuilder.BuiltMesh> Meshes)> loadResults;
            using (_assetResolver.PushScopes(additionalScopes, additionalFolders,
                       _currentSceneVanillaLooseOverridesBsa,
                       _currentSceneVanillaLooseOverridesModLoose))
            {
                loadResults = await Task.Run(() => LoadAllMeshParts(paths), cts.Token);
            }
            cts.Token.ThrowIfCancellationRequested();

            // Store pending scene data — GL work is deferred to the render callback
            // where the GL context is guaranteed to be current.
            int totalShapes = loadResults.Sum(r => r.Meshes.Count);
            LogLoadCheckpoint(loadStopwatch, "NIFs parsed + skinned (" + totalShapes +
                " shapes across " + loadResults.Count + " parts)");
            _renderThread.Invoke(() =>
            {
                _pendingScene = (loadResults, paths);
                _pendingLoadIdentityKey = identity.CacheKey;
                _pendingLoadHeadMeshOverride = overrideHeadMeshAbsolutePath;
                // Hand the clock to the render thread — ProcessPendingScene will
                // consume it on the next frame tick and log the GL-upload span.
                _pendingLoadStopwatch = loadStopwatch;
                StatusText = totalShapes > 0
                    ? $"Loaded {totalShapes} shape(s), setting up scene..."
                    : "No renderable shapes found for NPC";
            });
            LogLoadCheckpoint(loadStopwatch, "Scene queued for GL upload");

            // One-shot diagnostic dump of every render-state value that affects
            // final pixel brightness. Emitted here (before LoadAsync returns)
            // rather than from ProcessPendingScene's post-SceneCommitted hook,
            // because the live-preview host scopes its RenderLogCapture to the
            // span of `await Viewer.LoadAsync(...)` — by the time the render
            // thread actually drains _pendingScene and fires SceneCommitted, the
            // preview's capture file is already closed and the snapshot would
            // be dropped. The mugshot pathway holds its capture open across the
            // whole RenderToPngAsync call, so it catches the snapshot either
            // way. Side-by-side diffing of the two pathways' snapshots locates
            // pipeline divergences (lighting, tint operators, render quality
            // toggles) that cause off-by-tone renders.
            LogRenderStateSnapshot();
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
            _logger.LogError("CharacterViewer: Failed to load NPC " + identity.CacheKey + Environment.NewLine
                + ex.ToString());
            if (_loadCts == cts) _sceneRebuildPending = false;
        }
        finally
        {
            // Success path: _pendingScene is set and the sliced install will clear
            // IsLoading when the queue drains in ProcessPendingScene. Cancel/error
            // paths fall through here with _pendingScene == null and need to drop
            // the spinner immediately.
            //
            // Snapshot cleanup mirrors that split: success leaves _currentScene*
            // populated so ProcessPendingScene can re-push them onto the resolver
            // during the multi-tick install (cleared by ProcessPendingScene's
            // finalize after SceneCommitted). Cancel / error clears here because
            // SceneCommitted won't fire — but only if we're still the current
            // load. A newer LoadAsync that just took over has already overwritten
            // _currentScene*; clearing here would wipe its snapshot mid-flight.
            if (_loadCts == cts && _pendingScene == null)
            {
                IsLoading = false;
                _currentSceneScopes = null;
                _currentSceneFolders = null;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TEXTURE APPLICATION
    // ═══════════════════════════════════════════════════════════════════════

    private void ApplyTexturesToGlMesh(GlMesh glMesh, NifMeshBuilder.BuiltMesh built,
        Dictionary<int, string> effectiveTextures, ResolvedNpcMeshPaths meshPaths,
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

        // Skin tint: apply NPC's QNAM TextureLighting color to body
        // (ShaderType 5 = ST_SkinTint, production behavior) and to face
        // shapes (ShaderType 4 = ST_FaceTint, debug path). For face
        // shapes we always set the tint color but leave the actual
        // application gated by the GlRenderer.SkinTintApplyToFace
        // uniform — that way the host can flip the debug toggle at
        // runtime without re-loading the scene.
        if ((built.ShaderType == 5 || built.ShaderType == 4)
            && meshPaths.TextureLightingColor.HasValue)
        {
            var (r, g, b) = meshPaths.TextureLightingColor.Value;
            glMesh.HasTintColor = true;
            glMesh.TintColor = new System.Numerics.Vector3(r, g, b);
        }
        glMesh.IsFaceShape = (built.ShaderType == 4);
        glMesh.IsSkinShape = (built.ShaderType == 4 || built.ShaderType == 5);
        glMesh.SkinTintAlpha = built.SkinTintAlpha;

        // Eye shader (shader type 16 = ST_EyeEnvmap)
        //
        // Some FaceGen NIFs ship eyes authored as BSLSP_ENVMAP (1) instead
        // of BSLSP_EYE (16) and would otherwise miss the is_eye AO gate in
        // basic.frag, leaving the eyeball to receive SSAO darkening along
        // the eye-opening rim. Skyrim's naming convention uses plural
        // "Eyes" for actual eye shapes (MaleEyesHumanIceBlue, EyesChild,
        // KWA_FemaleEyesHuman) and singular "Eye" for accessories
        // (EyeShadow, 0EyeShadow, Eyelashes), so the substring check is
        // sufficient to disambiguate.
        if (built.ShaderType == 16
            || built.ShapeName.Contains("Eyes", StringComparison.Ordinal))
        {
            glMesh.IsEye = true;
        }

        // Environment mapping (SLSF1_Environment_Mapping bit 7, or SLSF1_Eye_Environment_Mapping bit 17)
        bool hasEnvMap = (built.ShaderFlags1 & (1u << 7)) != 0;
        bool hasEyeEnvMap = (built.ShaderFlags1 & (1u << 17)) != 0;
        if ((hasEnvMap || hasEyeEnvMap) && effectiveTextures.TryGetValue(4, out string? envMapPath))
        {
            var (envTex, envIsCube) = TextureManager.LoadEnvMap(envMapPath);
            if (envTex != 0)
            {
                glMesh.EnvMapTexture = envTex;
                glMesh.HasEnvironmentMap = true;
                glMesh.IsEnvMap2D = !envIsCube;
                glMesh.EnvMapScale = built.EnvironmentMapScale;
                glMesh.EyeCubemapScale = built.EyeCubemapScale;
                RecordTextureSource(glMesh,
                    envIsCube ? "Environment Cubemap" : "Environment Map (2D fallback)",
                    envMapPath);
            }

            if (effectiveTextures.TryGetValue(5, out string? envMaskPath))
            {
                glMesh.EnvMaskTexture = TextureManager.LoadTexture(envMaskPath);
                glMesh.HasEnvMask = true;
                RecordTextureSource(glMesh, "Environment Mask", envMaskPath);
            }
        }

        // Detail map (SLSF1_Facegen_Detail_Map, bit 10)
        bool detailFlagSet = (built.ShaderFlags1 & (1u << 10)) != 0;
        bool detailSlotPopulated = effectiveTextures.TryGetValue(3, out string? detailPath);
        if (detailFlagSet && detailSlotPopulated && detailPath != null)
        {
            glMesh.DetailTexture = TextureManager.LoadTexture(detailPath);
            glMesh.HasDetailMap = true;
            RecordTextureSource(glMesh, "Detail Map", detailPath);
        }
        else if (detailFlagSet && !detailSlotPopulated && built.ShaderType == 4 && Renderer.UseBlankDetailFallback)
        {
            // Experimental fallback: face shape has the detail flag set
            // but slot 3 is empty in its NIF. Substitute Bethesda's CK
            // default (the texture used when no complexion / freckle /
            // dirt / wound option is chosen). Helps test whether the
            // engine substitutes a similar default at runtime.
            const string blankDetailPath = "textures\\actors\\character\\male\\BlankDetailmap.dds";
            glMesh.DetailTexture = TextureManager.LoadTexture(blankDetailPath);
            glMesh.HasDetailMap = true;
            RecordTextureSource(glMesh, "Detail Map (BlankDetailmap fallback)", blankDetailPath);
        }

        // Track face shapes with an empty slot 3 for the optional
        // "force multiply on empty detail" debug path in basic.frag.
        // Set independently of whether the toggle is on; the shader-side
        // toggle (FaceTintMultiplyOnEmptyDetail) decides whether to act
        // on it.
        glMesh.IsFaceWithEmptyDetailSlot = built.ShaderType == 4
            && detailFlagSet
            && !detailSlotPopulated;

        // Double-sided (brow, eyelash, hair — thin geometry visible from both sides)
        glMesh.IsDoubleSided = built.IsDoubleSided;

        // Alpha test / blend
        if (built.HasAlphaTest || built.HasAlphaBlend)
        {
            if (glMesh.DiffuseTexture != TextureManager.WhiteTexture)
            {
                // Suppress the GL discard on BSLSP_FACE shapes (ShaderType=4)
                // that carry the NiAlphaProperty alpha-test bit.
                //
                // What we observed:
                //   - Vanilla Khajiit MaleHeadKhajiit carries
                //     NiAlphaProperty.AlphaTest=True with threshold 73 (≈0.286).
                //   - Its diffuse texture has alpha < 1 across the entire face
                //     (this is independently noted in the renderer's Pass-1
                //     comment in GlRenderer.cs, which deliberately disables
                //     SAMPLE_ALPHA_TO_COVERAGE for that reason).
                //   - It also has SLSF1_Vertex_Alpha set with vertex-color
                //     alpha varying from 1.0 down to ~0.325 (a low-alpha ring
                //     around the head/body seam — 133 of 1356 vertices on
                //     Ri'saad).
                //   - basic.frag multiplies vertex alpha into texture alpha
                //     before the discard test, so face fragments near the
                //     seam produce α ≈ 0.276 (below the 0.286 threshold) and
                //     discard. At ~750 px portrait resolution each discarded
                //     fragment spans a perceptible pixel-sized hole.
                //   - In vanilla in-game Skyrim, Khajiit faces render solid;
                //     this discard pattern does not appear there.
                //
                // What we have NOT verified:
                //   - The exact engine-side mechanism that produces the
                //     solid in-game face. Plausible candidates we haven't
                //     traced: (a) the engine ignoring the alpha-test bit on
                //     BSLSP_FACE entirely, (b) alpha-to-coverage running on
                //     a framebuffer configured differently from ours,
                //     (c) BSLSP_FACE being routed through a separate draw
                //     call that doesn't consult NiAlphaProperty,
                //     (d) Bethesda's actual vertex-alpha values differing
                //     from what niflysharp reads back. We haven't read
                //     engine source or Community Shaders' replacement shader
                //     for this specific path.
                //   - Authorial intent for the vertex_alpha ring. The
                //     seam-localized distribution makes a head/body seam
                //     fade a plausible reading, but Bethesda's authoring
                //     intent isn't documented.
                //
                // What this fix does:
                //   - For ShaderType==4 face shapes only, skip writing
                //     `built.HasAlphaTest` to `glMesh.UseAlphaTest`. The
                //     NIF's HasAlphaBlend / AlphaThreshold / blend factors
                //     are still propagated unchanged so any other shader
                //     path that consults them continues to see the
                //     NIF-authored values.
                //
                // The result is that face fragments along the seam render
                // with their actual partial alpha instead of being
                // discarded. That partial alpha then lands in the off-screen
                // pipeline's readback bytes — see the alpha-strip step in
                // GameWindowOffscreenRenderer.RenderInternalCore for the
                // companion piece of this two-half fix.
                bool suppressAlphaTestForFace = built.ShaderType == 4 && built.HasAlphaTest;
                glMesh.UseAlphaTest = built.HasAlphaTest && !suppressAlphaTestForFace;
                glMesh.HasAlphaBlend = built.HasAlphaBlend;
                glMesh.AlphaThreshold = built.AlphaThreshold;
                glMesh.SrcBlendIndex = built.SrcBlendIndex;
                glMesh.DstBlendIndex = built.DstBlendIndex;
            }
            else
            {
                // Alpha-tested / alpha-blended shape with no resolvable diffuse.
                // Without a usable alpha channel the discard threshold is
                // undefined and a flat-white billboard would mislead the user.
                // Render as wireframe placeholder by default so the shape's
                // silhouette stays visible alongside the host's missing-texture
                // overlay; if the host has opted out, fall back to silent
                // culling (matches pre-2.5.6 behavior).
                string disposition = RenderMissingTextureAsWireframe
                    ? "WIREFRAME-FALLBACK" : "CULLED";
                System.Diagnostics.Trace.WriteLine(
                    $"[CharacterViewer.ApplyMaterial] {disposition} shape='{glMesh.ShapeName}' " +
                    $"bodyPart='{glMesh.BodyPart}' " +
                    $"alphaTest={built.HasAlphaTest} alphaBlend={built.HasAlphaBlend} " +
                    $"hairTint={built.IsHairTintShader} " +
                    "(diffuse fell back to WhiteTexture — texture path missing or DDS load failed)");
                if (RenderMissingTextureAsWireframe)
                    glMesh.RenderAsWireframeFallback = true;
                else
                    glMesh.IsRendering = false;
            }
        }

        glMesh.IsHairTintShader = built.IsHairTintShader;
        glMesh.IsPrimaryHeadShape = built.IsPrimaryHeadShape;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TEXTURE OVERRIDES
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Neutral texture-override entry. Each <see cref="TextureOverride"/>
    /// names its target body part + slot explicitly, so the host doesn't
    /// have to encode that into a destination path the way SynthEBD's
    /// <see cref="FilePathReplacement"/> does. NPC Plugin Chooser 2 (and
    /// any future host) calls this directly.
    /// </summary>
    public void ApplyTextureOverrides(IEnumerable<TextureOverride> overrides)
    {
        var overrideList = overrides as List<TextureOverride> ?? overrides?.ToList() ?? new List<TextureOverride>();

        // Queue when the scene is empty, the texture manager isn't ready, OR a
        // rebuild is in-flight. The rebuild check is what catches the subgroup
        // re-selection case: between LoadAsync queueing _pendingScene and the
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

        foreach (var ov in overrideList)
        {
            string bodyPart = ov.BodyPart;
            int slot = ov.Slot;
            string source = ov.GameRelativePath;
            if (string.IsNullOrWhiteSpace(bodyPart) || string.IsNullOrWhiteSpace(source)) continue;

            // For Head, target only the primary head shape (the face — face/hair/eyes
            // are separate shapes with different meaning for each slot). For non-head
            // body parts, apply to every *skin* shape in that NIF: a body NIF can hold
            // multiple skin shapes (CBBE 3BA Body+Vagina), and they should all receive
            // the body diffuse. But non-skin shapes that share the same NIF (underwear
            // on the vanilla FemaleBody, fingernails on FemaleHands) keep their NIF-baked
            // textures — without this gate, ARMA[Body] TXST clobbers the brassiere with
            // FemaleBody_1.dds and you get a belly button on the underwear.
            List<GlMesh> targets;
            if (bodyPart == "Head")
            {
                if (!_meshesByBodyPart.TryGetValue(bodyPart, out var headMesh))
                {
                    LogVerbose("CharacterViewer: No Head mesh tracked for override (slot " + slot + ")");
                    continue;
                }
                targets = new List<GlMesh> { headMesh };
            }
            else
            {
                targets = Renderer.Meshes.Where(m => m.BodyPart == bodyPart && m.IsSkinShape).ToList();
                if (targets.Count == 0)
                {
                    LogVerbose("CharacterViewer: No skin meshes with BodyPart='" + bodyPart +
                        "' (slot " + slot + ")");
                    continue;
                }
            }

            foreach (var mesh in targets)
            {
                if (slot == 0)
                {
                    mesh.DiffuseTexture = TextureManager.LoadTexture(source);
                    RecordTextureSource(mesh, "Diffuse", source);
                }
                else if (slot == 1)
                {
                    // Shader handles MSN natively; no CPU resampling needed.
                    mesh.NormalTexture = TextureManager.LoadTexture(source);
                    mesh.HasNormalMap = true;
                    RecordTextureSource(mesh, "Normal Map", source);
                }
                else if (slot == 2)
                {
                    // Skin/SSS — only meaningful on skin-shader meshes; harmless on others
                    // since HasSkinMap gates shader sampling.
                    mesh.SkinTexture = TextureManager.LoadTexture(source);
                    mesh.HasSkinMap = true;
                    RecordTextureSource(mesh, "Skin/SSS", source);
                }
                else if (slot == 7)
                {
                    mesh.SpecularTexture = TextureManager.LoadTexture(source);
                    mesh.HasSpecularMap = true;
                    mesh.HasSpecular = true;
                    RecordTextureSource(mesh, "Specular", source);
                }
            }

            LogVerbose("CharacterViewer: Slot " + slot + " override '" + source +
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

    /// <summary>
    /// Pre-populates the OSD context that <see cref="ApplyMorphSet"/> consumes
    /// when no sibling .tri is present. Hosts call this once per scene change
    /// before invoking ApplyMorphSet on slider-driven morphs. SynthEBD's
    /// <see cref="ApplyBodySlide(BodySlideSetting, int)"/> wrapper does this
    /// internally via <see cref="LoadOsdFilesForGroup"/>; other hosts that
    /// don't have a SliderGroup → catalog mapping pass their pre-parsed OSD
    /// files in directly.
    /// </summary>
    public void SetMorphContext(List<OsdFile>? osdFiles)
    {
        _cachedOsdFiles = osdFiles;
    }

    /// <summary>
    /// Neutral morph-application entry. Applies <paramref name="morphs"/> to
    /// the loaded scene's body shapes at NPC weight <paramref name="weight"/>,
    /// preferring a sibling .tri (auto-loaded from disk next to the body NIF)
    /// over the OSD context set via <see cref="SetMorphContext"/>. NPC Plugin
    /// Chooser 2 (and any future host) calls this directly.
    /// </summary>
    public void ApplyMorphSet(MorphSet morphs, int weight)
    {
        if (_bodySlideDisabled)
        {
            NpcWeight = Math.Clamp(weight, 0, 100);
            LogVerbose("CharacterViewer: [BodySlideDisabled] ApplyMorphSet bypassed" +
                " (label='" + (morphs?.Label ?? "?") + "', weight=" + NpcWeight + ")");
            return;
        }

        if (_cachedBodyMeshes.Count == 0 || _sceneRebuildPending)
        {
            _pendingMorphSet = (morphs ?? new MorphSet(), weight);
            LogVerbose("CharacterViewer: ApplyMorphSet QUEUED (label='"
                + (morphs?.Label ?? "?") + "', weight=" + weight
                + ", cachedBodyMeshes=" + _cachedBodyMeshes.Count
                + ", sceneRebuildPending=" + _sceneRebuildPending + ")");
            return;
        }

        if (morphs == null) return;

        NpcWeight = Math.Clamp(weight, 0, 100);
        LogVerbose("CharacterViewer: ApplyMorphSet APPLIED (label='"
            + (morphs.Label ?? "?") + "', weight=" + NpcWeight
            + ", cachedBodyMeshes=" + _cachedBodyMeshes.Count + ")");

        try
        {
            // Idempotent — if ApplyBodySlide already loaded the .tri this is a no-op.
            TryLoadSiblingBodyTri();

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

                // Start from bind-pose positions. When the shape was loaded with a
                // _0.nif companion, lerp the snapshotted _0/_1 endpoints to the
                // current NpcWeight here — that's the game's "armor weight morph"
                // reproduced per-call, so changing BodySlide weight via ApplyMorphSet
                // (rather than reloading the NPC) still picks up the correct base
                // body. Shapes without cached _0/_1 (FaceGen head, hairs, etc., or
                // any shape whose _0 didn't pair by name + vert count) fall through
                // to the prior behavior of sourcing directly from BindPosePositions,
                // which carries the load-time blend that matches whatever weight the
                // NPC was loaded at — so the head/body neck stays aligned.
                var basePositions = originalMesh.BindPosePositions ?? originalMesh.Positions;
                var positions = new Vector3[basePositions.Length];
                var w0 = originalMesh.Weight0BindPosePositions;
                var w1 = originalMesh.Weight1BindPosePositions;
                if (w0 != null && w1 != null
                    && w0.Length == basePositions.Length
                    && w1.Length == basePositions.Length)
                {
                    float t = NpcWeight / 100f;
                    for (int i = 0; i < basePositions.Length; i++)
                    {
                        positions[i] = Vector3.Lerp(w0[i], w1[i], t);
                    }
                }
                else
                {
                    Array.Copy(basePositions, positions, basePositions.Length);
                }

                // Apply deformation -- prefer .tri (topology-matched, no LCP stripping),
                // fall back to OSD for meshes without "Build Morphs" output.
                if (_cachedBodyTri != null)
                {
                    _bodySlideDeformer.ApplyDeformationFromTri(positions, morphs, NpcWeight, _cachedBodyTri, shapeName);
                }
                else
                {
                    _bodySlideDeformer.ApplyDeformation(positions, morphs, NpcWeight, _cachedOsdFiles!, shapeName);
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
            _logger.LogError("CharacterViewer: ApplyMorphSet failed for '"
                + (morphs.Label ?? "?") + "' at weight " + NpcWeight + Environment.NewLine
                + ex.ToString());
        }

        // Fire regardless of deformation outcome so subscribers can refresh readouts;
        // a failed deformation leaves CpuPositions in a valid (undeformed) state that
        // is still meaningful to measure.
        RefreshKeyVertexMarkerPositions();
        BodySlideApplied?.Invoke();
    }

    // ApplyBodyGen and ApplyHeadPartsAsync moved to SynthEbdViewerHostState
    // and CharacterViewerSynthEbdExtensions in Phase B2c.2 — they reference
    // SynthEBD-only types (BodyGenConfig.BodyGenTemplate, HeadPart.TypeEnum,
    // FaceGenPreviewService) that the rendering tier must not depend on.

    /// <summary>
    /// Parses <paramref name="headNifPath"/> off-thread and queues the result for
    /// installation on the render thread via <see cref="ProcessPendingScene"/>.
    /// The install step removes current Head shape(s), creates new GlMesh(es),
    /// applies textures using the cached <see cref="_cachedMeshPaths"/> (face tint),
    /// and preserves all Body/Hands/Feet state untouched.
    /// </summary>
    /// <summary>
    /// Parses <paramref name="headNifPath"/> off-thread and queues the result
    /// for installation on the render thread via <see cref="ProcessPendingScene"/>.
    /// The install step removes current Head shape(s), creates new GlMesh(es),
    /// and re-applies head textures from the cached mesh paths. Used by
    /// SynthEBD's ApplyHeadPartsAsync extension as the fast path when the
    /// same NPC is already loaded — see <see cref="CanRebuildHeadOnly"/>.
    /// </summary>
    public async Task RebuildHeadOnlyAsync(string headNifPath, CancellationToken ct)
    {
        var headStopwatch = System.Diagnostics.Stopwatch.StartNew();
        LogLoadCheckpoint(headStopwatch, "RebuildHeadOnlyAsync begin (" +
            System.IO.Path.GetFileName(headNifPath) + ")");

        // Parse with no skeleton: FaceGen head NIFs are rigid / self-skinned and
        // the body skeleton is not needed to produce correct vertex positions.
        // This matches how LoadAllMeshParts invokes BuildFromFile for the Head
        // when skeletonNif is null. Push the per-load snapshot for the duration
        // of the off-thread parse — defensive (BuildFromFile parses NIFs and
        // shouldn't itself touch textures, but anything down the future stack
        // that does will see consistent scopes via AsyncLocal flow).
        List<NifMeshBuilder.BuiltMesh> meshes;
        try
        {
            using (_assetResolver.PushScopes(_currentSceneScopes, _currentSceneFolders,
                       _currentSceneVanillaLooseOverridesBsa,
                       _currentSceneVanillaLooseOverridesModLoose))
            {
                meshes = await Task.Run(() => _meshBuilder.BuildFromFile(headNifPath, skeletonNif: null), ct);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer.RebuildHeadOnlyAsync: head NIF parse failed: " +
                ex.ToString());
            return;
        }

        if (meshes.Count == 0)
        {
            LogVerbose("CharacterViewer.RebuildHeadOnlyAsync: no renderable shapes in " + headNifPath);
            return;
        }

        ct.ThrowIfCancellationRequested();

        LogLoadCheckpoint(headStopwatch, "Head NIF parsed (" + meshes.Count + " shape(s))");

        _renderThread.Invoke(() =>
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
        _currentLoadedIdentityKey = "";
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
        // BuiltMesh data (with its vertex/index buffers) until GC. _sceneInstall
        // can hold a queue of dozens of unwalked BuiltMesh entries mid-install.
        _pendingScene = null;
        _sceneInstall = null;
        _pendingTextureOverrides = null;
        _pendingMorphSet = null;
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
                + ex.ToString());
        }

        // Tears down reactive subscriptions added via DisposeWith(_disposables).
        _disposables.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    // ToMorphSet moved to SynthEbdViewerHostState (Phase B2c.2) — it
    // references SynthEBD's BodySlideSetting and BodySlideSlider types.

    private List<(string BodyPart, AssetSource? MeshSource, List<NifMeshBuilder.BuiltMesh> Meshes)> LoadAllMeshParts(
        ResolvedNpcMeshPaths meshPaths)
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
                    _missingMeshPaths.Add(meshPaths.SkeletonPath);
                }
            }
            else
            {
                _missingMeshPaths.Add(meshPaths.SkeletonPath);
            }
        }

        try
        {
            void TryLoad(string bodyPart, string? gamePath)
            {
                if (string.IsNullOrWhiteSpace(gamePath))
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[CharacterViewer.LoadAllMeshParts] {bodyPart}: no gamePath (skipped)");
                    return;
                }
                var source = _assetResolver.ResolveAssetSource(gamePath);
                if (source.ResolvedDiskPath == null)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[CharacterViewer.LoadAllMeshParts] {bodyPart}: gamePath='{gamePath}' UNRESOLVED");
                    // Host-expected mesh that didn't land on disk anywhere in the
                    // resolution chain. Track for the post-load diagnostics
                    // surface so hosts can flag the incomplete render.
                    _missingMeshPaths.Add(gamePath);
                    return;
                }
                var meshes = _meshBuilder.BuildFromFile(source.ResolvedDiskPath, skeletonNif, skelDiskPath, bodyPart);
                string shapeSummary = meshes.Count == 0 ? "" :
                    " [" + string.Join(", ", meshes.Select(m =>
                        m.ShapeName
                        + (m.HasAlphaTest ? "+aTest" : "")
                        + (m.HasAlphaBlend ? "+aBlend" : "")
                        + (m.IsHairTintShader ? "+hairTint" : ""))) + "]";
                System.Diagnostics.Trace.WriteLine(
                    $"[CharacterViewer.LoadAllMeshParts] {bodyPart}: gamePath='{gamePath}' " +
                    $"-> '{source.ResolvedDiskPath}' built {meshes.Count} shape(s){shapeSummary}");
                if (meshes.Count == 0) return;

                // Weight morph: armor meshes ship as _0/_1 pairs that the game engine
                // linearly interpolates by NpcWeight (0..100). The FaceGen head is already
                // baked at the NPC's weight so it needs no morph. Skinning is linear in
                // vertex position, so blending the already-skinned world-space positions
                // is equivalent to blending bind-pose and re-skinning (both _0 and _1
                // share the same skeleton and skinToBone transforms).
                //
                // Unlike the prior implementation we ALWAYS load _0 (even when the NPC's
                // record weight is 100) and snapshot BOTH _0 and _1 bind-pose verts onto
                // each BuiltMesh BEFORE BlendWeightMorph runs (which would mutate _1's
                // BindPosePositions in place). ApplyMorphSet then redoes the _0/_1 lerp
                // from those snapshots at the current NpcWeight on every call, so
                // changing weight via ApplyBodySlide produces the engine-equivalent base
                // mesh — not the load-time-frozen blend that strands the scan's high-weight
                // iterations on a low-weight base. The snapshots travel with the BuiltMesh
                // through the install pipeline (so ClearScene mid-install can't lose them),
                // and BindPosePositions itself keeps the original load-time blend behavior
                // so the head/body neck still aligns when nothing else has been applied.
                if (bodyPart != "Head")
                {
                    string? weight0Path = TryGetWeightZeroPath(gamePath);
                    if (weight0Path != null)
                    {
                        var weight0Source = _assetResolver.ResolveAssetSource(weight0Path);
                        if (weight0Source.ResolvedDiskPath != null)
                        {
                            var meshes0 = _meshBuilder.BuildFromFile(weight0Source.ResolvedDiskPath, skeletonNif, skelDiskPath, bodyPart);

                            // Snapshot endpoints onto each m1 BEFORE the in-place blend.
                            // After this loop completes, m1.Weight0BindPosePositions ==
                            // _0.nif bind pose, m1.Weight1BindPosePositions == _1.nif bind
                            // pose. BlendWeightMorph then proceeds as before, mutating
                            // m1.BindPosePositions into the load-time blend (unchanged
                            // behavior — preserves head/body neck alignment for any code
                            // path that uses BindPosePositions directly).
                            foreach (var m1 in meshes)
                            {
                                var m0 = meshes0.FirstOrDefault(m => m.ShapeName == m1.ShapeName);
                                if (m0?.BindPosePositions == null || m1.BindPosePositions == null)
                                {
                                    LogVerbose("CharacterViewer: [WeightSnapshot] '" + bodyPart + "' shape '"
                                        + m1.ShapeName + "' skipped (m0.BindPose null="
                                        + (m0?.BindPosePositions == null) + ", m1.BindPose null="
                                        + (m1.BindPosePositions == null) + ")");
                                    continue;
                                }
                                if (m0.BindPosePositions.Length != m1.BindPosePositions.Length)
                                {
                                    LogVerbose("CharacterViewer: [WeightSnapshot] '" + bodyPart + "' shape '"
                                        + m1.ShapeName + "' skipped (vert count mismatch: m0="
                                        + m0.BindPosePositions.Length + ", m1=" + m1.BindPosePositions.Length + ")");
                                    continue;
                                }
                                m1.Weight0BindPosePositions = (System.Numerics.Vector3[])m0.BindPosePositions.Clone();
                                m1.Weight1BindPosePositions = (System.Numerics.Vector3[])m1.BindPosePositions.Clone();
                                // Cheap divergence sniff so we can confirm the snapshots
                                // contain different data when the NIF actually has a _0/_1
                                // pair (a body that ships _0 == _1 would explain the scan
                                // matching old behavior even with snapshots wired in).
                                var w0p = m0.BindPosePositions;
                                var w1p = m1.BindPosePositions;
                                int n = w0p.Length;
                                float maxDelta = 0f;
                                if (n > 0)
                                {
                                    int step = System.Math.Max(1, n / 64);
                                    for (int i = 0; i < n; i += step)
                                    {
                                        float dx = w1p[i].X - w0p[i].X;
                                        float dy = w1p[i].Y - w0p[i].Y;
                                        float dz = w1p[i].Z - w0p[i].Z;
                                        float d2 = dx * dx + dy * dy + dz * dz;
                                        if (d2 > maxDelta) maxDelta = d2;
                                    }
                                    maxDelta = (float)System.Math.Sqrt(maxDelta);
                                }
                                LogVerbose("CharacterViewer: [WeightSnapshot] '" + bodyPart + "' shape '"
                                    + m1.ShapeName + "' snapshotted "
                                    + n + " verts, sampled max |_1 - _0| = " + maxDelta.ToString("F4"));
                            }

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
            // Hair / Tail come from worn-armor ARMA entries (biped slots 31
            // and 40), not from the FaceGen NIF. The Skyrim engine renders
            // these the same way as Body / Hands / Feet — skinned to the
            // body skeleton at runtime — so the existing skinning + texture
            // path handles them once the host populates the mesh path.
            // High Poly NPC Overhaul uses Hair via a "wig" ARMO on a bald
            // FaceGen; Khajiit / Argonian races use Tail.
            TryLoad("Hair", meshPaths.HairMeshPath);
            TryLoad("Tail", meshPaths.TailMeshPath);
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

        // Persist per-vertex bone indices + weights from the SkinningInfo so the
        // classifier's bone-transition criterion can find anatomical seams (e.g.
        // armpit = the boundary between torso-bone-weighted and arm-bone-weighted
        // vertices). Cloned because the BuiltMesh is shared via the NIF parse
        // cache and BodySlide deformations re-read it. Null for unskinned shapes.
        if (built.Skinning != null && built.Skinning.VertBoneIndices.Length > 0)
        {
            glMesh.CpuBoneIndices = (int[])built.Skinning.VertBoneIndices.Clone();
            glMesh.CpuBoneWeights = (float[])built.Skinning.VertBoneWeights.Clone();
        }

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

    // LoadOsdFilesForGroup, FindRegistryEntry, CollectShapeDataFolders moved
    // to SynthEbdOsdLoader (Phase B2c.2) — they walk SynthEBD's PatcherState
    // BodyTypeRegistry and BodyTypeRegistryEntry types. The viewer's neutral
    // path consumes pre-loaded OSDs via SetMorphContext.

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
