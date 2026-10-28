using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows;

namespace SynthEBD.BodyShapeViewer;

/// <summary>
/// SynthEBD's body-shape tooling for one <see cref="VM_CharacterViewer"/>: BodySlide morphs (sibling
/// <c>.tri</c> / OSD slider data), key-vertex / bounding-box / region-vertex picking and the pending-box
/// workflow, the zeroed-shape accessors, and the measurement / region / slider-heatmap overlays
/// (<see cref="BodyShapeOverlay"/>, registered in the viewer's <see cref="GlRenderer.Overlays"/>).
///
/// <para>Attaches to the viewer through its neutral seams only: morphs reach the meshes through
/// <see cref="VM_CharacterViewer.ApplyShapeDeformer"/> (an <see cref="IShapeDeformer"/> whose
/// <c>Deform</c> applies the slider deltas), base meshes come from
/// <see cref="VM_CharacterViewer.BodyBaseMeshes"/>, positions from the shape geometry accessors, and
/// scene lifetime from <see cref="VM_CharacterViewer.ShapesDeformed"/>,
/// <see cref="VM_CharacterViewer.SceneCleared"/> and <see cref="VM_CharacterViewer.GlContextReset"/>.</para>
///
/// <para>One instance per viewer, found with <see cref="For"/>. It is also stored in
/// <see cref="VM_CharacterViewer.HostData"/>, so the viewer's views bind to it as
/// <c>{Binding HostData.X}</c>. SynthEBD's DI attaches it to every viewer it creates (MainModule);
/// <see cref="For"/> attaches lazily for viewers created elsewhere (the offscreen renderer's
/// per-request viewers, reached from <see cref="Rendering.Offscreen.NpcRenderRequest.BeforeDraw"/>).</para>
/// </summary>
public sealed class BodyShapeViewerExtension : VM
{
    // ═══════════════════════════════════════════════════════════════════════
    //  ATTACHMENT
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly ConditionalWeakTable<VM_CharacterViewer, BodyShapeViewerExtension> _byViewer = new();
    private static Func<BodySlideDeformer>? _deformerFactory;
    private static Func<BodyTriFileParser>? _triParserFactory;
    private static Func<ICharacterViewerLogger>? _loggerFactory;
    private static Func<CharacterViewerLogGate>? _logGateFactory;

    /// <summary>Wires the shared services <see cref="For"/> uses when it has to attach lazily. Called once
    /// at startup (MainModule's build callback); lazy delegates, resolved on first use.</summary>
    public static void Configure(Func<BodySlideDeformer> deformer, Func<BodyTriFileParser> triParser,
        Func<ICharacterViewerLogger> logger, Func<CharacterViewerLogGate> logGate)
    {
        _deformerFactory = deformer;
        _triParserFactory = triParser;
        _loggerFactory = logger;
        _logGateFactory = logGate;
    }

    /// <summary>The extension attached to <paramref name="viewer"/>, attaching one from the
    /// <see cref="Configure"/>d services if there is none yet.</summary>
    public static BodyShapeViewerExtension For(VM_CharacterViewer viewer)
    {
        if (viewer == null) throw new ArgumentNullException(nameof(viewer));
        lock (_byViewer)
        {
            if (_byViewer.TryGetValue(viewer, out var existing)) return existing;
        }
        if (_deformerFactory == null || _triParserFactory == null || _loggerFactory == null || _logGateFactory == null)
        {
            throw new InvalidOperationException(
                "BodyShapeViewerExtension.Configure() must be called before the body-shape viewer is used.");
        }
        return Attach(viewer, _deformerFactory(), _triParserFactory(), _loggerFactory(), _logGateFactory());
    }

    /// <summary>Attaches an extension to <paramref name="viewer"/> with explicit services (hosts without
    /// <see cref="Configure"/>, e.g. test harnesses). Returns the existing one if already attached.</summary>
    public static BodyShapeViewerExtension Attach(VM_CharacterViewer viewer, BodySlideDeformer deformer,
        BodyTriFileParser triParser, ICharacterViewerLogger logger, CharacterViewerLogGate logGate)
    {
        if (viewer == null) throw new ArgumentNullException(nameof(viewer));
        lock (_byViewer)
        {
            if (_byViewer.TryGetValue(viewer, out var existing)) return existing;
            var created = new BodyShapeViewerExtension(viewer, deformer, triParser, logger, logGate);
            _byViewer.Add(viewer, created);
            return created;
        }
    }

    private readonly VM_CharacterViewer _viewer;
    private readonly BodySlideDeformer _bodySlideDeformer;
    private readonly BodyTriFileParser _bodyTriFileParser;
    private readonly ICharacterViewerLogger _logger;
    private readonly CharacterViewerLogGate _logGate;

    private BodyShapeViewerExtension(VM_CharacterViewer viewer, BodySlideDeformer deformer,
        BodyTriFileParser triParser, ICharacterViewerLogger logger, CharacterViewerLogGate logGate)
    {
        _viewer = viewer;
        _bodySlideDeformer = deformer;
        _bodyTriFileParser = triParser;
        _logger = logger;
        _logGate = logGate;

        _viewer.Renderer.Overlays.Add(Overlay);
        _viewer.HostData = this;

        _viewer.ShapesDeformed += OnShapesDeformed;
        _viewer.SceneCleared += OnSceneCleared;
        _viewer.GlContextReset += OnGlContextReset;
        // ClassifierControlsAvailable reads the viewer's RenderingUnavailable, which Fody can't see
        // from this class, so forward that one change by hand.
        _viewer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VM_CharacterViewer.RenderingUnavailable))
                ManuallyRaisePropertyChanged(nameof(ClassifierControlsAvailable));
        };

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

        // Per-axis symmetry mirror: when the user edits one side of a locked axis, mirror
        // the opposite side about 0 so the box stays centered on the symmetry plane. Guarded
        // by _applyingSymmetry so the mirror write doesn't re-enter the handler. We subscribe
        // to the raw PropertyChanged event (rather than six WhenAnyValue chains) so the write
        // ordering stays deterministic inside the guard window.
        PropertyChanged += OnPendingBoxPropertyChanged;
    }

    /// <summary>The viewer this extension is attached to.</summary>
    public VM_CharacterViewer Viewer => _viewer;

    /// <summary>Overlay channels of the body-shape authoring tools (key vertices, regions,
    /// measurements, slider heatmap), registered in the viewer's renderer overlay list.</summary>
    public BodyShapeOverlay Overlay { get; } = new();

    /// <summary>Routes an informational line to the viewer's logger only when verbose logging is on
    /// (the viewer's toolbar toggle or the shared log gate), like the viewer's own diagnostics.</summary>
    private void LogVerbose(string message)
    {
        if (_viewer.VerboseLog || (_logGate != null && _logGate.Verbose)) _logger?.LogMessage(message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BODYSLIDE MORPHS
    // ═══════════════════════════════════════════════════════════════════════

    // Kill-switch for the BodySlide deformation path. Originally set true while the
    // neck seam was under investigation; flipped back to false once the viewer was
    // integrated into the OBody editor. Kept as a mutable static (not const) so the
    // branch remains live code and this can be toggled again without code changes
    // if the seam or another deformation artifact returns.
    private static bool _bodySlideDisabled = false;

    /// <summary>True when the BodySlide kill-switch is on: <see cref="ApplyMorphSet"/> then never deforms, so
    /// <see cref="CurrentSceneMorph"/> never follows a request.</summary>
    public static bool BodySlideDisabled => _bodySlideDisabled;

    /// <summary>OSD context set via <see cref="SetMorphContext"/>; dropped with the scene.</summary>
    private List<OsdFile>? _cachedOsdFiles;

    /// <summary>Parsed sibling .tri for the current body NIF, cached across preset/weight
    /// changes so we don't re-parse on every RefreshPreview. Null when no .tri is
    /// present next to the NIF -- in that case we fall back to the OSD path.</summary>
    private BodyTriFile? _cachedBodyTri;

    /// <summary>Set when the sibling-.tri probe for the current scene's body NIF found nothing or failed to
    /// parse, so it isn't re-probed on every preset/weight change. Reset with the scene.</summary>
    private bool _bodyTriProbeAbandoned;

    /// <summary>The last real (non-flip) morph applied via <see cref="ApplyMorphSet"/>, kept so the
    /// pending-box "show zeroed" flip can restore the preset after temporarily rendering the
    /// undeformed body. Null until a preset has been applied this session.</summary>
    private (MorphSet Morphs, int Weight)? _lastAppliedMorphSet;

    /// <summary>Label tag on the synthetic empty MorphSet the zeroed-flip applies, so
    /// <see cref="ApplyMorphSet"/> knows not to record it as the "last applied" preset.</summary>
    private const string ZeroedFlipLabel = "(region zeroed-flip)";

    /// <summary>True when the last BodySlide/BodyGen deformation attempt couldn't find
    /// a sibling .tri for the worn body NIF. Bound to a red banner above the viewport
    /// so the user gets a visible cue to run BodySlide's "Build Morphs" (i.e. generate
    /// a Zeroed Sliders body with morphs). Reset on scene clear and on a successful
    /// .tri load.</summary>
    public bool BodyTriMissing { get; set; } = false;

    /// <summary>The morph (label, weight) the current scene's meshes carry, or null when none has been applied to
    /// this scene yet. A caller that applies a preset and must read the result (e.g. a measurement scan) waits
    /// until this matches its request and <see cref="HasPendingMorphSet"/> is false.</summary>
    public (string Label, int Weight)? CurrentSceneMorph =>
        _viewer.CurrentSceneShapeDeform is { Deformer: MorphSetShapeDeformer d } applied
            ? (d.Morphs.Label ?? "", applied.Weight)
            : null;

    /// <summary>True while a morph is queued for the next scene commit (<see cref="ApplyMorphSet"/> arrived before
    /// the scene was ready); the meshes don't carry it yet.</summary>
    public bool HasPendingMorphSet => _viewer.HasPendingShapeDeformer;

    /// <summary>The morph most recently requested through <see cref="ApplyMorphSet"/> (the viewer's retained
    /// scene input), or null. What the software fallback preview re-renders.</summary>
    public static (MorphSet Morphs, int Weight)? RequestedMorphOf(SceneInputsSnapshot? snapshot) =>
        snapshot?.ShapeDeformer is MorphSetShapeDeformer d ? (d.Morphs, snapshot.ShapeDeformWeight) : null;

    /// <summary>
    /// Pre-populates the OSD context that <see cref="ApplyMorphSet"/> consumes
    /// when no sibling .tri is present. Hosts call this once per scene change
    /// before invoking ApplyMorphSet on slider-driven morphs. SynthEBD's
    /// ApplyBodySlide wrapper does this internally via
    /// <see cref="SynthEbdOsdLoader"/>; callers that don't have a SliderGroup → catalog
    /// mapping pass their pre-parsed OSD files in directly.
    /// </summary>
    public void SetMorphContext(List<OsdFile>? osdFiles)
    {
        _cachedOsdFiles = osdFiles;
    }

    /// <summary>
    /// Applies <paramref name="morphs"/> to the loaded scene's body shapes at NPC weight
    /// <paramref name="weight"/>, preferring a sibling .tri (auto-loaded from disk next to the body
    /// NIF) over the OSD context set via <see cref="SetMorphContext"/>. Queued by the viewer when the
    /// scene isn't ready yet and applied when it commits.
    /// </summary>
    public void ApplyMorphSet(MorphSet morphs, int weight)
    {
        if (_bodySlideDisabled)
        {
            _viewer.NpcWeight = Math.Clamp(weight, 0, 100);
            LogVerbose("CharacterViewer: [BodySlideDisabled] ApplyMorphSet bypassed" +
                " (label='" + (morphs?.Label ?? "?") + "', weight=" + _viewer.NpcWeight + ")");
            return;
        }

        // The real (non zeroed-flip) morph is retained by the viewer for the software fallback snapshot --
        // including on the queue path and when GL never started -- and the fallback is notified.
        bool retain = morphs != null && morphs.Label != ZeroedFlipLabel;

        if (!_viewer.CanDeformShapes)
        {
            // Queued by the viewer; drained (and this deformer prepared) when the scene commits.
            _viewer.ApplyShapeDeformer(new MorphSetShapeDeformer(this, morphs ?? new MorphSet()), weight, retain);
            return;
        }

        if (morphs == null) return;
        _viewer.ApplyShapeDeformer(new MorphSetShapeDeformer(this, morphs), weight, retain);
    }

    /// <summary>The primary scene's BodySlide deformer: the per-application bookkeeping
    /// <see cref="ApplyMorphSet"/> did before deforming (zeroed-flip restore point, sibling .tri load),
    /// then the slider deltas per shape -- .tri preferred, OSD otherwise.</summary>
    private sealed class MorphSetShapeDeformer : IShapeDeformer
    {
        private readonly BodyShapeViewerExtension _owner;
        public MorphSetShapeDeformer(BodyShapeViewerExtension owner, MorphSet morphs)
        {
            _owner = owner;
            Morphs = morphs;
        }

        public MorphSet Morphs { get; }
        public string Label => Morphs.Label ?? "";

        public bool Prepare(string? bodyNifDiskPath, int weight)
        {
            // Remember the last real morph applied so the pending-box "show zeroed" flip can restore the
            // preset after temporarily rendering the undeformed body. Skipped for the synthetic zeroed
            // apply itself (tagged label) so flipping back doesn't restore "zeroed" as the preset.
            if (Morphs.Label != ZeroedFlipLabel)
                _owner._lastAppliedMorphSet = (Morphs, weight);

            _owner.LogVerbose("CharacterViewer: ApplyMorphSet APPLIED (label='"
                + (Morphs.Label ?? "?") + "', weight=" + Math.Clamp(weight, 0, 100)
                + ", cachedBodyMeshes=" + _owner._viewer.BodyBaseMeshes.Count + ")");

            // Idempotent — if ApplyBodySlide already loaded the .tri this is a no-op.
            _owner.TryLoadSiblingBodyTri();

            return _owner._cachedBodyTri != null
                || (_owner._cachedOsdFiles != null && _owner._cachedOsdFiles.Count > 0);
        }

        public void Deform(string shapeName, NifMeshBuilder.BuiltMesh baseMesh, Vector3[] positions, int weight)
        {
            // Apply deformation -- prefer .tri (topology-matched, no LCP stripping),
            // fall back to OSD for meshes without "Build Morphs" output.
            var bodyTri = _owner._cachedBodyTri;
            var osdFiles = _owner._cachedOsdFiles;
            if (bodyTri != null)
            {
                _owner._bodySlideDeformer.ApplyDeformationFromTri(positions, Morphs, weight, bodyTri, shapeName);
            }
            else if (osdFiles != null && osdFiles.Count > 0)
            {
                _owner._bodySlideDeformer.ApplyDeformation(positions, Morphs, weight, osdFiles, shapeName);
            }
        }
    }

    /// <summary>A guest overlay's BodySlide deformer (<see cref="LoadGuestAsync"/>): the guest's own preset,
    /// OSD context and sibling .tri, probed once against the guest's own body NIF -- which may be a
    /// completely different body mod from the primary's -- and falling through to the OSD files when
    /// absent. Lives as long as the viewer's retained guest request.</summary>
    private sealed class GuestMorphShapeDeformer : IShapeDeformer
    {
        private readonly BodyShapeViewerExtension _owner;
        private readonly List<OsdFile>? _osdFiles;
        private BodyTriFile? _bodyTri;

        public GuestMorphShapeDeformer(BodyShapeViewerExtension owner, MorphSet morphs, List<OsdFile>? osdFiles)
        {
            _owner = owner;
            Morphs = morphs;
            _osdFiles = osdFiles;
        }

        public MorphSet Morphs { get; }
        public string Label => Morphs.Label ?? "";

        public bool Prepare(string? bodyNifDiskPath, int weight)
        {
            // Probes for a .tri sibling of the guest's body NIF, matching the primary
            // scene's TryLoadSiblingBodyTri. Absent .tri simply leaves the OSD path in play.
            if (_bodyTri == null && bodyNifDiskPath is { } nif)
            {
                string? triPath = ProbeSiblingTriPath(nif);
                if (triPath == null)
                {
                    _owner.LogVerbose("CharacterViewer: no sibling .tri for guest body '" + nif
                        + "' — falling back to the OSD path.");
                }
                else
                {
                    _bodyTri = _owner._bodyTriFileParser.Parse(triPath);
                    if (_bodyTri == null)
                    {
                        _owner.LogVerbose("CharacterViewer: guest sibling .tri at '" + triPath
                            + "' failed to parse — falling back to the OSD path.");
                    }
                }
            }
            return _bodyTri != null || (_osdFiles != null && _osdFiles.Count > 0);
        }

        public void Deform(string shapeName, NifMeshBuilder.BuiltMesh baseMesh, Vector3[] positions, int weight)
        {
            if (_bodyTri != null)
            {
                _owner._bodySlideDeformer.ApplyDeformationFromTri(positions, Morphs, weight, _bodyTri, shapeName);
            }
            else if (_osdFiles != null && _osdFiles.Count > 0)
            {
                _owner._bodySlideDeformer.ApplyDeformation(positions, Morphs, weight, _osdFiles, shapeName);
            }
        }
    }

    /// <summary>
    /// Loads <paramref name="identity"/> as a guest overlay on the viewer (see
    /// <see cref="VM_CharacterViewer.LoadGuestAsync"/>), deformed by <paramref name="morphs"/> at
    /// <paramref name="weight"/> with <paramref name="osdFiles"/> as its OSD context. Null morphs = undeformed.
    /// </summary>
    public System.Threading.Tasks.Task LoadGuestAsync(NpcIdentity identity, MorphSet? morphs, int weight,
        List<OsdFile>? osdFiles = null, BodyReplacement? bodyReplacement = null)
        => _viewer.LoadGuestAsync(identity,
            morphs != null ? new GuestMorphShapeDeformer(this, morphs, osdFiles) : null,
            weight, bodyReplacement);

    /// <summary>Viewer hook: the body shapes were just re-deformed. Markers track the new positions,
    /// then <see cref="BodySlideApplied"/> fans out.</summary>
    private void OnShapesDeformed()
    {
        RefreshKeyVertexMarkerPositions();
        BodySlideApplied?.Invoke();
    }

    /// <summary>Viewer hook: the scene was cleared, so its per-scene morph data goes with it.</summary>
    private void OnSceneCleared()
    {
        _cachedOsdFiles = null;
        _cachedBodyTri = null;
        _bodyTriProbeAbandoned = false;
        BodyTriMissing = false;
    }

    /// <summary>Viewer hook: the GL context died and the scene with it. Same reset as
    /// <see cref="OnSceneCleared"/> except the missing-.tri banner, which a context loss never cleared.</summary>
    private void OnGlContextReset()
    {
        _cachedOsdFiles = null;
        _cachedBodyTri = null;
        _bodyTriProbeAbandoned = false;
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
        var bodyNifDiskPath = _viewer.CurrentBodyNifDiskPath;
        if (bodyNifDiskPath == null || _bodyTriProbeAbandoned) return;

        string? triPath = ProbeSiblingTriPath(bodyNifDiskPath);
        if (triPath == null)
        {
            LogVerbose("CharacterViewer: No sibling .tri found for '" + bodyNifDiskPath +
                "' -- falling back to OSD path (chopping bug possible if topology mismatches reference).");
            _bodyTriProbeAbandoned = true; // don't re-probe
            // A BodyReplacement body (a BodySlide source mesh) never has a .tri, and doesn't need one:
            // the host supplies the OSD authored against it. Only the NPC's own worn body warns.
            BodyTriMissing = _viewer.SceneBodyReplacement == null;
            return;
        }

        _cachedBodyTri = _bodyTriFileParser.Parse(triPath);
        if (_cachedBodyTri == null)
        {
            LogVerbose("CharacterViewer: Sibling .tri at '" + triPath +
                "' failed to parse -- falling back to OSD path.");
            _bodyTriProbeAbandoned = true;
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

    // ═══════════════════════════════════════════════════════════════════════
    //  CLASSIFIER TOOLBAR STATE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Master toggle for the BodySlide classifier toolbar cluster
    /// (Wireframe / Pick Vertex / Select Mirror / Clear Picks). Hidden by default
    /// and flipped on only when the viewer is embedded inside the OBody Body
    /// Type Profiles editor, where key-vertex assignment is the whole point.</summary>
    public bool ShowClassifierControls { get; set; } = false;

    /// <summary>
    /// True when the BodySlide-classifier toolbar + pick-info controls should actually show:
    /// the host enabled them (<see cref="ShowClassifierControls"/>) AND a live GL viewport
    /// exists. In the software fallback preview (<see cref="VM_CharacterViewer.RenderingUnavailable"/>) there is no
    /// live viewport to pick in, so the vertex-pick / bounding-box / region-edit controls are
    /// hidden — the fallback is view-only. Re-raised whenever either input changes (the viewer's
    /// flag by hand in the constructor), so the controls hide the moment GL start fails.
    /// </summary>
    public bool ClassifierControlsAvailable => ShowClassifierControls && !_viewer.RenderingUnavailable;

    /// <summary>One-line summary of the most recent vertex pick for the classifier
    /// pick-info panel. Empty when no picks in the current session.</summary>
    public string LastPickSummary { get; set; } = "";

    /// <summary>Axis selection for <see cref="ProjectLastPickAcrossAxis"/>: 0=X, 1=Y, 2=Z.
    /// Bound to the "Axis" ComboBox in the classifier toolbar; defaults to Z (the most
    /// common use case — pairing front/back midline anchors).</summary>
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
        if (_viewer.BodyBaseMeshes.Count == 0) return;
        if (PendingBoxShowZeroed)
        {
            // Render the undeformed body at the current weight (empty morphs = no slider deltas).
            ApplyMorphSet(new MorphSet { Label = ZeroedFlipLabel }, _viewer.NpcWeight);
        }
        else if (_lastAppliedMorphSet is { } last)
        {
            // Restore the preset that was showing before the flip.
            ApplyMorphSet(last.Morphs, last.Weight);
        }
        else
        {
            // No preset was ever applied — just clear to the undeformed base at the current weight.
            ApplyMorphSet(new MorphSet { Label = ZeroedFlipLabel }, _viewer.NpcWeight);
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
        var (origin, direction) = _viewer.Camera.ScreenPointToRay(
            mouseX, mouseY, viewportWidth, viewportHeight);

        GlMesh? closestMesh = null;
        int closestVertexIndex = -1;
        OpenTK.Mathematics.Vector3 closestLocal = default;
        float closestDist = float.MaxValue;

        foreach (var mesh in _viewer.Renderer.Meshes)
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

        Overlay.KeyVertexMarkers.Add(pick.LocalPos);
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
        AnyKeyVertexPicked?.Invoke(_viewer, pick);
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
        var viewProj = _viewer.Camera.GetViewMatrix() * _viewer.Camera.GetProjectionMatrix(aspect);
        float modelScale = _viewer.Renderer.ModelScale;

        GlMesh? bestMesh = null;
        int bestHitCount = 0;
        OpenTK.Mathematics.Vector3 bestMin = default;
        OpenTK.Mathematics.Vector3 bestMax = default;

        foreach (var mesh in _viewer.Renderer.Meshes)
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
        AnyKeyVertexBoxPicked?.Invoke(_viewer, pick);
    }

    /// <summary>Fans a region pick out to per-viewer and process-wide subscribers (parallels
    /// <see cref="NotifyKeyVertexBoxPicked"/>). The BodyTypeProfile editor subscribes to
    /// <see cref="AnyRegionBoxPicked"/> so a confirmed region box becomes a NamedRegion row.</summary>
    public void NotifyRegionBoxPicked(KeyVertexBoxPick pick)
    {
        RegionBoxPicked?.Invoke(pick);
        AnyRegionBoxPicked?.Invoke(_viewer, pick);
    }

    /// <summary>Fans a region vertex-edit pick out to per-viewer and process-wide subscribers (parallels
    /// <see cref="NotifyRegionBoxPicked"/>). No-ops when the pick is empty.</summary>
    public void NotifyRegionVertexEdited(RegionVertexEditPick pick)
    {
        if (pick.Count == 0) return;
        RegionVertexEdited?.Invoke(pick);
        AnyRegionVertexEdited?.Invoke(_viewer, pick);
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

        var zeroed = GetZeroedShapePositions(shape, _viewer.NpcWeight);
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
        var viewProj = _viewer.Camera.GetViewMatrix() * _viewer.Camera.GetProjectionMatrix(aspect);
        float modelScale = _viewer.Renderer.ModelScale;

        GlMesh? bestMesh = null;
        List<int>? bestIndices = null;

        foreach (var mesh in _viewer.Renderer.Meshes)
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
        var zeroed = GetZeroedShapePositions(shape, _viewer.NpcWeight);
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

        var eye = _viewer.Camera.GetEyePosition();
        var viewDir = _viewer.Camera.Target - eye;
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
    /// Parallel to <see cref="BodyShapeOverlay.KeyVertexMarkers"/>: holds full pick metadata
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
    /// mirrors it into <see cref="BodyShapeOverlay.SelectedKeyVertexMarkerIndices"/> so the
    /// corresponding marker spheres render in the "selected" color.</summary>
    public void SetSelectedPicks(IEnumerable<PickRow> selection)
    {
        _selectedPicks.Clear();
        Overlay.SelectedKeyVertexMarkerIndices.Clear();
        if (selection == null) return;
        foreach (var p in selection)
        {
            _selectedPicks.Add(p);
            // Picks and BodyShapeOverlay.KeyVertexMarkers are parallel — the row's position
            // in Picks is also its marker index.
            int idx = Picks.IndexOf(p);
            if (idx >= 0) Overlay.SelectedKeyVertexMarkerIndices.Add(idx);
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
        if (!_viewer.TryGetCurrentVertex(shapeName, newVertexIndex, out var newPos)) return false;

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
        if (rowIdx < Overlay.KeyVertexMarkers.Count)
        {
            Overlay.KeyVertexMarkers[rowIdx] = newPos;
        }

        if (rowIdx == Picks.Count - 1) LastPickSummary = Picks[rowIdx].Display;
        return true;
    }


    /// <summary>
    /// Replaces the renderer's BB-resolved marker set with <paramref name="positions"/>.
    /// Intended for the BodyTypeProfile editor, which resolves every <c>BoundingBox</c>
    /// key-vertex to a mesh-local position whenever <c>RefreshMeasurementValues</c> runs and
    /// pushes them here so the yellow markers track preset/weight change. Explicit picks
    /// continue to live in <see cref="BodyShapeOverlay.KeyVertexMarkers"/> and aren't touched.
    /// </summary>
    public void SetBoxResolvedMarkers(IEnumerable<OpenTK.Mathematics.Vector3> positions)
    {
        Overlay.BoxResolvedMarkers.Clear();
        if (positions == null) return;
        foreach (var p in positions) Overlay.BoxResolvedMarkers.Add(p);
    }

    /// <summary>Replaces the single preview marker (see
    /// <see cref="BodyShapeOverlay.PreviewKeyVertexMarkers"/>) with the resolved position of
    /// <paramref name="shapeName"/>[<paramref name="vertexIndex"/>]. Pass null/empty shape or
    /// negative index to clear. Used by the BodyTypeProfile editor to render a transient
    /// "you-are-here" sphere when the user selects a KeyVertex row, without populating the
    /// picks ListBox. Silently no-ops when the (shape, index) can't be resolved on the
    /// current mesh state.</summary>
    public void SetPreviewKeyVertex(string? shapeName, int vertexIndex)
    {
        Overlay.PreviewKeyVertexMarkers.Clear();
        if (string.IsNullOrEmpty(shapeName) || vertexIndex < 0) return;
        if (_viewer.TryGetCurrentVertex(shapeName, vertexIndex, out var pos))
        {
            Overlay.PreviewKeyVertexMarkers.Add(pos);
        }
    }

    /// <summary>Replaces the pending-box pick preview markers (see
    /// <see cref="BodyShapeOverlay.PreviewPickMarkers"/>) with the supplied world-space positions.
    /// Pass null or an empty enumerable to clear. Driven by the BodyTypeProfile editor while
    /// a pending box is being authored — it re-runs FindBestInBox on every box-coord /
    /// criterion change and pushes the resolved vertex (or pair) here so the user sees
    /// exactly what Confirm would commit.</summary>
    public void SetPreviewPickMarkers(IEnumerable<OpenTK.Mathematics.Vector3>? positions)
    {
        Overlay.PreviewPickMarkers.Clear();
        if (positions == null) return;
        foreach (var p in positions) Overlay.PreviewPickMarkers.Add(p);
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
        if (!_viewer.BodyBaseMeshes.TryGetValue(shapeName, out var originalMesh) || originalMesh == null) return null;

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

    /// <summary>
    /// Builds a <see cref="HeadlessBodyDeformer"/> for the named body shapes of the current scene: the same
    /// cached base meshes, skinning and sibling .tri that <see cref="ApplyMorphSet"/> deforms, captured so a
    /// batch measurement can deform presets without driving the viewer (no GL upload, no normals, no
    /// <see cref="BodySlideApplied"/>). Names are matched case-insensitively against the cached body meshes;
    /// shapes that aren't deformable body meshes are skipped (they never change under a morph). Returns null
    /// when there is nothing to deform, the scene is mid-rebuild, or BodySlide application is disabled.
    /// The deformer holds references to the current meshes, so a later scene change doesn't affect it.
    /// </summary>
    public HeadlessBodyDeformer? CreateHeadlessBodyDeformer(IEnumerable<string> shapeNames)
    {
        if (_bodySlideDisabled || !_viewer.CanDeformShapes || shapeNames == null) return null;
        TryLoadSiblingBodyTri(); // idempotent; the same .tri ApplyMorphSet would prefer

        var wanted = new HashSet<string>(shapeNames, StringComparer.OrdinalIgnoreCase);
        var shapes = new List<(string, NifMeshBuilder.BuiltMesh)>();
        foreach (var kvp in _viewer.BodyBaseMeshes)
        {
            if (wanted.Contains(kvp.Key) && kvp.Value != null) shapes.Add((kvp.Key, kvp.Value));
        }
        return shapes.Count == 0 ? null : new HeadlessBodyDeformer(shapes, _cachedBodyTri);
    }


    /// <summary>Removes every marker gizmo. Bound to the toolbar "Clear Picks"
    /// button. Phase 3 will replace this with per-profile clearing.</summary>
    public void ClearKeyVertexMarkers()
    {
        Overlay.KeyVertexMarkers.Clear();
        Overlay.SelectedKeyVertexMarkerIndices.Clear();
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

        Overlay.KeyVertexMarkers.Clear();
        // Indices may shift if any pick fails to resolve mid-list below — clear and
        // re-derive at the end from the still-valid _selectedPicks row references.
        Overlay.SelectedKeyVertexMarkerIndices.Clear();

        for (int i = 0; i < _keyVertexPicks.Count; i++)
        {
            var pick = _keyVertexPicks[i];
            var shapeName = pick.Mesh?.ShapeName ?? "";
            if (string.IsNullOrEmpty(shapeName)
                || !_viewer.TryGetCurrentVertex(shapeName, pick.VertexIndex, out var pos))
            {
                continue;
            }

            _keyVertexPicks[i] = new KeyVertexPick(pick.Mesh!, pick.VertexIndex, pos);
            Overlay.KeyVertexMarkers.Add(pos);

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
            if (idx >= 0) Overlay.SelectedKeyVertexMarkerIndices.Add(idx);
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
            var mesh = _viewer.Renderer.Meshes.FirstOrDefault(m =>
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
            var available = _viewer.GetCurrentShapeVertexCounts();
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
            if (!_viewer.TryGetCurrentVertex(src.Mesh.ShapeName, src.VertexIndex, out var livePos))
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

        if (!_viewer.TryGetCurrentVertex(src.Mesh.ShapeName, src.VertexIndex, out var originPos))
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
    /// <see cref="BodyShapeOverlay.KeyVertexMarkers"/>; pass <c>null</c> or an empty sequence
    /// to clear the overlay. Used by the BodyTypeProfile editor to visualize the
    /// currently-selected measurement.
    /// </summary>
    /// <summary>
    /// Single canonical entry point. Each tuple's <c>Label</c> populates
    /// <see cref="OverlayLineSegment.Label"/> for the hover-tooltip path;
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
        Overlay.MeasurementLines.Clear();
        if (segments == null) return;
        foreach (var s in segments)
        {
            Overlay.MeasurementLines.Add(new OverlayLineSegment
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
        Overlay.RegionCapMarkers.Clear();
        Overlay.RegionOverlayLines.Clear();
        if (loopVertices != null)
            foreach (var v in loopVertices) Overlay.RegionCapMarkers.Add(v);
        if (loopEdges != null)
            foreach (var (a, b) in loopEdges)
                Overlay.RegionOverlayLines.Add(new OverlayLineSegment { A = a, B = b, Color = edgeColor });
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
        Overlay.RegionSolidTriangles.Clear();
        Overlay.RegionWireLines.Clear();
        if (interleavedTriangles != null)
            Overlay.RegionSolidTriangles.AddRange(interleavedTriangles);
        if (wireEdges != null)
            foreach (var (a, b) in wireEdges)
                Overlay.RegionWireLines.Add(new OverlayLineSegment { A = a, B = b, Color = wireColor });
    }

    /// <summary>Replaces the region wireframe channel (<see cref="BodyShapeOverlay.RegionWireLines"/>) with
    /// <b>per-segment-colored</b> line segments, instead of the single-color wire <see cref="SetRegionSolid"/>
    /// pushes. The editor uses this to recolor an edited region's wireframe: cyan for untouched edges,
    /// green for the half-edges around curated <i>added</i> vertices, and small red/green node crosses
    /// for removed / isolated-added vertices. Pass null/empty to clear. Does not touch the solid-faces or
    /// cap-loop channels, so call it after <see cref="SetRegionSolid"/> / <see cref="SetRegionOverlay"/>.</summary>
    public void SetRegionWireColored(
        IEnumerable<(OpenTK.Mathematics.Vector3 A, OpenTK.Mathematics.Vector3 B, OpenTK.Mathematics.Vector3 Color)>? segments)
    {
        Overlay.RegionWireLines.Clear();
        if (segments == null) return;
        foreach (var s in segments)
            Overlay.RegionWireLines.Add(new OverlayLineSegment { A = s.A, B = s.B, Color = s.Color });
    }

    /// <summary>Replaces the measurement-region tint (<see cref="BodyShapeOverlay.MeasurementRegionTriangles"/>):
    /// the surface patches of the regions the displayed measurements use, as interleaved triangle
    /// vertices (6 floats each: position.xyz + normal.xyz) in the same pre-ModelScale space as the
    /// markers. Drawn translucent and depth-tested, so it shades the visible skin. Independent of the
    /// region editor's channels (<see cref="SetRegionSolid"/> / <see cref="ClearRegionOverlay"/> never
    /// touch it). Pass null/empty to clear. <paramref name="colorRanges"/> (in vertices) colors parts of the
    /// tint separately -- see <see cref="BodyShapeOverlay.MeasurementRegionColorRanges"/>; null/empty draws all of
    /// it in <see cref="MeasurementRegionDefaultColor"/>.</summary>
    public void SetMeasurementRegionTint(IEnumerable<float>? interleavedTriangles,
        IEnumerable<(int FirstVertex, int VertexCount, OpenTK.Mathematics.Vector3 Color)>? colorRanges = null)
    {
        Overlay.MeasurementRegionTriangles.Clear();
        Overlay.MeasurementRegionColorRanges.Clear();
        if (interleavedTriangles != null)
            Overlay.MeasurementRegionTriangles.AddRange(interleavedTriangles);
        if (colorRanges != null)
            Overlay.MeasurementRegionColorRanges.AddRange(colorRanges);
    }

    /// <summary>The measurement-region tint's color when no per-range colors are given.</summary>
    public OpenTK.Mathematics.Vector3 MeasurementRegionDefaultColor => Overlay.MeasurementRegionColor;

    /// <summary>Clears the region overlay (cap-loop markers + edges + the Solid surface + wireframe).
    /// Called when the editor deselects its region row or switches profiles.</summary>
    public void ClearRegionOverlay()
    {
        Overlay.RegionCapMarkers.Clear();
        Overlay.RegionOverlayLines.Clear();
        Overlay.RegionSolidTriangles.Clear();
        Overlay.RegionWireLines.Clear();
    }

    /// <summary>
    /// Lights up the vertices a single BodySlide slider moves as an unlit magnitude heatmap over the
    /// current (deformed) body, feeding <see cref="BodyShapeOverlay.SliderHeatmapTriangles"/>. Iterates the
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
        var output = Overlay.SliderHeatmapTriangles;
        output.Clear();

        if (string.IsNullOrWhiteSpace(sliderName)) return;
        if (_cachedBodyTri == null && (_cachedOsdFiles == null || _cachedOsdFiles.Count == 0)) return;

        int totalTris = 0;
        foreach (var kvp in _viewer.BodyBaseMeshes)
        {
            string shapeName = kvp.Key;
            var glMesh = _viewer.Renderer.Meshes.FirstOrDefault(m =>
                string.Equals(m.ShapeName, shapeName, StringComparison.OrdinalIgnoreCase));
            if (glMesh?.CpuPositions == null || glMesh.CpuPositions.Length == 0) continue;
            if (glMesh.CpuIndices == null || glMesh.CpuIndices.Length < 3) continue;

            var deltas = _bodySlideDeformer.TryGetSliderDeltas(sliderName, shapeName, _cachedBodyTri, _cachedOsdFiles);
            if (deltas == null || deltas.Count == 0) continue;

            totalTris += SliderHeatmapBuilder.Build(glMesh.CpuPositions, glMesh.CpuIndices, deltas, output, minDelta);
        }

        LogVerbose("CharacterViewer: HighlightSliderMorph('" + sliderName + "') -> "
            + totalTris + " triangles across " + _viewer.BodyBaseMeshes.Count + " body shape(s)");
    }

    /// <summary>Clears the slider-morph heatmap channel (highlight toggled off, slider/preset deselected,
    /// or NPC reloaded). Cheap; safe to call when nothing is highlighted.</summary>
    public void ClearSliderHighlight()
    {
        Overlay.SliderHeatmapTriangles.Clear();
    }

    /// <summary>
    /// Returns the label of the closest labeled measurement-line segment within
    /// <paramref name="thresholdPixels"/> screen-space distance of the cursor, or null when
    /// no labeled segment is close enough. Unlabeled segments are skipped — they still
    /// render through <see cref="BodyShapeOverlay.MeasurementLines"/> but don't surface a
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
        if (Overlay.MeasurementLines.Count == 0) return null;
        if (viewportWidth <= 0f || viewportHeight <= 0f) return null;

        float aspect = viewportWidth / viewportHeight;
        var vp = _viewer.Camera.GetViewMatrix() * _viewer.Camera.GetProjectionMatrix(aspect);
        float modelScale = _viewer.Renderer.ModelScale;
        float thresholdSq = thresholdPixels * thresholdPixels;

        string? best = null;
        float bestDistSq = float.MaxValue;
        float bestDepth = float.MaxValue;
        var mouse = new OpenTK.Mathematics.Vector2(mouseX, mouseY);

        for (int i = 0; i < Overlay.MeasurementLines.Count; i++)
        {
            var seg = Overlay.MeasurementLines[i];
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
}

/// <summary>Shorthand for <see cref="BodyShapeViewerExtension.For"/> at call sites:
/// <c>viewer.BodyShape().ApplyMorphSet(...)</c>.</summary>
public static class BodyShapeViewerAccess
{
    /// <summary>The body-shape extension attached to <paramref name="viewer"/> (attached on first use).</summary>
    public static BodyShapeViewerExtension BodyShape(this VM_CharacterViewer viewer) => BodyShapeViewerExtension.For(viewer);
}
