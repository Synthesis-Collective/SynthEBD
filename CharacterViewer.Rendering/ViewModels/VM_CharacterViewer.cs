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
// in scope, so it needs the new namespace. (SynthEBD's VMs bind DisposeWith via Noggog instead.)
using System.Reactive.Disposables.Fluent;
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
///
/// <para>Partial: the guest-overlay ("superimpose") scene lives in
/// <c>VM_CharacterViewer.GuestScene.cs</c>. That is a self-contained second scene
/// installed alongside the primary one, so keeping it out of this file avoids
/// interleaving it with the primary scene's install pipeline.</para>
/// </summary>
public partial class VM_CharacterViewer : ViewerVm
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

    /// <summary>Pending neutral mesh-override set to apply after scene setup.
    /// Mirrors <see cref="_pendingTextureOverrides"/>: a call to
    /// <see cref="ApplyMeshOverrides"/> that arrives while a rebuild is in
    /// flight is queued here and drained by <see cref="ProcessPendingScene"/>
    /// once the scene commits. A later call supersedes (replace semantics).</summary>
    private List<MeshOverride>? _pendingMeshOverrides;

    /// <summary>The last real (non-flip) morph applied via <see cref="ApplyMorphSet"/>, kept so the
    /// pending-box "show zeroed" flip can restore the preset after temporarily rendering the
    /// undeformed body. Null until a preset has been applied this session.</summary>
    private (MorphSet Morphs, int Weight)? _lastAppliedMorphSet;

    /// <summary>Label tag on the synthetic empty MorphSet the zeroed-flip applies, so
    /// <see cref="ApplyMorphSet"/> knows not to record it as the "last applied" preset.</summary>
    private const string ZeroedFlipLabel = "(region zeroed-flip)";

    // Durable "last requested" neutral inputs for the software fallback preview
    // (see TryGetSceneInputsSnapshot). Unlike the _pending* fields (drained/cleared
    // once the GL scene commits) and _lastAppliedMorphSet (only set on the GL deform
    // path), these are set UNCONDITIONALLY at the apply-method entry so they survive
    // even when GL never starts and no scene ever commits. Reset per-NPC on load.
    private List<TextureOverride>? _lastRequestedTextureOverrides;
    private List<MeshOverride>? _lastRequestedMeshOverrides;
    private (MorphSet Morphs, int Weight)? _lastRequestedMorphSet;

    /// <summary>NpcIdentity.CacheKey of the NPC whose scene is currently installed
    /// in the renderer. Captured at the end of ProcessPendingScene; cleared by
    /// ClearScene. Used by LoadAsync to short-circuit reloads of the same NPC
    /// when narrow editors (BodySlide preset change, AssetPack subgroup flip)
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
    /// one viewer alive at a time (NPC2's 3D-preview popups — each GLWpfControl
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

    /// <summary>Fired after a <see cref="ApplyMeshOverrides"/> set has actually
    /// been installed into the scene. Hosts that derive UI state from the apply
    /// result (NPC2 mirrors <see cref="MeshOverrideWarningDetails"/> onto its
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
    /// (SynthEBD's render-preview warning line, NPC2's mugshot icon). Each entry
    /// is "&lt;Key&gt;: &lt;reason&gt;" so the UI can name what's wrong.
    /// Recomputed on every apply (and drained queue), so it reflects the current
    /// scene once <see cref="SceneCommitted"/> has fired.</summary>
    public IReadOnlyList<string> MeshOverrideWarnings => _meshOverrideWarnings.ConvertAll(w => w.Message);

    /// <summary>Structured view of <see cref="MeshOverrideWarnings"/> — the same
    /// entries with a <see cref="MeshOverrideWarningKind"/> so hosts can route
    /// each warning (e.g. NPC2 excludes <see cref="MeshOverrideWarningKind.StalePhysicsConfig"/>
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
    /// <summary>SSAO occluder-thickness rejection distance in view-space
    /// units (2.5.20+). See <see cref="GlRenderer.SsaoThickness"/>.</summary>
    public float SsaoThickness { get; set; } = 1.5f;
    /// <summary>Max hair-to-background gap for hair to receive screen AO
    /// (2.5.20+). See <see cref="GlRenderer.SsaoHairGap"/>.</summary>
    public float SsaoHairGap { get; set; } = 0.8f;

    /// <summary>Eye catch-light toggle (2.5.13+).</summary>
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

    /// <summary>Tone-map exposure multiplier (2.5.19+). 1.0 = neutral
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

    /// <summary>Host-settable busy flag for work that happens OUTSIDE the viewer's own
    /// load pipeline but should still show the viewport busy overlay — e.g. SynthEBD's
    /// "Select from Config File" rolling a distribution-valid combination on a background
    /// thread before calling LoadAsync. The host sets it true before its work and false
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

    /// <summary>
    /// True when the BodySlide-classifier toolbar + pick-info controls should actually show:
    /// the host enabled them (<see cref="ShowClassifierControls"/>) AND a live GL viewport
    /// exists. In the software fallback preview (<see cref="RenderingUnavailable"/>) there is no
    /// live viewport to pick in, so the vertex-pick / bounding-box / region-edit controls are
    /// hidden — the fallback is view-only. PropertyChanged.Fody re-raises this whenever either
    /// input changes, so the controls hide the moment GL start fails.
    /// </summary>
    public bool ClassifierControlsAvailable => ShowClassifierControls && !RenderingUnavailable;

    /// <summary>
    /// Master toggle for the viewer toolbar's right-aligned "Compare" button. Hidden by
    /// default and flipped on only by hosts that supply a <see cref="CompareCommand"/> —
    /// in SynthEBD, the three OBody-menu preview hosts (BodySlides, Label by Measurements,
    /// Label by Sliders). Kept separate from the command itself so a host can gate
    /// visibility independently of whether the command happens to be executable.
    /// </summary>
    public bool ShowCompareButton { get; set; } = false;

    /// <summary>
    /// Invoked by the toolbar's "Compare" button. Deliberately typed as the framework's
    /// <see cref="ICommand"/> rather than a host type: this VM lives in the rendering tier
    /// and must not reference SynthEBD (or NPC2) models. SynthEBD points it at the command
    /// that opens <c>Window_BodySlideCompare</c>, seeded from the host menu's current
    /// preset / NPC / weight. Null when no host wired one up.
    /// </summary>
    public ICommand? CompareCommand { get; set; }

    /// <summary>
    /// True when the Compare button should actually show: the host enabled it, wired a
    /// command, and a live GL viewport exists. The software fallback preview
    /// (<see cref="RenderingUnavailable"/>) can't host the side-by-side viewers the button
    /// opens, so it hides there for the same reason the classifier controls do.
    /// PropertyChanged.Fody re-raises this whenever any input changes.
    /// </summary>
    public bool CompareButtonAvailable =>
        ShowCompareButton && CompareCommand != null && !RenderingUnavailable;

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
    public RelayCommand ConfirmPendingBoxAsRegionCommand   { get; private set; } = null!;
    public RelayCommand ConfirmPendingBoxAsRegionDuplicateCommand { get; private set; } = null!;
    public RelayCommand CancelPendingBoxCommand            { get; private set; } = null!;
    public RelayCommand ShrinkAlongViewAxisCommand         { get; private set; } = null!;

    /// <summary>
    /// Gates the viewer's informational log output. Errors (<c>LogError</c>) are never gated --
    /// only the noisy per-frame / per-load diagnostics flowing through <see cref="LogVerbose"/>.
    /// Off by default so selecting a preset doesn't bury the classifier's diagnostic log in
    /// viewer chatter. Toggle from the viewer toolbar when debugging mesh/NPC/lighting issues.
    /// </summary>
    public bool VerboseLog { get; set; } = false;

    /// <summary>True when verbose diagnostics should be emitted: either the user's
    /// <see cref="VerboseLog"/> toolbar toggle, or the shared
    /// <see cref="CharacterViewerLogGate"/> a host forces on for a capture session
    /// (NPC2's per-render RenderLogs files). The helpers (NifMeshBuilder,
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

    /// <summary>Public verbose-gated log used by <see cref="UC_CharacterViewer"/> for
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
    /// (<c>UC_CharacterViewer.TryStartGl</c>) via <see cref="NotifyRenderingUnavailable"/>.
    /// </summary>
    public bool RenderingUnavailable { get; private set; }

    /// <summary>
    /// Test-only global override (hidden Ctrl+Alt+Shift+G shortcut). When true, EVERY viewer VM
    /// behaves as if <see cref="RenderingUnavailable"/> — its loads capture inputs for the
    /// software fallback instead of building a GL scene it can never commit. It is <b>static</b>
    /// on purpose: the editor re-creates the viewer VM when the shortcut fires (a running
    /// GLWpfControl keeps its committed scene, so the toggle spins up a fresh VM), and without a
    /// process-wide flag that new VM would take the normal GL path and hang at "setting up scene".
    /// Real Wine/VM/RDP never needs this — GL startup fails on each control instance
    /// independently, so <see cref="RenderingUnavailable"/> is set per-VM there. Never set in
    /// production; NPC Plugin Chooser 2 leaves it false.
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
    /// Records a one-time, non-fatal GL-startup failure reported by <c>UC_CharacterViewer</c>
    /// when <c>GLWpfControl.Start()</c> throws: latches <see cref="RenderingUnavailable"/>,
    /// updates <see cref="StatusText"/>, and logs the cause through the always-on message channel
    /// (NOT gated by <see cref="VerboseLog"/>, unlike <see cref="LogViewerDiagnostic"/>) so the
    /// reason is visible in a normal log. Deliberately NOT the error channel: in SynthEBD an
    /// error log yanks the UI to the status-log page, but this state is non-fatal, already
    /// surfaced in-place by the viewer's own overlay/status text, and re-latched on every fresh
    /// viewer VM (e.g. per-preset VMs as the fallback preview follows preset switches).
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
    /// overrides, morph). SynthEBD's software fallback preview subscribes to this to
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
            Morphs: _lastRequestedMorphSet?.Morphs,
            MorphWeight: _lastRequestedMorphSet?.Weight ?? 50,
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

        // Seed the persisted render-pipeline settings from the host before their
        // renderer-mirror subscriptions are wired below, so the eager (no-Skip)
        // initial emission pushes the persisted value to the renderer and the
        // toolbar controls open in their saved state. The dedicated write-back
        // block further down (Skip(1)) then persists any user edit across sessions.
        // Shader-troubleshooting debug operators are deliberately session-scoped
        // (not persisted) — matching NPC Plugin Chooser 2.
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
        CopyPicksToClipboardCommand = new RelayCommand(
            canExecute: _ => Picks.Count > 0,
            execute: _ => CopyPicksToClipboard());
        ConfirmPendingBoxCommand = new RelayCommand(
            canExecute: _ => HasPendingBox,
            execute: _ => ConfirmPendingBox());
        ConfirmPendingBoxAsDuplicateCommand = new RelayCommand(
            canExecute: _ => HasPendingBox,
            execute: _ => ConfirmPendingBoxAsDuplicate());
        ConfirmPendingBoxAsRegionCommand = new RelayCommand(
            canExecute: _ => HasPendingBox,
            execute: _ => ConfirmPendingBoxAsRegion());
        ConfirmPendingBoxAsRegionDuplicateCommand = new RelayCommand(
            canExecute: _ => HasPendingBox,
            execute: _ => ConfirmPendingBoxAsRegionDuplicate());
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
    /// When true, a left-click in the viewport toggles a single mesh vertex into/out of the selected
    /// RegionVolume region's curated edit set, and a left-drag rubber-bands a screen rect to bulk-edit
    /// every enclosed vertex (Option B vertex-level region curation). The Add-vs-Remove direction is
    /// <see cref="RegionVertexEditAdditive"/>. Mutually exclusive with orbit and the other pick modes
    /// (the view checks them in order). Selection state lives on the editor VM; the viewer only reports
    /// picks via <see cref="RegionVertexEdited"/> and renders the live selection through the preview
    /// marker channel.
    /// </summary>
    public bool IsRegionVertexEditMode { get; set; } = false;

    /// <summary>Sub-mode for <see cref="IsRegionVertexEditMode"/>: true = additive (force the picked
    /// vertices into the region), false = subtractive (force them out). Bound to an Add/Remove toggle
    /// in the viewer toolbar; read at pick time and stamped onto each emitted
    /// <see cref="RegionVertexEditPick"/>.</summary>
    public bool RegionVertexEditAdditive { get; set; } = true;

    /// <summary>"Add Verts" toggle state: vertex-edit mode on AND additive. Setting true enters add
    /// mode (and clears remove); setting false exits vertex-edit mode entirely. Paired with
    /// <see cref="IsRegionVertexRemoveMode"/> as two mutually-exclusive toolbar toggle buttons — at most
    /// one is on, and both can be off (the default, so the user can orbit). Fody re-raises these when
    /// <see cref="IsRegionVertexEditMode"/> / <see cref="RegionVertexEditAdditive"/> change, so toggling
    /// one button visually releases the other.</summary>
    public bool IsRegionVertexAddMode
    {
        get => IsRegionVertexEditMode && RegionVertexEditAdditive;
        set
        {
            if (value) { RegionVertexEditAdditive = true; IsRegionVertexEditMode = true; }
            else IsRegionVertexEditMode = false;
        }
    }

    /// <summary>"Remove Verts" toggle state: vertex-edit mode on AND subtractive. See
    /// <see cref="IsRegionVertexAddMode"/>.</summary>
    public bool IsRegionVertexRemoveMode
    {
        get => IsRegionVertexEditMode && !RegionVertexEditAdditive;
        set
        {
            if (value) { RegionVertexEditAdditive = false; IsRegionVertexEditMode = true; }
            else IsRegionVertexEditMode = false;
        }
    }

    /// <summary>When the pending box is dismissed (confirmed or cancelled, or torn down on click-away),
    /// leave vertex-edit mode so a left-drag orbits the camera again instead of silently lassoing
    /// vertices. Fody calls this on every <see cref="HasPendingBox"/> change.</summary>
    private void OnHasPendingBoxChanged()
    {
        if (!HasPendingBox) IsRegionVertexEditMode = false;
    }

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

    /// <summary>While a box is pending, render the body in its sliders-0 (undeformed) state so the
    /// author can verify the ground-truth patch the stored box will bake against — the box is stored
    /// in zeroed space, so this shows exactly what gets resolved. Toggling re-renders the body (empty
    /// MorphSet when true; re-applies the last preset when false) without moving the box. Auto-reset
    /// to false whenever a pending box begins / confirms / cancels. Fody calls
    /// <see cref="OnPendingBoxShowZeroedChanged"/> on change.</summary>
    public bool PendingBoxShowZeroed { get; set; }

    private void OnPendingBoxShowZeroedChanged()
    {
        if (_cachedBodyMeshes.Count == 0) return;
        if (PendingBoxShowZeroed)
        {
            // Render the undeformed body at the current weight (empty morphs = no slider deltas).
            ApplyMorphSet(new MorphSet { Label = ZeroedFlipLabel }, NpcWeight);
        }
        else if (_lastAppliedMorphSet is { } last)
        {
            // Restore the preset that was showing before the flip.
            ApplyMorphSet(last.Morphs, last.Weight);
        }
        else
        {
            // No preset was ever applied — just clear to the undeformed base at the current weight.
            ApplyMorphSet(new MorphSet { Label = ZeroedFlipLabel }, NpcWeight);
        }
    }

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

    /// <summary>Per-viewer fan-out for region picks — a pending box confirmed as a RegionVolume
    /// region rather than a key vertex. Reuses the <see cref="KeyVertexBoxPick"/> payload for its
    /// ShapeName + AABB; the <see cref="KeyVertexBoxPick.Criterion"/> field is unused (a region has
    /// no criterion). Parallel to <see cref="KeyVertexBoxPicked"/>.</summary>
    public event Action<KeyVertexBoxPick>? RegionBoxPicked;

    /// <summary>Process-wide fan-out for region picks (parallel to <see cref="AnyKeyVertexBoxPicked"/>).</summary>
    public static event Action<VM_CharacterViewer, KeyVertexBoxPick>? AnyRegionBoxPicked;

    /// <summary>Per-viewer fan-out for a region vertex-edit pick (one click or a rubber-band of
    /// vertices toggled add/remove). Parallel to <see cref="RegionBoxPicked"/>; the editor VM applies it
    /// to the selected region's curated edit set and re-resolves.</summary>
    public event Action<RegionVertexEditPick>? RegionVertexEdited;

    /// <summary>Process-wide fan-out for region vertex-edit picks (parallel to <see cref="AnyRegionBoxPicked"/>).</summary>
    public static event Action<VM_CharacterViewer, RegionVertexEditPick>? AnyRegionVertexEdited;

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

    /// <summary>Result of a region vertex-edit pick: the target shape, the Add-vs-Remove direction, and
    /// the affected vertices given both as original indices (non-authoritative hints) and as their
    /// <b>sliders-0 (zeroed) positions</b> (the durable, renumber-stable identity the editor persists).
    /// One entry for a single-click toggle; many for a rubber-band bulk edit. The zeroed positions are
    /// looked up via <see cref="GetZeroedShapePositions"/> regardless of which body is displayed, so the
    /// stored edit is display-space-independent (the "Show zeroed body" flip needs no per-edit
    /// conversion).</summary>
    public readonly struct RegionVertexEditPick
    {
        public RegionVertexEditPick(
            string shapeName, bool additive,
            IReadOnlyList<int> vertexIndices,
            IReadOnlyList<OpenTK.Mathematics.Vector3> zeroedPositions)
        {
            ShapeName = shapeName ?? "";
            Additive = additive;
            VertexIndices = vertexIndices ?? Array.Empty<int>();
            ZeroedPositions = zeroedPositions ?? Array.Empty<OpenTK.Mathematics.Vector3>();
        }
        public string ShapeName { get; }
        public bool Additive { get; }
        public IReadOnlyList<int> VertexIndices { get; }
        public IReadOnlyList<OpenTK.Mathematics.Vector3> ZeroedPositions { get; }
        public int Count => VertexIndices.Count;
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

    /// <summary>Fans a region pick out to per-viewer and process-wide subscribers (parallels
    /// <see cref="NotifyKeyVertexBoxPicked"/>). The BodyTypeProfile editor subscribes to
    /// <see cref="AnyRegionBoxPicked"/> so a confirmed region box becomes a NamedRegion row.</summary>
    public void NotifyRegionBoxPicked(KeyVertexBoxPick pick)
    {
        RegionBoxPicked?.Invoke(pick);
        AnyRegionBoxPicked?.Invoke(this, pick);
    }

    /// <summary>Fans a region vertex-edit pick out to per-viewer and process-wide subscribers (parallels
    /// <see cref="NotifyRegionBoxPicked"/>). No-ops when the pick is empty.</summary>
    public void NotifyRegionVertexEdited(RegionVertexEditPick pick)
    {
        if (pick.Count == 0) return;
        RegionVertexEdited?.Invoke(pick);
        AnyRegionVertexEdited?.Invoke(this, pick);
    }

    /// <summary>Builds a single-vertex region edit from a ray pick: looks the picked vertex's
    /// <b>zeroed</b> position up via <see cref="GetZeroedShapePositions"/> (so the stored edit is
    /// display-space-independent), stamping the current <see cref="RegionVertexEditAdditive"/> direction.
    /// Falls back to the picked deformed position only when the zeroed snapshot is unavailable. Returns
    /// null when the pick has no shape.</summary>
    public RegionVertexEditPick? BuildRegionVertexEdit(KeyVertexPick pick)
    {
        var shape = pick.Mesh?.ShapeName ?? "";
        if (shape.Length == 0 || pick.VertexIndex < 0) return null;

        var zeroed = GetZeroedShapePositions(shape, NpcWeight);
        var zpos = (zeroed != null && pick.VertexIndex < zeroed.Length) ? zeroed[pick.VertexIndex] : pick.LocalPos;
        return new RegionVertexEditPick(shape, RegionVertexEditAdditive,
            new[] { pick.VertexIndex }, new[] { zpos });
    }

    /// <summary>Builds a bulk region edit from a screen rectangle: projects every rendered mesh's
    /// vertices through the current view-projection (matching <see cref="ComputeBoxFromScreenRect"/>),
    /// keeps the ones inside the rect, buckets by mesh, and takes the mesh with the most hits. The
    /// enclosed vertices' indices + zeroed positions are stamped with the current
    /// <see cref="RegionVertexEditAdditive"/> direction. Returns null on a degenerate rect or when no
    /// vertex fell inside it.</summary>
    public RegionVertexEditPick? BuildRegionVertexEditFromScreenRect(
        float x0, float y0, float x1, float y1, float viewportWidth, float viewportHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0) return null;

        float rectMinX = MathF.Min(x0, x1), rectMaxX = MathF.Max(x0, x1);
        float rectMinY = MathF.Min(y0, y1), rectMaxY = MathF.Max(y0, y1);
        if ((rectMaxX - rectMinX) < 4f || (rectMaxY - rectMinY) < 4f) return null;

        float aspect = viewportWidth / viewportHeight;
        var viewProj = Camera.GetViewMatrix() * Camera.GetProjectionMatrix(aspect);
        float modelScale = Renderer.ModelScale;

        GlMesh? bestMesh = null;
        List<int>? bestIndices = null;

        foreach (var mesh in Renderer.Meshes)
        {
            if (!mesh.IsRendering) continue;
            if (mesh.CpuPositions == null || mesh.CpuPositions.Length == 0) continue;

            var positions = mesh.CpuPositions;
            var indices = new List<int>();
            for (int i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                var world = new OpenTK.Mathematics.Vector4(p.X * modelScale, p.Y * modelScale, p.Z * modelScale, 1f);
                var clip = world * viewProj;
                if (clip.W <= 0f) continue;
                float screenX = (clip.X / clip.W * 0.5f + 0.5f) * viewportWidth;
                float screenY = (1f - (clip.Y / clip.W * 0.5f + 0.5f)) * viewportHeight;
                if (screenX < rectMinX || screenX > rectMaxX) continue;
                if (screenY < rectMinY || screenY > rectMaxY) continue;
                indices.Add(i);
            }

            if (bestIndices == null || indices.Count > bestIndices.Count)
            {
                bestIndices = indices;
                bestMesh = mesh;
            }
        }

        if (bestMesh == null || bestIndices == null || bestIndices.Count == 0) return null;

        var shape = bestMesh.ShapeName ?? "";
        var zeroed = GetZeroedShapePositions(shape, NpcWeight);
        var zpos = new OpenTK.Mathematics.Vector3[bestIndices.Count];
        for (int k = 0; k < bestIndices.Count; k++)
        {
            int idx = bestIndices[k];
            zpos[k] = (zeroed != null && idx < zeroed.Length)
                ? zeroed[idx]
                : new OpenTK.Mathematics.Vector3(bestMesh.CpuPositions![idx].X, bestMesh.CpuPositions![idx].Y, bestMesh.CpuPositions![idx].Z);
        }

        LogVerbose($"CharacterViewer: region vertex-edit rect captured {bestIndices.Count} vertices on '{shape}' (additive={RegionVertexEditAdditive}).");
        return new RegionVertexEditPick(shape, RegionVertexEditAdditive, bestIndices, zpos);
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

        // A fresh box starts on the deformed body (the user just drew it there); clear any leftover
        // zeroed-flip from a previous pending box without re-rendering (the new preset apply, if any,
        // already happened upstream).
        PendingBoxShowZeroed = false;
        HasPendingBox = true;
        // Auto-disable pick mode so the user can rotate the view with left-drag without
        // accidentally drawing a second box over the one they just captured. They can
        // re-enable the toggle to draw a new box.
        IsBoundingBoxPickMode = false;
    }

    /// <summary>Replaces the pending box's six min/max coords in one shot (without disturbing the
    /// shape name, criterion, or pending flag), suppressing the symmetry-mirror side effects during
    /// the multi-setter write. Used by the region edit path to re-express the box into the other body
    /// space when "Show zeroed body" is flipped. No-op when no box is pending.</summary>
    public void SetPendingBoxCoords(OpenTK.Mathematics.Vector3 min, OpenTK.Mathematics.Vector3 max)
    {
        if (!HasPendingBox) return;
        _applyingSymmetry = true;
        try
        {
            PendingBoxMinX = min.X; PendingBoxMinY = min.Y; PendingBoxMinZ = min.Z;
            PendingBoxMaxX = max.X; PendingBoxMaxY = max.Y; PendingBoxMaxZ = max.Z;
        }
        finally
        {
            _applyingSymmetry = false;
        }
    }

    /// <summary>If the body is currently flipped to its zeroed state for box authoring, restore the
    /// preset. Called when a pending box is confirmed or cancelled so the viewer never lingers in the
    /// zeroed state after the box is gone.</summary>
    private void ResetZeroedFlipIfActive()
    {
        if (PendingBoxShowZeroed) PendingBoxShowZeroed = false; // setter restores the preset via OnPendingBoxShowZeroedChanged
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
        ResetZeroedFlipIfActive();
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

    /// <summary>Confirms the pending box as a RegionVolume region rather than a key vertex: fires
    /// <see cref="AnyRegionBoxPicked"/> with the current AABB, then clears the pending state. The
    /// criterion is irrelevant for regions, so the pending criterion is passed through unused.
    /// Parallels <see cref="ConfirmPendingBox"/> but routes to the region channel.</summary>
    public void ConfirmPendingBoxAsRegion()
    {
        if (!HasPendingBox) return;
        var pick = new KeyVertexBoxPick(
            PendingBoxShapeName,
            new OpenTK.Mathematics.Vector3(PendingBoxMinX, PendingBoxMinY, PendingBoxMinZ),
            new OpenTK.Mathematics.Vector3(PendingBoxMaxX, PendingBoxMaxY, PendingBoxMaxZ),
            PendingBoxFinalCriterion);
        NotifyRegionBoxPicked(pick);
        ResetZeroedFlipIfActive();
        HasPendingBox = false;
    }

    /// <summary>Region analog of <see cref="ConfirmPendingBoxAsDuplicate"/>: fires the region pick with
    /// <see cref="KeyVertexBoxPick.IsDuplicate"/> set, so the editor forks a <b>new</b> region row from
    /// this box instead of updating the row currently being edited. The original stays selected and the
    /// box stays on screen (and un-re-seeded), so repeated clicks spawn identical copies off the same box.</summary>
    public void ConfirmPendingBoxAsRegionDuplicate()
    {
        if (!HasPendingBox) return;
        var pick = new KeyVertexBoxPick(
            PendingBoxShapeName,
            new OpenTK.Mathematics.Vector3(PendingBoxMinX, PendingBoxMinY, PendingBoxMinZ),
            new OpenTK.Mathematics.Vector3(PendingBoxMaxX, PendingBoxMaxY, PendingBoxMaxZ),
            PendingBoxFinalCriterion,
            isDuplicate: true);
        NotifyRegionBoxPicked(pick);
        // Leave HasPendingBox = true (like ConfirmPendingBoxAsDuplicate) so the user can keep forking
        // regions or then Confirm as Region to update the originally-edited row.
    }

    public void CancelPendingBox()
    {
        ResetZeroedFlipIfActive();
        HasPendingBox = false;
    }

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

    /// <summary>
    /// Returns a shape's vertex positions for the <b>sliders-0 (undeformed)</b> body at NPC weight
    /// <paramref name="weight"/>, in the same pre-ModelScale local space as <see cref="GetShapePositions"/>,
    /// or null when the shape isn't a cached deformable body mesh. Runs the same base-pose lerp +
    /// skinning pipeline as <see cref="ApplyMorphSet"/> but applies <b>no slider deltas</b> and is
    /// <b>non-destructive</b>: it does not touch <see cref="GlMesh.CpuPositions"/>, re-upload to the
    /// GPU, or fire <see cref="BodySlideApplied"/>, so it can be called mid-authoring without
    /// flicker or re-entrancy.
    ///
    /// <para>This is the topology-stable reference a RegionVolume region's box is authored against:
    /// a box drawn on a deformed preset is converted to the zeroed-space AABB that bounds the same
    /// vertex set, so the baked region tracks every preset (vertex indices are weight- and
    /// preset-independent). The vertex SET is itself weight-independent; only the one-time
    /// box→patch clip uses this snapshot, evaluated at the authoring weight.</para>
    /// </summary>
    public OpenTK.Mathematics.Vector3[]? GetZeroedShapePositions(string shapeName, int weight)
    {
        if (string.IsNullOrEmpty(shapeName)) return null;
        if (!_cachedBodyMeshes.TryGetValue(shapeName, out var originalMesh) || originalMesh == null) return null;

        var basePositions = originalMesh.BindPosePositions ?? originalMesh.Positions;
        if (basePositions == null || basePositions.Length == 0) return null;

        int w = Math.Clamp(weight, 0, 100);
        var positions = new Vector3[basePositions.Length];
        var w0 = originalMesh.Weight0BindPosePositions;
        var w1 = originalMesh.Weight1BindPosePositions;
        if (w0 != null && w1 != null && w0.Length == basePositions.Length && w1.Length == basePositions.Length)
        {
            float t = w / 100f;
            for (int i = 0; i < basePositions.Length; i++) positions[i] = Vector3.Lerp(w0[i], w1[i], t);
        }
        else
        {
            Array.Copy(basePositions, positions, basePositions.Length);
        }

        // No slider deltas — this IS the sliders-0 state. Apply skinning so the result is in the
        // same skinned local space as the deformed CpuPositions the box-pick lasso reads.
        if (originalMesh.Skinning != null)
        {
            var sourceNormals = originalMesh.BindPoseNormals ?? originalMesh.Normals;
            var normals = new Vector3[sourceNormals.Length];
            Array.Copy(sourceNormals, normals, sourceNormals.Length);
            NifMeshBuilder.ApplySkinning(positions, normals, originalMesh.Skinning, positions, normals);
        }

        var dst = new OpenTK.Mathematics.Vector3[positions.Length];
        for (int i = 0; i < positions.Length; i++)
            dst[i] = new OpenTK.Mathematics.Vector3(positions[i].X, positions[i].Y, positions[i].Z);
        return dst;
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

    /// <summary>Replaces the renderer's region-overlay channel (cap-loop markers + edges) with the
    /// supplied geometry. <paramref name="loopVertices"/> render as cap-loop marker spheres and
    /// <paramref name="loopEdges"/> as the loops' edges, both in the same pre-ModelScale mesh-local
    /// space as the markers. Driven by the BodyTypeProfile editor when a region row is selected, so
    /// the user sees exactly which boundary loop(s) the region's box resolved to (1 = chest, 2 =
    /// limb segment, 3+ = the box caught more than intended). Pass null/empty to clear.</summary>
    public void SetRegionOverlay(
        IEnumerable<OpenTK.Mathematics.Vector3>? loopVertices,
        IEnumerable<(OpenTK.Mathematics.Vector3 A, OpenTK.Mathematics.Vector3 B)>? loopEdges,
        OpenTK.Mathematics.Vector3 edgeColor)
    {
        Renderer.RegionCapMarkers.Clear();
        Renderer.RegionOverlayLines.Clear();
        if (loopVertices != null)
            foreach (var v in loopVertices) Renderer.RegionCapMarkers.Add(v);
        if (loopEdges != null)
            foreach (var (a, b) in loopEdges)
                Renderer.RegionOverlayLines.Add(new GlRenderer.MeasurementLineSegment { A = a, B = b, Color = edgeColor });
    }

    /// <summary>Replaces the renderer's "Solid" region-view geometry: the selected region's closed
    /// surface as interleaved triangle vertices (6 floats each: position.xyz + normal.xyz), in the
    /// same pre-ModelScale local space as the markers. Drawn lit + depth-off so the region reads as a
    /// solid magenta object through the body from any angle. Pass null/empty to clear (End-Cap mode).
    /// The caller builds this from the baked region's patch + caps evaluated on the deformed mesh.</summary>
    public void SetRegionSolid(
        IEnumerable<float>? interleavedTriangles,
        IEnumerable<(OpenTK.Mathematics.Vector3 A, OpenTK.Mathematics.Vector3 B)>? wireEdges = null,
        OpenTK.Mathematics.Vector3 wireColor = default)
    {
        Renderer.RegionSolidTriangles.Clear();
        Renderer.RegionWireLines.Clear();
        if (interleavedTriangles != null)
            Renderer.RegionSolidTriangles.AddRange(interleavedTriangles);
        if (wireEdges != null)
            foreach (var (a, b) in wireEdges)
                Renderer.RegionWireLines.Add(new GlRenderer.MeasurementLineSegment { A = a, B = b, Color = wireColor });
    }

    /// <summary>Replaces the region wireframe channel (<see cref="GlRenderer.RegionWireLines"/>) with
    /// <b>per-segment-colored</b> line segments, instead of the single-color wire <see cref="SetRegionSolid"/>
    /// pushes. The editor uses this to recolor an edited region's wireframe: cyan for untouched edges,
    /// green for the half-edges around curated <i>added</i> vertices, and small red/green node crosses
    /// for removed / isolated-added vertices. Pass null/empty to clear. Does not touch the solid-faces or
    /// cap-loop channels, so call it after <see cref="SetRegionSolid"/> / <see cref="SetRegionOverlay"/>.</summary>
    public void SetRegionWireColored(
        IEnumerable<(OpenTK.Mathematics.Vector3 A, OpenTK.Mathematics.Vector3 B, OpenTK.Mathematics.Vector3 Color)>? segments)
    {
        Renderer.RegionWireLines.Clear();
        if (segments == null) return;
        foreach (var s in segments)
            Renderer.RegionWireLines.Add(new GlRenderer.MeasurementLineSegment { A = s.A, B = s.B, Color = s.Color });
    }

    /// <summary>Clears the region overlay (cap-loop markers + edges + the Solid surface + wireframe).
    /// Called when the editor deselects its region row or switches profiles.</summary>
    public void ClearRegionOverlay()
    {
        Renderer.RegionCapMarkers.Clear();
        Renderer.RegionOverlayLines.Clear();
        Renderer.RegionSolidTriangles.Clear();
        Renderer.RegionWireLines.Clear();
    }

    /// <summary>
    /// Lights up the vertices a single BodySlide slider moves as an unlit magnitude heatmap over the
    /// current (deformed) body, feeding <see cref="GlRenderer.SliderHeatmapTriangles"/>. Iterates the
    /// cached deformable body shapes, pulls each shape's sparse per-vertex deltas for
    /// <paramref name="sliderName"/> from the already-loaded .tri/OSD morph context via
    /// <see cref="BodySlideDeformer.TryGetSliderDeltas"/> (no file re-parse), and builds the patch with
    /// <see cref="SliderHeatmapBuilder"/>. Replaces any prior highlight. Clears only (no patch) when the
    /// slider name is blank, no morph context is loaded, or the slider moves nothing above
    /// <paramref name="minDelta"/> — so the "requires a selected preset" contract (a preset apply is what
    /// loads the context) is enforced by the caller. Used by the Label-by-Sliders preview rail.
    /// </summary>
    public void HighlightSliderMorph(string? sliderName, float minDelta = SliderHeatmapBuilder.DefaultMinDelta)
    {
        var output = Renderer.SliderHeatmapTriangles;
        output.Clear();

        if (string.IsNullOrWhiteSpace(sliderName)) return;
        if (_cachedBodyTri == null && (_cachedOsdFiles == null || _cachedOsdFiles.Count == 0)) return;

        int totalTris = 0;
        foreach (var kvp in _cachedBodyMeshes)
        {
            string shapeName = kvp.Key;
            var glMesh = Renderer.Meshes.FirstOrDefault(m =>
                string.Equals(m.ShapeName, shapeName, StringComparison.OrdinalIgnoreCase));
            if (glMesh?.CpuPositions == null || glMesh.CpuPositions.Length == 0) continue;
            if (glMesh.CpuIndices == null || glMesh.CpuIndices.Length < 3) continue;

            var deltas = _bodySlideDeformer.TryGetSliderDeltas(sliderName, shapeName, _cachedBodyTri, _cachedOsdFiles);
            if (deltas == null || deltas.Count == 0) continue;

            totalTris += SliderHeatmapBuilder.Build(glMesh.CpuPositions, glMesh.CpuIndices, deltas, output, minDelta);
        }

        LogVerbose("CharacterViewer: HighlightSliderMorph('" + sliderName + "') -> "
            + totalTris + " triangles across " + _cachedBodyMeshes.Count + " body shape(s)");
    }

    /// <summary>Clears the slider-morph heatmap channel (highlight toggled off, slider/preset deselected,
    /// or NPC reloaded). Cheap; safe to call when nothing is highlighted.</summary>
    public void ClearSliderHighlight()
    {
        Renderer.SliderHeatmapTriangles.Clear();
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
    public void InitializeGl(string? shaderDirectory)
    {
        if (IsGlInitialized) return;

        TextureManager = new GlTextureManager(_previewCache, _logger, ResidentTextureCache, _logGate);
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
        _pendingMeshOverrides = null;
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

        // Head-only rebuild (P2) is independent of full-scene setup and runs
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
            _faceGenHairTint = ComputeFaceGenHairTint(queue);

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

        // Mesh overrides drain after texture/morph state so the synthesized
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

    /// <summary>Uploads one shape's GL mesh + textures and registers it in the
    /// per-body-part dictionaries. Mirrors the inner loop body the single-frame
    /// install used; called once per shape from the sliced install loop.</summary>
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
        // keep reporting (the Modpocalypse-KS case). MUST precede the
        // collision-proxy check below — IsFullyTransparent resolves + decodes
        // the shape's slot-0 diffuse, which for a fallback body NIF's extra
        // shapes is exactly the baseline-internal texture this bracket exists
        // to keep out of the badge (femalebody_etc_v2_1.dds leaked this way).
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

        // Host-designated head-shape hide (e.g. NPC2 antler Remove): the patch
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
        if (shape.TxstOverrides != null && shape.Built.ShaderType == 5)
            foreach (var (slot, path) in shape.TxstOverrides)
                effectiveTextures[slot] = path;

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

        _textureApplyInfoByMesh[glMesh] = new TextureApplyInfo(
            new Dictionary<int, string>(effectiveTextures),
            isHairTint, hairR, hairG, hairB, isFaceTint, faceTintPath);

        glMesh.BodyPart = shape.BodyPart;
        // Tag the base shape with the biped slot(s) it occupies so the
        // slot-occupancy resolver can let a mesh override (armor/headgear) hide
        // it. Derived per-shape (not just from the body-part group) so a hood
        // hides only the baked-in hair sub-shape of a FaceGen head, not the
        // face. Base shapes stay at draw priority 0 and never hide anything.
        glMesh.BipedSlots = BipedSlotsForBaseShape(shape.Built, shape.BodyPart);
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
    /// input — e.g. NPC Plugin Chooser 2 designating an antler head part to hide
    /// via <see cref="ResolvedNpcMeshPaths.HideHeadShapeNames"/>, which is applied
    /// at install time — so the same-NPC reload actually re-installs the head.</summary>
    public void ForceRebuildNextLoad() => _forceRebuildNextLoad = true;

    /// <summary>
    /// Neutral cache-driven load entry. Resolves <paramref name="identity"/>
    /// to a <see cref="ResolvedNpcMeshPaths"/> through the preview cache
    /// (whose <see cref="INpcMeshDataSource"/> adapter does the host-specific
    /// resolution — Mutagen for SynthEBD, NPC2's own scheme for NPC2), then
    /// hands off to <see cref="LoadAsync"/>. NPC Plugin Chooser 2 (and any
    /// other host) calls this directly with their own <see cref="NpcIdentity"/>.
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
            _lastRequestedMorphSet = null;
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

        // Each LoadTexture below can extract a DDS from a BSA and decode+upload
        // it — the finest-grained slow unit in an offscreen render. Check
        // RenderCancellation before each group so a host cancel aborts between
        // textures. No-op for the live preview (token defaults to None).
        RenderCancellation.ThrowIfCancellationRequested();

        // Diffuse (slot 0) — with special handling for hair tint and face tint.
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
        if (built.IsHairTintShader
            && (wornHairSlotTint.HasValue || built.HairTintColor.HasValue ||
                _npcHairColorFromRecord.HasValue)
            && effectiveTextures.TryGetValue(0, out string? hairDiffuse))
        {
            var (tR, tG, tB) = wornHairSlotTint
                               ?? built.HairTintColor
                               ?? _npcHairColorFromRecord!.Value;
            isHairTint = true; hairR = tR; hairG = tG; hairB = tB;
            glMesh.DiffuseTexture = TextureManager.LoadTexture(hairDiffuse);
            glMesh.TintColor = new System.Numerics.Vector3(tR, tG, tB);

            string tintSrc = wornHairSlotTint.HasValue
                ? "worn hair-slot tint from " +
                  (_faceGenHairTint.HasValue ? "the FaceGen's baked hair color" : "the NPC record HCLR") +
                  " (RaceMenu bEnableTintHairSlot emulation" +
                  (built.HairTintColor.HasValue
                      ? ", baked (" + built.HairTintColor.Value.R.ToString("F3")
                        + "," + built.HairTintColor.Value.G.ToString("F3")
                        + "," + built.HairTintColor.Value.B.ToString("F3") + ") overridden"
                      : "") + ")"
                : built.HairTintColor.HasValue
                ? "baked NIF hairTintColor" + (_npcHairColorFromRecord.HasValue
                    ? " (record HCLR=(" + _npcHairColorFromRecord.Value.R.ToString("F3")
                        + "," + _npcHairColorFromRecord.Value.G.ToString("F3")
                        + "," + _npcHairColorFromRecord.Value.B.ToString("F3") + ") unused)"
                    : "")
                : "HCLR (no baked tint)";

            if (built.HasGreyscaleToPaletteFlag)
            {
                glMesh.HasGreyscaleToPalette = true;
                glMesh.GreyscaleToPaletteScale = built.GreyscaleToPaletteScale;
                RecordTextureSource(glMesh, "Diffuse (hair tint, greyscale-to-palette)", hairDiffuse);
                LogVerbose("CharacterViewer: Hair tint (greyscale-to-palette): " +
                    "tint=(" + tR.ToString("F3") + "," + tG.ToString("F3") + "," + tB.ToString("F3") + ")" +
                    " src=" + tintSrc +
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
                    " src=" + tintSrc +
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
        RenderCancellation.ThrowIfCancellationRequested();
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
        RenderCancellation.ThrowIfCancellationRequested();
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

        // Glow map (slot 2 on NON-skin shaders, e.g. BSLSP_GLOWMAP gear like
        // the Nightingale cowl's _emit.dds). Gated on SLSF2_Glow_Map + OwnEmit:
        // without OwnEmit the shader's emissive term never fires (sk_default
        // computes emittance only under hasEmit), so loading would be wasted.
        // Typical authoring is white emissiveColor x 1.0 with the pattern in
        // the map; a black authored emissiveColor legitimately kills the glow
        // (e.g. the Kynreeve helmet's disabled ornament).
        RenderCancellation.ThrowIfCancellationRequested();
        bool glowFlagSet = (built.ShaderFlags2 & (1u << 6)) != 0   // SLSF2_Glow_Map
                        && (built.ShaderFlags1 & (1u << 22)) != 0; // SLSF1_Own_Emit
        if (glowFlagSet && !isSkinShader && effectiveTextures.TryGetValue(2, out string? glowPath))
        {
            int glowTex = TextureManager.LoadTexture(glowPath);
            if (glowTex != TextureManager.WhiteTexture)
            {
                glMesh.GlowTexture = glowTex;
                glMesh.HasGlowMap = true;
                RecordTextureSource(glMesh, "Glow Map", glowPath);
            }
        }

        // Specular map (slot 7). When SLSF2_Back_Lighting is set, slot 7 holds
        // a backlight mask instead of a specular mask (NifSkope sk_msn.frag
        // ignores the slot for specular in that case), so those shapes fall
        // through to the normal-map-alpha specular mask in the shader.
        RenderCancellation.ThrowIfCancellationRequested();
        bool slot7IsBacklight = (built.ShaderFlags2 & (1u << 27)) != 0; // SLSF2_Back_Lighting
        if (!slot7IsBacklight && effectiveTextures.TryGetValue(7, out string? specPath))
        {
            glMesh.SpecularTexture = TextureManager.LoadTexture(specPath);
            glMesh.HasSpecularMap = true;
            // SLSF1_Specular gates the whole specular term in the engine — a
            // shape can ship an authored _s.dds with the flag clear (vanilla
            // Keeper armor body, beggar-robe body proxies) and renders WITHOUT
            // specular in game (AUD-3). Previously forced true here.
            glMesh.HasSpecular = (built.ShaderFlags1 & (1u << 0)) != 0;
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
        // ornaments — 26 such shapes in one audited loadout) that would
        // otherwise take the eye cubemap scale and the eye AO opt-out
        // (AUD-5), so ApplyMeshOverrides passes allowEyeNameMatching: false.
        // ShaderType 16 is always trusted.
        if (built.ShaderType == 16
            || (allowEyeNameMatching
                && (meshPaths.EyeShapeNames.Contains(built.ShapeName.Trim())
                    || built.ShapeName.Contains("Eyes", StringComparison.Ordinal))))
        {
            glMesh.IsEye = true;
        }

        // Environment mapping (SLSF1_Environment_Mapping bit 7, or SLSF1_Eye_Environment_Mapping bit 17)
        RenderCancellation.ThrowIfCancellationRequested();
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
                // Engine-faithful scale selection: only the BSLSP_EYE shader
                // type uses eyeCubemapScale; ENVMAP-typed shapes — including
                // semantic eyes classified IsEye via head-part data — use
                // envMapScale in-game.
                glMesh.UseEyeCubemapScale = built.ShaderType == 16;
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
            int detailTex = TextureManager.LoadTexture(detailPath);
            if (detailTex != TextureManager.WhiteTexture)
            {
                glMesh.DetailTexture = detailTex;
                glMesh.HasDetailMap = true;
            }
            else
            {
                // Slot 3 names a file that doesn't exist in the load order
                // (e.g. female facegen NIFs baking
                // textures\actors\character\female\blankdetailmap.dds, which
                // vanilla only ships in the male directory). The engine treats
                // a missing detail map as "no detail map"; binding the 1x1
                // white fallback instead saturates the face to white under
                // BOTH detail blend modes (engine-style ~x4 multiply and
                // legacy overlay), so treat the slot as empty. Still recorded
                // below so the hover tooltip surfaces the broken path.
                detailSlotPopulated = false;
                LogVerbose("CharacterViewer: Detail map '" + detailPath +
                    "' not found — treating slot 3 as empty (engine behavior)");
            }
            RecordTextureSource(glMesh, "Detail Map", detailPath);
        }
        if (detailFlagSet && !detailSlotPopulated && built.ShaderType == 4 && Renderer.UseBlankDetailFallback)
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

        // Depth-write + material alpha. Set unconditionally (independent of the
        // alpha-test/blend branch below) so the alpha-blend pass can decide, per
        // shape, whether to write depth: solid blended geometry (ZBuffer_Write
        // set, opaque material) occludes what's behind it; overlay decals and
        // translucent materials do not. See GlRenderer Pass 2.
        glMesh.DepthWrite = built.ZBufferWrite;
        glMesh.IsDecal = built.IsDecal;
        glMesh.MaterialAlpha = built.MaterialAlpha;

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
                // Through LogVerbose, not Trace.WriteLine: this is the disposition of a shape whose
                // diffuse could not be resolved, which is exactly what someone reading a RenderLogs
                // capture is looking for. On Trace it reached a debugger and nothing else (AUD-8).
                LogVerbose(
                    $"CharacterViewer: {disposition} shape='{glMesh.ShapeName}' " +
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

        // Retain unconditionally for the software fallback snapshot (even on the queue
        // path below, and even when GL never started), then notify the fallback preview.
        _lastRequestedTextureOverrides = overrideList;
        RaiseSceneInputsChanged();

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
    //  MESH OVERRIDES
    //
    //  A neutral channel that SYNTHESIZES extra renderable shapes from .nif
    //  files the base NPC doesn't carry. The first consumer is an auxiliary
    //  armature on a non-base biped slot (e.g. slot 52) that some mods add to
    //  the actor at runtime by script: its mesh lives only in the selected
    //  asset-pack subgroup's WorldModel, so the resolver that walks the static
    //  WornArmor never sees it. The same channel later serves NPC2's "Include
    //  Default Outfit" / "headgear".
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
    /// head parts — the "Set Antler Head Parts" selector) and
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
    /// override set (so selecting a different subgroup / toggling a feature
    /// re-applies cleanly), exactly like <see cref="ApplyTextureOverrides(IEnumerable{TextureOverride})"/>.
    /// NPC Plugin Chooser 2 (and any future host) calls this directly.
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
    /// <para>Before the deferral this ran inline on the caller's thread. With
    /// two NPC2 preview popups open — private context each, both rendering from
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
                ApplyOneMeshOverride(ov, skeletonNif, skelDiskPath);
            }
        }
        finally
        {
            skeletonNif?.Dispose();
        }

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
    private void ApplyOneMeshOverride(MeshOverride ov, nifly.NifFile? skeletonNif, string? skelDiskPath)
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
        // VFS/BodySlide answer to "which copy of this NIF am I rendering") and
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
            // warning, which NPC2 persisted as a missing asset and re-staled
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
            // SKIN-SHAPE GATE (AUD-1): an ARMA's SkinTexture (NAM0) is the
            // engine's per-addon SKIN swap — it replaces the texture set of the
            // addon's skin-shader shapes only (exposed hands/arms on beast-race
            // gauntlets, revealing armor midriffs). Merging it onto every shape
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
            // (BodySlide/Outfit Studio output) still shows its variant in game
            // and the CK; without the fallback it rendered untextured/black here
            // (first seen as the "black skirt" on BodySlide-built Obi's
            // Nocturnal Noir). Full rules + rationale: AlternateTextureMatching.
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
                        "(mesh likely rebuilt/renamed, e.g. BodySlide output)");
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
            // ST_SkinTint -- the exposed shoulders/arms/midriff baked into a cuirass,
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
            // race-specific armatures) we are NOT modeling here. See RENDERING_PIPELINE.md
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
            // ones that are NOT genuine skin shapes. Revealing armors (e.g. the
            // light Hide cuirass) bake real body-skin shapes into the armor NIF
            // for the exposed midriff / shoulders: ShaderType 5 (ST_SkinTint,
            // SLSF1_FaceGen_RGB_Tint), textured with the body skin (FemaleBody).
            // The engine applies the NPC's skin tone to those exactly as it does
            // the slot-32 body, keyed purely on the shader type — it has no
            // notion of "armor vs skin." Stripping their tint here left the
            // revealed skin at the untextured default (pink/beige) so it clashed
            // with a tinted face/hands (e.g. a green Orc's torso). Preserve the
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
            glMesh.OverrideKey = ov.Key;       // so a re-apply can tear this down
            glMesh.BipedSlots = ov.BipedSlots;
            glMesh.HidesSlots = ov.EffectiveHidesSlots;
            glMesh.SlotDrawPriority = SlotDrawPriorityForKind(ov.Kind);
            glMesh.ShowWireframe = ShowWireframe;
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
    /// folder). Mirrors NPC2's <c>AssetHandler.TryNormalizePhysicsXmlPath</c>.</summary>
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
    /// synthesized (those carry a non-null <see cref="GlMesh.OverrideKey"/>),
    /// disposing GL resources and dropping their cached texture-apply info.</summary>
    private void RemoveAppliedMeshOverrides()
    {
        var existing = Renderer.Meshes.Where(m => m.OverrideKey != null).ToList();
        foreach (var m in existing)
        {
            Renderer.RemoveMesh(m);
            _textureApplyInfoByMesh.Remove(m);
            m.Dispose();
        }
    }

    /// <summary>Recomputes per-shape slot-occupancy visibility across the whole
    /// scene: a shape is hidden when some strictly-higher-priority shape
    /// <see cref="GlMesh.HidesSlots"/> one of its <see cref="GlMesh.BipedSlots"/>.
    /// Body armor (priority 1) hides the base body (priority 0); headgear
    /// (priority 2) hides hair (priority 0). Only the slot-hiding flag is
    /// touched — missing-texture culling (<see cref="GlMesh.IsRendering"/>) is
    /// left alone, and both combine in <see cref="GlMesh.ShouldRender"/>.</summary>
    private void ResolveSlotVisibility()
    {
        var meshes = Renderer.Meshes;
        foreach (var m in meshes) m.HiddenBySlotOccupancy = false;

        foreach (var occluder in meshes)
        {
            if (occluder.HidesSlots == 0) continue;
            foreach (var m in meshes)
            {
                if (ReferenceEquals(m, occluder)) continue;
                if (m.SlotDrawPriority < occluder.SlotDrawPriority
                    && (m.BipedSlots & occluder.HidesSlots) != 0)
                {
                    m.HiddenBySlotOccupancy = true;
                }
            }
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

        // Retain the real (non zeroed-flip) morph unconditionally for the software fallback
        // snapshot — including on the queue path below and when GL never started, neither of
        // which reaches the _lastAppliedMorphSet write further down. Then notify the fallback.
        if (morphs != null && morphs.Label != ZeroedFlipLabel)
        {
            _lastRequestedMorphSet = (morphs, weight);
            RaiseSceneInputsChanged();
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

        // Remember the last real morph applied so the pending-box "show zeroed" flip can restore the
        // preset after temporarily rendering the undeformed body. Skipped for the synthetic zeroed
        // apply itself (tagged label) so flipping back doesn't restore "zeroed" as the preset.
        if (morphs.Label != ZeroedFlipLabel)
            _lastAppliedMorphSet = (morphs, weight);

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

            // GL uploads are collected here and issued in one marshalled block after the
            // CPU pass below. ApplyMorphSet runs on the host's UI thread, OUTSIDE the
            // control's render callback, so it is the one hot-path place where this VM
            // touches GL without the owning context being current by construction. With a
            // single viewer that was harmless; with concurrent viewers (the BodySlide
            // Compare window) buffer names collide across contexts and an unpinned
            // UpdateVertexData writes into a sibling viewer's VBO. Batching keeps it to one
            // MakeCurrent per apply instead of one per shape. See the contract on
            // RenderThreadMarshaller.
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
                // reproduced per-call, so changing BodySlide weight via ApplyMorphSet
                // (rather than reloading the NPC) still picks up the correct base
                // body. Shapes without cached _0/_1 (FaceGen head, hairs, etc., or
                // any shape whose _0 didn't pair by name + vert count) fall through
                // to the prior behavior of sourcing directly from BindPosePositions,
                // which carries the load-time blend that matches whatever weight the
                // NPC was loaded at — so the head/body neck stays aligned.
                var (positions, normals) = DeformShape(
                    originalMesh, shapeName, morphs, NpcWeight, _cachedBodyTri, _cachedOsdFiles);

                // Queue the GPU re-upload; issued together below under the owning context.
                var vertexData = BuildInterleavedVertexData(positions, normals,
                    originalMesh.TextureCoordinates, originalMesh.Tangents, originalMesh.Bitangents,
                    originalMesh.VertexColors);
                pendingUploads.Add((glMesh, vertexData));

                // Update CPU-side positions for hit testing (no GL involved, so it stays
                // out of the marshalled block — pick/measure code reads it immediately
                // after ApplyMorphSet returns).
                glMesh.CpuPositions = positions;
            }

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
            glMesh.BipedSlots = BipedSlotsForBaseShape(built, "Head");
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
        // ClearMeshes disposes EVERY mesh in the renderer, guest-overlay shapes included.
        // Tell the guest scene its meshes are gone so it drops the dangling references and
        // re-arms its install — the overlay is meant to survive the host swapping its own
        // preset/NPC, and that swap goes through here.
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
        _cachedOsdFiles = null;
        _cachedBodyNifDiskPath = null;
        _cachedBodyTri = null;
        BodyTriMissing = false;
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
    }

    private bool _disposed;

    /// <summary>
    /// Releases GL resources (shaders, VBO/VAO, loaded textures), cancels any in-flight
    /// NPC-load async work, and drops scene caches. Called when the owning parent VM
    /// (e.g. VM_BodyGenTemplateMenu, VM_BodySlideSetting) is itself disposed — which
    /// in turn happens when its grandparent (e.g. a BodyGen config being swapped) is
    /// torn down.
    ///
    /// GL delete calls must run while THIS viewer's context is current, so the
    /// teardown goes through <see cref="RenderThreadMarshaller"/>. "No context
    /// current => silent no-op" only holds while a single GL context exists in
    /// the process: with two live viewers the names collide, and deleting under
    /// a sibling's context destroys the sibling's shaders / VAOs / textures
    /// instead of ours. A host with concurrent viewers must therefore install a
    /// context-pinning marshaller (NPC2 does); hosts with one viewer keep the
    /// plain dispatch marshaller and are unaffected. We wrap in try/catch so a
    /// stray driver throw doesn't propagate out of the dispose chain and bring
    /// down the settings load.
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
        _pendingMeshOverrides = null;
        _pendingHeadReplace = null;
        // Guest overlay: drop the retained request (it pins a full set of BuiltMeshes) and
        // the mesh references. The GL objects themselves are released by Renderer.Dispose
        // below along with every other mesh, so there is nothing to delete individually.
        _guestRequest = null;
        _guestBodyTri = null;
        _guestInstallPending = false;
        _guestMeshes.Clear();
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

    // ToMorphSet moved to SynthEbdViewerHostState (Phase B2c.2) — it
    // references SynthEBD's BodySlideSetting and BodySlideSlider types.

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
    /// <summary>
    /// Pure geometry step of a BodySlide apply: takes one shape's bind-pose data and returns
    /// freshly allocated deformed positions + normals. Does no GL work and touches no VM
    /// state, so both the primary scene (<see cref="ApplyMorphSet"/>) and the guest overlay
    /// (<c>VM_CharacterViewer.GuestScene.cs</c>) can share it — the two differ only in which
    /// mesh set, .tri/OSD context and weight they feed in.
    ///
    /// <para>The steps, in order: lerp the <c>_0</c>/<c>_1</c> companion snapshots at
    /// <paramref name="weight"/> (the game's "armor weight morph", reproduced per call so
    /// changing weight without reloading still picks the right base body); apply the slider
    /// deltas, preferring the topology-matched <paramref name="bodyTri"/> over
    /// <paramref name="osdFiles"/>; recalculate normals against the deformed positions;
    /// re-apply skinning.</para>
    ///
    /// <para>Shapes with no cached <c>_0</c>/<c>_1</c> pair (FaceGen head, hair, or any shape
    /// whose <c>_0</c> didn't pair by name + vertex count) source directly from
    /// <c>BindPosePositions</c>, which carries the load-time blend matching the weight the
    /// NPC was loaded at — that is what keeps the head/body neck seam aligned.</para>
    /// </summary>
    private (Vector3[] Positions, Vector3[] Normals) DeformShape(
        NifMeshBuilder.BuiltMesh originalMesh,
        string shapeName,
        MorphSet morphs,
        int weight,
        BodyTriFile? bodyTri,
        List<OsdFile>? osdFiles)
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

        // Apply deformation -- prefer .tri (topology-matched, no LCP stripping),
        // fall back to OSD for meshes without "Build Morphs" output.
        if (bodyTri != null)
        {
            _bodySlideDeformer.ApplyDeformationFromTri(positions, morphs, weight, bodyTri, shapeName);
        }
        else if (osdFiles != null && osdFiles.Count > 0)
        {
            _bodySlideDeformer.ApplyDeformation(positions, morphs, weight, osdFiles, shapeName);
        }

        // Recalculate normals
        var sourceNormals = originalMesh.BindPoseNormals ?? originalMesh.Normals;
        var normals = new Vector3[sourceNormals.Length];
        Array.Copy(sourceNormals, normals, sourceNormals.Length);
        BodySlideDeformer.RecalculateNormals(positions, originalMesh.Indices, normals);

        // Re-apply skinning
        if (originalMesh.Skinning != null)
            NifMeshBuilder.ApplySkinning(positions, normals, originalMesh.Skinning, positions, normals);

        return (positions, normals);
    }

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

    /// <summary>Maps a single BipedObjectFlag bit (the asset-pack
    /// <c>(BipedObjectFlag)N</c> encoding, i.e. <c>1 &lt;&lt; (slot-30)</c>) to a
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
