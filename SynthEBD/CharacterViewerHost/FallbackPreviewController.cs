using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CharacterViewer.Rendering;
using CharacterViewer.Rendering.Offscreen;

namespace SynthEBD;

/// <summary>
/// Drives SynthEBD's <b>software fallback preview</b> — the view-only, orbitable
/// image shown in <see cref="UC_CharacterViewer"/> when the embedded
/// <c>GLWpfControl</c> can't start because the OpenGL driver lacks
/// <c>WGL_NV_DX_interop</c> (Wine/Proton, VMs with a virtual GPU, RDP). The live
/// <see cref="VM_CharacterViewer"/> still captures its neutral inputs (it just
/// never uploads a GL scene — see <see cref="VM_CharacterViewer.RenderingUnavailable"/>);
/// this controller reads those via <see cref="VM_CharacterViewer.TryGetSceneInputsSnapshot"/>
/// and re-renders them through the shared offscreen
/// <see cref="IOffscreenRenderer"/> (a hidden GLFW <c>GameWindow</c> + FBO that
/// needs no NV_DX_interop, so plain WGL under Wine works), marshalling each
/// BGRA32 frame into a <see cref="WriteableBitmap"/>.
///
/// <para><b>Threading:</b> every public method and all internal render-loop
/// state run on the WPF UI (main) thread. The offscreen renderer owns its own
/// render thread; its awaited results resume on the UI thread via the captured
/// dispatcher context, where the <see cref="WriteableBitmap"/> is written. No GL
/// call ever happens on the UI thread.</para>
///
/// <para><b>Lifetime:</b> one controller per <see cref="VM_CharacterViewer"/>
/// (persisted via <see cref="FallbackPreviewControllerRegistry"/>'s
/// <see cref="ConditionalWeakTable{TKey,TValue}"/>, so it is collected with its
/// VM). A <see cref="UC_CharacterViewer"/> instance <see cref="Attach"/>es on
/// load and <see cref="Detach"/>es on unload; the controller outlives individual
/// control instances, exactly like the VM it mirrors.</para>
/// </summary>
public sealed class FallbackPreviewController
{
    private readonly VM_CharacterViewer _vm;
    private readonly Func<IOffscreenRenderer> _sharedRenderer;
    private readonly ICharacterViewerLogger _logger;
    private readonly int _instanceId;
    private readonly Dispatcher? _dispatcher;

    // Cap so a maximized window on a 4K display can't ask the software path for an
    // absurd framebuffer (memory + per-render cost scale with W*H).
    private const int MaxRenderDimension = 2048;

    // ── UI-thread-only state ────────────────────────────────────────────────
    private WriteableBitmap? _bitmap;
    private object? _owner;   // the control currently attached; guards against a stale Detach
    private Action<WriteableBitmap>? _onFrame;
    private Action<Exception>? _onFailed;
    private Action<bool>? _onBusy;
    private int _lastWidth, _lastHeight;
    private bool _attached;
    private bool _failed;
    private Exception? _failure;
    private object? _prewarmedPaths;   // reference-identity of the last prewarmed MeshPaths

    // Render loop: single render in flight, latest wins.
    private OffscreenRenderRequest? _pending;
    private CancellationTokenSource? _pendingCts;
    private CancellationTokenSource? _inFlightCts;
    private bool _running;

    // Diagnostic latches so the always-on log gets one line per state, not per frame.
    private bool _loggedNoSnapshot;
    private bool _loggedFirstFrame;

    internal FallbackPreviewController(VM_CharacterViewer vm,
        Func<IOffscreenRenderer> sharedRenderer, ICharacterViewerLogger logger, int instanceId)
    {
        _vm = vm;
        _sharedRenderer = sharedRenderer;
        _logger = logger;
        _instanceId = instanceId;
        _dispatcher = Application.Current?.Dispatcher;

        // Re-render whenever the live VM's retained inputs change (NPC load, texture /
        // mesh overrides, morph) or its lighting / background change. Subscribed for the
        // controller's whole lifetime; the VM↔controller reference cycle is self-contained
        // and collectible once the host releases the VM (the registry keys it weakly).
        _vm.SceneInputsChanged += OnSceneInputsChanged;
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.LogViewerDiagnostic("FallbackPreview: controller #" + instanceId + " bound to VM #"
            + _vm.GetHashCode().ToString("X"));
    }

    /// <summary>Wires this controller to a live <see cref="UC_CharacterViewer"/>: the
    /// callbacks receive each produced frame, a permanent failure, and busy-state
    /// transitions. Re-shows the last frame immediately and kicks a fresh render at the
    /// last known size. If offscreen rendering already failed permanently, reports that
    /// straight away so the control can fall back to the static placeholder.
    /// <paramref name="owner"/> identifies the attaching control so a stale
    /// <see cref="Detach"/> from a control that has since been superseded is ignored
    /// (WPF does not guarantee the old control's Unloaded fires before the new one attaches).</summary>
    public void Attach(object owner, Action<WriteableBitmap> onFrame, Action<Exception> onFailed, Action<bool> onBusy)
    {
        _owner = owner;
        _onFrame = onFrame;
        _onFailed = onFailed;
        _onBusy = onBusy;
        _attached = true;

        if (_failed)
        {
            onFailed(_failure ?? new InvalidOperationException("offscreen rendering unavailable"));
            return;
        }

        if (_bitmap != null) onFrame(_bitmap);                // re-show last frame on re-attach
        if (_lastWidth > 0 && _lastHeight > 0)
            ScheduleRender(_lastWidth, _lastHeight, lowRes: false);
    }

    /// <summary>Detaches <paramref name="owner"/> (on <c>Unloaded</c>, or when it re-binds to a
    /// different VM's controller): clears the callbacks so frames stop flowing to a gone control.
    /// No-op when <paramref name="owner"/> is not the currently attached control — a later
    /// attacher has already taken over and its callbacks must survive the old control's
    /// (possibly late-firing) Unloaded. The controller (and its last frame) survive for the
    /// next control instance that attaches.</summary>
    public void Detach(object owner)
    {
        if (!ReferenceEquals(_owner, owner)) return;
        _owner = null;
        _attached = false;
        _onFrame = null;
        _onFailed = null;
        _onBusy = null;
        // Deliberately do NOT clear _pending or cancel the in-flight render: the editor re-creates
        // the viewer control on fallback-state changes, so let the render finish and update the
        // bitmap — the next control instance shows the latest frame on Attach.
    }

    /// <summary>Requests a render at <paramref name="width"/>×<paramref name="height"/>
    /// (logical pixels). <paramref name="lowRes"/> halves the framebuffer for during-drag
    /// responsiveness (the <see cref="WriteableBitmap"/> upscales in the Image). Latest
    /// request wins; any in-flight render is cancelled.</summary>
    public void RequestRender(int width, int height, bool lowRes) => ScheduleRender(width, height, lowRes);

    private void ScheduleRender(int width, int height, bool lowRes)
    {
        // NOTE: no !_attached gate. The editor re-creates the viewer control on fallback-state
        // changes, so we keep rendering into the bitmap even while momentarily detached; the
        // next control instance shows the latest frame on Attach.
        if (_failed) return;
        if (width <= 0 || height <= 0)
        {
            _vm.LogViewerDiagnostic($"FallbackPreview: ScheduleRender skipped — size {width}x{height}");
            return;
        }
        _lastWidth = width;
        _lastHeight = height;

        var snap = _vm.TryGetSceneInputsSnapshot();
        if (snap == null)
        {
            // No NPC/mesh has been loaded into this viewer, so there is nothing to render.
            _vm.LogViewerDiagnostic("FallbackPreview: ScheduleRender skipped — no scene inputs "
                + "snapshot (no NPC/mesh loaded into this viewer yet).");
            if (!_loggedNoSnapshot)
            {
                _loggedNoSnapshot = true;
                _logger?.LogMessage("CharacterViewer: software preview has nothing to draw yet — "
                    + "no NPC/preview is loaded in this viewer. It will render once a preview loads.");
            }
            return;
        }
        _loggedNoSnapshot = false;

        var cts = new CancellationTokenSource();
        _pending = BuildRequest(snap, width, height, lowRes, cts.Token);
        _pendingCts = cts;
        _inFlightCts?.Cancel();     // supersede the in-flight render — latest wins

        _vm.LogViewerDiagnostic($"FallbackPreview: scheduled morph=[{snap.Morphs?.Label ?? "none"}]"
            + $" running={_running} attached={_attached} {width}x{height}");
        if (!_running) _ = PumpAsync();
    }

    private async Task PumpAsync()
    {
        _running = true;
        _onBusy?.Invoke(true);
        try
        {
            while (_pending != null)
            {
                var req = _pending;
                _pending = null;
                _inFlightCts = _pendingCts;

                IOffscreenRenderer renderer;
                try { renderer = _sharedRenderer(); }
                catch (Exception ex) { Fail(ex, "offscreen renderer construction failed"); return; }

                // Prewarm CPU caches (NIF parse + DDS decode) off the render thread once per
                // NPC, matching the documented prewarm-then-render pattern. Best-effort.
                if (!ReferenceEquals(req.MeshPaths, _prewarmedPaths))
                {
                    _prewarmedPaths = req.MeshPaths;
                    try { await renderer.PrewarmAsync(req); } catch { /* best-effort */ }
                    if (_pending != null) continue;   // a newer request arrived during prewarm — render that instead
                }

                var camDesc = req.Camera is CameraFraming.OrbitState os
                    ? $"az={os.Azimuth:F0} el={os.Elevation:F0} dist={os.Distance:F0} tgt=({os.TargetX:F0},{os.TargetY:F0},{os.TargetZ:F0})"
                    : req.Camera.GetType().Name;
                _vm.LogViewerDiagnostic($"FallbackPreview: rendering {req.Width}x{req.Height} cam[{camDesc}]"
                    + $" morph=[{req.Morphs?.Label ?? "none"}]@w{req.MorphWeight} sliders={req.Morphs?.Sliders?.Count ?? 0}"
                    + $" texOv={(req.TextureOverrides != null)}");

                byte[] bgra;
                try
                {
                    bgra = await renderer.RenderToBgra32Async(req);
                }
                catch (OperationCanceledException) { continue; }          // superseded — loop for the newer request
                catch (Exception ex) { Fail(ex, "offscreen render failed"); return; }

                // Update the bitmap even if the control detached mid-render — WriteFrame's onFrame
                // callback is null-safe, and the next control instance shows the frame on Attach.

                int missMesh = req.MissingMeshPathsOut?.Count ?? 0;
                int missTex = req.MissingTexturePathsOut?.Count ?? 0;
                _vm.LogViewerDiagnostic($"FallbackPreview: frame {req.Width}x{req.Height}, {bgra.Length} bytes"
                    + $", missingMeshes={missMesh}, missingTex={missTex}");
                if (missMesh > 0)
                {
                    _logger?.LogMessage("CharacterViewer: software preview — offscreen render could not resolve "
                        + missMesh + " mesh path(s): " + string.Join("; ", req.MissingMeshPathsOut!));
                }
                if (!_loggedFirstFrame)
                {
                    _loggedFirstFrame = true;
                    _logger?.LogMessage($"CharacterViewer: software preview produced its first frame "
                        + $"({req.Width}x{req.Height}, {missMesh} missing mesh(es)).");
                }

                WriteFrame(bgra, req.Width, req.Height);
            }
        }
        finally
        {
            _running = false;
            _onBusy?.Invoke(false);
        }
    }

    private OffscreenRenderRequest BuildRequest(SceneInputsSnapshot snap, int width, int height, bool lowRes, CancellationToken ct)
    {
        int rw = Math.Clamp(lowRes ? width / 2 : width, 1, MaxRenderDimension);
        int rh = Math.Clamp(lowRes ? height / 2 : height, 1, MaxRenderDimension);

        // Reuse the live VM's OrbitCamera verbatim (UC_CharacterViewer feeds the same
        // OnMouseMove/OnMouseWheel into it in fallback mode as in live mode), so mouse
        // feel is identical. OrbitState is written onto the offscreen VM's camera as-is.
        var cam = _vm.Camera;
        var camera = new CameraFraming.OrbitState(
            Distance: cam.Distance,
            Azimuth: cam.Azimuth,
            Elevation: cam.Elevation,
            TargetX: cam.Target.X,
            TargetY: cam.Target.Y,
            TargetZ: cam.Target.Z);

        return new OffscreenRenderRequest
        {
            MeshPaths = snap.MeshPaths,
            OverrideHeadMeshAbsolutePath = snap.OverrideHeadMeshAbsolutePath,
            TextureOverrides = snap.TextureOverrides,
            MeshOverrides = snap.MeshOverrides,
            Morphs = snap.Morphs,
            MorphWeight = snap.MorphWeight,
            Width = rw,
            Height = rh,
            Lighting = snap.Lighting,
            Colors = snap.Colors,
            BackgroundRgb = snap.BackgroundRgb,
            Camera = camera,
            Cancellation = ct,
            AdditionalScopes = snap.AdditionalScopes,
            AdditionalDataFolders = snap.AdditionalDataFolders,
            VanillaLooseOverridesBsa = snap.VanillaLooseOverridesBsa,
            VanillaLooseOverridesModLoose = snap.VanillaLooseOverridesModLoose,
            // Forward the live VM's render-quality state so the Light / Render Settings
            // panels affect the fallback exactly as they would the live viewport.
            RenderMissingTextureAsWireframe = _vm.RenderMissingTextureAsWireframe,
            EnableToneMapping = _vm.EnableToneMapping,
            EnableShadows = _vm.EnableShadows,
            EnableAmbientOcclusion = _vm.EnableAmbientOcclusion,
            SsaoRadius = _vm.SsaoRadius,
            SsaoBias = _vm.SsaoBias,
            SsaoIntensity = _vm.SsaoIntensity,
            SsaoThickness = _vm.SsaoThickness,
            SsaoHairGap = _vm.SsaoHairGap,
            EnableEyeCatchlight = _vm.EnableEyeCatchlight,
            SubsurfaceStrength = _vm.SubsurfaceStrength,
            SkinSaturationBoost = _vm.SkinSaturationBoost,
            VignetteRadius = _vm.VignetteRadius,
            VignetteIntensity = _vm.VignetteIntensity,
            Exposure = _vm.Exposure,
            TonemapHairRelief = _vm.TonemapHairRelief,
            DaylightBoost = _vm.DaylightBoost,
            DaylightBoostIntensity = _vm.DaylightBoostIntensity,
            EnableBloom = _vm.EnableBloom,
            BloomIntensity = _vm.BloomIntensity,
            // Opt into miss tracking so a blank frame can be diagnosed (unresolved meshes vs
            // an empty/off-frame scene) from the viewer log.
            MissingMeshPathsOut = new System.Collections.Generic.List<string>(),
            MissingTexturePathsOut = new System.Collections.Generic.List<string>(),
        };
    }

    private void WriteFrame(byte[] bgra, int width, int height)
    {
        int stride = width * 4;
        if (bgra.Length < stride * height) return;   // defensive: never WritePixels a short buffer

        if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), bgra, stride, 0);
        _onFrame?.Invoke(_bitmap);
    }

    private void Fail(Exception ex, string what)
    {
        _failed = true;
        _failure = ex;
        _pending = null;
        _logger?.LogError("FallbackPreview #" + _instanceId + ": " + what
            + " — degrading to static placeholder: " + ex.Message);
        _onFailed?.Invoke(ex);
    }

    private void OnSceneInputsChanged() => Dispatch(() =>
    {
        _vm.LogViewerDiagnostic("FallbackPreview: SceneInputsChanged -> schedule");
        ScheduleRender(_lastWidth, _lastHeight, lowRes: false);
    });

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(VM_CharacterViewer.SelectedLightingLayout):
            case nameof(VM_CharacterViewer.SelectedLightingColorScheme):
            case nameof(VM_CharacterViewer.BackgroundColor):
                Dispatch(() => ScheduleRender(_lastWidth, _lastHeight, lowRes: false));
                break;
        }
    }

    // Marshal onto the UI thread — VM change notifications can arrive on a background
    // thread (e.g. a load continuation), and all render-loop state + the WriteableBitmap
    // are UI-thread-affine.
    private void Dispatch(Action action)
    {
        var d = _dispatcher;
        if (d == null || d.CheckAccess()) action();
        else d.BeginInvoke(action);
    }
}

/// <summary>
/// Per-viewer lookup for <see cref="FallbackPreviewController"/> instances, plus the
/// single shared offscreen <see cref="IOffscreenRenderer"/> they all render through.
/// Mirrors <see cref="SynthEbdViewerHostStateRegistry"/>: <see cref="Configure"/> is
/// called once from MainModule's container-build callback with lazy resolver delegates
/// (deferred so post-build environment state is wired by first use), and controllers are
/// keyed weakly per VM via a <see cref="ConditionalWeakTable{TKey,TValue}"/> so each is
/// collected with its viewer — no manual controller disposal.
///
/// <para>The offscreen renderer is expensive (a hidden GLFW <c>GameWindow</c>) and
/// serializes renders internally, so a single instance is shared across every fallback
/// viewer and built lazily on first use. On a machine where GL starts normally, nothing
/// here is ever touched — no renderer, no <c>GameWindow</c>, no controller.</para>
/// </summary>
public static class FallbackPreviewControllerRegistry
{
    private static readonly ConditionalWeakTable<VM_CharacterViewer, FallbackPreviewController> _controllers = new();

    private static Func<IOffscreenRenderer>? _rendererFactory;
    private static Func<ICharacterViewerLogger>? _loggerFactory;

    private static readonly object _rendererGate = new();
    private static IOffscreenRenderer? _sharedRenderer;
    private static int _instanceCounter;

    /// <summary>Wires the lazy resolvers the fallback preview needs. <paramref name="rendererFactory"/>
    /// builds the shared offscreen renderer from the same dependency set
    /// <see cref="Offscreen.OffscreenRendererFactory.Create"/> takes (all already registered in
    /// MainModule); it is invoked at most once, on the process main thread.</summary>
    public static void Configure(Func<IOffscreenRenderer> rendererFactory, Func<ICharacterViewerLogger> loggerFactory)
    {
        _rendererFactory = rendererFactory;
        _loggerFactory = loggerFactory;
    }

    internal static FallbackPreviewController GetOrCreate(VM_CharacterViewer vm)
    {
        if (_rendererFactory == null || _loggerFactory == null)
        {
            throw new InvalidOperationException(
                "FallbackPreviewControllerRegistry.Configure() must be called before the fallback preview is used.");
        }
        return _controllers.GetValue(vm, k =>
            new FallbackPreviewController(k, GetSharedRenderer, _loggerFactory(),
                Interlocked.Increment(ref _instanceCounter)));
    }

    /// <summary>Lazily builds the single shared offscreen renderer. MUST first be reached on the
    /// process main thread — GLFW installs its event hook on the constructing thread — which it is:
    /// the only caller is a controller's render loop, and that runs on the WPF UI (main) thread.</summary>
    private static IOffscreenRenderer GetSharedRenderer()
    {
        lock (_rendererGate)
        {
            return _sharedRenderer ??= _rendererFactory!();
        }
    }

    /// <summary>Drops the shared renderer's caches when SynthEBD rebuilds its game
    /// environment / load order (resolved paths + decoded pixels + uploaded textures may
    /// now be stale). No-op if the renderer was never built.</summary>
    public static void InvalidateCaches()
    {
        IOffscreenRenderer? r;
        lock (_rendererGate) { r = _sharedRenderer; }
        r?.InvalidateCaches();
    }

    /// <summary>Disposes the shared offscreen renderer on app shutdown (tears down its hidden
    /// GameWindow + GL context on the render thread). Safe when no fallback render ever
    /// happened — a no-op if the renderer was never built.</summary>
    public static async Task ShutdownAsync()
    {
        IOffscreenRenderer? r;
        lock (_rendererGate) { r = _sharedRenderer; _sharedRenderer = null; }
        if (r != null)
        {
            try { await r.DisposeAsync(); }
            catch { /* best-effort on shutdown */ }
        }
    }
}
