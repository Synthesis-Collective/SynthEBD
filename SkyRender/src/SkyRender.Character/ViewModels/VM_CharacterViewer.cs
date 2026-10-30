using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reactive.Disposables;
// ReactiveUI 23 / System.Reactive moved the DisposeWith(CompositeDisposable) extension from
// System.Reactive.Disposables.DisposableMixins to System.Reactive.Disposables.Fluent. This VM
// disposes into a CompositeDisposable (_disposables) and has no Noggog IDisposableDropoff overload
// in scope, so it needs the new namespace.
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ReactiveUI;
using MediaColor = System.Windows.Media.Color;

namespace SkyRender.Character;

/// <summary>
/// Controls which editing features are available in the character viewer.
/// </summary>
[Flags]
public enum ViewerMode
{
    ReadOnly = 0,
    TextureEdit = 1,
    ShapeEdit = 2,
    Full = TextureEdit | ShapeEdit
}

/// <summary>
/// ViewModel for the 3D character viewer. Manages the OpenGL rendering scene,
/// loaded mesh data, textures, and user interaction.
///
/// <para>Partial: the guest-overlay ("superimpose") scene lives in
/// <c>VM_CharacterViewer.GuestScene.cs</c>. That is a self-contained second scene
/// installed alongside the primary one, so keeping it out of this file avoids
/// interleaving it with the primary scene's install pipeline.</para>
/// </summary>
public partial class VM_CharacterViewer : ViewerVm
{
    /// <summary>Composite for reactive subscriptions added via
    /// <c>DisposeWith</c> (System.Reactive.Disposables.Fluent).
    /// Disposed in <see cref="Dispose"/>.</summary>
    private readonly CompositeDisposable _disposables = new();

    private readonly NifMeshBuilder _meshBuilder;
    private readonly GameAssetResolver _assetResolver;
    private readonly IRenderLogger _logger;
    private readonly RenderLogGate _logGate;

    private CancellationTokenSource? _loadCts;

    // GL rendering objects — created once, reused across NPC loads
    public GlRenderer Renderer { get; } = new(RenderProfile.Character);
    public GlTextureManager? TextureManager { get; private set; }
    public OrbitCamera Camera { get; } = new();

    /// <summary>Tracks which GlMesh corresponds to which body part for texture overrides.</summary>
    private readonly Dictionary<string, GlMesh> _meshesByBodyPart = new();

    /// <summary>Tracks BuiltMesh per body part for shape deformation and normal override resampling.</summary>
    private readonly Dictionary<string, NifMeshBuilder.BuiltMesh> _builtMeshesByBodyPart = new();

    /// <summary>Cached base meshes of the body shapes, for re-deforming without reloading.
    /// <see cref="NifMeshBuilder.BuiltMesh.BindPosePositions"/> here holds the
    /// load-time blend (so head/body neck alignment is preserved when nothing else
    /// has been applied), and the per-mesh
    /// <see cref="NifMeshBuilder.BuiltMesh.Weight0BindPosePositions"/> /
    /// <see cref="NifMeshBuilder.BuiltMesh.Weight1BindPosePositions"/> snapshots
    /// taken before <c>BlendWeightMorph</c> let <see cref="ApplyShapeDeformer"/> redo the
    /// engine-equivalent _0/_1 lerp at any NpcWeight on demand.</summary>
    private readonly Dictionary<string, NifMeshBuilder.BuiltMesh> _cachedBodyMeshes = new();

    /// <summary>Disk path of the currently-loaded body NIF, captured once per scene at
    /// install and handed to <see cref="IShapeDeformer.Prepare"/> so a host deformer can
    /// find files that sit next to it.</summary>
    private string? _cachedBodyNifDiskPath;

    private ResolvedNpcMeshPaths? _cachedMeshPaths;

    /// <summary>NPC's HairColor record (HCLR) resolved from HeadData.HairColor FormLink,
    /// in 0..1 linear floats. Null if the NPC has no HairColor set or it fails to resolve.
    /// Fallback tint only: the engine renders the NIF's baked BSLSP hairTintColor when
    /// one is present (verified empirically — editing the NIF alone changes the in-game
    /// color), so this applies just to hair-tint shapes with no baked tint. WORN
    /// hair-slot items are the exception and take
    /// <see cref="ResolvedNpcMeshPaths.WornHairSlotTintRgb"/> over their baked value —
    /// the engine doesn't tint worn armor at all, RaceMenu does.</summary>
    private (float R, float G, float B)? _npcHairColorFromRecord;

    /// <summary>The hair color the CK baked into THIS NPC's FaceGen, taken from
    /// the first non-neutral HairTint shape in the head group (the hair if the
    /// FaceGen has any, else the brows or beard — the CK writes the same color
    /// to all of them). Preferred over
    /// <see cref="ResolvedNpcMeshPaths.WornHairSlotTintRgb"/> when tinting a worn
    /// wig, so the wig matches the beard and brows by construction even where the
    /// record and the FaceGen disagree (a color record overridden later in the
    /// load order than the appearance mod's FaceGen export). Recomputed per
    /// scene; null when the FaceGen carries no informative HairTint shape.</summary>
    private (float R, float G, float B)? _faceGenHairTint;

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
        int TotalShapes,
        int LoadGeneration)
    {
        public int Installed { get; set; }

        /// <summary>What the texture visibility lock did across the install's ticks,
        /// logged once at scene commit.</summary>
        public TextureLockTally LockTally { get; } = new();
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
    /// setup. Hosts convert their own override type to <see cref="TextureOverride"/>
    /// before calling <see cref="ApplyTextureOverrides(IEnumerable{TextureOverride})"/>.</summary>
    private List<TextureOverride>? _pendingTextureOverrides;

    /// <summary>Pending shape deformation to apply after scene setup: an
    /// <see cref="ApplyShapeDeformer"/> call that arrived before the body shapes were
    /// installed. Drained by <see cref="ProcessPendingScene"/> once the scene commits;
    /// a later call supersedes.</summary>
    private (IShapeDeformer Deformer, int Weight, bool Retain)? _pendingShapeDeform;

    /// <summary>Pending neutral mesh-override set to apply after scene setup.
    /// Mirrors <see cref="_pendingTextureOverrides"/>: a call to
    /// <see cref="ApplyMeshOverrides"/> that arrives while a rebuild is in
    /// flight is queued here and drained by <see cref="ProcessPendingScene"/>
    /// once the scene commits. A later call supersedes (replace semantics).
    /// Like the other queues it waits for the newest load's scene: an older
    /// scene finishing its install meanwhile leaves it queued.</summary>
    private List<MeshOverride>? _pendingMeshOverrides;

    /// <summary>The deformer (and the weight it was applied at) the <b>current scene's</b> body shapes carry: set
    /// each time <see cref="ApplyShapeDeformer"/> actually deforms, cleared when a new scene commits (its meshes
    /// start at their base). Never stale across a reload, and not set while a deformation is only queued.</summary>
    private (IShapeDeformer Deformer, int Weight)? _currentSceneShapeDeform;

    /// <summary>The deformer and weight the current scene's body shapes carry, or null when none has been applied
    /// to this scene yet. A caller that applies a deformation and must read the result waits until this matches
    /// its request and <see cref="HasPendingShapeDeformer"/> is false.</summary>
    public (IShapeDeformer Deformer, int Weight)? CurrentSceneShapeDeform => _currentSceneShapeDeform;

    /// <summary>True while a deformation is queued for the next scene commit (<see cref="ApplyShapeDeformer"/>
    /// arrived before the scene was ready); the meshes don't carry it yet.</summary>
    public bool HasPendingShapeDeformer => _pendingShapeDeform != null;

    // Durable "last requested" neutral inputs for the software fallback preview
    // (see TryGetSceneInputsSnapshot). Unlike the _pending* fields (drained/cleared
    // once the GL scene commits) and _currentSceneShapeDeform (only set on the GL deform
    // path), these are set UNCONDITIONALLY at the apply-method entry so they survive
    // even when GL never starts and no scene ever commits. Reset per-NPC on load.
    private List<TextureOverride>? _lastRequestedTextureOverrides;
    private List<MeshOverride>? _lastRequestedMeshOverrides;
    private (IShapeDeformer Deformer, int Weight)? _lastRequestedShapeDeform;

    /// <summary>NpcIdentity.CacheKey of the NPC whose scene is currently installed
    /// in the renderer. Captured at the end of ProcessPendingScene; cleared by
    /// ClearScene. Used by LoadAsync to short-circuit reloads of the same NPC
    /// when narrow editors (e.g. a preset or texture-variant change)
    /// call LoadAsync defensively even though only a narrow downstream update
    /// is needed. Empty string when no NPC is loaded.</summary>
    private string _currentLoadedIdentityKey = "";

    /// <summary>One-shot flag forcing the next <see cref="LoadAsync"/> to bypass
    /// its same-identity short-circuit and fully rebuild. Set by
    /// <see cref="RequestReload"/> whenever the lib raises
    /// <see cref="ReloadRequested"/> because an upload-time-consumed setting
    /// (e.g. <see cref="RenderMissingTextureAsWireframe"/>,
    /// <see cref="UseBlankDetailFallback"/>) changed: the host responds by
    /// reloading the SAME NPC, so without this the short-circuit would skip the
    /// rebuild and the toggle would never take visible effect. Reset once a load
    /// proceeds past the short-circuit.</summary>
    private bool _forceRebuildNextLoad;

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

    /// <summary>Counts the rebuilding loads: <see cref="LoadAsync"/> takes the next value when it
    /// starts a real rebuild (not the same-NPC short-circuit). Each queued scene, and the install
    /// made from it, carries the generation of the load that produced it, so the commit can tell
    /// whether a newer load has started since.</summary>
    private int _loadGeneration;

    /// <summary>Generation of the load whose results are queued in _pendingScene; carried into
    /// <see cref="SceneInstallState.LoadGeneration"/> with the other _pendingLoad* fields.</summary>
    private int _pendingLoadGeneration;

    /// <summary>Stopwatch started at LoadNpcAsync entry and threaded through to
    /// ProcessPendingScene so the GL-upload checkpoint can report elapsed-from-initiation.
    /// Set under Application.Current.Dispatcher when the scene is queued; consumed
    /// (and cleared) on the render thread when the scene is installed.</summary>
    private System.Diagnostics.Stopwatch? _pendingLoadStopwatch;

    /// <summary>Pending head-only rebuild. Set by RebuildHeadOnlyAsync after the
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
    private bool _currentSceneAllowLoadOrderFallback;

    private readonly CharacterPreviewCache _previewCache;
    private IRenderThreadMarshaller _renderThread;

    /// <summary>
    /// The marshaller that reaches the thread + GL context owning this viewer's
    /// scene. Seeded from the constructor argument; settable so a host can
    /// install a per-viewer one once it knows which GL surface this instance
    /// draws into.
    ///
    /// <para><b>Why a host would replace it:</b> GL object names are
    /// per-context, and two freshly-created contexts hand out the SAME low
    /// integers for the same allocation sequence. A host that keeps more than
    /// one viewer alive at a time (e.g. several 3D-preview popups — each GLWpfControl
    /// mints its own private context) therefore needs every GL call this VM
    /// makes outside the render callback to land in ITS context, not whichever
    /// sibling rendered most recently. A marshaller that makes the owning
    /// context current before running the action supplies that guarantee;
    /// a plain dispatch-to-UI-thread marshaller does not.</para>
    /// </summary>
    public IRenderThreadMarshaller RenderThreadMarshaller
    {
        get => _renderThread;
        set => _renderThread = value ?? new InlineRenderThreadMarshaller();
    }

    /// <summary>True when this viewer defers its GL work to a host render
    /// callback rather than owning a dedicated render thread. Interactive
    /// hosts (WPF) do; the offscreen renderer and tests run inline on the
    /// thread that already holds the context.</summary>
    private bool DefersGlToRenderCallback => _renderThread is not InlineRenderThreadMarshaller;

    // ═══════════════════════════════════════════════════════════════════════
    //  PUBLIC HOST-EXTENSION SURFACE
    //  These members exist for host-side extension methods that wrap the
    //  neutral viewer with their own host-coupled workflows. The VM itself
    //  doesn't reference host types.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Fired at the end of <see cref="ProcessPendingScene"/> after
    /// the new scene is installed and any pending neutral overrides and shape
    /// deformations have drained. Hosts subscribe to drain their own queues that
    /// depend on scene-ready state (e.g. a deformation that needs
    /// <see cref="CurrentBodyNifDiskPath"/>).
    /// <para>It also fires for a scene that finished installing after a newer
    /// <see cref="LoadAsync"/> started (another NPC picked while it uploaded). That
    /// scene is superseded: nothing queued is drained into it, the rebuild stays
    /// pending and <see cref="IsLoading"/> stays true, and the queued overrides go in
    /// with the newer scene's own commit. Compare <see cref="CurrentLoadedIdentityKey"/>
    /// with the NPC you loaded to tell the two apart.</para></summary>
    public event Action? SceneCommitted;

    /// <summary>Fired after a <see cref="ApplyMeshOverrides"/> set has actually
    /// been installed into the scene. Hosts that derive UI state from the apply
    /// result (e.g. mirroring <see cref="MeshOverrideWarningDetails"/> onto an
    /// attire warning badge) must refresh here rather than on return from
    /// ApplyMeshOverrides: for interactive hosts that call is a queue, and the
    /// GL work runs one render tick later. Unlike <see cref="SceneCommitted"/>
    /// this fires for override-only changes with no scene rebuild — the attire
    /// toggle case.</summary>
    public event Action? MeshOverridesApplied;

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
    /// before the scene commits. Hosts that load files sitting next to the body
    /// NIF read this to know where to look.</summary>
    public string? CurrentBodyNifDiskPath => _cachedBodyNifDiskPath;

    /// <summary>The installed scene's body shapes as loaded, keyed by shape name: the base
    /// meshes <see cref="ApplyShapeDeformer"/> re-derives every deformation from (bind pose,
    /// _0/_1 weight endpoints, skinning). Empty before the scene commits. Read-only by
    /// contract: a host that mutates these arrays corrupts every later deformation.</summary>
    public IReadOnlyDictionary<string, NifMeshBuilder.BuiltMesh> BodyBaseMeshes => _cachedBodyMeshes;

    /// <summary>True when <see cref="ApplyShapeDeformer"/> would deform immediately: the body
    /// shapes are installed and no scene rebuild is pending. While false, an application is
    /// queued for the next scene commit.</summary>
    public bool CanDeformShapes => _cachedBodyMeshes.Count > 0 && !_sceneRebuildPending;

    /// <summary>The <see cref="BodyReplacement"/> captured by the last load, i.e. the one the installed
    /// (or installing) scene's shapes use; null when it shows the NPC's own body. Unlike
    /// <see cref="BodyReplacement"/>, which is the request for the next load.</summary>
    public BodyReplacement? SceneBodyReplacement => _sceneBodyReplacement;

    /// <summary>Opaque host object attached to this viewer. The library never reads it; it lets
    /// a host keep per-viewer state beside the viewer and lets the host's views bind to it
    /// (<c>{Binding HostData.Property}</c>). Raises <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>
    /// when replaced.</summary>
    public object? HostData { get; set; }

    /// <summary>Fired after <see cref="ApplyShapeDeformer"/> has rewritten the body shapes'
    /// CPU positions (and queued their GPU re-upload), including when the deformation threw
    /// part-way, so subscribers can refresh anything derived from vertex positions. Not fired
    /// when the deformer declined (<see cref="IShapeDeformer.Prepare"/> returned false) or the
    /// application was only queued; a queued one fires when it drains at scene commit, before
    /// <see cref="SceneCommitted"/>.</summary>
    public event Action? ShapesDeformed;

    /// <summary>Fired by <see cref="ClearScene"/> once the scene's meshes and per-scene caches
    /// are dropped, including at the start of every new scene's install. Hosts that cache data
    /// derived from the current scene's files reset it here (a GL context loss, which also drops
    /// the scene, is reported through <see cref="GlContextReset"/> instead).</summary>
    public event Action? SceneCleared;

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

    /// <summary>Warnings from the most recent <see cref="ApplyMeshOverrides"/>,
    /// covering two cases:
    /// <list type="bullet">
    ///   <item>a shape that could NOT be rendered — the override NIF didn't
    ///   resolve, or it is weighted to a bone present in neither the skeleton
    ///   nor the mesh NIF (it would collapse to the origin, so it is skipped);</item>
    ///   <item>a shape that DID render but is weighted to bones absent from the
    ///   resolved skeleton (resolved via the mesh-NIF fallback) — the loaded
    ///   skeleton is missing bones the auxiliary mesh expects, so the result can
    ///   be misaligned until a compatible skeleton mod is installed.</item>
    /// </list>
    /// Hosts surface these as a missing-asset / incompatible-skeleton warning
    /// (e.g. a render-preview warning line or a mugshot icon). Each entry
    /// is "&lt;Key&gt;: &lt;reason&gt;" so the UI can name what's wrong.
    /// Recomputed on every apply (and drained queue), so it reflects the current
    /// scene once <see cref="SceneCommitted"/> has fired.</summary>
    public IReadOnlyList<string> MeshOverrideWarnings => _meshOverrideWarnings.ConvertAll(w => w.Message);

    /// <summary>Structured view of <see cref="MeshOverrideWarnings"/> — the same
    /// entries with a <see cref="MeshOverrideWarningKind"/> so hosts can route
    /// each warning (e.g. a host may exclude <see cref="MeshOverrideWarningKind.StalePhysicsConfig"/>
    /// from its persisted missing-asset list so it never re-stales a mugshot).</summary>
    public IReadOnlyList<MeshOverrideWarning> MeshOverrideWarningDetails => _meshOverrideWarnings;
    private readonly List<MeshOverrideWarning> _meshOverrideWarnings = new();

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

    /// <summary>Portrait-quality tone-mapping toggle. When true,
    /// <see cref="GlRenderer"/> applies an ACES filmic tone-mapper plus
    /// sRGB framebuffer encoding plus a mild saturation boost at the end
    /// of the fragment shader. Hosts mutate before
    /// <see cref="LoadAsync"/> (or any subsequent render) — the value is
    /// read each frame, so toggling at runtime is effective on the next
    /// render without requiring a reload.</summary>
    public bool EnableToneMapping { get; set; } = false;

    /// <summary>Shadow-map toggle. When true,
    /// <see cref="GlRenderer"/> runs an extra depth-only pass from the
    /// key light's POV and samples the resulting shadow map with PCF
    /// in <c>basic.frag</c>. Off: legacy occlusion-free lighting.</summary>
    public bool EnableShadows { get; set; } = false;

    // --- Hair-shadow troubleshooting toggles (A/B/C) ---
    // Diagnostic knobs for the "brow ridge" bangs cast onto the forehead.
    // Surfaced in the Shader Troubleshooting UI for A/B comparison; all
    // default OFF (current behavior) and mirror to GlRenderer live. Not
    // persisted — they reset per session until a winning approach is picked.

    /// <summary>Strategy A: exclude hair from the shadow caster set.
    /// Mirrors to <see cref="GlRenderer.ExcludeHairShadowCaster"/>.</summary>
    public bool ExcludeHairShadowCaster { get; set; } = false;

    /// <summary>Strategy B (default ON): constant-bias + wide-PCF soft
    /// shadow — the shipped default; also relieves the over-dark neck under
    /// the jaw. Mirrors to <see cref="GlRenderer.SoftenShadowEdges"/>.</summary>
    public bool SoftenShadowEdges { get; set; } = true;

    /// <summary>PCF kernel step (texels) for Strategy B.
    /// Mirrors to <see cref="GlRenderer.ShadowPcfRadius"/>.</summary>
    public float ShadowPcfRadius { get; set; } = 1.5f;

    /// <summary>Strategy C: tighten the light frustum.
    /// Mirrors to <see cref="GlRenderer.TightShadowFrustum"/>.</summary>
    public bool TightShadowFrustum { get; set; } = false;

    /// <summary>Light-frustum radius (world units) for Strategy C.
    /// Mirrors to <see cref="GlRenderer.ShadowFrustumRadius"/>.</summary>
    public float ShadowFrustumRadius { get; set; } = 100f;

    /// <summary>Screen-space ambient occlusion toggle. When
    /// true, <see cref="GlRenderer"/> runs a depth pre-pass + SSAO
    /// post-process before the main passes and samples the AO texture
    /// in <c>basic.frag</c> to darken crevices.</summary>
    public bool EnableAmbientOcclusion { get; set; } = false;

    /// <summary>SSAO radius in world units.</summary>
    public float SsaoRadius { get; set; } = 4.0f;
    /// <summary>SSAO depth-comparison bias in world units.</summary>
    public float SsaoBias { get; set; } = 0.05f;
    /// <summary>SSAO power-curve exponent.</summary>
    public float SsaoIntensity { get; set; } = 1.5f;
    /// <summary>SSAO occluder-thickness rejection distance in view-space
    /// units. See <see cref="GlRenderer.SsaoThickness"/>.</summary>
    public float SsaoThickness { get; set; } = 1.5f;
    /// <summary>Max hair-to-background gap for hair to receive screen AO
    /// See <see cref="GlRenderer.SsaoHairGap"/>.</summary>
    public float SsaoHairGap { get; set; } = 0.8f;

    /// <summary>Eye catch-light toggle.</summary>
    public bool EnableEyeCatchlight { get; set; } = false;

    /// <summary>Achromatic (dielectric) skin specular toggle. When true
    /// (default), specular is added on top of the albedo*light term rather
    /// than tinted by the base color, matching NifSkope / Community Shaders.
    /// Mirrors to <see cref="GlRenderer.SpecularAchromatic"/>.</summary>
    public bool SpecularAchromatic { get; set; } = true;

    /// <summary>Game-faithful skin soft-lighting toggle. When true
    /// (default), the SSS term uses the NifSkope / Community Shaders wrap at
    /// honest material strength. Mirrors to
    /// <see cref="GlRenderer.SkinFaithfulSoftLight"/>.</summary>
    public bool SkinFaithfulSoftLight { get; set; } = true;

    /// <summary>Hair finishing toggle (default ON). When true, hair pixels skip
    /// the fresnel contour darkening and use a gentler exposure pull-down, so
    /// the brown hair midtone is not crushed by the skin-tuned finishing
    /// chain. Mirrors to <see cref="GlRenderer.TonemapHairRelief"/>.</summary>
    public bool TonemapHairRelief { get; set; } = true;

    /// <summary>Neutral-white-tint hair albedo compensation strength (default 1.0;
    /// 0 = off). Applies the sRGB->linear the gamma-space pipeline skips to
    /// near-white-tint hair only (fixes a red wig reading pink); colored-tint hair
    /// is exempt. Mirrors to <see cref="GlRenderer.HairAlbedoCompensate"/>.</summary>
    public float HairAlbedoCompensate { get; set; } = 1.0f;

    /// <summary>Toggle (default ON). When true, directional lights get a
    /// noon-sun gain (<see cref="DaylightBoostIntensity"/>) + slight warmth
    /// (ambient untouched), lifting blonde hair toward its in-game daylight
    /// look without hand-tuning the Key light. Mirrors to
    /// <see cref="GlRenderer.DaylightBoost"/>.</summary>
    public bool DaylightBoost { get; set; } = true;

    /// <summary>Directional-light gain when <see cref="DaylightBoost"/> is on.
    /// 1.0 = warmth only; higher brightens. Mirrors to
    /// <see cref="GlRenderer.DaylightBoostIntensity"/>.</summary>
    public float DaylightBoostIntensity { get; set; } = 1.1f;

    /// <summary>Toggle (default ON, needs tone-mapping). When true, a
    /// bright-pass + blur bloom glow is composited over the scene so hair
    /// highlights bleed into the soft halo the engine produces. Mirrors to
    /// <see cref="GlRenderer.EnableBloom"/>.</summary>
    public bool EnableBloom { get; set; } = true;

    /// <summary>Bloom composite gain when <see cref="EnableBloom"/> is on. 0 =
    /// no visible glow; higher = stronger. Mirrors to
    /// <see cref="GlRenderer.BloomIntensity"/>.</summary>
    public float BloomIntensity { get; set; } = 0.7f;

    /// <summary>Subsurface scattering strength multiplier. 0
    /// disables SSS contribution from the corrected pipeline; 1.0 is
    /// honest source-value SSS; higher boosts the warm skin look.</summary>
    public float SubsurfaceStrength { get; set; } = 0f;

    /// <summary>Skin-only saturation multiplier applied post-tint,
    /// pre-lighting. 1.0 is no-op (default). &gt;1 boosts chroma, restoring
    /// race-distinguishing skin character that the downstream pipeline
    /// (ACES tonemap + lighting + post-process) tends to compress toward
    /// neutral — Orc green, Redguard brown, and Imperial warmth all
    /// recover at the same setting. Gated on IsSkinShape, so hair, eyes,
    /// and brows are excluded.</summary>
    public float SkinSaturationBoost { get; set; } = 1.0f;

    /// <summary>Vignette inner radius in NDC units. The
    /// circular zone within this distance of screen center is
    /// unaffected; falloff smoothsteps from here out to the corner.
    /// Folded under <see cref="EnableToneMapping"/> in basic.frag.</summary>
    public float VignetteRadius { get; set; } = 0.7f;

    /// <summary>Vignette darkening strength. 0 = off,
    /// 1.0 = corners to black.</summary>
    public float VignetteIntensity { get; set; } = 0f;

    /// <summary>Tone-map exposure multiplier. 1.0 = neutral
    /// (the legacy look); &gt;1 brightens, &lt;1 darkens. Scales the linear
    /// color into the ACES curve. Folded under <see cref="EnableToneMapping"/>
    /// in basic.frag, so it only takes effect when tone-mapping is on.</summary>
    public float Exposure { get; set; } = 1.0f;

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
    /// (vanilla Addvar, Nordic Faces, Aia, Angeline, Bjartur, a replacer-provided Lydia).</summary>
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
    /// Hosts read this to decide between full
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

    /// <summary>Cancellation for an in-progress offscreen render. Set per-render
    /// by <c>GameWindowOffscreenRenderer</c> (which creates a fresh VM each
    /// render, so this never leaks across renders). Checked at granular points
    /// in the install/texture path — a single shape can pull half a dozen
    /// BSA-extracted, DDS-decoded textures — so a host cancel ("Cancel Mugshot
    /// Load") aborts mid-shape instead of finishing every texture. Defaults to
    /// <see cref="CancellationToken.None"/>; the live preview's long-lived VM
    /// leaves it unset and relies on <see cref="LoadAsync"/>'s own token, so
    /// these checks are no-ops there.</summary>
    public CancellationToken RenderCancellation { get; set; } = CancellationToken.None;

    /// <summary>Optional render-context-shared GL texture cache, set per-render by
    /// the offscreen renderer before <see cref="InitializeGl"/> so this VM's
    /// <see cref="GlTextureManager"/> shares uploaded textures with sibling renders
    /// instead of re-uploading them. Null for the live preview (its own context,
    /// one long-lived VM), which keeps per-VM texture ownership.</summary>
    public ResidentTextureCache? ResidentTextureCache { get; set; }

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

    /// <summary>
    /// Counterpart to <see cref="Offscreen.OffscreenRenderRequest.AllowLoadOrderFallback"/>
    /// for the live preview path: engine-order resolution for EVERY asset in
    /// the scene (base meshes and textures included), not just overrides that
    /// carry <see cref="MeshOverride.AllowLoadOrderFallback"/>. Snapshotted at
    /// <see cref="LoadAsync"/> entry and pushed to the resolver alongside
    /// <see cref="AdditionalScopes"/>. Default false.
    /// </summary>
    public bool AllowLoadOrderFallback { get; set; } = false;

    public VM_CharacterViewer(
        GameAssetResolver assetResolver,
        ICharacterViewerSettings generalSettings,
        CharacterPreviewCache previewCache,
        RenderLogGate logGate,
        IRenderLogger logger,
        IRenderThreadMarshaller? renderThread = null)
    {
        _logGate = logGate;
        _previewCache = previewCache;
        // Mesh parser is shared via the preview cache so its parsed-NIF LRU
        // survives across viewer instances (a host may dispose and recreate
        // viewers frequently).
        _meshBuilder = previewCache.MeshBuilder;
        _assetResolver = assetResolver;
        _generalSettings = generalSettings;
        _logger = logger;
        _renderThread = renderThread ?? new InlineRenderThreadMarshaller();

        // Forward the renderer's one-shot GL-state dump (FRAMEBUFFER_SRGB,
        // viewport, color-attachment encoding, ...) to the same logger pipeline
        // the rest of the VM uses. The renderer itself has no logger reference,
        // so we wire its DiagnosticLog delegate to _logger here. A host logger adapter's
        // AsyncLocal sink routes the resulting line into the active capture file
        // (mugshot or live-preview), matching how Built shape / [Skinning] / etc.
        // already reach the same file from NifMeshBuilder and friends.
        Renderer.DiagnosticLog = msg => _logger.LogMessage(msg);

        // Verbose-log state lives in the host's ICharacterViewerSettings as the single source of truth.
        // Every live VM_CharacterViewer instance reactively syncs its local VerboseLog
        // property (for the toolbar checkbox) and the shared _logGate (which helpers
        // GameAssetResolver / NifMeshBuilder / CharacterPreviewCache
        // and host-side helpers consult) from the settings value.
        //
        // Previously each viewer read settings once at ctor and owned a stale local
        // copy. With multiple simultaneous viewer instances (several host menus and editors each
        // embedding a viewer), a toggle
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

    /// <summary>Host-settable busy flag for work that happens OUTSIDE the viewer's own
    /// load pipeline but should still show the viewport busy overlay — e.g. a host choosing
    /// what to show on a background thread before calling LoadAsync. The host sets it true before its work and false
    /// when done (typically in a finally); <see cref="IsLoading"/> takes over coverage for
    /// the load→scene-commit window, so OR-ing the two keeps the overlay up continuously.</summary>
    public bool IsHostBusy { get; set; }

    /// <summary>Overlay caption shown while <see cref="IsHostBusy"/> is the active busy
    /// source. The viewer's own loads always show "Loading...".</summary>
    public string HostBusyMessage { get; set; } = "Working...";

    /// <summary>Drives the viewport busy overlay: the viewer's own load pipeline
    /// (<see cref="IsLoading"/>) or host-flagged external work (<see cref="IsHostBusy"/>).</summary>
    public bool IsBusyOverlayVisible => IsLoading || IsHostBusy;

    /// <summary>Caption for the busy overlay. A live load reads "Loading..." even when the
    /// host flag is also up, since by then the host's pre-work has handed off to the loader.</summary>
    public string BusyOverlayText => IsLoading ? "Loading..." : HostBusyMessage;

    public int NpcWeight { get; set; } = 50;

    /// <summary>The NPC record's Height field, a uniform full-model scale
    /// multiplier (1.0 = default). Refreshed whenever a new NPC is loaded;
    /// acts as the fallback when no per-assignment override is set.</summary>
    public float NpcBaseHeight { get; set; } = 1.0f;

    /// <summary>Host-supplied Height override. When non-null, replaces <see cref="NpcBaseHeight"/>
    /// as the applied scale. Null means "fall back to the NPC record".</summary>
    public float? HeightOverride { get; set; }

    /// <summary>Host-chosen body type to show instead of the NPC's own: the body NIF and the matching
    /// hands / feet / tail meshes it lists, plus where their skin comes from (see
    /// <see cref="SkyRender.Character.BodyReplacement"/>). Applied on every subsequent
    /// <see cref="LoadAsync"/>; null = the NPC's own meshes and skin. Sticky like
    /// <see cref="HeightOverride"/>; a changed value defeats the same-NPC short-circuit. Replacement meshes
    /// have no sibling .tri or _0/_1 pair; any deformation comes from the host's
    /// <see cref="IShapeDeformer"/>.</summary>
    public BodyReplacement? BodyReplacement { get; set; }

    /// <summary>The <see cref="BodyReplacement"/> captured by the last <see cref="LoadAsync"/>, read when
    /// that load's shapes install (the public property may change before then).</summary>
    private BodyReplacement? _sceneBodyReplacement;

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

    public double AmbientIntensity { get; set; } = LightingPresets.DefaultLayout.Ambient;

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

    public double KeyLightIntensity   { get; set; } = LightingPresets.DefaultLayout.KeyIntensity;
    public double KeyLightAzimuth     { get; set; } = LightingPresets.DefaultLayout.KeyAzimuth;
    public double KeyLightElevation   { get; set; } = LightingPresets.DefaultLayout.KeyElevation;
    public MediaColor KeyLightColor   { get; set; } = MediaColor.FromRgb(255, 245, 224); // warm key
    public bool KeyLightEnabled       { get; set; } = true;

    public double FillLightIntensity  { get; set; } = LightingPresets.DefaultLayout.FillIntensity;
    public double FillLightAzimuth    { get; set; } = LightingPresets.DefaultLayout.FillAzimuth;
    public double FillLightElevation  { get; set; } = LightingPresets.DefaultLayout.FillElevation;
    public MediaColor FillLightColor  { get; set; } = MediaColor.FromRgb(230, 237, 255);
    public bool FillLightEnabled      { get; set; } = true;

    public double RimLightIntensity   { get; set; } = LightingPresets.DefaultLayout.RimIntensity;
    public double RimLightAzimuth     { get; set; } = LightingPresets.DefaultLayout.RimAzimuth;
    public double RimLightElevation   { get; set; } = LightingPresets.DefaultLayout.RimElevation;
    public MediaColor RimLightColor   { get; set; } = MediaColor.FromRgb(255, 243, 217);
    public bool RimLightEnabled       { get; set; } = true;

    /// <summary>Master toggle for the per-light control UI and the 3D arrow gizmos.
    /// When false, the lighting controls are hidden and arrows are not drawn.</summary>
    public bool ShowLightControls { get; set; } = false;

    /// <summary>When true, every currently-loaded mesh draws its triangle edges
    /// as a wireframe overlay. Toggled from the host's viewer toolbar.</summary>
    public bool ShowWireframe { get; set; } = false;

    /// <summary>
    /// Master toggle for the viewer toolbar's right-aligned "Compare" button. Hidden by
    /// default and flipped on only by hosts that supply a <see cref="CompareCommand"/> —
    /// typically the host's preview panels that can open a side-by-side comparison. Kept separate from the command itself so a host can gate
    /// visibility independently of whether the command happens to be executable.
    /// </summary>
    public bool ShowCompareButton { get; set; } = false;

    /// <summary>
    /// Invoked by the toolbar's "Compare" button. Deliberately typed as the framework's
    /// <see cref="ICommand"/> rather than a host type: this VM lives in the rendering tier
    /// and must not reference host models. A host points it at the command that opens
    /// its comparison window, seeded from the host menu's current state. Null when no host wired one up.
    /// </summary>
    public ICommand? CompareCommand { get; set; }

    /// <summary>
    /// True when the Compare button should actually show: the host enabled it, wired a
    /// command, and a live GL viewport exists. The software fallback preview
    /// (<see cref="RenderingUnavailable"/>) can't host the side-by-side viewers the button
    /// opens, so it hides there.
    /// PropertyChanged.Fody re-raises this whenever any input changes.
    /// </summary>
    public bool CompareButtonAvailable =>
        ShowCompareButton && CompareCommand != null && !RenderingUnavailable;

    /// <summary>
    /// Gates the viewer's informational log output. Errors (<c>LogError</c>) are never gated --
    /// only the noisy per-frame / per-load diagnostics flowing through <see cref="LogVerbose"/>.
    /// Off by default so selecting a preset doesn't bury the host's own log in
    /// viewer chatter. Toggle from the viewer toolbar when debugging mesh/NPC/lighting issues.
    /// </summary>
    public bool VerboseLog { get; set; } = false;

    /// <summary>True when verbose diagnostics should be emitted: either the user's
    /// <see cref="VerboseLog"/> toolbar toggle, or the shared
    /// <see cref="RenderLogGate"/> a host forces on for a capture session
    /// (a host's per-render RenderLogs files). The helpers (NifMeshBuilder,
    /// GameAssetResolver, ...) already consult the gate; without this the VM's own
    /// load/apply trace (mesh overrides, AlternateTextures matching, texture
    /// routing) was missing from capture-session logs unless the user also had the
    /// verbose checkbox on.</summary>
    private bool VerboseActive => VerboseLog || (_logGate != null && _logGate.Verbose);

    /// <summary>Routes an informational line to <see cref="_logger"/> only when
    /// <see cref="VerboseActive"/> is on. Keeps error paths (which call <c>_logger.LogError</c>
    /// directly) visible at all times.</summary>
    private void LogVerbose(string message)
    {
        if (VerboseActive) _logger?.LogMessage(message);
    }

    /// <summary>Public verbose-gated log used by the host's viewer control for
    /// view-lifecycle diagnostics (instance-ids, Loaded/Unloaded, first-render details).
    /// Kept on the VM so multiple UC instances sharing this VM all route through the
    /// same <see cref="VerboseLog"/> toggle. Does nothing when <see cref="VerboseLog"/>
    /// is off, so the messages are free for end users.</summary>
    public void LogViewerDiagnostic(string message)
    {
        if (VerboseLog) _logger?.LogMessage(message);
    }

    /// <summary>
    /// True once the embedded GL viewport has permanently failed to start because the host's
    /// OpenGL driver lacks the <c>WGL_NV_DX_interop</c> extension GLWpfControl needs to share
    /// its surface with WPF's D3D compositor (expected under Wine/Proton, virtual machines, and
    /// remote desktop). When true no render loop will ever run, so <see cref="LoadAsync"/> and
    /// <see cref="LoadByIdentityAsync"/> short-circuit — a preview request can't leave the scene
    /// queue stuck mid-install or <see cref="IsLoading"/> stuck true — and dependent UI can
    /// disable viewer-only affordances instead of driving a dead panel. Set by the view
    /// (the host's viewer control) via <see cref="NotifyRenderingUnavailable"/>.
    /// </summary>
    public bool RenderingUnavailable { get; private set; }

    /// <summary>
    /// Test-only global override (set by a host test hook). When true, EVERY viewer VM
    /// behaves as if <see cref="RenderingUnavailable"/> — its loads capture inputs for the
    /// software fallback instead of building a GL scene it can never commit. It is <b>static</b>
    /// on purpose: the editor re-creates the viewer VM when the shortcut fires (a running
    /// GLWpfControl keeps its committed scene, so the toggle spins up a fresh VM), and without a
    /// process-wide flag that new VM would take the normal GL path and hang at "setting up scene".
    /// Real Wine/VM/RDP never needs this — GL startup fails on each control instance
    /// independently, so <see cref="RenderingUnavailable"/> is set per-VM there. Never set in
    /// production; a host leaves it false.
    /// </summary>
    public static bool ForceRenderingUnavailableForTesting { get; set; }

    /// <summary>
    /// Set by <see cref="Offscreen.GameWindowOffscreenRenderer"/> on the throwaway VM it builds
    /// per render. Such a VM owns a real (offscreen) GL context and MUST build the scene, so it
    /// opts out of the <see cref="ForceRenderingUnavailableForTesting"/> capture-and-skip path in
    /// <see cref="LoadAsync"/> — otherwise the fallback preview would render nothing. Default
    /// false: only the offscreen renderer sets it; live viewer VMs leave it alone.
    /// </summary>
    public bool IsOffscreenRenderInstance { get; set; }

    /// <summary>
    /// Records a one-time, non-fatal GL-startup failure reported by the host's viewer control
    /// when <c>GLWpfControl.Start()</c> throws: latches <see cref="RenderingUnavailable"/>,
    /// updates <see cref="StatusText"/>, and logs the cause through the always-on message channel
    /// (NOT gated by <see cref="VerboseLog"/>, unlike <see cref="LogViewerDiagnostic"/>) so the
    /// reason is visible in a normal log. Deliberately NOT the error channel: a host may
    /// surface error logs prominently, but this state is non-fatal, already
    /// surfaced in-place by the viewer's own overlay/status text, and re-latched on every fresh
    /// viewer VM (e.g. per-scene VMs as the fallback preview follows scene changes).
    /// Idempotent — only the first call per VM logs.
    /// </summary>
    public void NotifyRenderingUnavailable(string reason)
    {
        if (RenderingUnavailable) return;
        RenderingUnavailable = true;
        StatusText = "3D preview unavailable on this system";
        _logger?.LogMessage("CharacterViewer: 3D preview unavailable — " + reason);
    }

    /// <summary>
    /// Raised whenever the retained scene inputs change (NPC load, texture / mesh
    /// overrides, morph). A host's software fallback preview subscribes to this to
    /// re-render through the offscreen renderer. Lighting / background changes flow
    /// through the normal <see cref="INotifyPropertyChanged"/> surface instead, so
    /// this covers only the non-property inputs. No subscribers in the normal (live GL)
    /// path, so raising it there is a cheap no-op.
    /// </summary>
    public event Action? SceneInputsChanged;

    private void RaiseSceneInputsChanged()
    {
        LogViewerDiagnostic("VM #" + GetHashCode().ToString("X")
            + " RaiseSceneInputsChanged (subscribers=" + (SceneInputsChanged?.GetInvocationList().Length ?? 0) + ")");
        SceneInputsChanged?.Invoke();
    }

    /// <summary>
    /// Returns an immutable snapshot of the neutral scene inputs currently retained
    /// (mesh paths + head override + texture / mesh overrides + morph + lighting +
    /// background + scoping), or <c>null</c> when no NPC has been loaded yet. Used by the
    /// software fallback preview to build <see cref="Offscreen.OffscreenRenderRequest"/>s;
    /// carries everything a request needs except the per-render size, camera, and
    /// cancellation token. GL-free — safe to read whether or not the GL context started.
    /// </summary>
    public SceneInputsSnapshot? TryGetSceneInputsSnapshot()
    {
        var paths = _cachedMeshPaths;
        if (paths == null) return null;

        return new SceneInputsSnapshot(
            MeshPaths: paths,
            OverrideHeadMeshAbsolutePath: _currentHeadMeshOverride,
            TextureOverrides: _lastRequestedTextureOverrides,
            MeshOverrides: _lastRequestedMeshOverrides,
            ShapeDeformer: _lastRequestedShapeDeform?.Deformer,
            ShapeDeformWeight: _lastRequestedShapeDeform?.Weight ?? 50,
            Lighting: SelectedLightingLayout,
            Colors: SelectedLightingColorScheme,
            BackgroundRgb: (BackgroundColor.R, BackgroundColor.G, BackgroundColor.B),
            AdditionalScopes: AdditionalScopes,
            AdditionalDataFolders: AdditionalDataFolders,
            VanillaLooseOverridesBsa: VanillaLooseOverridesBsa,
            VanillaLooseOverridesModLoose: VanillaLooseOverridesModLoose,
            AllowLoadOrderFallback: AllowLoadOrderFallback);
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

    public IReadOnlyList<LightingLayout> LightingLayouts { get; private set; } =
        LightingPresets.BuiltInLayouts;

    public IReadOnlyList<LightingColorScheme> LightingColorSchemes { get; private set; } =
        LightingPresets.BuiltInColorSchemes;

    public LightingLayout SelectedLightingLayout { get; set; } =
        LightingPresets.DefaultLayout;

    public LightingColorScheme SelectedLightingColorScheme { get; set; } =
        LightingPresets.DefaultColorScheme;

    public ICommand SaveLayoutPresetCommand { get; private set; } = null!;
    public ICommand SaveColorSchemePresetCommand { get; private set; } = null!;
    public ICommand DeleteSelectedLayoutCommand { get; private set; } = null!;
    public ICommand DeleteSelectedColorSchemeCommand { get; private set; } = null!;

    /// <summary>Write-back helper for the persisted render-pipeline settings
    /// (<see cref="InitializeLightingState"/>'s persistence block). No-ops on the
    /// offscreen render instance — that VM receives transient per-request values
    /// from an <see cref="Offscreen.OffscreenRenderRequest"/> that must never be
    /// persisted — and skips the write when the target already equals
    /// <paramref name="value"/> so settings aren't needlessly marked dirty.</summary>
    private void PersistViewerSetting<T>(Func<T> get, Action<T> set, T value)
    {
        if (IsOffscreenRenderInstance) return;
        if (!EqualityComparer<T>.Default.Equals(get(), value)) set(value);
    }

    private void InitializeLightingState()
    {
        // Rebuild the combined (built-in + user) preset lists.
        var layouts = new List<LightingLayout>(LightingPresets.BuiltInLayouts);
        layouts.AddRange(_generalSettings.UserLightingLayouts);
        LightingLayouts = layouts;

        var schemes = new List<LightingColorScheme>(LightingPresets.BuiltInColorSchemes);
        schemes.AddRange(_generalSettings.UserLightingColorSchemes);
        LightingColorSchemes = schemes;

        // Resolve persisted selections (empty / unknown names fall back to defaults).
        SelectedLightingLayout = LightingPresets.FindLayoutOrDefault(
            _generalSettings.CharacterViewerLightingLayout, _generalSettings.UserLightingLayouts);
        SelectedLightingColorScheme = LightingPresets.FindColorSchemeOrDefault(
            _generalSettings.CharacterViewerLightingColorScheme, _generalSettings.UserLightingColorSchemes);

        // Seed per-light fields from the resolved layout/scheme before any UI binding
        // runs, so the editor panel opens with values that match the picked preset.
        ApplyPresetToFields(SelectedLightingLayout, SelectedLightingColorScheme);

        // Seed the persisted render-pipeline settings from the host before their
        // renderer-mirror subscriptions are wired below, so the eager (no-Skip)
        // initial emission pushes the persisted value to the renderer and the
        // toolbar controls open in their saved state. The dedicated write-back
        // block further down (Skip(1)) then persists any user edit across sessions.
        // Shader-troubleshooting debug operators are deliberately session-scoped
        // (not persisted).
        RenderMissingTextureAsWireframe = _generalSettings.CharacterViewerRenderMissingTextureAsWireframe;
        EnableToneMapping = _generalSettings.CharacterViewerEnableToneMapping;
        EnableShadows = _generalSettings.CharacterViewerEnableShadows;
        // Hair-shadow "brow ridge" mitigations (A/B/C). B (soften) ships on.
        ExcludeHairShadowCaster = _generalSettings.CharacterViewerExcludeHairShadowCaster;
        SoftenShadowEdges = _generalSettings.CharacterViewerSoftenShadowEdges;
        ShadowPcfRadius = _generalSettings.CharacterViewerShadowPcfRadius;
        TightShadowFrustum = _generalSettings.CharacterViewerTightShadowFrustum;
        ShadowFrustumRadius = _generalSettings.CharacterViewerShadowFrustumRadius;
        EnableAmbientOcclusion = _generalSettings.CharacterViewerEnableAmbientOcclusion;
        SsaoRadius = _generalSettings.CharacterViewerSsaoRadius;
        SsaoBias = _generalSettings.CharacterViewerSsaoBias;
        SsaoIntensity = _generalSettings.CharacterViewerSsaoIntensity;
        SsaoThickness = _generalSettings.CharacterViewerSsaoThickness;
        SsaoHairGap = _generalSettings.CharacterViewerSsaoHairGap;
        EnableEyeCatchlight = _generalSettings.CharacterViewerEnableEyeCatchlight;
        SubsurfaceStrength = _generalSettings.CharacterViewerSubsurfaceStrength;
        VignetteRadius = _generalSettings.CharacterViewerVignetteRadius;
        VignetteIntensity = _generalSettings.CharacterViewerVignetteIntensity;
        SkinSaturationBoost = _generalSettings.CharacterViewerSkinSaturationBoost;
        Exposure = _generalSettings.CharacterViewerExposure;
        TonemapHairRelief = _generalSettings.CharacterViewerTonemapHairRelief;
        HairAlbedoCompensate = _generalSettings.CharacterViewerHairAlbedoCompensate;
        DaylightBoost = _generalSettings.CharacterViewerDaylightBoost;
        DaylightBoostIntensity = _generalSettings.CharacterViewerDaylightBoostIntensity;
        EnableBloom = _generalSettings.CharacterViewerEnableBloom;
        BloomIntensity = _generalSettings.CharacterViewerBloomIntensity;

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
        this.WhenAnyValue(x => x.ExcludeHairShadowCaster)
            .Subscribe(v => Renderer.ExcludeHairShadowCaster = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SoftenShadowEdges)
            .Subscribe(v => Renderer.SoftenShadowEdges = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.ShadowPcfRadius)
            .Subscribe(v => Renderer.ShadowPcfRadius = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.TightShadowFrustum)
            .Subscribe(v => Renderer.TightShadowFrustum = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.ShadowFrustumRadius)
            .Subscribe(v => Renderer.ShadowFrustumRadius = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.EnableAmbientOcclusion)
            .Subscribe(v => Renderer.EnableAmbientOcclusion = v)
            .DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SsaoRadius)
            .Subscribe(v => Renderer.SsaoRadius = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SsaoBias)
            .Subscribe(v => Renderer.SsaoBias = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SsaoIntensity)
            .Subscribe(v => Renderer.SsaoIntensity = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SsaoThickness)
            .Subscribe(v => Renderer.SsaoThickness = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SsaoHairGap)
            .Subscribe(v => Renderer.SsaoHairGap = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.EnableEyeCatchlight)
            .Subscribe(v => Renderer.EnableEyeCatchlight = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SpecularAchromatic)
            .Subscribe(v => Renderer.SpecularAchromatic = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SkinFaithfulSoftLight)
            .Subscribe(v => Renderer.SkinFaithfulSoftLight = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.TonemapHairRelief)
            .Subscribe(v => Renderer.TonemapHairRelief = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.HairAlbedoCompensate)
            .Subscribe(v => Renderer.HairAlbedoCompensate = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.DaylightBoost)
            .Subscribe(v => Renderer.DaylightBoost = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.DaylightBoostIntensity)
            .Subscribe(v => Renderer.DaylightBoostIntensity = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.EnableBloom)
            .Subscribe(v => Renderer.EnableBloom = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.BloomIntensity)
            .Subscribe(v => Renderer.BloomIntensity = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SubsurfaceStrength)
            .Subscribe(v => Renderer.SubsurfaceStrength = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SkinSaturationBoost)
            .Subscribe(v => Renderer.SkinSaturationBoost = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.VignetteRadius)
            .Subscribe(v => Renderer.VignetteRadius = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.VignetteIntensity)
            .Subscribe(v => Renderer.VignetteIntensity = v).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.Exposure)
            .Subscribe(v => Renderer.Exposure = v).DisposeWith(_disposables);
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
            .Subscribe(_ => RequestReload())
            .DisposeWith(_disposables);

        // RenderMissingTextureAsWireframe is consumed during ApplyMaterial
        // (mesh-upload time), so toggling it at runtime needs the host to
        // re-load the current NPC for the change to take effect on
        // already-loaded shapes. Skip(1) so the initial value emission
        // doesn't fire a reload before any host has wired the handler.
        this.WhenAnyValue(x => x.RenderMissingTextureAsWireframe)
            .Skip(1)
            .Subscribe(_ => RequestReload())
            .DisposeWith(_disposables);

        // ── Persist render-pipeline edits back to host settings ──────────────
        // Skip(1) so the construction-time seed above doesn't write straight back;
        // each writer no-ops when the value is unchanged. These write to the same
        // host-owned settings the seed read from, so the choices round-trip across
        // sessions (the host persists them to disk on save). The renderer-mirror
        // subscriptions above stay the source of truth for pushing values to
        // GlRenderer; this block only handles persistence.
        //
        // Gated on !IsOffscreenRenderInstance: the offscreen render VM receives its
        // values from an OffscreenRenderRequest (see GameWindowOffscreenRenderer)
        // and must never write those transient per-render values back into the
        // shared host settings. IsOffscreenRenderInstance is set right after
        // construction, before any request value is applied, so the guard (read at
        // callback time) is already true by the time these fire on that instance.
        this.WhenAnyValue(x => x.RenderMissingTextureAsWireframe).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerRenderMissingTextureAsWireframe, x => _generalSettings.CharacterViewerRenderMissingTextureAsWireframe = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.EnableToneMapping).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerEnableToneMapping, x => _generalSettings.CharacterViewerEnableToneMapping = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.EnableShadows).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerEnableShadows, x => _generalSettings.CharacterViewerEnableShadows = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.ExcludeHairShadowCaster).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerExcludeHairShadowCaster, x => _generalSettings.CharacterViewerExcludeHairShadowCaster = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SoftenShadowEdges).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerSoftenShadowEdges, x => _generalSettings.CharacterViewerSoftenShadowEdges = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.ShadowPcfRadius).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerShadowPcfRadius, x => _generalSettings.CharacterViewerShadowPcfRadius = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.TightShadowFrustum).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerTightShadowFrustum, x => _generalSettings.CharacterViewerTightShadowFrustum = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.ShadowFrustumRadius).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerShadowFrustumRadius, x => _generalSettings.CharacterViewerShadowFrustumRadius = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.EnableAmbientOcclusion).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerEnableAmbientOcclusion, x => _generalSettings.CharacterViewerEnableAmbientOcclusion = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SsaoRadius).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerSsaoRadius, x => _generalSettings.CharacterViewerSsaoRadius = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SsaoBias).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerSsaoBias, x => _generalSettings.CharacterViewerSsaoBias = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SsaoIntensity).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerSsaoIntensity, x => _generalSettings.CharacterViewerSsaoIntensity = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SsaoThickness).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerSsaoThickness, x => _generalSettings.CharacterViewerSsaoThickness = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SsaoHairGap).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerSsaoHairGap, x => _generalSettings.CharacterViewerSsaoHairGap = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.EnableEyeCatchlight).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerEnableEyeCatchlight, x => _generalSettings.CharacterViewerEnableEyeCatchlight = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SubsurfaceStrength).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerSubsurfaceStrength, x => _generalSettings.CharacterViewerSubsurfaceStrength = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.VignetteRadius).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerVignetteRadius, x => _generalSettings.CharacterViewerVignetteRadius = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.VignetteIntensity).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerVignetteIntensity, x => _generalSettings.CharacterViewerVignetteIntensity = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.SkinSaturationBoost).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerSkinSaturationBoost, x => _generalSettings.CharacterViewerSkinSaturationBoost = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.Exposure).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerExposure, x => _generalSettings.CharacterViewerExposure = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.TonemapHairRelief).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerTonemapHairRelief, x => _generalSettings.CharacterViewerTonemapHairRelief = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.HairAlbedoCompensate).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerHairAlbedoCompensate, x => _generalSettings.CharacterViewerHairAlbedoCompensate = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.DaylightBoost).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerDaylightBoost, x => _generalSettings.CharacterViewerDaylightBoost = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.DaylightBoostIntensity).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerDaylightBoostIntensity, x => _generalSettings.CharacterViewerDaylightBoostIntensity = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.EnableBloom).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerEnableBloom, x => _generalSettings.CharacterViewerEnableBloom = x, v)).DisposeWith(_disposables);
        this.WhenAnyValue(x => x.BloomIntensity).Skip(1).Subscribe(v =>
            PersistViewerSetting(() => _generalSettings.CharacterViewerBloomIntensity, x => _generalSettings.CharacterViewerBloomIntensity = x, v)).DisposeWith(_disposables);

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
    }

    /// <summary>Normalizes the per-light fields from a layout+scheme, suppressing
    /// intermediate renderer pushes so the combined state is sent once at the end.</summary>
    private void ApplyPresetToFields(LightingLayout layout,
        LightingColorScheme colors)
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
        foreach (var b in LightingPresets.BuiltInLayouts)
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

        var preset = new LightingLayout
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

        foreach (var b in LightingPresets.BuiltInColorSchemes)
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

        var scheme = new LightingColorScheme
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
        SelectedLightingLayout = LightingPresets.DefaultLayout;
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
        SelectedLightingColorScheme = LightingPresets.DefaultColorScheme;
    }

    private void RebuildLayoutList()
    {
        var layouts = new List<LightingLayout>(LightingPresets.BuiltInLayouts);
        layouts.AddRange(_generalSettings.UserLightingLayouts);
        LightingLayouts = layouts;
    }

    private void RebuildColorSchemeList()
    {
        var schemes = new List<LightingColorScheme>(LightingPresets.BuiltInColorSchemes);
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
    /// independent of a host's per-capture logging toggle.
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
            $"Vignette(R={VignetteRadius:F2} I={VignetteIntensity:F2}), " +
            $"Exposure={Exposure:F2}");
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
    /// Emits via <c>_logger.LogMessage</c> directly so the AsyncLocal
    /// sink routes the lines to the active capture file.
    /// <para>Only the offscreen mugshot pathway reliably catches these
    /// lines, because the live-preview host scopes its
    /// log capture to the span of <c>await Viewer.LoadAsync(...)</c>
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
    //  TEXTURE VISIBILITY LOCK
    //
    //  The right-click menu's per-slot texture toggles live on each GlMesh, so
    //  they are lost whenever a load builds new meshes — which happens whenever
    //  a host reloads, e.g. on every weight change. The lock remembers the choices at the viewer level and
    //  re-applies them to every mesh created afterwards. Session-only by
    //  design: an invisible, persisted "textures off" would mislead later.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>The remembered choices; see <see cref="TextureVisibilityLock"/>.</summary>
    private readonly TextureVisibilityLock _textureLock = new();

    /// <summary>A guest overlay's Translucent style draws its flat overlay colour through the
    /// Tint Color toggle, so the lock never sets that slot on a guest mesh: hiding it would
    /// turn the orange overlay grey.</summary>
    private const TextureSlots GuestLockableTextureSlots = TextureSlots.All & ~TextureSlots.TintColor;

    /// <summary>True while "Lock Texture Visibility" is on: the per-slot texture choices are
    /// remembered and re-applied to every mesh this viewer creates — NPC loads, weight
    /// changes, head-only rebuilds, mesh overrides, guest overlays. Session-only; change it
    /// through <see cref="SetTextureVisibilityLocked"/>.</summary>
    public bool IsTextureVisibilityLocked { get; private set; }

    /// <summary>True while the lock is on AND remembers at least one slot as hidden. Drives the
    /// viewport's "Textures hidden (locked)" badge: review sessions can run long, and it must
    /// stay obvious that every NPC loaded is being drawn with textures off.</summary>
    public bool TexturesHiddenByLock { get; private set; }

    /// <summary>
    /// Engages or releases the texture visibility lock. Engaging remembers the choices on
    /// screen now (<see cref="TextureVisibilityLock.Capture"/>) and drops anything an earlier
    /// lock held, so it can't resurface. Releasing only stops the re-apply: what is shown stays
    /// as it is — turning textures back on is <see cref="ResetAllTextures"/>.
    /// </summary>
    public void SetTextureVisibilityLocked(bool locked)
    {
        if (locked == IsTextureVisibilityLocked) return;
        IsTextureVisibilityLocked = locked;
        if (locked) _textureLock.Capture(PrimarySceneMeshes());
        else _textureLock.Clear();
        RefreshTexturesHiddenByLock();
        LogVerbose("CharacterViewer: texture visibility lock " + (locked
            ? "engaged; remembered hidden slots: " + DescribeTextureSlots(_textureLock.HiddenSlots)
            : "released"));
    }

    /// <summary>Switches <paramref name="slots"/> on or off on one mesh — the right-click
    /// menu's per-slot checkboxes. While locked the choice is remembered too, so the lock
    /// follows the latest choice rather than freezing the one it engaged with. A guest overlay
    /// mesh changes on its own: the lock only learns from the scene being edited.</summary>
    public void SetTextureSlotEnabled(GlMesh mesh, TextureSlots slots, bool enabled)
    {
        mesh.SetTextureSlotsEnabled(slots, enabled);
        if (!IsTextureVisibilityLocked || _guestMeshes.Contains(mesh)) return;
        _textureLock.Record(mesh, slots, enabled);
        RefreshTexturesHiddenByLock();
    }

    /// <summary>Switches off every texture slot each mesh in the viewer has (a guest overlay
    /// keeps its Tint Color). While locked this is remembered as "hide everything", which
    /// also covers parts a later NPC has and this one doesn't.</summary>
    public void HideAllTextures()
    {
        foreach (var mesh in Renderer.Meshes)
            mesh.SetTextureSlotsEnabled(mesh.PresentTextureSlots() & LockableTextureSlots(mesh), false);
        if (!IsTextureVisibilityLocked) return;
        _textureLock.RecordAllHidden(PrimarySceneMeshes());
        RefreshTexturesHiddenByLock();
    }

    /// <summary>Switches every texture slot back on for every mesh. While locked, the
    /// remembered state returns to all-on as well, so textures stay restored across the next
    /// load; the lock itself stays engaged.</summary>
    public void ResetAllTextures()
    {
        foreach (var mesh in Renderer.Meshes)
            mesh.SetTextureSlotsEnabled(TextureSlots.All, true);
        _textureLock.Clear();
        RefreshTexturesHiddenByLock();
    }

    private void RefreshTexturesHiddenByLock() =>
        TexturesHiddenByLock = IsTextureVisibilityLocked && _textureLock.HiddenSlots != TextureSlots.None;

    /// <summary>The meshes the lock learns from: all but a guest overlay's, which share shape
    /// names with the primary model and would otherwise overwrite its choices.</summary>
    private IEnumerable<GlMesh> PrimarySceneMeshes() =>
        Renderer.Meshes.Where(m => !_guestMeshes.Contains(m));

    private TextureSlots LockableTextureSlots(GlMesh mesh) =>
        _guestMeshes.Contains(mesh) ? GuestLockableTextureSlots : TextureSlots.All;

    /// <summary>What the lock did to one batch of new meshes, for the verbose log line.</summary>
    private sealed class TextureLockTally
    {
        public int Meshes;
        public TextureSlots Hidden;
        public readonly List<string> Unmatched = new();
    }

    /// <summary>
    /// Re-applies the locked choices to a mesh about to join the scene, limited to
    /// <paramref name="limitTo"/>. Called at every <c>Renderer.AddMesh</c> site once the
    /// mesh's textures, <c>BodyPart</c> and shader flags are set, so it is drawn with the
    /// remembered choice from its first frame; a sweep at scene commit would show the sliced
    /// install's early shapes textured for a few frames first. No-op while unlocked.
    /// </summary>
    private void ApplyTextureVisibilityLock(GlMesh mesh, TextureLockTally tally,
        TextureSlots limitTo = TextureSlots.All)
    {
        if (!IsTextureVisibilityLocked) return;
        limitTo &= LockableTextureSlots(mesh);
        if (limitTo == TextureSlots.None) return;

        var applied = _textureLock.Apply(mesh, limitTo);
        tally.Meshes++;
        tally.Hidden |= applied & mesh.DisabledTextureSlots();
        if (applied == TextureSlots.None)
            tally.Unmatched.Add("'" + mesh.ShapeName + "' (" + mesh.BodyPart + "/" +
                TextureVisibilityLock.ShapeKind(mesh) + ")");
    }

    /// <summary>One verbose line per batch the lock re-applied to: mesh count, slots hidden,
    /// and any mesh no remembered choice reached. Cheap to have when "the lock didn't hold"
    /// gets reported.</summary>
    private void LogTextureLockTally(TextureLockTally tally, string context)
    {
        if (tally.Meshes == 0) return;
        LogVerbose("CharacterViewer: texture visibility lock re-applied to " + tally.Meshes +
            " mesh(es) (" + context + "); hidden: " + DescribeTextureSlots(tally.Hidden) +
            (tally.Unmatched.Count > 0
                ? "; no remembered choice for " + string.Join(", ", tally.Unmatched)
                : ""));
    }

    private static string DescribeTextureSlots(TextureSlots slots) =>
        slots == TextureSlots.None ? "none" : slots.ToString();

    // ═══════════════════════════════════════════════════════════════════════
    //  SECTION CLIP PLANE  (Clip F / Clip B / V)
    // ═══════════════════════════════════════════════════════════════════════
    //
    // A single world-space plane, always perpendicular to one of the three world
    // axes, that hides either the half of the model in front of it or the half
    // behind it. The axis is chosen ONCE, when the user arms a clip, as whichever
    // world axis the camera is most nearly looking down — so a front-on view gives
    // the XY plane, a side view YZ, a top-down view XZ — and then stays LOCKED while
    // the camera orbits. Locked rather than live-resolved deliberately: re-picking it
    // per frame makes the cut snap to a different axis mid-orbit, and the depth the
    // user scrolled to stops meaning what it meant a moment earlier. Re-arm (toggle
    // off, toggle on) to re-detect against the new view.
    //
    // The plane composed here is handed to GlRenderer.ClipPlane; the renderer does
    // not re-derive it. See GlRenderer's SECTION CLIP PLANE region.

    /// <summary>Which half-space the clip discards, or <see cref="ClipPlaneMode.None"/>.
    /// Fody calls <see cref="OnClipModeChanged"/> on every change, which arms the plane
    /// and pushes it to the renderer.</summary>
    public ClipPlaneMode ClipMode { get; set; } = ClipPlaneMode.None;

    /// <summary>"Clip F" toggle state. Setting true discards everything between the
    /// camera and the plane; setting false clears the clip entirely. Paired with
    /// <see cref="IsClipBack"/> as two mutually-exclusive toolbar toggles — they are
    /// the two half-spaces of ONE plane, so arming both would discard the whole model.
    /// Fody re-raises both when <see cref="ClipMode"/> changes, so pressing one
    /// visually releases the other.</summary>
    public bool IsClipFront
    {
        get => ClipMode == ClipPlaneMode.Front;
        set => ClipMode = value ? ClipPlaneMode.Front : ClipPlaneMode.None;
    }

    /// <summary>"Clip B" toggle state: discards everything beyond the plane. See
    /// <see cref="IsClipFront"/>.</summary>
    public bool IsClipBack
    {
        get => ClipMode == ClipPlaneMode.Back;
        set => ClipMode = value ? ClipPlaneMode.Back : ClipPlaneMode.None;
    }

    /// <summary>True while either clip is armed. Drives the visibility of the "V"
    /// button and the plane-name caption in the viewport gizmo cluster.</summary>
    public bool IsClipActive => ClipMode != ClipPlaneMode.None;

    /// <summary>"V" toggle: draw the translucent plane marker. On by default, and
    /// purely cosmetic — the clip keeps working with the marker hidden, and Ctrl+wheel
    /// keeps moving the plane either way.</summary>
    public bool ShowClipPlaneVisual { get; set; } = true;

    /// <summary>Name of the locked plane ("XY", "XZ" or "YZ") for the caption under
    /// the axis gizmo, or empty when no clip is armed. Axis names match the gizmo's
    /// X/Y/Z lines, i.e. renderer world space, not NIF space.</summary>
    public string ClipPlaneLabel { get; set; } = string.Empty;

    /// <summary>The locked plane normal, oriented to point AWAY from the camera as it
    /// stood when the clip was armed (i.e. along the view direction). Both clip modes
    /// are expressed against this one vector, which is why flipping F to B leaves the
    /// plane exactly where it is and only swaps which side survives.</summary>
    private OpenTK.Mathematics.Vector3 _clipNormal = OpenTK.Mathematics.Vector3.UnitZ;

    /// <summary>Signed position of the plane along <see cref="_clipNormal"/>, in world
    /// units. Seeded to the camera target's coordinate on that axis when armed, then
    /// moved by <see cref="NudgeClipPlane"/>.</summary>
    private float _clipOffset;

    /// <summary>Whether <see cref="_clipNormal"/> / <see cref="_clipOffset"/> currently
    /// hold a live lock. Cleared when the clip is released, so the next arm re-detects
    /// the axis against the camera as it stands then.</summary>
    private bool _clipArmed;

    /// <summary>Fody hook on <see cref="ClipMode"/>. Arms the plane on the None-to-armed
    /// transition and pushes the result. Deliberately does NOT re-arm when flipping
    /// Front to Back directly: that is the user asking to see the other half of a cut
    /// they already positioned, not to start a new one.</summary>
    private void OnClipModeChanged()
    {
        if (ClipMode == ClipPlaneMode.None)
        {
            _clipArmed = false;
            ClipPlaneLabel = string.Empty;
        }
        else if (!_clipArmed)
        {
            ArmClipPlane();
        }
        PushClipPlaneToRenderer();
    }

    /// <summary>Fody hook on <see cref="ShowClipPlaneVisual"/>.</summary>
    private void OnShowClipPlaneVisualChanged() => PushClipPlaneToRenderer();

    /// <summary>
    /// Picks the world axis the camera is most nearly looking down, orients it away
    /// from the camera, and parks the plane at the camera's orbit target. Called once
    /// per arming; the result then stays put until the clip is released.
    /// </summary>
    private void ArmClipPlane()
    {
        var forward = Camera.Target - Camera.GetEyePosition();
        if (forward.LengthSquared < 1e-9f) forward = -OpenTK.Mathematics.Vector3.UnitZ;
        forward = OpenTK.Mathematics.Vector3.Normalize(forward);

        // Largest |component| of the view direction = the axis most nearly parallel to
        // it = the plane most nearly face-on to the viewer. A plane is named for the two
        // axes it CONTAINS, so the X axis yields the YZ plane, and so on.
        float ax = MathF.Abs(forward.X), ay = MathF.Abs(forward.Y), az = MathF.Abs(forward.Z);
        OpenTK.Mathematics.Vector3 axis;
        if (ax >= ay && ax >= az) { axis = OpenTK.Mathematics.Vector3.UnitX; ClipPlaneLabel = "YZ"; }
        else if (ay >= az) { axis = OpenTK.Mathematics.Vector3.UnitY; ClipPlaneLabel = "XZ"; }
        else { axis = OpenTK.Mathematics.Vector3.UnitZ; ClipPlaneLabel = "XY"; }

        _clipNormal = OpenTK.Mathematics.Vector3.Dot(forward, axis) < 0f ? -axis : axis;
        _clipOffset = OpenTK.Mathematics.Vector3.Dot(Camera.Target, _clipNormal);
        _clipArmed = true;

        LogVerbose("CharacterViewer: clip plane armed on " + ClipPlaneLabel
            + " (normal " + _clipNormal + ", offset " + _clipOffset.ToString("F2") + ")");
    }

    /// <summary>
    /// Slides the clip plane along its locked normal. Positive <paramref name="notches"/>
    /// pushes it away from the camera (deeper into the model), negative pulls it back
    /// toward the camera. Bound to Ctrl+wheel in the viewport; the plain wheel is left
    /// to camera zoom.
    ///
    /// <para>The step scales with camera distance, so one notch covers a comparable
    /// fraction of the screen whether the user is framed on a whole body or zoomed into
    /// a face. It is deliberately fine — roughly a quarter of a percent of the camera
    /// distance — because positioning a section cut is an aiming task: the useful travel
    /// is the few units around a surface the user is trying to slice through, and a step
    /// coarse enough to cross the whole body in a handful of notches skips straight past
    /// it. Spinning the wheel covers long distances perfectly well.</para>
    /// </summary>
    public void NudgeClipPlane(float notches)
    {
        if (ClipMode == ClipPlaneMode.None || notches == 0f) return;
        float step = MathF.Max(0.03f, Camera.Distance * 0.0025f);
        _clipOffset += notches * step;
        PushClipPlaneToRenderer();
    }

    /// <summary>
    /// Packs the current mode + locked normal + offset into the renderer's
    /// (normal.xyz, -distance) plane vector.
    ///
    /// <para>Front keeps <c>dot(p, n) &gt;= offset</c> — everything at or beyond the
    /// plane — discarding what lies between it and the camera. Back is that half-space
    /// negated. Off pushes the identity plane (0,0,0,1), a constant +1 clip distance, so
    /// the uniform stays harmless even if a stray draw left GL_CLIP_DISTANCE0 on.</para>
    /// </summary>
    private void PushClipPlaneToRenderer()
    {
        if (ClipMode == ClipPlaneMode.None || !_clipArmed)
        {
            Renderer.ClipEnabled = false;
            Renderer.ShowClipPlaneVisual = false;
            Renderer.ClipPlane = new OpenTK.Mathematics.Vector4(0f, 0f, 0f, 1f);
            return;
        }

        bool front = ClipMode == ClipPlaneMode.Front;
        var n = front ? _clipNormal : -_clipNormal;
        float d = front ? _clipOffset : -_clipOffset;

        Renderer.ClipPlane = new OpenTK.Mathematics.Vector4(n.X, n.Y, n.Z, -d);
        Renderer.ClipEnabled = true;
        Renderer.ShowClipPlaneVisual = ShowClipPlaneVisual;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SHAPE GEOMETRY ACCESS
    //
    //  Read access to the CPU-side geometry of the installed scene, for hosts that
    //  measure, pick or annotate it. Positions are post-deformation (see
    //  ApplyShapeDeformer) and pre-ModelScale, i.e. the space of GlMesh.CpuPositions.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Look up the current (post-deformation, pre-ModelScale) position of a vertex on a
    /// mesh by shape name and index. Returns false when the shape isn't present in the
    /// current viewer state or the index is out of range.
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
    /// Returns every vertex position for a shape in the same pre-ModelScale local space as
    /// <see cref="TryGetCurrentVertex"/>, or null when the shape isn't currently loaded.
    /// Allocates a fresh array per call (positions are converted from
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
    /// CPU-side geometry. The array is
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
    /// bone indices + weights (4 entries each per vertex, flat-packed). Returns <c>(null, null)</c> when the
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
    /// positions, keyed case-insensitively by shape name. A cheap topology check for hosts
    /// that store per-shape vertex indices.
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

    // ═══════════════════════════════════════════════════════════════════════
    //  GL INITIALIZATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called once after the GL context is ready (from the view's OnRender or Loaded event).
    /// </summary>
    public void InitializeGl(string? shaderDirectory)
    {
        if (IsGlInitialized) return;

        TextureManager = new GlTextureManager(_previewCache.Assets, _logger, ResidentTextureCache, _logGate);
        TextureManager.Initialize();
        Renderer.Initialize(shaderDirectory);

        // Selections were resolved from settings in the ctor; push the current
        // field values to the renderer now that it's initialized.
        PushAllLightsToRenderer();
        Renderer.ShowKeyLightVisualization = ShowLightControls;
        // Same reason as the lights: a host viewer control recreated by WPF navigation
        // brings a fresh GL context but keeps this VM's clip state, so re-push it rather
        // than relying on the renderer's defaults still matching.
        PushClipPlaneToRenderer();

        IsGlInitialized = true;

        LogVerbose("CharacterViewer: GL initialized");
    }

    /// <summary>
    /// Drops all GL-bound state when the owning host viewer control is recreated during
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
        _cachedBodyNifDiskPath = null;
        _cachedMeshPaths = null;

        // Any pending buffers were captured against the dead context — drop them so
        // ProcessPendingScene doesn't try to upload stale BuiltMesh data as if it were
        // fresh. LoadNpcAsync will re-populate on the next preview request. A partial
        // sliced install also dies with the dead context — its in-flight queue and the
        // already-uploaded GL handles are equally invalid.
        _pendingScene = null;
        _sceneInstall = null;
        _pendingTextureOverrides = null;
        _pendingShapeDeform = null;
        _pendingMeshOverrides = null;
        _pendingHeadReplace = null;

        _currentLoadedIdentityKey = "";
        _currentHeadMeshOverride = null;
        _sceneRebuildPending = true;

        IsGlInitialized = false;

        // Host VMs subscribe to re-issue their last preview
        // request so the user sees their previously-loaded character without having to
        // re-click a preset. Raised after state is reset so handlers see a clean VM.
        GlContextReset?.Invoke();
    }

    /// <summary>Fires after <see cref="HandleGlContextLoss"/> finishes resetting VM state.
    /// Host VMs subscribe to re-trigger their last preview
    /// so the viewer isn't
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
    public void ProcessPendingSceneToCompletion(int maxIterations = 200, CancellationToken ct = default)
    {
        for (int i = 0; i < maxIterations && HasPendingSceneWork; i++)
        {
            // Each tick installs one shape — texture decode + GL upload, the
            // other half of an offscreen render's cost. Check between ticks so a
            // host cancellation drops the in-flight render without draining the
            // whole install queue. Default token (live-preview callers) never
            // cancels here.
            ct.ThrowIfCancellationRequested();
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
            _currentSceneVanillaLooseOverridesModLoose,
            _currentSceneAllowLoadOrderFallback);

        // Head-only rebuild is independent of full-scene setup and runs
        // without touching Body/Hands/Feet. Drain it here so the render callback
        // owns all GL-side scene mutations.
        if (_pendingHeadReplace != null && IsGlInitialized)
        {
            InstallReplacedHead();
        }

        if (!IsGlInitialized) return;

        // ── 0. Guest overlay ("superimpose"). Drains its own queue and no-ops unless a
        //       guest is pending and the primary scene is quiescent — the guard lives in
        //       ProcessPendingGuestScene, which also re-installs the overlay after a primary
        //       ClearScene has torn its meshes down along with everything else.
        ProcessPendingGuestScene();

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
            // of the order the mesh data source returned them in.
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
            _faceGenHairTint = ComputeFaceGenHairTint(queue);

            _sceneInstall = new SceneInstallState(
                MeshPaths: meshPaths,
                Pending: queue,
                LoadIdentityKey: _pendingLoadIdentityKey,
                HeadMeshOverride: _pendingLoadHeadMeshOverride,
                LoadStopwatch: loadStopwatch,
                TotalShapes: queue.Count,
                LoadGeneration: _pendingLoadGeneration);
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

        // ── 1b. Quiescent-scene override drain. ApplyMeshOverrides queues instead
        //       of applying when the host defers GL to this callback (see its
        //       remarks), so a narrow toggle — attire / headgear, no reload —
        //       has no scene commit to ride in on. Drain it here, where this
        //       viewer's context is current, rather than only at step 3.
        //
        //       Gated on a committed, quiescent scene: with an install in flight
        //       or a rebuild pending, the meshes these overrides would attach to
        //       are about to be destroyed, and step 3 re-drains after the commit.
        if (_sceneInstall == null && _pendingMeshOverrides != null && CanApplyMeshOverrides)
        {
            var quiescentOverrides = _pendingMeshOverrides;
            _pendingMeshOverrides = null;
            ApplyMeshOverridesCore(quiescentOverrides);
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
        //       texture overrides / shape deformations that arrived during the install.
        //
        //       Unless the install is stale: a newer LoadAsync started after this
        //       scene's load (another NPC picked while this one was uploading) and is
        //       still in flight. Its rebuild gate stays up, and everything queued now
        //       (that NPC's outfit, texture overrides, deformation) belongs to the newer
        //       scene, which replaces this one when its models are read. Draining here
        //       would attach it to this scene and lose it in the teardown. A newer load
        //       that was cancelled or failed with no replacement has dropped the gate,
        //       so this scene is then the current one and commits normally.
        bool superseded = _sceneRebuildPending && install.LoadGeneration != _loadGeneration;
        LogLoadCheckpoint(install.LoadStopwatch,
            "Scene committed (" + install.TotalShapes +
            " shapes -> " + Renderer.Meshes.Count + " GL meshes) — " +
            (superseded ? "superseded by a newer load, queued updates left for it" : "load complete"));
        // Logged before the override drains below, which report their own batches.
        LogTextureLockTally(install.LockTally, "scene install");

        _currentLoadedIdentityKey = install.LoadIdentityKey;
        _currentHeadMeshOverride = install.HeadMeshOverride;
        _sceneInstall = null;
        _currentSceneShapeDeform = null; // the new meshes are at their base until a deformation is applied below or later

        if (superseded)
        {
            // The newer load's status line and spinner stay up; hosts still hear that this
            // scene went in (CurrentLoadedIdentityKey names it).
            SceneCommitted?.Invoke();
            LogMeshStateSnapshot();
            return;
        }

        StatusText = install.TotalShapes > 0
            ? $"Loaded {install.TotalShapes} shape(s) for NPC"
            : "No renderable shapes found for NPC";

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

        if (_pendingShapeDeform != null)
        {
            var (deformer, weight, retain) = _pendingShapeDeform.Value;
            _pendingShapeDeform = null;
            ApplyShapeDeformer(deformer, weight, retain);
        }

        // Mesh overrides drain after texture/deformation state so the synthesized
        // shapes (e.g. an auxiliary slot-52 mesh) are built against a
        // fully-textured base and any slot-N texture overrides queued above have
        // already been routed.
        // Still inside this method's PushScopes bracket, so the override NIF /
        // skeleton resolve with the load's scope chain.
        // Straight to the core: the public entry point would re-queue this on a
        // host that defers GL to the render callback, and we ARE that callback.
        // The readiness re-check preserves the old behavior of leaving the set
        // queued when the freshly-committed scene has nothing to attach to.
        if (_pendingMeshOverrides != null && CanApplyMeshOverrides)
        {
            var meshOverrides = _pendingMeshOverrides;
            _pendingMeshOverrides = null;
            ApplyMeshOverridesCore(meshOverrides);
        }

        // Notify host-side queues that the scene is now ready for narrow updates.
        // Fired after the neutral drains above so subscribers see a fully-committed
        // scene, including any pending texture/deformation state from the previous scene.
        SceneCommitted?.Invoke();

        // Per-mesh state dump for diagnostic capture. Lives here (not at end
        // of LoadAsync) because the GlMesh array isn't populated until
        // InstallOneShape runs on the render thread — which only happens
        // after LoadAsync queues _pendingScene and the render thread ticks.
        LogMeshStateSnapshot();

        // The per-load snapshot deliberately SURVIVES scene commit: it is the
        // committed scene's resolution context, and post-commit narrow updates
        // (an attire toggle's ApplyMeshOverrides, texture overrides, head
        // replace) re-push it so mod-scoped assets keep resolving. Clearing it
        // here made those updates fall back to the vanilla data folder + BSAs —
        // masked whenever the mod's files also existed under Data (load-order
        // installs), exposed as unresolvable textures / physics XMLs (white
        // wireframe attire) when they did not. The next LoadAsync overwrites
        // the snapshot; ClearScene and the load cancel/error path clear it.
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

    /// <summary>
    /// True if a shape is an invisible physics/collision proxy that should not be
    /// rendered. SMP-enabled hair (and some armor) ships collision bodies — e.g.
    /// "_BDO_colHeadBDOH", a capsule that encloses the head — textured with a
    /// fully-transparent placeholder ("0alfa.dds": white RGB, alpha 0) and carrying
    /// NO NiAlphaProperty. In-game the physics system detaches them from the render
    /// graph; with no physics here they would otherwise rasterize as an opaque white
    /// blob over the face/body. We detect the transparent-placeholder diffuse
    /// directly — name-independent, since authors choose collision-shape names
    /// freely. Gated on no alpha blend/test so a shape that legitimately relies on
    /// alpha is never culled, and only RGBA-format DDS can read as fully transparent
    /// (see <see cref="CharacterPreviewCache.IsFullyTransparent"/>), so ordinary
    /// opaque skin/armor diffuse never matches. <paramref name="effectiveTextures"/>
    /// is the post-override texture set so a real diffuse swapped in over the
    /// placeholder correctly spares the shape.
    /// </summary>
    private bool IsInvisibleCollisionProxy(NifMeshBuilder.BuiltMesh built,
        IReadOnlyDictionary<int, string> effectiveTextures)
        => !built.HasAlphaBlend && !built.HasAlphaTest
            && effectiveTextures.TryGetValue(0, out var diffuse)
            && _previewCache.IsFullyTransparent(diffuse);

    private void LogCollisionProxyCull(NifMeshBuilder.BuiltMesh built)
        => LogVerbose("CharacterViewer: Skipping shape '" + built.ShapeName +
            "' (fully-transparent diffuse with no alpha property - invisible physics/collision proxy)");

    /// <summary>Reads <see cref="_faceGenHairTint"/> off the built head shapes.
    /// Head-group only: a worn wig's own shapes carry the placeholder tint this
    /// exists to replace, and body/hands/feet are never HairTint. Neutral white
    /// is skipped as uninformative (it's a no-op multiply, not a hair color).</summary>
    private static (float R, float G, float B)? ComputeFaceGenHairTint(IEnumerable<PendingShape> shapes)
    {
        foreach (var shape in shapes)
        {
            if (shape.BodyPart != "Head" || !shape.Built.IsHairTintShader) continue;
            var tint = shape.Built.HairTintColor;
            if (tint == null) continue;
            if (tint.Value.R >= 0.98f && tint.Value.G >= 0.98f && tint.Value.B >= 0.98f) continue;
            return tint;
        }
        return null;
    }

    /// <summary>Uploads one shape's GL mesh + textures and registers it in the
    /// per-body-part dictionaries. Mirrors the inner loop body the single-frame
    /// install used; called once per shape from the sliced install loop.</summary>
    private void InstallOneShape(SceneInstallState install, PendingShape shape)
    {
        // Cheap pre-shape bail: avoids creating a GL mesh we'd only tear down.
        RenderCancellation.ThrowIfCancellationRequested();

        // Referencer scoping for the data-folder-fallback report: when this
        // shape's source NIF was itself resolved from the data folder (the
        // user's global body/skin baseline, not the depicted mod's file), the
        // textures it references are that baseline's internals — mute their
        // fallback reports so they don't read as dependencies of the mod. The
        // NIF's own resolve (before this method) still reported, so a genuine
        // out-of-scope mesh keeps its dependency line. A conditional using:
        // null (no-op) for in-scope NIFs, whose out-of-scope textures must
        // keep reporting (e.g. a mod whose textures sit outside its scope). MUST precede the
        // collision-proxy check below — IsFullyTransparent resolves + decodes
        // the shape's slot-0 diffuse, which for a fallback body NIF's extra
        // shapes is exactly the baseline-internal texture this bracket exists
        // to keep out of the badge (a baseline body texture leaked this way).
        using var __fallbackReportMute = shape.MeshSource?.ViaDataFolderFallback == true
            ? _assetResolver.PushDataFolderFallbackReportSuppression()
            : null;

        // Cull invisible physics/collision proxies before the geometry upload. The
        // diffuse is read straight from the NIF texture set: these proxies are
        // ShaderType 0, so the ShaderType-5-only TXST skin overrides applied later
        // in InstallOneShapeTextures never change slot 0 here.
        if (IsInvisibleCollisionProxy(shape.Built, shape.Built.TexturePaths))
        {
            LogCollisionProxyCull(shape.Built);
            return;
        }

        // Host-designated head-shape hide (e.g. a host removing a head part): the patch
        // strips this baked head-part shape from the FaceGen NIF, so the preview
        // must not draw it either. Base-scene head shapes only — attire overrides
        // install through a different path and are never filtered here.
        if (shape.BodyPart == "Head" && install.MeshPaths.HideHeadShapeNames.Count > 0 &&
            install.MeshPaths.HideHeadShapeNames.Contains((shape.Built.ShapeName ?? string.Empty).Trim()))
        {
            LogVerbose("CharacterViewer: hiding head shape '" + shape.Built.ShapeName +
                       "' (host-designated HideHeadShapeNames — antler/head-part removal).");
            return;
        }

        var glMesh = CreateGlMesh(shape.Built);
        glMesh.MeshSource = shape.MeshSource;

        // ApplyTexturesToGlMesh checks RenderCancellation between texture loads;
        // if it throws, glMesh is built (GL buffers uploaded) but not yet handed
        // to Renderer, so dispose it here to avoid leaking those buffers on a
        // mid-shape cancel.
        try
        {
            InstallOneShapeTextures(install, shape, glMesh);
        }
        catch (OperationCanceledException)
        {
            glMesh.Dispose();
            throw;
        }
    }

    private void InstallOneShapeTextures(SceneInstallState install, PendingShape shape, GlMesh glMesh)
    {
        var effectiveTextures = new Dictionary<int, string>(shape.Built.TexturePaths);
        // ARMA TXST overrides target the body part's *skin* (e.g. ARMA[Body] → FemaleBody_1.dds).
        // Body/Hands/Feet NIFs can contain non-skin shapes (a clothing shape, fingernails,
        // attached armor) that share the NIF but ship their own diffuse/normal. Gate on
        // BSLSP shader type 5 (ST_SkinTint) so the body skin texture doesn't bleed onto
        // those shapes — engine behavior, and matches IsSkinShape elsewhere.
        // A body replacement re-skins every body part's skin shapes (body, hands, feet, tail) from one
        // source, whether the part's mesh was swapped or kept, so there is no seam at the wrists/ankles.
        var sceneReplacement = _sceneBodyReplacement;
        bool replacementSkin = sceneReplacement != null && !sceneReplacement.UsesNpcSkin
            && SkyRender.Character.BodyReplacement.SkinBodyParts.Contains(shape.BodyPart) && shape.Built.ShaderType == 5;
        bool neutralSkin = false;
        if (replacementSkin)
            neutralSkin = !ResolveReplacementBodyTextures(shape.Built, sceneReplacement!.TextureFolder, effectiveTextures);
        else if (shape.TxstOverrides != null && shape.Built.ShaderType == 5)
            foreach (var (slot, path) in shape.TxstOverrides)
                effectiveTextures[slot] = path;
        else
            ApplyReplacementHeadTextures(shape.Built, shape.BodyPart, sceneReplacement, effectiveTextures);

        bool isHairTint = false;
        float hairR = 0, hairG = 0, hairB = 0;
        bool isFaceTint = false;
        string? faceTintPath = null;

        ApplyTexturesToGlMesh(glMesh, shape.Built, effectiveTextures, install.MeshPaths,
            ref isHairTint, ref hairR, ref hairG, ref hairB,
            ref isFaceTint, ref faceTintPath,
            // "Hair" here is the worn hair-slot ARMA (slot 31) loaded from
            // HairMeshPath — FaceGen-baked hair is tagged "Head".
            isWornHairSlotItem: shape.BodyPart == "Hair");
        if (neutralSkin) ApplyNeutralSkin(glMesh);

        _textureApplyInfoByMesh[glMesh] = new TextureApplyInfo(
            new Dictionary<int, string>(effectiveTextures),
            isHairTint, hairR, hairG, hairB, isFaceTint, faceTintPath);

        glMesh.BodyPart = shape.BodyPart;
        // Tag the base shape with the biped slot(s) it occupies so the
        // slot-occupancy resolver can let a mesh override (armor/headgear) hide
        // it. Derived per-shape (not just from the body-part group) so a hood
        // hides only the baked-in hair sub-shape of a FaceGen head, not the
        // face. Base shapes stay at draw priority 0 and never hide anything.
        glMesh.Npc().BipedSlots = BipedSlotsForBaseShape(shape.Built, shape.BodyPart);
        glMesh.ShowWireframe = ShowWireframe;
        ApplyTextureVisibilityLock(glMesh, install.LockTally);
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

            // Cache the body NIF's disk path once per scene; it is handed to
            // IShapeDeformer.Prepare so a host deformer can find data that sits
            // next to the NIF it deforms.
            if (_cachedBodyNifDiskPath == null && shape.MeshSource?.ResolvedDiskPath != null)
            {
                _cachedBodyNifDiskPath = shape.MeshSource.ResolvedDiskPath;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LOADING — Full NPC
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Raises <see cref="ReloadRequested"/> after marking the next
    /// <see cref="LoadAsync"/> to force a full rebuild. Used by the toggles
    /// whose effect is baked in at mesh-upload time (so they can't be applied to
    /// an already-loaded scene): the host reacts by reloading the SAME NPC, and
    /// the same-identity short-circuit in LoadAsync would otherwise skip that
    /// rebuild — leaving the toggle with no visible effect. See
    /// <see cref="_forceRebuildNextLoad"/>.</summary>
    private void RequestReload()
    {
        _forceRebuildNextLoad = true;
        ReloadRequested?.Invoke();
    }

    /// <summary>Marks the next <see cref="LoadAsync"/> to force a full rebuild
    /// even for the same NPC identity (bypassing the same-identity short-circuit).
    /// For a host that drives its OWN reload after changing a mesh-upload-time
    /// input — e.g. a host designating a head part to hide
    /// via <see cref="ResolvedNpcMeshPaths.HideHeadShapeNames"/>, which is applied
    /// at install time — so the same-NPC reload actually re-installs the head.</summary>
    public void ForceRebuildNextLoad() => _forceRebuildNextLoad = true;

    /// <summary>
    /// Resolves <paramref name="identity"/>'s mesh paths and parses / decodes its assets into the
    /// shared <see cref="CharacterPreviewCache"/> <b>without touching the current scene</b>, so a
    /// later <see cref="LoadByIdentityAsync"/> for the same NPC hits warm entries instead of doing
    /// the work on the render thread.
    ///
    /// <para>Strictly best-effort and side-effect-free from the viewer's point of view: it does not
    /// change <see cref="StatusText"/>, does not set <see cref="IsLoading"/>, does not cancel an
    /// in-flight load, and swallows its own failures. Anything it misses simply loads lazily as
    /// before, so calling it can cost time but can never change what is rendered.</para>
    ///
    /// <para>Runs its work on the thread pool. The caller owns the cancellation token and should
    /// cancel when the prediction it prefetched for stops being the likely next load -- otherwise
    /// a stale prewarm competes for I/O with the load the user actually asked for.</para>
    ///
    /// <para>Useful when a host can predict the next NPC it will show while the user is still
    /// looking at the current one.</para>
    /// </summary>
    /// <param name="identity">NPC to warm. Same identity shape <see cref="LoadByIdentityAsync"/>
    /// takes, so the cache keys line up.</param>
    /// <param name="ct">Cancels the resolve / parse / decode work.</param>
    public async Task PrewarmIdentityAsync(NpcIdentity identity, CancellationToken ct = default)
    {
        if (identity == null) return;
        // Already the committed scene: every asset it needs is resident, so there is nothing to
        // warm and the work would be pure waste.
        if (identity.CacheKey == _currentLoadedIdentityKey) return;

        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var paths = _previewCache.GetOrResolveMeshPaths(identity);
                if (paths == null) return;
                ct.ThrowIfCancellationRequested();
                _previewCache.PrewarmNpc(paths, null, ct);
            }, ct);
        }
        catch (OperationCanceledException)
        {
            // Expected whenever the prediction changes mid-flight; not worth a log line.
        }
        catch (Exception ex)
        {
            LogVerbose("CharacterViewer: prewarm of " + identity.CacheKey + " failed (harmless): " +
                ex.Message);
        }
    }

    /// <summary>
    /// Neutral cache-driven load entry. Resolves <paramref name="identity"/>
    /// to a <see cref="ResolvedNpcMeshPaths"/> through the preview cache
    /// (whose <see cref="INpcMeshDataSource"/> adapter does the host-specific
    /// resolution, e.g. through Mutagen), then hands off to
    /// <see cref="LoadAsync"/>. Hosts call this directly with their own
    /// <see cref="NpcIdentity"/>.
    /// </summary>
    public async Task LoadByIdentityAsync(NpcIdentity identity, string? overrideHeadMeshAbsolutePath = null,
        IReadOnlySet<string>? overrideEyeShapeNames = null)
    {
        // NOTE: no RenderingUnavailable short-circuit here. Even when the GL viewport
        // can't start, the software fallback preview needs the resolved mesh paths, so
        // we still resolve and forward to LoadAsync — which retains the neutral inputs
        // and skips only the GL scene build (see the RenderingUnavailable branch there).
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

        await LoadAsync(identity, meshPaths, overrideHeadMeshAbsolutePath,
            overrideEyeShapeNames: overrideEyeShapeNames);
    }

    /// <summary>
    /// Neutral rendering-tier entry point. Loads <paramref name="paths"/> into
    /// the GL scene with the given <paramref name="identity"/> as the cache /
    /// short-circuit key. The host is responsible for
    /// producing <see cref="ResolvedNpcMeshPaths"/> beforehand — this method
    /// has no knowledge of Mutagen, FormKeys, or any host-specific NPC model.
    ///
    /// Same-identity short-circuit: narrow editors (e.g. a preset or
    /// texture-variant change) call this defensively before their narrow
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
        string? overrideHeadMeshAbsolutePath = null, CancellationToken externalCt = default,
        IReadOnlySet<string>? overrideEyeShapeNames = null)
    {
        // Merge host-supplied eye shape names (head-part preview flows whose
        // override NIF bakes an ASSIGNED eyes part, not the record's) into the
        // POCO before anything captures it — the merged instance flows into
        // _cachedMeshPaths, the pending scene, the SceneInputsSnapshot the
        // software fallback reads, and IsEye classification. Union, not
        // replace: unassigned slots keep the record's parts in the override
        // NIF, so the record-derived names must stay valid alongside.
        if (overrideEyeShapeNames is { Count: > 0 })
        {
            var mergedEyeNames = new HashSet<string>(paths.EyeShapeNames, StringComparer.OrdinalIgnoreCase);
            mergedEyeNames.UnionWith(overrideEyeShapeNames);
            paths = paths.WithEyeShapeNames(mergedEyeNames);
        }

        // Host-chosen body type (alternate body / hands / feet / tail meshes, to preview a
        // body type that isn't the installed one). Folded into the POCO for the same reason as the
        // eye names above: everything downstream -- LoadAllMeshParts, the software fallback's
        // SceneInputsSnapshot -- then sees one consistent set of part paths. Its skin choice is baked
        // in at install, so any change to it on the same NPC must defeat the short-circuit below
        // (compared against the replacement the scene was built with).
        var replacement = string.IsNullOrWhiteSpace(BodyReplacement?.BodyNifPath) ? null : BodyReplacement;
        bool replacementUnchanged = Equals(replacement, _sceneBodyReplacement);
        _sceneBodyReplacement = replacement;
        if (replacement != null)
        {
            paths = paths.WithBodyReplacement(replacement);
        }

        if ((RenderingUnavailable || ForceRenderingUnavailableForTesting) && !IsOffscreenRenderInstance)
        {
            // No GL surface on this system (WGL_NV_DX_interop missing) — the render loop
            // that drains ProcessPendingScene never runs, so a full load would only queue a
            // scene that can't be committed and leave IsLoading stuck true. Instead RETAIN the
            // neutral inputs (identity + resolved paths + head override) so the software
            // fallback preview can re-express them as offscreen render requests, reset the
            // per-NPC override/morph retention for the new NPC, notify subscribers, and skip
            // the NIF parse / CPU skin / GL upload (the offscreen renderer redoes that in its
            // own throwaway VM). Only the GL work is gated — the input capture is not.
            _currentLoadedIdentityKey = identity.CacheKey;
            _cachedMeshPaths = paths;
            _currentHeadMeshOverride = overrideHeadMeshAbsolutePath;
            _lastRequestedTextureOverrides = null;
            _lastRequestedMeshOverrides = null;
            _lastRequestedShapeDeform = null;
            // Clear any stale "Loading meshes... / setting up scene..." status left from a prior
            // live-mode load — in fallback the GL scene never commits, so nothing else updates it.
            // This status line is the ONLY view-only announcement (the in-viewport badge was
            // removed because it covered the render), so carry the full explanation here.
            StatusText = "Software preview (view only) — rotate / zoom / pan. Editing requires hardware OpenGL interop.";
            RaiseSceneInputsChanged();
            return;
        }

        if (!_sceneRebuildPending
            && _meshesByBodyPart.Count > 0
            && identity.CacheKey == _currentLoadedIdentityKey
            && overrideHeadMeshAbsolutePath == null
            && _currentHeadMeshOverride == null
            // Same NPC but a different body NIF or body replacement (set, changed or cleared) must rebuild.
            && string.Equals(paths.BodyMeshPath, _cachedMeshPaths?.BodyMeshPath, StringComparison.OrdinalIgnoreCase)
            && replacementUnchanged
            && !_forceRebuildNextLoad)
        {
            LogVerbose("CharacterViewer: LoadAsync same-identity short-circuit (" +
                identity.CacheKey + ")");
            return;
        }

        // Proceeding with a real rebuild — consume the one-shot force flag so a
        // later defensive same-identity LoadAsync can short-circuit normally.
        _forceRebuildNextLoad = false;

        _loadCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        _loadCts = cts;

        // Reset the per-load missing-mesh list. LoadAllMeshParts appends to it
        // as each TryLoad attempt encounters a gamePath that no scope could
        // resolve to disk; the renderer / host reads it after LoadAsync
        // completes to surface incomplete-render warnings.
        _missingMeshPaths.Clear();
        _meshOverrideWarnings.Clear();
        TextureManager?.ClearMissingTexturePaths();

        // Mark a rebuild as in-flight so any ApplyTextureOverrides calls arriving
        // between now and when ProcessPendingScene finishes are queued rather than
        // applied to the soon-to-be-destroyed current meshes.
        _sceneRebuildPending = true;
        // A new generation: a scene still installing from an earlier load is now stale and
        // leaves the queued updates for this one (ProcessPendingScene, step 3).
        int loadGeneration = ++_loadGeneration;

        // Snapshot the host's resolution scoping into per-VM fields. Two
        // consumers of these snapshots:
        //   (a) The PushScopes bracket below covers off-thread NIF parsing
        //       inside Task.Run — AsyncLocal flows through ExecutionContext.
        //   (b) ProcessPendingScene re-pushes from the snapshot fields on
        //       every install tick because the WPF render callback that
        //       fires it is on a different ExecutionContext that does NOT
        //       inherit this method's AsyncLocal value.
        // AdditionalScopes wins over AdditionalDataFolders
        // when both are provided.
        var additionalScopes = AdditionalScopes;
        var additionalFolders = AdditionalDataFolders;
        _currentSceneScopes = additionalScopes;
        _currentSceneFolders = additionalFolders;
        _currentSceneVanillaLooseOverridesBsa = VanillaLooseOverridesBsa;
        _currentSceneVanillaLooseOverridesModLoose = VanillaLooseOverridesModLoose;
        _currentSceneAllowLoadOrderFallback = AllowLoadOrderFallback;

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
                       _currentSceneVanillaLooseOverridesModLoose,
                       _currentSceneAllowLoadOrderFallback))
            {
                // Offscreen renderer (inline marshaller): we're already on a
                // dedicated render thread, not the WPF UI thread, so the Task.Run
                // hop buys nothing — it only adds ThreadPool scheduling latency and
                // competes with the prewarm/encode workers for pool threads while
                // the render thread sits blocked on the result. Run inline so build
                // is just the render thread's own (now mostly cache-hit) work.
                // Interactive hosts (WPF dispatcher marshaller) keep the Task.Run so
                // the UI thread stays responsive during the parse.
                loadResults = _renderThread is InlineRenderThreadMarshaller
                    ? LoadAllMeshParts(paths, cts.Token)
                    : await Task.Run(() => LoadAllMeshParts(paths, cts.Token), cts.Token);
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
                _pendingLoadGeneration = loadGeneration;
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
            // because the live-preview host scopes its log capture to the
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
            // unhelpful or empty Message. Log the full chained exception so the log
            // shows the full failure.
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

    /// <param name="isWornHairSlotItem">True when this shape comes from a WORN
    /// hair-slot item (biped 31) rather than the FaceGen/head-part scene. Such
    /// shapes are tinted by RaceMenu in game, not by the engine, so a supplied
    /// <see cref="ResolvedNpcMeshPaths.WornHairSlotTintRgb"/> overrides their
    /// baked placeholder tint. See that property for the full rationale.</param>
    private void ApplyTexturesToGlMesh(GlMesh glMesh, NifMeshBuilder.BuiltMesh built,
        Dictionary<int, string> effectiveTextures, ResolvedNpcMeshPaths meshPaths,
        ref bool isHairTint, ref float hairR, ref float hairG, ref float hairB,
        ref bool isFaceTint, ref string? faceTintPath,
        bool allowEyeNameMatching = true,
        bool isWornHairSlotItem = false)
    {
        if (TextureManager == null) return;
        var textureManager = TextureManager;

        // Slot 0 for the two NPC-specific diffuse cases; every other slot (and plain diffuse)
        // is bound by the core NifMaterialBinder below.
        //
        // Two tinting modes match the Skyrim engine (and NPC Portrait Creator):
        //   1. SLSF1_Greyscale_To_Palette_Color flag set:
        //      Texture is greyscale; shader: baseColor.rrr * tint_color * greyscaleToPaletteScale
        //   2. BSLSP_HAIRTINT shader type only (flag NOT set):
        //      Texture is full RGB; shader: baseColor.rgb *= tint_color (simple multiply)
        //
        // The tint color is the NIF's baked hairTintColor when present, falling
        // back to the NPC record's HairColor (HCLR) only when the shape has no
        // baked tint. This mirrors the engine: empirically (2026-07-18), editing
        // a hair NIF's hairTintColor changes the in-game color with no plugin
        // edit, so at render time the mesh's baked value wins over the record.
        // (A FaceGen export bakes the record color into the NIF, which is why
        // the two normally agree.) Tint values are sRGB 0..1, the same space the
        // shader multiplies against the raw sRGB diffuse texels, so no gamma
        // rebase is needed. Entered whenever a hair-tint shape has EITHER a
        // baked tint or a resolved HCLR, so a shape with no baked color still
        // gets tinted.
        //
        // ONE exception: a WORN hair-slot item. The engine never tints worn
        // armor, so its baked value is not what the player sees — RaceMenu's
        // skee64 (bEnableTintHairSlot) recolors it from the actor's hair color,
        // which is why wig meshes ship a near-black placeholder tint and why
        // mods like High Poly NPC Overhaul look black-haired without RaceMenu.
        // When the host supplies WornHairSlotTintRgb it wins over the baked
        // placeholder for those shapes only (see that property). Within that,
        // this FaceGen's OWN baked hair color wins over the host's record-derived
        // value, so the wig matches the beard and brows even when a color record
        // was overridden after the appearance mod exported its FaceGen.
        var wornHairSlotTint = isWornHairSlotItem && meshPaths.WornHairSlotTintRgb.HasValue
            ? (_faceGenHairTint ?? meshPaths.WornHairSlotTintRgb)
            : null;

        bool outHairTint = false, outFaceTint = false;
        float outHairR = 0f, outHairG = 0f, outHairB = 0f;
        string? outFaceTintPath = null;

        bool BindNpcDiffuse(GlMesh mesh, NifMeshBuilder.BuiltMesh b, IReadOnlyDictionary<int, string> textures)
        {
            if (b.IsHairTintShader
                && (wornHairSlotTint.HasValue || b.HairTintColor.HasValue ||
                    _npcHairColorFromRecord.HasValue)
                && textures.TryGetValue(0, out string? hairDiffuse))
            {
                var (tR, tG, tB) = wornHairSlotTint
                                   ?? b.HairTintColor
                                   ?? _npcHairColorFromRecord!.Value;
                outHairTint = true; outHairR = tR; outHairG = tG; outHairB = tB;
                mesh.DiffuseTexture = textureManager.LoadTexture(hairDiffuse);
                mesh.TintColor = new System.Numerics.Vector3(tR, tG, tB);

                string tintSrc = wornHairSlotTint.HasValue
                    ? "worn hair-slot tint from " +
                      (_faceGenHairTint.HasValue ? "the FaceGen's baked hair color" : "the NPC record HCLR") +
                      " (RaceMenu bEnableTintHairSlot emulation" +
                      (b.HairTintColor.HasValue
                          ? ", baked (" + b.HairTintColor.Value.R.ToString("F3")
                            + "," + b.HairTintColor.Value.G.ToString("F3")
                            + "," + b.HairTintColor.Value.B.ToString("F3") + ") overridden"
                          : "") + ")"
                    : b.HairTintColor.HasValue
                    ? "baked NIF hairTintColor" + (_npcHairColorFromRecord.HasValue
                        ? " (record HCLR=(" + _npcHairColorFromRecord.Value.R.ToString("F3")
                            + "," + _npcHairColorFromRecord.Value.G.ToString("F3")
                            + "," + _npcHairColorFromRecord.Value.B.ToString("F3") + ") unused)"
                        : "")
                    : "HCLR (no baked tint)";

                if (b.HasGreyscaleToPaletteFlag)
                {
                    mesh.HasGreyscaleToPalette = true;
                    mesh.GreyscaleToPaletteScale = b.GreyscaleToPaletteScale;
                    RecordTextureSource(mesh, "Diffuse (hair tint, greyscale-to-palette)", hairDiffuse);
                    LogVerbose("CharacterViewer: Hair tint (greyscale-to-palette): " +
                        "tint=(" + tR.ToString("F3") + "," + tG.ToString("F3") + "," + tB.ToString("F3") + ")" +
                        " src=" + tintSrc +
                        " scale=" + b.GreyscaleToPaletteScale.ToString("F2") +
                        " -> baseColor.rrr * tint * scale" +
                        " | diffuse=" + System.IO.Path.GetFileName(hairDiffuse));
                }
                else
                {
                    mesh.HasTintColor = true;
                    RecordTextureSource(mesh, "Diffuse (hair tint, RGB multiply)", hairDiffuse);
                    LogVerbose("CharacterViewer: Hair tint (simple RGB multiply): " +
                        "tint=(" + tR.ToString("F3") + "," + tG.ToString("F3") + "," + tB.ToString("F3") + ")" +
                        " src=" + tintSrc +
                        " -> baseColor.rgb *= tint" +
                        " | diffuse=" + System.IO.Path.GetFileName(hairDiffuse));
                }
                return true;
            }
            if (b.IsPrimaryHeadShape && textures.TryGetValue(0, out string? headDiffuse) &&
                meshPaths.FaceTintPath != null)
            {
                outFaceTint = true;
                outFaceTintPath = meshPaths.FaceTintPath;
                // Load diffuse and face tint as separate textures so they can be
                // toggled independently. The shader blends them via has_face_tint_map.
                mesh.DiffuseTexture = textureManager.LoadTexture(headDiffuse);
                mesh.FaceTintTexture = textureManager.LoadTexture(meshPaths.FaceTintPath);
                mesh.HasFaceTintMap = true;
                RecordTextureSource(mesh, "Diffuse", headDiffuse);
                RecordTextureSource(mesh, "Face Tint", meshPaths.FaceTintPath);
                return true;
            }
            return false;
        }

        // Eye classification beyond the eye shader type (16, always trusted).
        //
        // Some FaceGen NIFs ship eyes authored as BSLSP_ENVMAP (1) instead
        // of BSLSP_EYE (16) and would otherwise miss the is_eye AO gate in
        // basic.frag, leaving the eyeball to receive eye-socket SSAO that no
        // ambient setting can lift (AO multiplies the ambient term) and
        // losing the catchlight + shadow-cast skip. Two name-based
        // recoveries, both trusted only for base-scene shapes:
        //   1. AUTHORITATIVE — the host's resolved HeadPart records
        //      (EyeShapeNames). FaceGen bakes one shape per geometry-bearing
        //      head part, named after its EditorID, so membership in the
        //      EditorID set of Eyes-typed parts (+ their Extra Parts) IS
        //      eyeball geometry regardless of what the modder named it.
        //   2. HEURISTIC fallback for hosts without head-part data: plural
        //      "Eyes" substring. Skyrim's own convention uses plural for
        //      eyeballs (MaleEyesHumanIceBlue, EyesChild) and singular "Eye"
        //      for accessories (EyeShadow, Eyelashes) — but custom mods
        //      break it: FoxGlove Auri's ENVMAP eyeball is "FoxGloveEyeMesh"
        //      (singular) and is only caught by route 1.
        // Attire overrides never contain real eyeballs, but DO contain
        // decorative shapes literally named "Eyes"/"Eyes01" (helmet
        // ornaments) that would otherwise take the eye cubemap scale and
        // the eye AO opt-out, so ApplyMeshOverrides passes allowEyeNameMatching: false.
        Func<NifMeshBuilder.BuiltMesh, bool>? isEyeShape = allowEyeNameMatching
            ? b => meshPaths.EyeShapeNames.Contains(b.ShapeName.Trim())
                   || b.ShapeName.Contains("Eyes", StringComparison.Ordinal)
            : null;

        NifMaterialBinder.Bind(textureManager, _assetResolver, glMesh, built, effectiveTextures,
            new MaterialBindOptions
            {
                // Each LoadTexture can extract a DDS from a BSA and decode+upload it — the
                // finest-grained slow unit in an offscreen render — so the binder checks this
                // before each group. No-op for the live preview (token defaults to None).
                Cancellation = RenderCancellation,
                RenderMissingTextureAsWireframe = RenderMissingTextureAsWireframe,
                BindDiffuse = BindNpcDiffuse,
                // NPC QNAM TextureLighting colour for the skin-tint / face-tint shader types.
                SkinTintColor = meshPaths.TextureLightingColor,
                IsEyeShape = isEyeShape,
                // Experimental fallback: a face shape with the detail flag set but an empty
                // slot 3 gets Bethesda's CK default (the texture used when no complexion /
                // freckle / dirt / wound option is chosen), to test whether the engine
                // substitutes a similar default at runtime.
                EmptyFaceDetailFallbackPath = Renderer.UseBlankDetailFallback
                    ? "textures\\actors\\character\\male\\BlankDetailmap.dds"
                    : null,
                LogVerbose = LogVerbose,
            });

        if (outHairTint) { isHairTint = true; hairR = outHairR; hairG = outHairG; hairB = outHairB; }
        if (outFaceTint) { isFaceTint = true; faceTintPath = outFaceTintPath; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TEXTURE OVERRIDES
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Neutral texture-override entry. Each <see cref="TextureOverride"/>
    /// names its target body part + slot explicitly, so hosts needn't
    /// encode it into a destination path. Hosts call this directly.
    /// </summary>
    public void ApplyTextureOverrides(IEnumerable<TextureOverride> overrides)
    {
        var overrideList = overrides as List<TextureOverride> ?? overrides?.ToList() ?? new List<TextureOverride>();

        // Retain unconditionally for the software fallback snapshot (even on the queue
        // path below, and even when GL never started), then notify the fallback preview.
        _lastRequestedTextureOverrides = overrideList;
        RaiseSceneInputsChanged();

        // Queue when the scene is empty, the texture manager isn't ready, OR a
        // rebuild is in-flight. The rebuild check is what catches the texture
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

        // An override can give a mesh a slot it was created without (a normal, skin, detail
        // or specular map), which the texture visibility lock never saw. Note each target's
        // slots before its first override so the lock can cover what appeared, below.
        var slotsBeforeOverride = IsTextureVisibilityLocked ? new Dictionary<GlMesh, TextureSlots>() : null;

        // Shape-named overrides (worn-armor AlternateTextures targeting one named
        // sub-shape) must apply AFTER the flat body-wide overrides so they win on
        // their shape: a config can carry both a body-wide skin diffuse and a
        // per-shape AlternateTexture for the same slot, and the per-shape one is the
        // more specific. OrderBy is stable, so same-specificity order is preserved.
        foreach (var ov in overrideList.OrderBy(o => string.IsNullOrEmpty(o.ShapeName) ? 0 : 1))
        {
            string bodyPart = ov.BodyPart;
            int slot = ov.Slot;
            string source = ov.GameRelativePath;
            if (string.IsNullOrWhiteSpace(bodyPart) || string.IsNullOrWhiteSpace(source)) continue;

            // A shape-named override targets exactly one shape (by its NIF geometry node
            // name) within the body part, regardless of shader type — a worn-armor
            // AlternateTextures (MODS) entry retextures a single named sub-shape of a
            // multi-shape body NIF, which may not be a skin shape. The flat body-wide
            // branch below deliberately can't express this, so it is handled first and
            // separately.
            List<GlMesh> targets;
            if (!string.IsNullOrEmpty(ov.ShapeName))
            {
                targets = Renderer.Meshes
                    .Where(m => m.BodyPart == bodyPart &&
                                string.Equals(m.ShapeName, ov.ShapeName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (targets.Count == 0)
                {
                    LogVerbose("CharacterViewer: No shape named '" + ov.ShapeName + "' with BodyPart='" +
                        bodyPart + "' for AlternateTexture override (slot " + slot + ")");
                    continue;
                }
                LogVerbose("CharacterViewer: AlternateTexture override '" + source + "' → shape '" +
                    ov.ShapeName + "' (" + bodyPart + ", slot " + slot + ")");
            }
            // For Head, target only the primary head shape (the face — face/hair/eyes
            // are separate shapes with different meaning for each slot). For non-head
            // body parts, apply to every *skin* shape in that NIF: a body NIF can hold
            // multiple skin shapes (some body replacers split the torso into more than
            // one skin shape), and they should all receive the body diffuse. But non-skin
            // shapes that share the same NIF (a clothing shape on the vanilla FemaleBody,
            // fingernails on FemaleHands) keep their NIF-baked textures — without this
            // gate, ARMA[Body] TXST clobbers that shape's own texture with FemaleBody_1.dds
            // and it inherits body detail it shouldn't.
            else if (bodyPart == "Head")
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
                slotsBeforeOverride?.TryAdd(mesh, mesh.PresentTextureSlots());

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
                else if (slot == 3)
                {
                    // Facegen detail (complexion/dirt) override, e.g. a config
                    // routing to HeadTexture.Height.GivenPath. If the file
                    // doesn't resolve, keep the mesh's current detail state
                    // rather than binding the white fallback — a white detail
                    // sample saturates the face under both blend modes (see
                    // ApplyTexturesToGlMesh's slot-3 demotion).
                    int detailTex = TextureManager.LoadTexture(source);
                    if (detailTex != TextureManager.WhiteTexture)
                    {
                        mesh.DetailTexture = detailTex;
                        mesh.HasDetailMap = true;
                        mesh.IsFaceWithEmptyDetailSlot = false;
                    }
                    else
                    {
                        LogVerbose("CharacterViewer: Detail map override '" + source +
                            "' not found — keeping existing slot 3 state");
                    }
                    RecordTextureSource(mesh, "Detail Map", source);
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

        if (slotsBeforeOverride != null)
        {
            var lockTally = new TextureLockTally();
            foreach (var (mesh, slotsBefore) in slotsBeforeOverride)
                ApplyTextureVisibilityLock(mesh, lockTally, mesh.PresentTextureSlots() & ~slotsBefore);
            LogTextureLockTally(lockTally, "texture overrides");
        }
    }

    /// <summary>
    /// Resolves a texture's origin via the asset resolver and records it on
    /// the mesh for display in the hover tooltip. If the slot already has an
    /// entry, replaces it (used when texture overrides update a slot).
    /// </summary>
    private void RecordTextureSource(GlMesh mesh, string slotLabel, string? gamePath)
        => NifMaterialBinder.RecordTextureSource(_assetResolver, mesh, slotLabel, gamePath);

    // ═══════════════════════════════════════════════════════════════════════
    //  MESH OVERRIDES
    //
    //  A neutral channel that SYNTHESIZES extra renderable shapes from .nif
    //  files the base NPC doesn't carry. The first consumer is an auxiliary
    //  armature on a non-base biped slot (e.g. slot 52) that some mods add to
    //  the actor at runtime by script: its mesh lives only in the host-selected
    //  armature's WorldModel, so the resolver that walks the static
    //  WornArmor never sees it. The same channel later serves outfit and
    //  headgear previews.
    //
    //  Mirrors ApplyTextureOverrides: replace-on-reapply, queue while a rebuild
    //  is in flight, drain on scene commit. Each override loads its NIF,
    //  CPU-skins it to the current skeleton, applies its bundled textures (or
    //  the NIF's own), tints by NIF shader type, and registers the shape under
    //  the override Key with its biped slots for occupancy/hiding.
    // ═══════════════════════════════════════════════════════════════════════

    // Saved original emissive of each currently glow-highlighted base-head shape
    // (see SetHighlightedShapeNames), restored when the highlight moves off it.
    private readonly Dictionary<GlMesh, (bool HasEmissive, System.Numerics.Vector3 Color, float Multiple)>
        _highlightSavedEmissive = new();

    /// <summary>
    /// Live-highlights the base-scene head and hair shapes whose names are in
    /// <paramref name="shapeNames"/> (case-insensitive) with a bright emissive
    /// glow, restoring the original emissive as the highlight moves or clears
    /// (pass null/empty to clear). Covers <c>BodyPart == "Head"</c> (FaceGen
    /// head parts — a host's head-part selector) and
    /// <c>BodyPart == "Hair"</c> (the worn-armor hair-slot ARMA channel — the
    /// host's skin-carried-wig selector). Other meshes and attire overrides are
    /// never touched. Takes effect on the next frame (the viewport draws
    /// continuously); no reload needed.
    /// </summary>
    public void SetHighlightedShapeNames(IEnumerable<string>? shapeNames)
    {
        var want = shapeNames == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(shapeNames, StringComparer.OrdinalIgnoreCase);

        // Self-heal: drop saved entries for meshes a reload already removed.
        if (_highlightSavedEmissive.Count > 0)
        {
            var live = new HashSet<GlMesh>(Renderer.Meshes);
            foreach (var stale in _highlightSavedEmissive.Keys.Where(k => !live.Contains(k)).ToList())
                _highlightSavedEmissive.Remove(stale);
        }

        // Restore any highlighted mesh that's no longer wanted.
        foreach (var kv in _highlightSavedEmissive.ToList())
        {
            var mesh = kv.Key;
            if (mesh.BodyPart is "Head" or "Hair" && want.Contains((mesh.ShapeName ?? string.Empty).Trim())) continue;
            mesh.HasEmissive = kv.Value.HasEmissive;
            mesh.EmissiveColor = kv.Value.Color;
            mesh.EmissiveMultiple = kv.Value.Multiple;
            _highlightSavedEmissive.Remove(mesh);
        }

        if (want.Count == 0) return;

        foreach (var mesh in Renderer.Meshes)
        {
            if (mesh.BodyPart is not ("Head" or "Hair")) continue;
            if (!want.Contains((mesh.ShapeName ?? string.Empty).Trim())) continue;
            if (_highlightSavedEmissive.ContainsKey(mesh)) continue; // already highlighted
            _highlightSavedEmissive[mesh] = (mesh.HasEmissive, mesh.EmissiveColor, mesh.EmissiveMultiple);
            mesh.HasEmissive = true;
            mesh.EmissiveColor = new System.Numerics.Vector3(0.15f, 0.9f, 1.0f); // bright cyan glow
            mesh.EmissiveMultiple = 3.0f;
        }
    }

    /// <summary>
    /// Neutral mesh-override entry. Each <see cref="MeshOverride"/> names a
    /// game-relative <c>.nif</c>, the biped slot(s) it occupies, and an optional
    /// bundled texture set. Replace semantics: this call supersedes the previous
    /// override set (so selecting a different variant / toggling a feature
    /// re-applies cleanly), exactly like <see cref="ApplyTextureOverrides(IEnumerable{TextureOverride})"/>.
    /// Hosts call this directly.
    ///
    /// <para><b>Deferred for interactive hosts.</b> Installing an override set
    /// is GL work — it deletes the previous set's VAOs/VBOs/textures and uploads
    /// the new ones. GL object names are per-context, so that work is only safe
    /// where THIS viewer's context is current, and for a host that drives the
    /// scene from a render callback the only such place is that callback. So
    /// when <see cref="RenderThreadMarshaller"/> says we defer to one, this
    /// method only queues; <see cref="ProcessPendingScene"/> drains it on the
    /// next tick and raises <see cref="MeshOverridesApplied"/>. Callers that
    /// read <see cref="MeshOverrideWarningDetails"/> must wait for that event.
    /// The offscreen renderer (inline marshaller, dedicated thread, single
    /// context) still applies synchronously, which is why
    /// <paramref name="ct"/> remains meaningful there.</para>
    ///
    /// <para><b>Switching NPCs.</b> A set queued while a load is in flight goes in
    /// with the scene of the newest <see cref="LoadAsync"/>, whether it was queued
    /// just before that call or after it: the previous NPC's scene finishing its
    /// upload in between does not take it.</para>
    ///
    /// <para>Before the deferral this ran inline on the caller's thread. With
    /// two host preview popups open — private context each, both rendering from
    /// the WPF UI thread — an attire toggle in one window deleted the OTHER
    /// window's meshes and textures by ID collision, because the context current
    /// on that thread belonged to whichever popup rendered most recently.</para>
    /// </summary>
    public void ApplyMeshOverrides(IEnumerable<MeshOverride> overrides, CancellationToken ct = default)
    {
        var overrideList = overrides as List<MeshOverride> ?? overrides?.ToList() ?? new List<MeshOverride>();

        // Retain unconditionally for the software fallback snapshot, then notify the
        // fallback preview (mirrors ApplyTextureOverrides).
        _lastRequestedMeshOverrides = overrideList;
        RaiseSceneInputsChanged();

        if (!CanApplyMeshOverrides || DefersGlToRenderCallback)
        {
            LogVerbose("CharacterViewer: ApplyMeshOverrides queuing " + overrideList.Count +
                " override(s); reason=" + (CanApplyMeshOverrides ? "render-callback-deferred" : "scene-not-ready") +
                ", meshes=" + _meshesByBodyPart.Count +
                ", texMgr=" + (TextureManager != null) +
                ", rebuildPending=" + _sceneRebuildPending +
                ", meshPaths=" + (_cachedMeshPaths != null));
            _pendingMeshOverrides = overrideList;
            return;
        }

        ApplyMeshOverridesCore(overrideList, ct);
    }

    /// <summary>Scene state <see cref="ApplyMeshOverridesCore"/> needs in place
    /// before it can install anything: a committed scene with meshes, an up
    /// texture manager, no rebuild in flight (those meshes are about to be
    /// destroyed), and cached base paths — the skeleton path comes off them.
    /// Mirrors <see cref="ApplyTextureOverrides(IEnumerable{TextureOverride})"/>'s
    /// gate plus the mesh-paths term.</summary>
    private bool CanApplyMeshOverrides =>
        _meshesByBodyPart.Count > 0 && TextureManager != null
        && !_sceneRebuildPending && _cachedMeshPaths != null;

    /// <summary>The GL half of <see cref="ApplyMeshOverrides"/>. MUST run with
    /// this viewer's GL context current — i.e. either inline on a host that
    /// owns its render thread, or from <see cref="ProcessPendingScene"/>.
    /// Callers are responsible for <see cref="CanApplyMeshOverrides"/>.</summary>
    private void ApplyMeshOverridesCore(List<MeshOverride> overrideList, CancellationToken ct = default)
    {
        // Re-checked rather than assumed: a queued set drains a tick or more
        // after it was requested, and the scene can have been torn down since.
        if (_cachedMeshPaths == null) return;

        LogVerbose("CharacterViewer: ApplyMeshOverrides applying " + overrideList.Count + " override(s)");

        // Replace: tear down shapes the previous override set synthesized, and
        // reset the skipped-asset surface for this fresh pass.
        RemoveAppliedMeshOverrides();
        _meshOverrideWarnings.Clear();

        // Resolve under the load's scope chain so the override NIF / skeleton
        // follow the same loose/BSA/scoped resolution as the base meshes. When
        // we're draining from ProcessPendingScene this nests harmlessly inside
        // that method's own bracket; when called directly post-commit the
        // snapshot fields are null and this is a no-op push.
        using var __scopes = _assetResolver.PushScopes(
            _currentSceneScopes, _currentSceneFolders,
            _currentSceneVanillaLooseOverridesBsa,
            _currentSceneVanillaLooseOverridesModLoose,
            _currentSceneAllowLoadOrderFallback);

        nifly.NifFile? skeletonNif = null;
        string? skelDiskPath = null;
        var lockTally = new TextureLockTally();
        try
        {
            if (!string.IsNullOrWhiteSpace(_cachedMeshPaths.SkeletonPath))
            {
                skelDiskPath = _assetResolver.ResolveAssetPath(_cachedMeshPaths.SkeletonPath);
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

            foreach (var ov in overrideList)
            {
                ct.ThrowIfCancellationRequested();
                if (ov == null || string.IsNullOrWhiteSpace(ov.MeshPath)) continue;
                ApplyOneMeshOverride(ov, skeletonNif, skelDiskPath, lockTally);
            }
        }
        finally
        {
            skeletonNif?.Dispose();
        }
        LogTextureLockTally(lockTally, "mesh overrides");

        // Recompute slot occupancy now that the override shapes are in the scene
        // (e.g. armor hides the base body, headgear hides hair). An auxiliary
        // armature on a free slot collides with nothing, so this is a no-op for
        // that case.
        ResolveSlotVisibility();

        // The apply is complete — hosts can now read MeshOverrideWarningDetails
        // and the installed shape set. Fires on both drain paths (post-commit
        // and quiescent-scene) as well as the inline offscreen path.
        MeshOverridesApplied?.Invoke();
    }

    /// <summary>Loads, skins, textures, and registers one mesh override's
    /// shapes. Surfaces unrenderable shapes (mesh not found, or weighted to a
    /// bone in neither the skeleton nor the mesh) and skeleton-compatibility
    /// problems (bones the mesh expects but the skeleton lacks) on
    /// <see cref="MeshOverrideWarnings"/> instead of crashing or silently
    /// rendering a collapsed or misaligned shape.</summary>
    private void ApplyOneMeshOverride(MeshOverride ov, nifly.NifFile? skeletonNif, string? skelDiskPath,
        TextureLockTally lockTally)
    {
        // Per-override resolution widening (see MeshOverride.AllowLoadOrderFallback).
        // Nested inside ApplyMeshOverrides' scope bracket, so it only flips this one
        // bit and leaves the scope chain intact. Scoped to the WHOLE method rather
        // than the resolve below: the weight-0 companion mesh and every texture this
        // override binds (ApplyTexturesToGlMesh, further down) come from the same
        // out-of-scope mod, and all of them run synchronously on this flow.
        // ORed with the scene-level flag: a scene running engine-order resolution
        // must not have an un-flagged override RESET the ambient value to false.
        using var __loadOrderFallback = _assetResolver.PushLoadOrderFallback(
            ov.AllowLoadOrderFallback || _currentSceneAllowLoadOrderFallback);

        var source = _assetResolver.ResolveAssetSource(ov.MeshPath);
        if (source.ResolvedDiskPath == null)
        {
            _missingMeshPaths.Add(ov.MeshPath);
            _meshOverrideWarnings.Add(new MeshOverrideWarning(MeshOverrideWarningKind.MeshNotFound,
                ov.Key + ": mesh not found (" + ov.MeshPath + ")"));
            LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key +
                "' mesh UNRESOLVED: " + ov.MeshPath);
            return;
        }

        // Referencer scoping for the data-folder-fallback report: a fallback-
        // resolved override NIF already reported ITSELF (the resolve above);
        // everything requested on its behalf below — the weight-0 companion,
        // every texture bind, the linked physics XMLs — is that NIF's internal
        // reference set, so mute those reports. In-scope override NIFs (null
        // token) keep reporting their out-of-scope textures as dependencies.
        using var __fallbackReportMute = source.ViaDataFolderFallback
            ? _assetResolver.PushDataFolderFallbackReportSuppression()
            : null;

        // bipedBodyPart: null disables the dismember-partition slot filter — an
        // auxiliary NIF is the source for exactly one slot and we want all its
        // shapes, not just those carrying a particular partition id.
        var built = _meshBuilder.BuildFromFile(source.ResolvedDiskPath, skeletonNif, skelDiskPath, bipedBodyPart: null);
        if (built.Count == 0)
        {
            _meshOverrideWarnings.Add(new MeshOverrideWarning(MeshOverrideWarningKind.NoRenderableShapes,
                ov.Key + ": no renderable shapes in " + System.IO.Path.GetFileName(ov.MeshPath)));
            LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key + "' produced 0 shapes");
            return;
        }

        // Outfit asset-resolution documentation: the exact disk file loaded (the
        // VFS answer to "which copy of this NIF am I rendering") and
        // the shape inventory as built — [file-block ordinal]'3D name' — the same
        // two identity fields the record's AlternateTextures entries carry, so a
        // capture log can be compared 1:1 against the CK Model Data table / xEdit
        // MO3S entries.
        if (VerboseActive)
            LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key + "' loaded '" +
                source.ResolvedDiskPath + "' -> " + built.Count + " renderable shape(s): " +
                string.Join(", ", built.Select(m => "[" + m.ShapeOrdinal + "]'" + m.ShapeName + "'")));

        // Weight morph: the override NIF is the _1 (weight-100) variant. Its bones
        // and vertices are authored to fit a weight-100 body, so on a body morphed
        // to NpcWeight < 100 it would float (the auxiliary mesh sat low/forward at
        // weight 75). Blend in the _0 companion at t = NpcWeight/100 so the
        // override tracks the same weight morph the base body gets in
        // LoadAllMeshParts.
        string? weight0Path = TryGetWeightZeroPath(ov.MeshPath);
        if (weight0Path != null)
        {
            var weight0Source = _assetResolver.ResolveAssetSource(weight0Path);
            if (weight0Source.ResolvedDiskPath != null)
            {
                var built0 = _meshBuilder.BuildFromFile(weight0Source.ResolvedDiskPath, skeletonNif, skelDiskPath, bipedBodyPart: null);
                BlendWeightMorph(built0, built, NpcWeight / 100f, ov.Key);
            }
            else
            {
                LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key +
                    "' weight-0 '" + weight0Path + "' not found — rendering at full (_1) weight");
            }
        }

        // AlternateTextures matching state (consumed in the per-shape block
        // below): the dangling-name pool is the set of entries eligible for the
        // 3D-index fallback, and altConsumed tracks entries that applied
        // anywhere so the leftovers can be logged instead of failing silently
        // (the pre-fallback failure mode: variant renders in game/CK but
        // untextured here). Matching rules live in AlternateTextureMatching.
        List<AlternateTextureSpec>? altIndexFallbackPool = null;
        HashSet<AlternateTextureSpec>? altConsumed = null;
        Dictionary<string, List<int>>? altShapeOrdinalsByName = null;
        if (ov.AlternateTextures is { Count: > 0 } altSpecsAll)
        {
            altConsumed = new HashSet<AlternateTextureSpec>();
            altIndexFallbackPool = AlternateTextureMatching.DanglingNameEntries(
                altSpecsAll, built.Select(m => m.ShapeName));

            // Null unless this mesh has same-named shapes, in which case the 3D Index breaks the
            // tie so an entry lands on one shape as the engine would, not on every namesake.
            // ShapeOrdinal, not the position in `built`: the biped filter and failed builds leave
            // holes, and these ordinals are compared against the record's NIF-space 3D Index.
            altShapeOrdinalsByName = AlternateTextureMatching.BuildShapeOrdinalsByName(
                built.Select(m => (m.ShapeName, m.ShapeOrdinal)));
            if (altShapeOrdinalsByName != null)
            {
                LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key + "' has duplicate shape " +
                    "name(s) {" + string.Join(", ", altShapeOrdinalsByName.Select(kv =>
                        "'" + kv.Key + "' at " + string.Join("/", kv.Value))) +
                    "} — AlternateTextures entries naming them bind by 3D Index");
            }

            // Manifest as received from the host (slot paths post-rebase, so an
            // absolute path here means the host redirected the TXST into a mod
            // folder), followed by the subset whose 3D Name matched no built
            // shape — the only entries eligible to bind by 3D Index.
            if (VerboseActive)
            {
                LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key +
                    "' AlternateTextures manifest (" + altSpecsAll.Count + " entries):");
                foreach (var spec in altSpecsAll)
                    LogVerbose("CharacterViewer:   altTex [3D index " + spec.ShapeIndex +
                        "] name='" + spec.ShapeName + "' slots {" +
                        string.Join(", ", spec.Textures.OrderBy(kv => kv.Key)
                            .Select(kv => kv.Key + "=" + kv.Value)) + "}");
                LogVerbose("CharacterViewer:   altTex 3D-index fallback pool (name matched no shape): " +
                    (altIndexFallbackPool.Count == 0
                        ? "(empty — every entry name-matched a shape)"
                        : string.Join(", ", altIndexFallbackPool.Select(s =>
                            "[" + s.ShapeIndex + "]'" + s.ShapeName + "'"))));
            }
        }

        int installed = 0;
        foreach (var b in built)
        {
            // Skip a shape weighted to bones that resolve from no source —
            // present in neither the skeleton nor the mesh's own NIF — which
            // would collapse its vertices to the origin. No crash, no bind-pose
            // collapse; surface it so the host can warn. Bones absent from the
            // skeleton but embedded in the mesh NIF do NOT trip this: they render
            // via the mesh-NIF fallback like the base body.
            if (b.UnresolvedSkinBones is { Count: > 0 } unresolved)
            {
                _meshOverrideWarnings.Add(new MeshOverrideWarning(MeshOverrideWarningKind.UnresolvedBones,
                    ov.Key + ": unresolved bone(s) [" +
                    string.Join(", ", unresolved) + "] for shape '" + b.ShapeName + "'"));
                LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key + "' shape '" +
                    b.ShapeName + "' SKIPPED — bones in neither skeleton nor mesh NIF [" +
                    string.Join(", ", unresolved) + "]");
                continue;
            }

            // Skeleton-compatibility check: the shape renders (its bones resolved
            // via the mesh-NIF fallback), but the resolved skeleton is missing
            // bones the mesh expects. The base meshes get those bones from the
            // skeleton while this one falls back to its own copies, so it can be
            // misaligned (this is how a missing skeleton mod manifests — the
            // auxiliary mesh sits in a slightly wrong frame from the body). Warn
            // but still render.
            //
            // Exception: SMP/HDT physics bones (skirt/hair/cloak chains) exist
            // ONLY in the mesh NIF by design — no skeleton ships them; the
            // physics engine animates them at runtime, and the bind-pose
            // fallback render is exactly the authored rest pose. When the NIF
            // links a physics XML, bones that config drives are not evidence
            // of a missing skeleton mod, so they are excluded from the warning
            // (previously every SMP outfit tripped a false "install XPMSSE"
            // warning, which a host persisted as a missing asset and re-staled
            // the mugshot every session). Bones the config does NOT name still
            // warn — an SMP outfit can also be weighted to genuine XPMSSE-only
            // skeleton bones.
            if (b.BonesAbsentFromSkeleton is { Count: > 0 } skelAbsent)
            {
                var skelMissing = FilterPhysicsDrivenBones(ov, b, skelAbsent,
                    source.ResolvedDiskPath, out var stalePhysicsNote);
                if (stalePhysicsNote != null)
                {
                    // Record-equality Contains: several shapes of one NIF share
                    // the same physics chains — one warning per distinct note.
                    var staleWarning = new MeshOverrideWarning(
                        MeshOverrideWarningKind.StalePhysicsConfig, ov.Key + ": " + stalePhysicsNote);
                    if (!_meshOverrideWarnings.Contains(staleWarning))
                        _meshOverrideWarnings.Add(staleWarning);
                    LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key + "' shape '" +
                        b.ShapeName + "' STALE-PHYSICS-CONFIG — " + stalePhysicsNote);
                }
                if (skelMissing.Count > 0)
                {
                    _meshOverrideWarnings.Add(new MeshOverrideWarning(
                        MeshOverrideWarningKind.SkeletonMissingBones,
                        ov.Key + ": the loaded skeleton is missing bone(s) [" +
                        string.Join(", ", skelMissing) + "] this mesh needs — it may be misaligned. " +
                        "Install the skeleton these meshes require (e.g. XPMSSE / XP32 Maximum Skeleton)."));
                    LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key + "' shape '" +
                        b.ShapeName + "' SKELETON-INCOMPATIBLE — skeleton lacks [" +
                        string.Join(", ", skelMissing) + "] (rendered via mesh-NIF fallback, may be misaligned)");
                }
            }

            var glMesh = CreateGlMesh(b);
            glMesh.MeshSource = source;

            // Bundled textures (the selection's slot-N SkinTexture.* / ARMA TXST)
            // override the NIF's own embedded set; null leaves the NIF's own.
            // SKIN-SHAPE GATE: an ARMA's SkinTexture (NAM0) is the
            // engine's per-addon SKIN swap — it replaces the texture set of the
            // addon's skin-shader shapes only (exposed hands/arms on beast-race
            // gauntlets, armor with exposed skin areas). Merging it onto every shape
            // repainted armor material with skin textures (vanilla beast
            // gauntlets, Forsworn Briarheart). Mirrors the base-scene path,
            // which applies TXST skin overrides only to ShaderType-5 shapes.
            var effectiveTextures = new Dictionary<int, string>(b.TexturePaths);
            bool isSkinShapeForFlatTxst = b.ShaderType == 4 || b.ShaderType == 5;
            if (ov.Textures != null && isSkinShapeForFlatTxst)
                foreach (var kv in ov.Textures)
                    effectiveTextures[kv.Key] = kv.Value;

            // Per-shape AlternateTextures (MODS): a distinct TextureSet targeted
            // at one shape of this NIF, matched by 3D Name first with a 3D-index
            // fallback for entries whose name matches no shape — the engine keys
            // on the index, so a mesh whose shapes a rebuild renamed
            // (mesh-editing tool output) still shows its variant in game
            // and the CK; without the fallback it rendered untextured/black here
            // (first seen on a rebuilt third-party armor mesh). Full rules + rationale: AlternateTextureMatching.
            // More specific than the mesh-wide flat Textures above, so it is
            // folded on top (wins per slot for this shape; later entries win
            // within the list).
            IReadOnlyDictionary<int, string>? shapeTxst = null;
            if (ov.AlternateTextures is { Count: > 0 } altSpecs)
            {
                var viaIndex = new List<AlternateTextureSpec>();
                var skippedAmbiguous = new List<AlternateTextureSpec>();
                shapeTxst = AlternateTextureMatching.MatchForShape(
                    altSpecs, altIndexFallbackPool, b.ShapeName, b.ShapeOrdinal,
                    altConsumed, viaIndex, altShapeOrdinalsByName, skippedAmbiguous);
                foreach (var spec in viaIndex)
                {
                    LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key +
                        "' AlternateTextures entry [3D index " + spec.ShapeIndex + ", name '" +
                        spec.ShapeName + "'] applied to shape '" + b.ShapeName +
                        "' by 3D-INDEX fallback — no shape bears the record's name " +
                        "(mesh likely rebuilt/renamed by a mesh-editing tool)");
                }
                foreach (var spec in skippedAmbiguous)
                {
                    LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key +
                        "' AlternateTextures entry [3D index " + spec.ShapeIndex + ", name '" +
                        spec.ShapeName + "'] NOT applied to shape ordinal " + b.ShapeOrdinal +
                        " — another shape of the same name sits at the record's 3D index, and the " +
                        "engine binds the entry there");
                }
                // Per-shape verdict, both directions: which route bound the
                // TXST (or that nothing targeted this shape at all), so a log
                // shows the complete shape-by-shape application table.
                if (VerboseActive)
                {
                    if (shapeTxst != null)
                        LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key + "' shape [" +
                            b.ShapeOrdinal + "]'" + b.ShapeName + "' alt-texture slots via " +
                            (viaIndex.Count > 0 ? "3D-INDEX fallback" : "3D Name match") + ": {" +
                            string.Join(", ", shapeTxst.OrderBy(kv => kv.Key)
                                .Select(kv => kv.Key + "=" + kv.Value)) + "}");
                    else
                        LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key + "' shape [" +
                            b.ShapeOrdinal + "]'" + b.ShapeName + "' matched no AlternateTextures " +
                            "entry — keeps its embedded/base textures");
                }
            }
            else if (ov.ShapeTextures != null && ov.ShapeTextures.TryGetValue(b.ShapeName, out var st))
            {
                // Legacy name-only channel (hosts that don't supply 3D indices).
                shapeTxst = st;
            }
            if (shapeTxst != null)
            {
                foreach (var kv in shapeTxst)
                    effectiveTextures[kv.Key] = kv.Value;
            }

            // Armor skin inheritance. An armor NIF's bare-skin shapes (ShaderType 5,
            // ST_SkinTint -- exposed skin areas baked into a cuirass,
            // textured below with the QNAM skin tint) ship a PLACEHOLDER body diffuse
            // (typically MaleBody_1.dds). The engine paints the ACTOR's race skin onto
            // them -- the same skin the base body uses -- UNLESS the ArmorAddon carries
            // its own SkinTexture (NAM0), which takes precedence per slot. Without this,
            // a distinct-skinned race (e.g. Snow Elf, whose body is MaleBodySnowElf.dds)
            // renders default-tan arms under armor while the face/bare body stay pale.
            // We reuse the host-resolved base-body skin TXST (race skin) already cached
            // in _cachedMeshPaths.TxstTextures: the Body set for body/forearm/calf/feet
            // skin, the Hands set for hand-slot pieces (gauntlets). The ShaderType==5
            // gate keeps this off the metal shapes for free.
            //
            // CAVEAT (intentionally documented): this is the OBSERVED engine result, not
            // behavior taken from authoritative documentation. It was confirmed with
            // Knight-Paladin Gelebor in the Ancient Falmer cuirass -- the ArmorAddon's
            // NAM0 skin texture is empty and the cuirass NIF references MaleBody_1.dds,
            // yet in-game his arms are as pale as his face, so the only possible source
            // is the SnowElfRace skin. This contradicts a fair amount of online "lore"
            // that says armor uses its own baked skin texture. There may be tertiary /
            // advanced engine behavior (per-armor skin swaps, skin-tone interactions,
            // race-specific armatures) we are NOT modeling here. See CHARACTER_PIPELINE.md
            // ("Armor skin inheritance").
            if (b.ShaderType == 5 && _cachedMeshPaths != null)
            {
                // Hands slot bit = 1 << (33 - 30) = 8 in the MeshOverride slot encoding.
                string raceSkinPart = (ov.BipedSlots & (1 << 3)) != 0 ? "Hands" : "Body";
                if (_cachedMeshPaths.TxstTextures.TryGetValue(raceSkinPart, out var raceSkinTxst))
                {
                    bool appliedAny = false;
                    foreach (var (slot, path) in raceSkinTxst)
                        if ((ov.Textures == null || !ov.Textures.ContainsKey(slot))
                            && (shapeTxst == null || !shapeTxst.ContainsKey(slot)))
                        {
                            effectiveTextures[slot] = path;
                            appliedAny = true;
                        }
                    if (appliedAny)
                        LogVerbose("CharacterViewer: armor skin inheritance applied race-skin '" +
                            raceSkinPart + "' TXST to skin shape '" + b.ShapeName + "' of override '" +
                            ov.Key + "' (diffuse=" + (effectiveTextures.TryGetValue(0, out var d0)
                                ? System.IO.Path.GetFileName(d0) : "(none)") + ")");
                }
            }

            // Cull invisible physics/collision proxies (e.g. SMP armor collision
            // bodies). Checked against the post-override texture set so a real
            // diffuse swapped in over the placeholder spares the shape. glMesh is
            // already built here, so dispose it (as the cancellation path does).
            if (IsInvisibleCollisionProxy(b, effectiveTextures))
            {
                LogCollisionProxyCull(b);
                glMesh.Dispose();
                continue;
            }

            // The merged per-slot set this shape will actually try to load
            // (NIF-embedded -> flat ov.Textures -> AlternateTextures -> race-skin
            // inheritance, later wins). Each path's subsequent loose/BSA
            // resolution is logged by the asset resolver as it loads.
            if (VerboseActive)
                LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key + "' shape [" +
                    b.ShapeOrdinal + "]'" + b.ShapeName + "' final texture set {" +
                    string.Join(", ", effectiveTextures.OrderBy(kv => kv.Key)
                        .Select(kv => kv.Key + "=" + kv.Value)) + "}");

            bool isHairTint = false;
            float hairR = 0, hairG = 0, hairB = 0;
            bool isFaceTint = false;
            string? faceTintPath = null;
            ApplyTexturesToGlMesh(glMesh, b, effectiveTextures, _cachedMeshPaths!,
                ref isHairTint, ref hairR, ref hairG, ref hairB,
                ref isFaceTint, ref faceTintPath,
                allowEyeNameMatching: false,
                // An outfit-carried wig arrives through this channel instead of
                // HairMeshPath; it is just as much a worn hair-slot item.
                isWornHairSlotItem: ov.Kind == MeshOverrideKind.Hair ||
                                    string.Equals(ov.Key, "Hair", StringComparison.OrdinalIgnoreCase));

            _textureApplyInfoByMesh[glMesh] = new TextureApplyInfo(
                new Dictionary<int, string>(effectiveTextures),
                isHairTint, hairR, hairG, hairB, isFaceTint, faceTintPath);

            // Kind defaults: Skin lets the shader decide tint, so the auxiliary
            // mesh picks up the body QNAM skin tint like any slot-32 skin shape.
            // Armor / Headgear shapes drop the skin QNAM tint — BUT only the
            // ones that are NOT genuine skin shapes. Some armors bake
            // skin-shader shapes into the armor NIF for exposed areas:
            // ShaderType 5 (ST_SkinTint, SLSF1_FaceGen_RGB_Tint), textured with
            // the body skin. The engine applies the NPC's skin tone to those
            // exactly as it does the slot-32 body, keyed purely on the shader
            // type — it has no notion of "armor vs skin." Stripping their tint
            // here left that skin at the untextured default (pink/beige) so it
            // clashed with a tinted face/hands (e.g. an Orc's arms). Preserve the
            // tint for ShaderType 4/5 skin shapes; only non-skin armor material
            // (leather/metal, ShaderType 0/1/etc.) loses it. Note non-skin
            // shapes never had HasTintColor set in the first place, so this is a
            // no-op for them and only matters as a guard against future regressions.
            bool isSkinShaderShape = b.ShaderType == 4 || b.ShaderType == 5;
            if ((ov.Kind == MeshOverrideKind.Armor || ov.Kind == MeshOverrideKind.Headgear)
                && !b.IsHairTintShader
                && !isSkinShaderShape)
            {
                glMesh.HasTintColor = false;
                glMesh.IsSkinShape = false;
            }

            glMesh.BodyPart = ov.Key;          // so slot-N texture overrides route here
            glMesh.Npc().OverrideKey = ov.Key;       // so a re-apply can tear this down
            glMesh.Npc().BipedSlots = ov.BipedSlots;
            glMesh.Npc().HidesSlots = ov.EffectiveHidesSlots;
            glMesh.Npc().SlotDrawPriority = SlotDrawPriorityForKind(ov.Kind);
            glMesh.ShowWireframe = ShowWireframe;
            ApplyTextureVisibilityLock(glMesh, lockTally);
            Renderer.AddMesh(glMesh);
            installed++;
        }

        // Surface AlternateTextures entries that bound to nothing — before the
        // index fallback this failure was silent and presented as an
        // untextured/black shape that "works in game" (the engine matches by
        // index). Log-only: dangling entries also occur in benign wild data,
        // so this doesn't join MeshOverrideWarnings.
        if (ov.AlternateTextures is { Count: > 0 } specsAll && altConsumed != null)
        {
            foreach (var spec in specsAll)
            {
                if (altConsumed.Contains(spec)) continue;
                LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key +
                    "' AlternateTextures entry [3D index " + spec.ShapeIndex + ", name '" +
                    spec.ShapeName + "'] matched NO shape by name or index — its TextureSet " +
                    "was not applied (mesh shape list/names differ from the record)");
            }
        }

        LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key + "' installed " +
            installed + "/" + built.Count + " shape(s) from " + System.IO.Path.GetFileName(ov.MeshPath) +
            " (slots=" + ov.BipedSlots + ", kind=" + ov.Kind + ")");
    }

    /// <summary>Splits a shape's skeleton-absent bones into physics-driven ones
    /// (named by the mesh NIF's linked SMP/HDT physics XML — expected to live
    /// only in the mesh NIF, so no warning) and genuinely missing ones (returned
    /// for the skeleton-compatibility warning). When the NIF links physics
    /// XML(s) but NONE of them resolve — a stale link in the mod itself (e.g.
    /// the author renamed the config and never updated the NiStringExtraData) —
    /// sibling *.xml files in the mesh's own folder are consulted instead;
    /// bones they name are physics-driven, and <paramref name="stalePhysicsNote"/>
    /// describes the broken link so the caller can surface it as its own
    /// (informational, non-asset) warning. When the NIF links no physics XML at
    /// all, or nothing readable names the bones, every bone is returned
    /// unchanged — unclassifiable stays warned (conservative).</summary>
    private List<string> FilterPhysicsDrivenBones(MeshOverride ov,
        NifMeshBuilder.BuiltMesh b, IReadOnlyList<string> skelAbsent,
        string meshDiskPath, out string? stalePhysicsNote)
    {
        stalePhysicsNote = null;
        if (b.PhysicsXmlPaths is not { Count: > 0 } xmlRefs)
            return new List<string>(skelAbsent);

        // Bone references appear as attribute values throughout the SMP schema
        // (<bone name=...>, per-vertex-shape/per-triangle-shape name=...,
        // constraint bodyA=/bodyB=...), so collect every attribute value of
        // every parseable XML rather than modeling the schema. XMLs that fail
        // to parse (SMP configs are hand-authored) fall back to a whole-name
        // substring scan of the raw text.
        var attributeValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unparsedTexts = new List<string>();
        int readable = 0;
        foreach (var raw in xmlRefs)
        {
            if (!TryNormalizePhysicsXmlPath(raw, ov.MeshPath, out var xmlRelPath)) continue;

            string? diskPath = null;
            try { diskPath = _assetResolver.ResolveAssetSource(xmlRelPath).ResolvedDiskPath; }
            catch { /* treated as unresolved below */ }
            if (diskPath == null)
            {
                LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key + "' physics XML '" +
                    xmlRelPath + "' did not resolve — cannot classify physics bones from it");
                continue;
            }

            if (!TryCollectPhysicsXmlBoneNames(diskPath, ov.Key, attributeValues, unparsedTexts))
                continue;
            readable++;
        }

        // Sibling fallback: every linked config is unresolvable, so the link
        // itself is broken. The real config usually still ships beside the
        // mesh under a different name (observed: Skirt_1.nif linking
        // 'SkirtYXXY.xml' while the mod ships 'SkirtY1.xml'), so scan the
        // mesh's own folder. Only reachable for loose meshes / extracted
        // folders — a BSA-sourced mesh's cache folder simply has no XMLs and
        // the scan is a no-op.
        string? siblingSource = null;
        List<string>? siblingNames = null;
        if (readable == 0)
        {
            foreach (var xmlPath in EnumerateSiblingPhysicsXmls(meshDiskPath))
            {
                if (!TryCollectPhysicsXmlBoneNames(xmlPath, ov.Key, attributeValues, unparsedTexts))
                    continue;
                readable++;
                (siblingNames ??= new List<string>()).Add(System.IO.Path.GetFileName(xmlPath));
            }
            if (readable == 0) return new List<string>(skelAbsent);
            siblingSource = string.Join(", ", siblingNames!);
        }

        var remaining = new List<string>();
        var physicsDriven = new List<string>();
        foreach (var bone in skelAbsent)
        {
            bool isPhysics = attributeValues.Contains(bone) ||
                unparsedTexts.Any(t => t.IndexOf(bone, StringComparison.OrdinalIgnoreCase) >= 0);
            (isPhysics ? physicsDriven : remaining).Add(bone);
        }

        if (physicsDriven.Count > 0)
        {
            // A sibling whose FILENAME matches the linked path means the config
            // is exactly where the link says — the resolver just could not see
            // it (a scope gap, not a mod defect). Only a sibling under a
            // DIFFERENT name evidences a genuinely stale link in the mod.
            bool linkNameShipsBesideMesh = siblingNames != null && xmlRefs.Any(r =>
                siblingNames.Any(s => string.Equals(
                    s, System.IO.Path.GetFileName(r), StringComparison.OrdinalIgnoreCase)));
            if (siblingSource != null)
            {
                stalePhysicsNote = linkNameShipsBesideMesh
                    ? "the mesh links physics config '" + string.Join(", ", xmlRefs) +
                      "' which ships beside the mesh ('" + siblingSource +
                      "') but did not resolve through the asset chain — a viewer resolution gap, " +
                      "not a mod defect. Its physics bone(s) [" + string.Join(", ", physicsDriven) +
                      "] render at their authored rest pose, which is correct for a still portrait; " +
                      "in game the physics config should load normally."
                    : "the mesh links physics config '" + string.Join(", ", xmlRefs) +
                      "' which does not exist (a stale link in the mod itself), but sibling config '" +
                      siblingSource + "' names its physics bone(s) [" + string.Join(", ", physicsDriven) +
                      "] — the preview renders them at their authored rest pose and is correct. " +
                      "In game the outfit's physics likely will not load until the mod fixes the link.";
            }
            LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ov.Key + "' shape '" + b.ShapeName +
                "' " + physicsDriven.Count + " skeleton-absent bone(s) are SMP-physics-driven " +
                "(named by " + (siblingSource == null
                    ? "the mesh's physics XML"
                    : "sibling physics config(s) " + siblingSource +
                      (linkNameShipsBesideMesh
                          ? " — the linked XML exists beside the mesh but did not resolve"
                          : " — the linked XML is stale")) +
                "; they live only in the mesh NIF by design) — " +
                "no skeleton warning for [" + string.Join(", ", physicsDriven) + "]");
        }
        return remaining;
    }

    /// <summary>Reads one physics XML and pours its bone-name evidence into
    /// <paramref name="attributeValues"/> (parseable XML: every attribute value)
    /// or <paramref name="unparsedTexts"/> (hand-authored XML that fails to
    /// parse: raw text for substring scan). False when the file was unreadable
    /// and contributed nothing.</summary>
    private bool TryCollectPhysicsXmlBoneNames(string diskPath, string ovKey,
        HashSet<string> attributeValues, List<string> unparsedTexts)
    {
        string text;
        try { text = File.ReadAllText(diskPath); }
        catch (Exception ex)
        {
            LogVerbose("CharacterViewer: ApplyMeshOverrides '" + ovKey + "' physics XML '" +
                diskPath + "' unreadable (" + ex.Message + ")");
            return false;
        }

        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(text);
            foreach (var el in doc.Descendants())
                foreach (var attr in el.Attributes())
                    attributeValues.Add(attr.Value);
        }
        catch
        {
            unparsedTexts.Add(text);
        }
        return true;
    }

    /// <summary>*.xml files sitting beside a mesh on disk, for the stale-link
    /// sibling fallback. Empty on any IO problem (no folder, no access).</summary>
    private static IEnumerable<string> EnumerateSiblingPhysicsXmls(string meshDiskPath)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(meshDiskPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return Array.Empty<string>();
            return Directory.EnumerateFiles(dir, "*.xml", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Normalizes a physics-XML reference as stored in an
    /// NiStringExtraData into a Data-relative path for the asset resolver.
    /// Handles forward slashes, a leading "…\data\" prefix, and a bare
    /// filename (resolved into the referencing mesh's own Data-relative
    /// folder).</summary>
    private static bool TryNormalizePhysicsXmlPath(string rawValue, string meshGamePath, out string xmlRelPath)
    {
        xmlRelPath = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue)) return false;

        var cleaned = rawValue.Replace('/', '\\').Trim().Trim('"').TrimStart('\\');
        var segs = cleaned.Split('\\', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Strip a leading "…\data\" prefix if the path was stored with one.
        int dataIdx = segs.FindIndex(s => s.Equals("data", StringComparison.OrdinalIgnoreCase));
        if (dataIdx >= 0 && dataIdx + 1 < segs.Count)
            segs = segs.Skip(dataIdx + 1).ToList();

        if (segs.Count == 0) return false;

        if (segs.Count == 1)
        {
            // Bare filename: siblings of the mesh that references it.
            var meshDir = System.IO.Path.GetDirectoryName(meshGamePath.Replace('/', '\\'));
            if (string.IsNullOrEmpty(meshDir)) return false;
            xmlRelPath = meshDir + "\\" + segs[0];
            return true;
        }

        xmlRelPath = string.Join("\\", segs);
        return true;
    }

    /// <summary>Removes every shape a prior <see cref="ApplyMeshOverrides"/>
    /// synthesized (those carry a non-null <see cref="NpcMeshInfo.OverrideKey"/>),
    /// disposing GL resources and dropping their cached texture-apply info.</summary>
    private void RemoveAppliedMeshOverrides()
    {
        var existing = Renderer.Meshes.Where(m => m.Npc().OverrideKey != null).ToList();
        foreach (var m in existing)
        {
            Renderer.RemoveMesh(m);
            _textureApplyInfoByMesh.Remove(m);
            m.Dispose();
        }
    }

    /// <summary>Recomputes per-shape slot-occupancy visibility across the whole
    /// scene: a shape is hidden when some strictly-higher-priority shape
    /// <see cref="NpcMeshInfo.HidesSlots"/> one of its <see cref="NpcMeshInfo.BipedSlots"/>.
    /// Body armor (priority 1) hides the base body (priority 0); headgear
    /// (priority 2) hides hair (priority 0). Only the slot-hiding flag is
    /// touched — missing-texture culling (<see cref="GlMesh.IsRendering"/>) is
    /// left alone, and both combine in <see cref="GlMesh.ShouldRender"/>.</summary>
    private void ResolveSlotVisibility()
    {
        var meshes = Renderer.Meshes;
        foreach (var m in meshes) m.IsHiddenByHost = false;

        foreach (var occluder in meshes)
        {
            if (occluder.Npc().HidesSlots == 0) continue;
            foreach (var m in meshes)
            {
                if (ReferenceEquals(m, occluder)) continue;
                if (m.Npc().SlotDrawPriority < occluder.Npc().SlotDrawPriority
                    && (m.Npc().BipedSlots & occluder.Npc().HidesSlots) != 0)
                {
                    m.IsHiddenByHost = true;
                }
            }
        }

        // Partition hiding inside the shapes still drawn: a worn piece's dismember-partition
        // triangles of a slot another piece covers (an armor skin's calves under boots). Only
        // shapes split across two or more hideable partitions carry the per-triangle data; the
        // renderer re-uploads a shape's index buffer when its mask changes.
        foreach (var m in meshes)
        {
            if (m.TrianglePartitionSlots == null) continue;
            int mask = m.IsHiddenByHost ? 0 : PartitionOcclusion.HiddenSlots(
                m.Npc(), meshes.Where(o => !ReferenceEquals(o, m) && o.ShouldRender).Select(o => o.Npc()));
            if (mask != m.HiddenPartitionSlots && mask != 0)
            {
                LogVerbose("SkyRender: [Partitions] '" + m.ShapeName + "' hides partition slot(s) "
                    + string.Join(",", Enumerable.Range(30, 32).Where(s => (mask & (1 << (s - 30))) != 0)));
            }
            m.HiddenPartitionSlots = mask;
        }
    }

    /// <summary>Slot-occupancy precedence for a mesh-override kind. Skin / Hair /
    /// Other sit with the base shapes at 0; Armor at 1; Headgear at 2.</summary>
    private static int SlotDrawPriorityForKind(MeshOverrideKind kind) => kind switch
    {
        MeshOverrideKind.Armor => 1,
        MeshOverrideKind.Headgear => 2,
        _ => 0,
    };

    /// <summary>Maps a base body-part label to the BipedObjectFlag bit its slot
    /// occupies, so base shapes can be hidden by an overlapping mesh override.</summary>
    private static int BodyPartToBipedFlag(string? bodyPart) => bodyPart switch
    {
        "Head" => 1,      // slot 30
        "Hair" => 2,      // slot 31
        "Body" => 4,      // slot 32
        "Hands" => 8,     // slot 33
        "Feet" => 128,    // slot 37
        "Tail" => 1024,   // slot 40
        _ => 0,
    };

    /// <summary>Per-shape biped-slot tag for a base (non-override) shape. Almost
    /// always just the body-part's slot, but a FaceGen head NIF bundles the face,
    /// eyes, brows AND — for most standalone NPC replacers — the hair into one
    /// "Head" group. Tagging them all slot 30 (Head) means a hood that hides the
    /// hair slot (31) can't reach the baked-in hair, so it clips through. For
    /// non-primary head sub-shapes we derive the slot from the shape's own
    /// dismember partition (e.g. 131 → slot 31) so the resolver hides just the
    /// hair, exactly as body armor hides the base body.
    /// <para>The primary head (face) and any shape lacking head-region partitions
    /// (plain-skinned eyes/brows/mouth) keep the coarse "Head" slot, so they're
    /// never wrongly culled. Only head-region slots are honoured: a head
    /// accessory mis-authored with the body partition (32) is ignored rather than
    /// becoming hideable by body armor — dismember values are not a reliable
    /// shape-role signal for the head region across modder conventions.</para></summary>
    private static int BipedSlotsForBaseShape(NifMeshBuilder.BuiltMesh built, string? bodyPart)
    {
        int groupSlots = BodyPartToBipedFlag(bodyPart);
        if (bodyPart != "Head" || built.IsPrimaryHeadShape) return groupSlots;

        var parts = built.DismemberPartitions;
        if (parts == null || parts.Count == 0) return groupSlots;

        int mask = 0;
        foreach (var p in parts)
        {
            // Head-region slots only: Head(30), Hair(31), LongHair(41),
            // Circlet(42), Ears(43). 1 << (slot - 30) matches BodyPartToBipedFlag.
            switch (NifMeshBuilder.PartitionToBipedSlot(p))
            {
                case 30: mask |= 1 << 0; break;
                case 31: mask |= 1 << 1; break;
                case 41: mask |= 1 << 11; break;
                case 42: mask |= 1 << 12; break;
                case 43: mask |= 1 << 13; break;
            }
        }
        return mask != 0 ? mask : groupSlots;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SHAPE DEFORMATION (host deform hook)
    //
    //  The viewer owns the mechanics of re-deforming the installed body shapes:
    //  the queue-until-installed rule, the per-shape base re-derivation (bind
    //  pose, or the engine's _0/_1 weight blend at the requested weight), the
    //  normal recompute + re-skinning, and the GPU re-upload under the owning
    //  context. What the deformation IS comes from the host, through one
    //  IShapeDeformer.Deform call per shape with the whole position array.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Re-deforms the installed scene's body shapes (<see cref="BodyBaseMeshes"/>) with
    /// <paramref name="deformer"/> at NPC weight <paramref name="weight"/>. For each shape the
    /// viewer starts from its base pose at that weight, calls
    /// <see cref="IShapeDeformer.Deform"/> once with the whole position array, recomputes normals,
    /// re-applies skinning and re-uploads; <see cref="GlMesh.CpuPositions"/> carry the result as
    /// soon as this returns. Sets <see cref="NpcWeight"/>.
    ///
    /// <para>When the body shapes aren't installed yet (<see cref="CanDeformShapes"/> false) the
    /// call is queued and runs when the next scene commits, before <see cref="SceneCommitted"/>;
    /// a later call supersedes a queued one. With <paramref name="retainAsSceneInput"/> (the
    /// default) the deformer is also retained for <see cref="TryGetSceneInputsSnapshot"/> and
    /// <see cref="SceneInputsChanged"/> is raised, whether it applies now, queues, or never
    /// applies (no GL context); pass false for a transient application that must not replace
    /// the retained one.</para>
    /// </summary>
    public void ApplyShapeDeformer(IShapeDeformer deformer, int weight, bool retainAsSceneInput = true)
    {
        if (deformer == null) return;

        // Retain unconditionally for the software fallback snapshot -- including on the queue
        // path below and when GL never started -- then notify the fallback.
        if (retainAsSceneInput)
        {
            _lastRequestedShapeDeform = (deformer, weight);
            RaiseSceneInputsChanged();
        }

        if (!CanDeformShapes)
        {
            _pendingShapeDeform = (deformer, weight, retainAsSceneInput);
            LogVerbose("CharacterViewer: ApplyShapeDeformer QUEUED (label='"
                + (deformer.Label ?? "?") + "', weight=" + weight
                + ", bodyBaseMeshes=" + _cachedBodyMeshes.Count
                + ", sceneRebuildPending=" + _sceneRebuildPending + ")");
            return;
        }

        NpcWeight = Math.Clamp(weight, 0, 100);

        try
        {
            // The deformer's per-application setup (e.g. locating data that sits next to the
            // body NIF). Declining leaves the shapes exactly as they are.
            if (!deformer.Prepare(_cachedBodyNifDiskPath, weight)) return;

            // GL uploads are collected here and issued in one marshalled block after the
            // CPU pass below. ApplyShapeDeformer runs on the host's UI thread, OUTSIDE the
            // control's render callback, so it is the one hot-path place where this VM
            // touches GL without the owning context being current by construction. With a
            // single viewer that was harmless; with concurrent viewers buffer names collide
            // across contexts and an unpinned UpdateVertexData writes into a sibling viewer's
            // VBO. Batching keeps it to one MakeCurrent per apply instead of one per shape.
            // See the contract on RenderThreadMarshaller.
            var pendingUploads = new List<(GlMesh Mesh, float[] VertexData)>();

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
                // reproduced per-call, so changing weight via ApplyShapeDeformer
                // (rather than reloading the NPC) still picks up the correct base
                // body. Shapes without cached _0/_1 (FaceGen head, hairs, etc., or
                // any shape whose _0 didn't pair by name + vert count) fall through
                // to the prior behavior of sourcing directly from BindPosePositions,
                // which carries the load-time blend that matches whatever weight the
                // NPC was loaded at — so the head/body neck stays aligned.
                var (positions, normals) = DeformShape(originalMesh, shapeName, deformer, NpcWeight);

                // Queue the GPU re-upload; issued together below under the owning context.
                var vertexData = GlMeshFactory.Interleave(positions, normals,
                    originalMesh.TextureCoordinates, originalMesh.Tangents, originalMesh.Bitangents,
                    originalMesh.VertexColors);
                pendingUploads.Add((glMesh, vertexData));

                // Update CPU-side positions for hit testing (no GL involved, so it stays
                // out of the marshalled block — pick/measure code reads it immediately
                // after ApplyShapeDeformer returns).
                glMesh.CpuPositions = positions;
            }
            // The CPU positions now carry this deformation (set after the deform, so a declined
            // Prepare above leaves the previous record, which still describes the untouched meshes).
            _currentSceneShapeDeform = (deformer, weight);

            if (pendingUploads.Count > 0)
            {
                _renderThread.Invoke(() =>
                {
                    foreach (var (mesh, data) in pendingUploads)
                    {
                        mesh.UpdateVertexData(data);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            // Surface deformer / skinning / GPU-upload failures with a full stack so the
            // host's log actually shows what went wrong.
            _logger.LogError("CharacterViewer: ApplyShapeDeformer failed for '"
                + (deformer.Label ?? "?") + "' at weight " + NpcWeight + Environment.NewLine
                + ex.ToString());
        }

        // Fire regardless of deformation outcome so subscribers can refresh readouts;
        // a failed deformation leaves CpuPositions in a valid (undeformed) state that
        // is still meaningful to measure.
        ShapesDeformed?.Invoke();
    }

    /// <summary>
    /// Parses <paramref name="headNifPath"/> off-thread and queues the result
    /// for installation on the render thread via <see cref="ProcessPendingScene"/>.
    /// The install step removes current Head shape(s), creates new GlMesh(es),
    /// and re-applies head textures from the cached mesh paths, leaving all
    /// Body/Hands/Feet state untouched. Used by hosts as the fast path when the
    /// same NPC is already loaded — see <see cref="CanRebuildHeadOnly"/>.
    /// </summary>
    public async Task RebuildHeadOnlyAsync(string headNifPath, CancellationToken ct,
        IReadOnlySet<string>? additionalEyeShapeNames = null)
    {
        var headStopwatch = System.Diagnostics.Stopwatch.StartNew();
        LogLoadCheckpoint(headStopwatch, "RebuildHeadOnlyAsync begin (" +
            System.IO.Path.GetFileName(headNifPath) + ")");

        // Same merge as LoadAsync's overrideEyeShapeNames, for the fast path:
        // InstallReplacedHead classifies the new head shapes against
        // _cachedMeshPaths, so an ASSIGNED eyes part baked into headNifPath
        // must be in its EyeShapeNames before the install runs. Union with the
        // existing set (immutable-POCO swap; the render thread reads the
        // reference at install time).
        if (additionalEyeShapeNames is { Count: > 0 } && _cachedMeshPaths != null)
        {
            var mergedEyeNames = new HashSet<string>(_cachedMeshPaths.EyeShapeNames, StringComparer.OrdinalIgnoreCase);
            mergedEyeNames.UnionWith(additionalEyeShapeNames);
            _cachedMeshPaths = _cachedMeshPaths.WithEyeShapeNames(mergedEyeNames);
        }

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
                       _currentSceneVanillaLooseOverridesModLoose,
                       _currentSceneAllowLoadOrderFallback))
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
        var lockTally = new TextureLockTally();
        foreach (var built in meshes)
        {
            // Cull invisible physics/collision proxies before the geometry upload.
            // Head never has TxstTextures overrides, so the NIF texture set is the
            // effective one.
            if (IsInvisibleCollisionProxy(built, built.TexturePaths))
            {
                LogCollisionProxyCull(built);
                continue;
            }

            var glMesh = CreateGlMesh(built);

            var effectiveTextures = new Dictionary<int, string>(built.TexturePaths);
            // Head never has TxstTextures overrides (see ProcessPendingScene's
            // bodyPart != "Head" guard) — nothing to merge in. A body replacement's skin
            // still reaches the face, as on the full scene install.
            ApplyReplacementHeadTextures(built, "Head", _sceneBodyReplacement, effectiveTextures);

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
            glMesh.Npc().BipedSlots = BipedSlotsForBaseShape(built, "Head");
            glMesh.ShowWireframe = ShowWireframe;
            ApplyTextureVisibilityLock(glMesh, lockTally);
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

        LogTextureLockTally(lockTally, "head-only rebuild");
        LogLoadCheckpoint(headStopwatch, "Head install complete (" + meshes.Count +
            " shape(s) from " + System.IO.Path.GetFileName(headNifPath) + ")");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SCENE MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════════

    public void ClearScene()
    {
        Renderer.ClearMeshes();
        // ClearMeshes disposes EVERY mesh in the renderer, guest-overlay shapes included.
        // Tell the guest scene its meshes are gone so it drops the dangling references and
        // re-arms its install — the overlay is meant to survive the host swapping its own
        // NPC, and that swap goes through here.
        NotifyGuestMeshesDestroyed();
        // Drop the per-VM GL texture cache with the scene. It is keyed on
        // game-RELATIVE paths, and a long-lived live-preview VM can load
        // successive scenes under DIFFERENT resolution scope chains where the
        // same relative path maps to different files (the offscreen path is
        // immune only because it builds a fresh VM per render) — a surviving
        // entry would serve the previous scope's pixels to the new scene.
        // Runs under the same GL-context constraint as ClearMeshes above.
        // Same-identity reloads short-circuit before ClearScene, so repeat
        // loads of an unchanged scene still reuse their uploads; resident-
        // cache handles are owned by the resident cache and are not deleted
        // here, only their per-VM lookup entries.
        TextureManager?.ClearCache();
        _meshesByBodyPart.Clear();
        _builtMeshesByBodyPart.Clear();
        _cachedBodyMeshes.Clear();
        _textureApplyInfoByMesh.Clear();
        _cachedBodyNifDiskPath = null;
        _currentLoadedIdentityKey = "";
        _currentHeadMeshOverride = null;
        // _currentSceneScopes/_currentSceneFolders are deliberately NOT cleared
        // here: ProcessPendingScene calls ClearScene at the START of a new
        // scene's install — after LoadAsync has already stored that scene's
        // snapshot — so nulling them here wipes the incoming scene's resolution
        // context and every mod-scoped base texture fails to resolve. Their
        // lifetime is LoadAsync-to-next-LoadAsync (or the load cancel/error
        // path); after a standalone ClearScene the stale snapshot is inert —
        // there are no shapes left for narrow updates to touch.

        SceneCleared?.Invoke();
    }

    private bool _disposed;

    /// <summary>
    /// Releases GL resources (shaders, VBO/VAO, loaded textures), cancels any in-flight
    /// NPC-load async work, and drops scene caches. Called when the owning parent VM
    /// (a host menu or editor) is itself disposed.
    ///
    /// GL delete calls must run while THIS viewer's context is current, so the
    /// teardown goes through <see cref="RenderThreadMarshaller"/>. "No context
    /// current => silent no-op" only holds while a single GL context exists in
    /// the process: with two live viewers the names collide, and deleting under
    /// a sibling's context destroys the sibling's shaders / VAOs / textures
    /// instead of ours. A host with concurrent viewers must therefore install a
    /// context-pinning marshaller; hosts with one viewer keep the
    /// plain dispatch marshaller and are unaffected. We wrap in try/catch so a
    /// stray driver throw doesn't propagate out of the host's dispose chain.
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
        _pendingShapeDeform = null;
        _pendingMeshOverrides = null;
        _pendingHeadReplace = null;
        // Guest overlay: drop the retained request (it pins a full set of BuiltMeshes) and
        // the mesh references. The GL objects themselves are released by Renderer.Dispose
        // below along with every other mesh, so there is nothing to delete individually.
        _guestRequest = null;
        _guestInstallPending = false;
        _guestMeshes.Clear();
        _meshesByBodyPart.Clear();
        _builtMeshesByBodyPart.Clear();
        _cachedBodyMeshes.Clear();
        _textureApplyInfoByMesh.Clear();
        _cachedBodyNifDiskPath = null;
        _cachedMeshPaths = null;

        try
        {
            if (IsGlInitialized)
            {
                _renderThread.Invoke(() =>
                {
                    TextureManager?.Dispose();
                    TextureManager = null;
                    Renderer.Dispose();
                    IsGlInitialized = false;
                });
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

    private List<(string BodyPart, AssetSource? MeshSource, List<NifMeshBuilder.BuiltMesh> Meshes)> LoadAllMeshParts(
        ResolvedNpcMeshPaths meshPaths, CancellationToken ct = default)
    {
        var results = new List<(string, AssetSource?, List<NifMeshBuilder.BuiltMesh>)>();

        // Resolve the skeleton's disk path eagerly (cheap, cached) so it can key the
        // parsed-NIF cache, but DEFER the actual NIF parse until a body part is an
        // actual cache miss. When the offscreen prewarm pipeline has already warmed
        // every body part, all the BuildFromFile calls below hit the cache and this
        // skeleton NIF is never loaded — it was effectively the whole `build` phase
        // on the render thread, so skipping it is the win. skelDiskPath is kept for
        // the cache key regardless of whether the NIF later parses, matching the
        // rule CharacterPreviewCache.PrewarmNpc uses so the keys line up.
        ct.ThrowIfCancellationRequested();
        nifly.NifFile? skeletonNif = null;
        string? skelDiskPath = !string.IsNullOrWhiteSpace(meshPaths.SkeletonPath)
            ? _assetResolver.ResolveAssetPath(meshPaths.SkeletonPath)
            : null;
        if (!string.IsNullOrWhiteSpace(meshPaths.SkeletonPath) && skelDiskPath == null)
            _missingMeshPaths.Add(meshPaths.SkeletonPath);

        bool skeletonLoadAttempted = false;
        // Memoized lazy loader handed to BuildFromFile; invoked only on a real parse.
        nifly.NifFile? LoadSkeleton()
        {
            if (skeletonLoadAttempted) return skeletonNif;
            skeletonLoadAttempted = true;
            if (skelDiskPath != null)
            {
                var sk = new nifly.NifFile();
                if (sk.Load(skelDiskPath) == 0) skeletonNif = sk;
                else { sk.Dispose(); _missingMeshPaths.Add(meshPaths.SkeletonPath); }
            }
            return skeletonNif;
        }

        try
        {
            void TryLoad(string bodyPart, string? gamePath)
            {
                ct.ThrowIfCancellationRequested();
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
                // Referencer scoping for the data-folder-fallback report (see
                // InstallOneShape, which brackets the texture installs of the
                // same NIF): a fallback-resolved part reported itself above;
                // its weight-0 companion resolve and the verbose NIF dump
                // inside BuildFromFile (which re-resolves every referenced
                // texture when render logging is on) are its internals.
                using var __fallbackReportMute = source.ViaDataFolderFallback
                    ? _assetResolver.PushDataFolderFallbackReportSuppression()
                    : null;
                var meshes = _meshBuilder.BuildFromFile(source.ResolvedDiskPath, skeletonNif, skelDiskPath, bodyPart, ct, LoadSkeleton);
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
                // BindPosePositions in place). ApplyShapeDeformer then redoes the _0/_1 lerp
                // from those snapshots at the current NpcWeight on every call, so
                // changing weight through it produces the engine-equivalent base
                // mesh — not the load-time-frozen blend, which would leave later weight
                // changes on the load-time base. The snapshots travel with the BuiltMesh
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
                            ct.ThrowIfCancellationRequested();
                            var meshes0 = _meshBuilder.BuildFromFile(weight0Source.ResolvedDiskPath, skeletonNif, skelDiskPath, bodyPart, ct, LoadSkeleton);

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
                                // pair.
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
    /// Pure geometry step of a shape deformation: takes one shape's bind-pose data and returns
    /// freshly allocated deformed positions + normals. Does no GL work and touches no VM
    /// state, so both the primary scene (<see cref="ApplyShapeDeformer"/>) and the guest overlay
    /// (<c>VM_CharacterViewer.GuestScene.cs</c>) can share it — the two differ only in which
    /// mesh set, deformer and weight they feed in.
    ///
    /// <para>The steps, in order: lerp the <c>_0</c>/<c>_1</c> companion snapshots at
    /// <paramref name="weight"/> (the game's "armor weight morph", reproduced per call so
    /// changing weight without reloading still picks the right base body); hand the whole
    /// position array to <paramref name="deformer"/> (one call per shape; null applies
    /// nothing); recalculate normals against the deformed positions; re-apply skinning.</para>
    ///
    /// <para>Shapes with no cached <c>_0</c>/<c>_1</c> pair (FaceGen head, hair, or any shape
    /// whose <c>_0</c> didn't pair by name + vertex count) source directly from
    /// <c>BindPosePositions</c>, which carries the load-time blend matching the weight the
    /// NPC was loaded at — that is what keeps the head/body neck seam aligned.</para>
    /// </summary>
    private (Vector3[] Positions, Vector3[] Normals) DeformShape(
        NifMeshBuilder.BuiltMesh originalMesh,
        string shapeName,
        IShapeDeformer? deformer,
        int weight)
    {
        var basePositions = originalMesh.BindPosePositions ?? originalMesh.Positions;
        var positions = new Vector3[basePositions.Length];
        var w0 = originalMesh.Weight0BindPosePositions;
        var w1 = originalMesh.Weight1BindPosePositions;
        if (w0 != null && w1 != null
            && w0.Length == basePositions.Length
            && w1.Length == basePositions.Length)
        {
            float t = weight / 100f;
            for (int i = 0; i < basePositions.Length; i++)
            {
                positions[i] = Vector3.Lerp(w0[i], w1[i], t);
            }
        }
        else
        {
            Array.Copy(basePositions, positions, basePositions.Length);
        }

        // Host deformation: one call per shape with the whole array, never per vertex.
        deformer?.Deform(shapeName, originalMesh, positions, weight);

        // Recalculate normals
        var sourceNormals = originalMesh.BindPoseNormals ?? originalMesh.Normals;
        var normals = new Vector3[sourceNormals.Length];
        Array.Copy(sourceNormals, normals, sourceNormals.Length);
        NifMeshBuilder.RecalculateNormals(positions, originalMesh.Indices, normals);

        // Re-apply skinning
        if (originalMesh.Skinning != null)
            NifMeshBuilder.ApplySkinning(positions, normals, originalMesh.Skinning, positions, normals);

        return (positions, normals);
    }

    /// <summary>
    /// Creates a GlMesh from a BuiltMesh, uploading interleaved vertex data and indices.
    /// </summary>
    private static GlMesh CreateGlMesh(NifMeshBuilder.BuiltMesh built) => GlMeshFactory.Create(built);

    /// <summary>Fills <paramref name="effectiveTextures"/> for a skin shape of the
    /// <see cref="BodyReplacement"/> body part: each of the NIF's embedded texture paths that exists
    /// under <paramref name="textureFolder"/> (tried as given and with a <c>textures\</c> prefix), as an
    /// absolute path; nothing from the NPC. Returns false -- render neutral beige -- when the diffuse
    /// (slot 0) isn't found there, since a partial set over another body's diffuse would look worse.</summary>
    private static bool ResolveReplacementBodyTextures(NifMeshBuilder.BuiltMesh built, string? textureFolder,
        Dictionary<int, string> effectiveTextures)
    {
        effectiveTextures.Clear();
        if (string.IsNullOrWhiteSpace(textureFolder)) return false;

        foreach (var (slot, path) in built.TexturePaths)
        {
            if (ResolveTextureUnderFolder(textureFolder, path) is { } resolved) effectiveTextures[slot] = resolved;
        }

        if (effectiveTextures.ContainsKey(0)) return true;
        effectiveTextures.Clear();
        return false;
    }

    /// <summary>The rule <see cref="SkyRender.Character.BodyReplacement.TextureFolder"/> lookups use: a NIF-embedded texture path,
    /// tried under <paramref name="textureFolder"/> as given and with a <c>textures\</c> prefix (NIFs
    /// store both forms). The absolute path when the file exists, else null. Public so a host can predict
    /// what a replacement body will show before loading it (e.g. picker labels).</summary>
    public static string? ResolveTextureUnderFolder(string? textureFolder, string? nifTexturePath)
    {
        if (string.IsNullOrWhiteSpace(textureFolder) || string.IsNullOrWhiteSpace(nifTexturePath)) return null;
        var rel = nifTexturePath.Replace('/', '\\').TrimStart('\\');
        var direct = Path.Combine(textureFolder, rel);
        if (File.Exists(direct)) return direct;
        var prefixed = Path.Combine(textureFolder, "textures", rel);
        return !rel.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase) && File.Exists(prefixed) ? prefixed : null;
    }

    /// <summary>
    /// Gives the head the replacement body's skin too, so the neck has no seam: for the FaceGen head's
    /// face-tint skin shape (<c>ShaderType 4</c>), every texture slot the NIF names -- diffuse, normal,
    /// subsurface, specular, and the detail / complexion map (blankdetailmap, age40, freckles...) -- is
    /// re-pointed to the same path under <see cref="SkyRender.Character.BodyReplacement.TextureFolder"/>
    /// when that file exists there, slot by slot; a slot the folder doesn't have keeps the NPC's file. Skin
    /// mods ship their head textures as a set with the body's, and heads share one UV layout across body
    /// mods, so they always fit. An NPC whose face uses its own texture paths (a follower or replacer's
    /// custom head) matches nothing and keeps them. The FaceGen tint is not a NIF slot and stays the NPC's.
    /// Never with <c>UsesNpcSkin</c> (the NPC's skin already matches) or neutral beige (no folder).
    /// Returns true when it re-pointed any slot.
    /// </summary>
    public static bool ApplyReplacementHeadTextures(NifMeshBuilder.BuiltMesh built, string? bodyPart,
        SkyRender.Character.BodyReplacement? replacement, Dictionary<int, string> effectiveTextures)
    {
        if (replacement == null || replacement.UsesNpcSkin || string.IsNullOrWhiteSpace(replacement.TextureFolder)
            || bodyPart != "Head" || built.ShaderType != 4) return false;

        bool any = false;
        foreach (var (slot, path) in effectiveTextures.ToList())
        {
            if (ResolveTextureUnderFolder(replacement.TextureFolder, path) is { } found)
            {
                effectiveTextures[slot] = found;
                any = true;
            }
        }
        return any;
    }

    /// <summary>Flat neutral skin for a replacement body with no textures of its own: the beige
    /// diffuse, untinted (the NPC's skin tint belongs to the NPC's own textures).</summary>
    private void ApplyNeutralSkin(GlMesh glMesh)
    {
        if (TextureManager == null) return;
        glMesh.DiffuseTexture = TextureManager.NeutralSkinTexture;
        glMesh.HasTintColor = false;
        RecordTextureSource(glMesh, "Diffuse", "(neutral skin - replacement body has no textures of its own)");
    }

    // Derives the weight-0 counterpart path for a NIF path that ends in "_1.nif".
    // Returns null if the input doesn't follow the engine's _0/_1 weight-pair naming.
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

    /// <summary>
    /// Maps a texture/mesh destination path to a body-part routing key, or null when it names
    /// none. Destination paths are the library's generic record-path format: a dotted path into
    /// the NPC's records, where <c>HeadTexture...</c> targets the head, and
    /// <c>...SkinTexture...</c> / <c>...WorldModel...</c> on an armor addon target the biped slot
    /// named in the path, either as <c>BipedObjectFlag.Body</c> / <c>.Hands</c> / <c>.Feet</c> or
    /// numerically as <c>(BipedObjectFlag)N</c> (see <see cref="BipedFlagToBodyPart"/>).
    /// </summary>
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

            // Numeric biped-slot destinations, e.g. an auxiliary slot-52
            // armature: "...HasFlag((BipedObjectFlag)4194304)...". Route them
            // generically by slot number so a slot's SkinTexture.* / WorldModel
            // lands on the synthesized override shape. No semantic per-slot case
            // is needed - the slot number is the only routing key required.
            const string marker = "(BipedObjectFlag)";
            int mi = destination.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (mi >= 0)
            {
                int p = mi + marker.Length;
                int start = p;
                while (p < destination.Length && char.IsDigit(destination[p])) p++;
                if (p > start && int.TryParse(destination.Substring(start, p - start), out int flag))
                    return BipedFlagToBodyPart(flag);
            }
        }
        return null;
    }

    /// <summary>Maps a single BipedObjectFlag bit (the BipedObjectFlag bit
    /// encoding <c>(BipedObjectFlag)N</c>, i.e. <c>1 &lt;&lt; (slot-30)</c>) to a
    /// body-part routing key. Named base parts keep their existing labels so
    /// their textures route to base shapes; everything else (an auxiliary
    /// armature on slot 52, modded slots) becomes a generic "Slot{n}". Returns
    /// null for a zero / multi-bit mask. Public so hosts build a MeshOverride.Key
    /// that matches what <see cref="ParseBodyPart"/> routes textures to.</summary>
    public static string? BipedFlagToBodyPart(int flag)
    {
        switch (flag)
        {
            case 1: return "Head";    // slot 30
            case 2: return "Hair";    // slot 31
            case 4: return "Body";    // slot 32
            case 8: return "Hands";   // slot 33
            case 128: return "Feet";  // slot 37
            case 1024: return "Tail"; // slot 40
        }
        // Single-bit mask → slot 30 + bitIndex (e.g. 4194304 = 1<<22 → "Slot52").
        if (flag > 0 && (flag & (flag - 1)) == 0)
        {
            int bit = System.Numerics.BitOperations.Log2((uint)flag);
            return "Slot" + (30 + bit);
        }
        return null;
    }

    /// <summary>
    /// Maps a destination path (the generic record-path format described on
    /// <see cref="ParseBodyPart"/>) to a NIF texture-set slot by its final texture-set field:
    /// <c>Diffuse</c> = 0, <c>NormalOrGloss</c> = 1, <c>GlowOrDetailMap</c> = 2, <c>Height</c> = 3,
    /// <c>BacklightMaskOrSpecular</c> = 7. Null when no known field is named.
    /// </summary>
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
