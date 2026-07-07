using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenTK.Wpf;

namespace SynthEBD;

/// <summary>
/// Code-behind for the embedded OpenGL character viewer (hosts an <see cref="OpenTK.Wpf.GLWpfControl"/>).
/// Drives the GL lifecycle (deferred start, sleep/wake suspend, context-loss recovery on navigation),
/// translates mouse input into camera orbit/pan/zoom and the pick modes (key-vertex, bounding-box,
/// region vertex-edit, light-arrow), renders the axis gizmo and pending-box wireframe overlays each frame,
/// and builds the right-click texture-toggle menu and hover tooltips. All VM interaction goes through a
/// lazily-resolved <see cref="VM_CharacterViewer"/> (a persistent singleton re-bound when WPF recreates
/// this control). See also <c>RENDERING_PIPELINE.md</c>.
/// </summary>
public partial class UC_CharacterViewer : UserControl
{
    /// <summary>Wires the GL control's mouse events, the hover-tooltip dwell timer, the deferred GL-start hooks (SizeChanged/Loaded), the axis-gizmo placement, the pending-box wireframe lines, and sleep/wake power-mode suspension; unsubscribes power events on unload.</summary>
    public UC_CharacterViewer()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Wire mouse events once — handlers lazily resolve _vm
        GlControl.MouseDown += GlControl_MouseDown;
        GlControl.MouseMove += GlControl_MouseMove;
        GlControl.MouseUp += GlControl_MouseUp;
        GlControl.MouseWheel += GlControl_MouseWheel;
        GlControl.MouseRightButtonUp += GlControl_MouseRightButtonUp;
        GlControl.MouseLeave += GlControl_MouseLeave;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _hoverTimer.Tick += HoverTimer_Tick;

        _hoverTooltip = new ToolTip
        {
            PlacementTarget = GlControl,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
            HasDropShadow = true,
        };

        // Start GL when the control gets a valid size (handles deferred layout)
        GlControl.SizeChanged += (_, _) => TryStartGl();
        Loaded += (_, _) =>
        {
            _vm ??= DataContext as VM_CharacterViewer;
            _vm?.LogViewerDiagnostic("UC_CharacterViewer #" + _instanceId + " Loaded (w="
                + GlControl.ActualWidth.ToString("F0") + ", h="
                + GlControl.ActualHeight.ToString("F0") + ", glStarted=" + _glStarted + ")");
            TryStartGl();
            // If GL was already started (navigating back to a reused UC), the visibility
            // toggle inside TryStartGl doesn't run — but GLWpfControl's render-loop
            // registration only survives as long as the control's CompositionTarget.Rendering
            // subscription is alive, and that subscription can be lost when the parent
            // is unloaded and re-shown. Force the toggle here too so the render loop
            // resumes regardless of whether this is the first Loaded or a subsequent one.
            if (_glStarted)
            {
                GlControl.Visibility = Visibility.Collapsed;
                GlControl.Visibility = Visibility.Visible;
            }
            // Re-attach the software fallback if a prior Unloaded detached it (a tab switch
            // that kept this control instance). First activation attaches inside TryStartGl's
            // failure path; this covers a subsequent re-show of the same control.
            if (_fallbackActive && _fallback != null)
                _fallback.Attach(OnFallbackFrame, OnFallbackFailed, OnFallbackBusy);
        };

        // Place the axis gizmo in the bottom-left once the overlay Canvas has a real size.
        // Only fires once so subsequent user drags are not clobbered by layout events.
        GizmoCanvas.SizeChanged += GizmoCanvas_SizeChanged;

        BuildBoxWireframeLines();

        // Suspend GL rendering during sleep/wake to prevent context-lost crashes.
        // The GPU's OpenGL context is invalidated when the PC sleeps; collapsing
        // the control unsubscribes from CompositionTarget.Rendering so OnRender
        // (which calls glfwMakeContextCurrent) is never hit with a dead context.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Unloaded += (_, _) =>
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _unloaded = true;
            // Detach from the (surviving, per-VM) fallback controller and stop its timers so a
            // running DispatcherTimer can't root this control after it leaves the tree.
            _fallback?.Detach();
            _fallbackSettleTimer?.Stop();
            _fallbackResizeTimer?.Stop();
            _vm?.LogViewerDiagnostic("UC_CharacterViewer #" + _instanceId + " Unloaded");
        };
    }

    // Instance-id + lifecycle flags for diagnosing the grey-screen-after-navigation bug.
    // VM_CharacterViewer is a persistent singleton but UC_CharacterViewer is re-created
    // by WPF's ContentPresenter every time the user navigates back to the Body Type
    // Profiles tab. If the old UC's CompositionTarget.Rendering subscription lingers
    // past Unloaded, its OnRender ticks keep writing to a detached D3D surface while
    // the new UC's surface stays grey. The id lets us tell which UC is firing which
    // callback in the verbose log; _firstRenderLogged gates a one-shot "first OnRender"
    // diagnostic per instance; _unloaded tags any OnRender that fires after Unloaded
    // as a zombie tick (logged at most once per instance).
    private static int _instanceCounter;
    private readonly int _instanceId = System.Threading.Interlocked.Increment(ref _instanceCounter);
    private bool _firstRenderLogged;
    private bool _unloaded;
    private bool _zombieRenderLogged;

    private VM_CharacterViewer? _vm;
    private bool _glStarted;
    // Latched true if GlControl.Start() throws — the OpenGL driver lacks WGL_NV_DX_interop
    // (Wine/Proton, a VM's virtual GPU, an RDP session, some legacy drivers). This is an
    // unrecoverable environment limitation, not a transient error, so once it trips we drop
    // into the placeholder state and make every SizeChanged/Loaded re-entry into TryStartGl a
    // no-op instead of re-throwing an unhandled dispatcher exception on each layout pass.
    private bool _glFailed;

    // Software fallback preview state (only ever touched when _glFailed is true). The
    // per-VM FallbackPreviewController renders the retained scene inputs through the
    // offscreen renderer and hands back WriteableBitmap frames; this control feeds it
    // orbit/pan/zoom from FallbackImage's mouse events and debounces resizes. _fallbackDragging
    // gates the low-res-during-drag path; the settle timer restores full-res after interaction
    // stops; the resize timer coalesces splitter drags.
    private FallbackPreviewController? _fallback;
    private bool _fallbackActive;
    private bool _fallbackDragging;
    private DispatcherTimer? _fallbackSettleTimer;
    private DispatcherTimer? _fallbackResizeTimer;

    // Hover tooltip state. _currentHoverMesh tracks the mesh-tooltip identity so we only
    // rebuild the content TextBlock when the hover target changes (the textures + asset-
    // source rebuild allocates a non-trivial amount). _currentHoverMeasurementLabel does
    // the same for the measurement-line tooltip path; the two are mutually exclusive —
    // when one is showing, the other's identity is cleared so flipping between them
    // forces a content rebuild on the next tick.
    private readonly DispatcherTimer _hoverTimer;
    private readonly ToolTip _hoverTooltip;
    private Point _lastMousePos;
    private GlMesh? _currentHoverMesh;
    private string? _currentHoverMeasurementLabel;

    /// <summary>Pixel-distance threshold for the measurement-line hover hit-test. Lines
    /// render at GL line-width 4.5 (see <c>GlRenderer.DrawMeasurementLines</c>), so an 8px
    /// threshold gives a comfortable click target without bleeding into nearby segments
    /// when multiple measurements are on screen at once. Tweak in tandem with the GL line
    /// width if either changes.</summary>
    private const float MeasurementLineHoverThresholdPx = 8.0f;

    // BB-pick drag state. Populated on MouseDown when IsBoundingBoxPickMode is on; MouseMove
    // updates BoxSelectionRect's Canvas.Left/Top/Width/Height; MouseUp hands the final screen
    // rect to the VM's ComputeBoxFromScreenRect + NotifyKeyVertexBoxPicked path.
    private bool _boxDragging;
    private Point _boxDragStart;

    // Region vertex-edit drag state. Shares the BoxSelectionRect rubber-band with BB pick. On MouseUp a
    // tiny rect is treated as a single-vertex click (ray pick → toggle), a real rect as a bulk edit.
    private bool _vertexEditDragging;
    private Point _vertexEditDragStart;

    // Axis-gizmo drag state. MouseDown on AxisGizmoBorder seeds the offset between the cursor
    // and the widget's Canvas.Left/Top; MouseMove keeps that offset constant so the border
    // tracks the cursor without snapping. MouseUp releases capture.
    private bool _gizmoDragging;
    private Point _gizmoDragStart;
    private double _gizmoStartLeft;
    private double _gizmoStartTop;
    // Set true after the first SizeChanged places the widget in the bottom-left corner so
    // the default position is only applied once (user drags stick after that).
    private bool _gizmoDefaultPositioned;

    // The 12 AABB-edge lines that make up the pending-box wireframe overlay. Populated once
    // in the constructor and stored here so UpdateBoxWireframe can rewrite their endpoints
    // each frame without re-creating Line instances.
    private readonly System.Windows.Shapes.Line[] _boxWireLines = new System.Windows.Shapes.Line[12];

    /// <summary>Re-subscribes the pick-collection and pick-selection-request events to the new <see cref="VM_CharacterViewer"/> when the DataContext changes, then attempts to start GL.</summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.Picks.CollectionChanged -= Picks_CollectionChanged;
            _vm.RequestPickSelection -= OnVmRequestPickSelection;
        }

        _vm = DataContext as VM_CharacterViewer;

        if (_vm != null)
        {
            _vm.Picks.CollectionChanged += Picks_CollectionChanged;
            _vm.RequestPickSelection += OnVmRequestPickSelection;
        }

        TryStartGl();
    }

    /// <summary>Reflects an external selection request (e.g., user picked a KeyVertex in the
    /// BodyTypeProfile editor) into the PicksList. Setting SelectedItems fires
    /// PicksList_SelectionChanged which routes into VM.SetSelectedPicks, so the renderer
    /// marker turns green through the normal path.</summary>
    private void OnVmRequestPickSelection(IReadOnlyList<VM_CharacterViewer.PickRow> rows)
    {
        if (rows == null) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            PicksList.SelectedItems.Clear();
            foreach (var row in rows) PicksList.SelectedItems.Add(row);
        }));
    }

    /// <summary>Auto-selects the most recently added pick (so "Select Mirror" defaults to it); clears the VM selection on a collection reset.</summary>
    private void Picks_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Auto-select the most recently added pick so Select Mirror operates on it
        // by default (user can still Ctrl/Shift-click to multi-select).
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
            && e.NewItems != null && e.NewItems.Count > 0)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PicksList.SelectedItems.Clear();
                foreach (var item in e.NewItems) PicksList.SelectedItems.Add(item);
            }));
        }
        else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            _vm?.SetSelectedPicks(Array.Empty<VM_CharacterViewer.PickRow>());
        }
    }

    /// <summary>Forwards the list's selected pick rows to <see cref="VM_CharacterViewer.SetSelectedPicks"/>.</summary>
    private void PicksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        var sel = new List<VM_CharacterViewer.PickRow>(PicksList.SelectedItems.Count);
        foreach (var item in PicksList.SelectedItems)
            if (item is VM_CharacterViewer.PickRow row) sel.Add(row);
        _vm.SetSelectedPicks(sel);
    }

    /// <summary>Starts the GL render loop once the control is loaded and has a non-zero size; idempotent. Toggles visibility afterward to work around a GLWpfControl bug where continuous rendering isn't registered when the control is already visible at Start().</summary>
    private void TryStartGl()
    {
        if (_glStarted || _glFailed) return;
        if (!IsLoaded)
        {
            (_vm ??= DataContext as VM_CharacterViewer)?.LogViewerDiagnostic(
                "UC_CharacterViewer #" + _instanceId + " TryStartGl skipped: !IsLoaded");
            return;
        }
        if (GlControl.ActualWidth <= 0 || GlControl.ActualHeight <= 0)
        {
            (_vm ??= DataContext as VM_CharacterViewer)?.LogViewerDiagnostic(
                "UC_CharacterViewer #" + _instanceId + " TryStartGl skipped: zero size (w="
                + GlControl.ActualWidth + ", h=" + GlControl.ActualHeight + ")");
            return;
        }

        var settings = new GLWpfControlSettings
        {
            MajorVersion = 3,
            MinorVersion = 3,
            RenderContinuously = true
        };
        try
        {
            GlControl.Start(settings);
        }
        catch (Exception ex)
        {
            // GLWpfControl.Start() throws when the OpenGL driver lacks WGL_NV_DX_interop,
            // the extension it uses to share its render surface with WPF's D3D compositor.
            // Current GLWpfControl versions surface this as a managed exception whose message
            // names NV_DX_interop; older versions took native code paths. We catch broadly
            // (not by exception type or message) so the degradation survives across versions,
            // and latch _glFailed so the SizeChanged/Loaded handlers that re-invoke TryStartGl
            // stop retrying instead of rethrowing on every layout pass. This is a permanent
            // environment limitation (see _glFailed), so we degrade to a placeholder rather
            // than attempt any recovery.
            _glFailed = true;
            _vm ??= DataContext as VM_CharacterViewer;
            // Per-instance verbose diagnostic (mirrors the file's other lifecycle logs) ...
            _vm?.LogViewerDiagnostic("UC_CharacterViewer #" + _instanceId
                + " TryStartGl: Start() FAILED: " + ex.Message);
            // ... plus a single always-on warning through the VM's non-verbose error channel.
            _vm?.NotifyRenderingUnavailable(
                "GLWpfControl.Start() threw " + ex.GetType().Name + ": " + ex.Message);
            // Try the view-only software fallback (offscreen renderer → WriteableBitmap);
            // it falls back to the static placeholder if even that can't be set up.
            ActivateFallbackOrPlaceholder();
            return;
        }

        _glStarted = true;

        // GLWpfControl bug: Start() registers CompositionTarget.Rendering only
        // inside IsVisibleChanged, but if the control is already visible when
        // Start() is called, that event never fires. Force continuous rendering
        // by toggling visibility to trigger the handler.
        GlControl.Visibility = Visibility.Collapsed;
        GlControl.Visibility = Visibility.Visible;

        (_vm ??= DataContext as VM_CharacterViewer)?.LogViewerDiagnostic(
            "UC_CharacterViewer #" + _instanceId + " TryStartGl: Start() OK + visibility toggled (w="
            + GlControl.ActualWidth.ToString("F0") + ", h="
            + GlControl.ActualHeight.ToString("F0") + ")");
    }

    /// <summary>
    /// Swaps the live viewport for the static "3D preview unavailable" placeholder after a
    /// GL-start failure (see the catch in <see cref="TryStartGl"/>): collapses the GL control
    /// and the overlays that only make sense over a live viewport (axis gizmo, pending-box
    /// wireframe) and reveals <c>GlUnavailablePanel</c>. The placeholder is the last child in
    /// its grid row, so it also masks the loading / pending-box panels should their VM flags
    /// ever be set. View-only — the VM flag and logging are handled at the TryStartGl catch site.
    /// </summary>
    private void ShowGlUnavailablePlaceholder()
    {
        GlControl.Visibility = Visibility.Collapsed;
        GizmoCanvas.Visibility = Visibility.Collapsed;
        BoxWireframeCanvas.Visibility = Visibility.Collapsed;
        GlUnavailablePanel.Visibility = Visibility.Visible;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SOFTWARE FALLBACK PREVIEW (view-only; no NV_DX_interop required)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// GL startup failed: collapse the live viewport and try to bring up the view-only
    /// software fallback preview (the offscreen GL renderer, which needs no NV_DX_interop,
    /// producing <see cref="WriteableBitmap"/> frames). If the fallback controller can't be
    /// obtained (registry unconfigured, truly headless), degrade to the static placeholder.
    /// Runs once per control instance, from the <see cref="TryStartGl"/> catch (guarded by
    /// <c>_glFailed</c>).
    /// </summary>
    private void ActivateFallbackOrPlaceholder()
    {
        // Collapse the live viewport + its overlays either way (same as the static path).
        GlControl.Visibility = Visibility.Collapsed;
        GizmoCanvas.Visibility = Visibility.Collapsed;
        BoxWireframeCanvas.Visibility = Visibility.Collapsed;

        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) { ShowGlUnavailablePlaceholder(); return; }

        try
        {
            _fallback = FallbackPreviewControllerRegistry.GetOrCreate(_vm);
        }
        catch (Exception ex)
        {
            _vm.LogViewerDiagnostic("UC_CharacterViewer #" + _instanceId
                + " fallback preview unavailable (" + ex.Message + "); showing static placeholder");
            ShowGlUnavailablePlaceholder();
            return;
        }

        _fallbackActive = true;

        // After interaction stops, re-render once at full resolution + high-quality scaling
        // (during a drag we render half-res + fast scaling for responsiveness).
        _fallbackSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _fallbackSettleTimer.Tick += FallbackSettle_Tick;
        // Coalesce splitter-drag resizes into a single re-render at the settled size.
        _fallbackResizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _fallbackResizeTimer.Tick += FallbackResize_Tick;

        // Wire camera input + resize on the fallback image (no pick/edit handlers — those
        // need the live VM and are out of scope for the view-only fallback).
        FallbackImage.MouseDown += FallbackImage_MouseDown;
        FallbackImage.MouseMove += FallbackImage_MouseMove;
        FallbackImage.MouseUp += FallbackImage_MouseUp;
        FallbackImage.MouseWheel += FallbackImage_MouseWheel;
        FallbackImage.SizeChanged += FallbackImage_SizeChanged;

        FallbackPanel.Visibility = Visibility.Visible;

        // Receive frames / permanent failure / busy transitions. Attach re-shows any prior
        // frame and kicks a render if a size is already known; otherwise the first
        // SizeChanged (once layout gives the image a size) does the initial render.
        _fallback.Attach(OnFallbackFrame, OnFallbackFailed, OnFallbackBusy);
        RequestFallbackRender(lowRes: false);
    }

    /// <summary>Controller callback (UI thread): a new frame is ready — show it, and once
    /// the software preview is producing frames, drop any static placeholder that was up.</summary>
    private void OnFallbackFrame(WriteableBitmap bmp)
    {
        if (!ReferenceEquals(FallbackImage.Source, bmp)) FallbackImage.Source = bmp;
        if (GlUnavailablePanel.Visibility == Visibility.Visible)
            GlUnavailablePanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Controller callback (UI thread): even the offscreen renderer can't run
    /// (truly headless). Collapse the software preview and show the static placeholder.</summary>
    private void OnFallbackFailed(Exception ex)
    {
        _fallbackActive = false;
        _fallbackSettleTimer?.Stop();
        _fallbackResizeTimer?.Stop();
        FallbackPanel.Visibility = Visibility.Collapsed;
        ShowGlUnavailablePlaceholder();
    }

    /// <summary>Controller callback (UI thread): toggles the "Rendering..." busy chip so the
    /// pause after a drag reads as intentional work rather than a freeze.</summary>
    private void OnFallbackBusy(bool busy)
    {
        FallbackBusyIndicator.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Requests a fallback render at the image's current pixel size. No-op until the
    /// image has a real size or if the fallback isn't active.</summary>
    private void RequestFallbackRender(bool lowRes)
    {
        if (!_fallbackActive || _fallback == null) return;
        int w = (int)Math.Round(FallbackImage.ActualWidth);
        int h = (int)Math.Round(FallbackImage.ActualHeight);
        if (w <= 0 || h <= 0) return;
        _fallback.RequestRender(w, h, lowRes);
    }

    private void RestartFallbackSettleTimer()
    {
        _fallbackSettleTimer?.Stop();
        _fallbackSettleTimer?.Start();
    }

    /// <summary>Left-drag orbits, middle-drag pans — same OrbitCamera and button conventions
    /// as the live viewport's <see cref="GlControl_MouseDown"/>, minus the pick modes.</summary>
    private void FallbackImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;
        if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Middle) return;

        var pos = e.GetPosition(FallbackImage);
        _vm.Camera.OnMouseDown((float)pos.X, (float)pos.Y,
            leftButton: e.ChangedButton == MouseButton.Left,
            middleButton: e.ChangedButton == MouseButton.Middle);
        _fallbackDragging = true;
        RenderOptions.SetBitmapScalingMode(FallbackImage, BitmapScalingMode.LowQuality);
        FallbackImage.CaptureMouse();
    }

    private void FallbackImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_fallbackDragging) return;
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;

        var pos = e.GetPosition(FallbackImage);
        _vm.Camera.OnMouseMove((float)pos.X, (float)pos.Y);
        RequestFallbackRender(lowRes: true);
        RestartFallbackSettleTimer();
    }

    private void FallbackImage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_fallbackDragging) return;
        _fallbackDragging = false;
        _vm?.Camera.OnMouseUp();
        FallbackImage.ReleaseMouseCapture();
        RestartFallbackSettleTimer();   // settle → one full-res render
    }

    private void FallbackImage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;

        _vm.Camera.OnMouseWheel(e.Delta);
        RenderOptions.SetBitmapScalingMode(FallbackImage, BitmapScalingMode.LowQuality);
        RequestFallbackRender(lowRes: true);
        RestartFallbackSettleTimer();
    }

    private void FallbackImage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Debounce: dragging a GridSplitter fires a flood of SizeChanged; re-render once
        // it settles (also handles the initial render once layout gives the image a size).
        _fallbackResizeTimer?.Stop();
        _fallbackResizeTimer?.Start();
    }

    private void FallbackSettle_Tick(object? sender, EventArgs e)
    {
        _fallbackSettleTimer?.Stop();
        RenderOptions.SetBitmapScalingMode(FallbackImage, BitmapScalingMode.HighQuality);
        RequestFallbackRender(lowRes: false);
    }

    private void FallbackResize_Tick(object? sender, EventArgs e)
    {
        _fallbackResizeTimer?.Stop();
        RequestFallbackRender(lowRes: false);
    }

    /// <summary>Collapses the GL control before sleep and restores it (deferred) on resume, so OnRender never runs against an invalidated GL context across a sleep/wake cycle.</summary>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        // GL never started on this system — there is no render loop to suspend, and the
        // Resume branch below would otherwise un-collapse the failed GlControl back over
        // the placeholder. Leave the placeholder state untouched across sleep/wake.
        if (_glFailed) return;

        if (e.Mode == PowerModes.Suspend)
        {
            // Collapse before sleep — stops the render loop so OnRender won't
            // fire with an invalid GL context during or immediately after wake.
            Dispatcher.BeginInvoke(() => GlControl.Visibility = Visibility.Collapsed);
        }
        else if (e.Mode == PowerModes.Resume)
        {
            // Delay restore to give the GPU driver time to reinitialize.
            Dispatcher.BeginInvoke(() =>
            {
                GlControl.Visibility = Visibility.Visible;
            }, DispatcherPriority.Background);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  GL RENDER CALLBACK
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Per-frame GL render callback: recovers from context loss on a reused VM, initializes GL on the first frame, uploads any pending scene, renders at device-pixel resolution, and updates the axis-gizmo + box-wireframe overlays. Emits one-shot first-render / zombie-render diagnostics.</summary>
    /// <param name="delta">Time since the previous frame (unused; rendering is state-driven).</param>
    private void GlControl_OnRender(TimeSpan delta)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;

        // Zombie-render diagnostic: if OnRender fires after Unloaded, this UC's
        // CompositionTarget.Rendering subscription was not cleanly released, and
        // it is now writing to a detached D3D surface. Logged once per instance
        // so the noise stays bounded on a stuck loop.
        if (_unloaded && !_zombieRenderLogged)
        {
            _zombieRenderLogged = true;
            _vm.LogViewerDiagnostic("UC_CharacterViewer #" + _instanceId
                + " OnRender AFTER Unloaded (zombie render tick)");
        }

        // Stale-VM detection: GLWpfControl 4.x creates a new GL context per control
        // instance. When the user navigates away from and back to a page that hosts
        // UC_CharacterViewer, WPF destroys the old UC (and its context) and spins up
        // a new one. The persistent VM is unaware of the context swap: IsGlInitialized
        // stays true, and Renderer keeps the shader/VAO/VBO/texture IDs from the dead
        // context. Those IDs are invalid in this new context, so Render() draws 10
        // meshes to nowhere — a grey screen. Detecting the case on this UC's first
        // OnRender (before anything tries to draw) and asking the VM to forget the
        // dead IDs lets the normal InitializeGl path below rebuild everything against
        // this context. LoadNpcAsync's same-NPC short-circuit is disarmed inside
        // HandleGlContextLoss so the next preview request rebuilds the scene.
        if (!_firstRenderLogged && _vm.IsGlInitialized)
        {
            _vm.LogViewerDiagnostic("UC_CharacterViewer #" + _instanceId
                + " first OnRender on reused VM: invoking HandleGlContextLoss to rebind GL resources");
            _vm.HandleGlContextLoss();
        }

        // Initialize GL on first render — context is guaranteed current here.
        // The shader directory ships next to CharacterViewer.Rendering.dll;
        // ModuleResourceLocator resolves it from the assembly's on-disk path
        // so the same code works whether SynthEBD or NPC2 is the host.
        if (!_vm.IsGlInitialized)
        {
            _vm.InitializeGl(ModuleResourceLocator.ShaderDirectory);
        }

        // Process any pending scene setup (mesh upload, texture loading)
        _vm.ProcessPendingScene();

        // Use device pixels for GL viewport — WPF logical units cause
        // bottom-left quadrant rendering on high-DPI displays
        var source = PresentationSource.FromVisual(GlControl);
        double dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        int w = (int)(GlControl.ActualWidth * dpiScaleX);
        int h = (int)(GlControl.ActualHeight * dpiScaleY);
        if (w > 0 && h > 0)
        {
            _vm.Renderer.Render(_vm.Camera, w, h);
        }

        // First-render diagnostic (once per UC instance): confirms which instance
        // actually owns the render loop that reaches pixels, plus the state the
        // renderer sees at that moment (viewport, mesh count, GL init). If the
        // new instance's id never logs this line after a grey-screen repro, its
        // render subscription was never wired up.
        if (!_firstRenderLogged)
        {
            _firstRenderLogged = true;
            _vm.LogViewerDiagnostic("UC_CharacterViewer #" + _instanceId
                + " first OnRender: viewport=" + w + "x" + h
                + ", meshes=" + _vm.Renderer.Meshes.Count
                + ", glInit=" + _vm.IsGlInitialized
                + ", unloaded=" + _unloaded);
        }

        UpdateAxisGizmo();
        UpdateBoxWireframe();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PENDING-BOX 3D WIREFRAME OVERLAY
    // ═══════════════════════════════════════════════════════════════════════

    // AABB edge list: index pairs into the 8-corner array built in UpdateBoxWireframe.
    // Corner indexing: bit 0 = X (0=min, 1=max), bit 1 = Y, bit 2 = Z.
    // 12 edges = 4 bottom loop + 4 top loop + 4 verticals.
    private static readonly (int a, int b)[] BoxEdgeIndices =
    {
        (0b000, 0b001), (0b001, 0b101), (0b101, 0b100), (0b100, 0b000), // y=min loop
        (0b010, 0b011), (0b011, 0b111), (0b111, 0b110), (0b110, 0b010), // y=max loop
        (0b000, 0b010), (0b001, 0b011), (0b100, 0b110), (0b101, 0b111), // verticals
    };

    /// <summary>Creates the 12 reusable WPF lines for the pending-box AABB wireframe overlay once, so <see cref="UpdateBoxWireframe"/> can rewrite their endpoints each frame without reallocating.</summary>
    private void BuildBoxWireframeLines()
    {
        var stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xCC, 0x40));
        stroke.Freeze();
        for (int i = 0; i < _boxWireLines.Length; i++)
        {
            var line = new System.Windows.Shapes.Line
            {
                Stroke = stroke,
                StrokeThickness = 1.5,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeStartLineCap = PenLineCap.Round,
            };
            _boxWireLines[i] = line;
            BoxWireframeCanvas.Children.Add(line);
        }
    }

    /// <summary>
    /// Projects the 8 AABB corners of the pending box (mesh-local, pre-ModelScale)
    /// through model*view*projection each frame and rewrites the 12 edge lines in
    /// WPF screen coords. Mirrors the projection path in
    /// <see cref="VM_CharacterViewer.ComputeBoxFromScreenRect"/> so the wireframe
    /// overlays exactly the vertices the BB-pick would match.
    /// </summary>
    private void UpdateBoxWireframe()
    {
        if (_vm == null || !_vm.HasPendingBox) return;
        double vw = GlControl.ActualWidth;
        double vh = GlControl.ActualHeight;
        if (vw <= 0 || vh <= 0) return;

        float aspect = (float)(vw / vh);
        var viewProj = _vm.Camera.GetViewMatrix() * _vm.Camera.GetProjectionMatrix(aspect);
        float s = _vm.Renderer.ModelScale;

        float minX = _vm.PendingBoxMinX, maxX = _vm.PendingBoxMaxX;
        float minY = _vm.PendingBoxMinY, maxY = _vm.PendingBoxMaxY;
        float minZ = _vm.PendingBoxMinZ, maxZ = _vm.PendingBoxMaxZ;

        Span<double> sx = stackalloc double[8];
        Span<double> sy = stackalloc double[8];
        Span<bool>   sv = stackalloc bool[8];
        for (int i = 0; i < 8; i++)
        {
            float x = ((i & 1) == 0 ? minX : maxX) * s;
            float y = ((i & 2) == 0 ? minY : maxY) * s;
            float z = ((i & 4) == 0 ? minZ : maxZ) * s;
            var clip = new OpenTK.Mathematics.Vector4(x, y, z, 1f) * viewProj;
            if (clip.W <= 0f) { sv[i] = false; continue; }
            double ndcX = clip.X / clip.W;
            double ndcY = clip.Y / clip.W;
            sx[i] = (ndcX * 0.5 + 0.5) * vw;
            sy[i] = (1.0 - (ndcY * 0.5 + 0.5)) * vh;
            sv[i] = true;
        }

        for (int i = 0; i < BoxEdgeIndices.Length; i++)
        {
            var (a, b) = BoxEdgeIndices[i];
            var line = _boxWireLines[i];
            if (!sv[a] || !sv[b]) { line.Visibility = Visibility.Collapsed; continue; }
            line.Visibility = Visibility.Visible;
            line.X1 = sx[a]; line.Y1 = sy[a];
            line.X2 = sx[b]; line.Y2 = sy[b];
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  AXIS ORIENTATION GIZMO
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Rebuilds the three axis lines + labels so their screen directions match the
    /// camera's current orientation. Uses only the rotation portion of the view matrix
    /// (W=0 on the basis vectors), then takes the transformed X/Y as screen dx/dy —
    /// equivalent to an orthographic projection, which is what ViewCube-style gizmos
    /// want (no perspective distortion on a tiny widget). WPF is Y-down so view-Y is
    /// flipped when writing screen coordinates. Called every GL frame; cheap.
    /// </summary>
    private void UpdateAxisGizmo()
    {
        if (_vm == null) return;
        var view = _vm.Camera.GetViewMatrix();

        var xView = new OpenTK.Mathematics.Vector4(1f, 0f, 0f, 0f) * view;
        var yView = new OpenTK.Mathematics.Vector4(0f, 1f, 0f, 0f) * view;
        var zView = new OpenTK.Mathematics.Vector4(0f, 0f, 1f, 0f) * view;

        const float cx = 40f, cy = 40f;
        const float length = 26f;
        UpdateAxisLine(AxisX_Line, AxisX_Label, cx, cy, xView.X, xView.Y, length);
        UpdateAxisLine(AxisY_Line, AxisY_Label, cx, cy, yView.X, yView.Y, length);
        UpdateAxisLine(AxisZ_Line, AxisZ_Label, cx, cy, zView.X, zView.Y, length);
    }

    /// <summary>Positions one gizmo axis line and its label from a screen-space direction (dx, dy) about the widget centre; flips Y for WPF's Y-down coordinates.</summary>
    private static void UpdateAxisLine(
        System.Windows.Shapes.Line line, TextBlock label,
        float cx, float cy, float dx, float dy, float length)
    {
        float ex = cx + dx * length;
        float ey = cy - dy * length; // WPF Y-down
        line.X1 = cx; line.Y1 = cy;
        line.X2 = ex; line.Y2 = ey;
        Canvas.SetLeft(label, ex - 4);
        Canvas.SetTop(label, ey - 8);
    }

    /// <summary>Places the axis-gizmo widget in the bottom-left corner the first time the overlay canvas gets a real size (only once, so later user drags stick).</summary>
    private void GizmoCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_gizmoDefaultPositioned) return;
        if (GizmoCanvas.ActualHeight <= 0 || AxisGizmoBorder.Height <= 0) return;

        Canvas.SetLeft(AxisGizmoBorder, 10);
        Canvas.SetTop(AxisGizmoBorder, GizmoCanvas.ActualHeight - AxisGizmoBorder.Height - 10);
        _gizmoDefaultPositioned = true;
    }

    /// <summary>Begins dragging the axis-gizmo widget, capturing the mouse and seeding the cursor-to-widget offset.</summary>
    private void AxisGizmo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _gizmoDragging = true;
        _gizmoDragStart = e.GetPosition(GizmoCanvas);
        _gizmoStartLeft = Canvas.GetLeft(AxisGizmoBorder);
        _gizmoStartTop = Canvas.GetTop(AxisGizmoBorder);
        if (double.IsNaN(_gizmoStartLeft)) _gizmoStartLeft = 10;
        if (double.IsNaN(_gizmoStartTop))
            _gizmoStartTop = Math.Max(0, GizmoCanvas.ActualHeight - AxisGizmoBorder.Height - 10);
        AxisGizmoBorder.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>While dragging, moves the axis-gizmo widget with the cursor, clamped inside the overlay canvas.</summary>
    private void AxisGizmo_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_gizmoDragging) return;
        var pos = e.GetPosition(GizmoCanvas);
        double newLeft = _gizmoStartLeft + (pos.X - _gizmoDragStart.X);
        double newTop  = _gizmoStartTop  + (pos.Y - _gizmoDragStart.Y);

        double maxLeft = Math.Max(0, GizmoCanvas.ActualWidth  - AxisGizmoBorder.ActualWidth);
        double maxTop  = Math.Max(0, GizmoCanvas.ActualHeight - AxisGizmoBorder.ActualHeight);
        newLeft = Math.Clamp(newLeft, 0, maxLeft);
        newTop  = Math.Clamp(newTop,  0, maxTop);

        Canvas.SetLeft(AxisGizmoBorder, newLeft);
        Canvas.SetTop(AxisGizmoBorder, newTop);
    }

    /// <summary>Ends the axis-gizmo drag and releases mouse capture.</summary>
    private void AxisGizmo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_gizmoDragging) return;
        _gizmoDragging = false;
        AxisGizmoBorder.ReleaseMouseCapture();
        e.Handled = true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MOUSE → ORBIT CAMERA
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Left-button handler: routes to the active pick mode (key-vertex, bounding-box rubber-band, region vertex-edit, or light-arrow) if one is on, otherwise begins a camera orbit/pan. Cancels any hover tooltip.</summary>
    private void GlControl_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;

        // Any camera-driving button press cancels the hover tooltip
        HideHoverTooltip();

        var pos = e.GetPosition(GlControl);

        // Key-vertex picking: left-click finds the nearest triangle vertex
        // under the cursor and hands it to the VM instead of orbiting the
        // camera. Takes precedence over light-arrow picking because the
        // classifier workflow is the exclusive focus when this mode is on.
        if (e.ChangedButton == MouseButton.Left && _vm.IsKeyVertexPickMode)
        {
            var pick = _vm.HitTestKeyVertex(
                (float)pos.X, (float)pos.Y,
                (float)GlControl.ActualWidth, (float)GlControl.ActualHeight);
            if (pick.HasValue)
            {
                _vm.NotifyKeyVertexPicked(pick.Value);
            }
            e.Handled = true;
            return;
        }

        // BB picking: left-drag paints a screen rect; on MouseUp the VM projects every
        // mesh vertex through it and returns a mesh-local AABB. Also takes precedence
        // over orbit + light arrows when the mode is on.
        if (e.ChangedButton == MouseButton.Left && _vm.IsBoundingBoxPickMode)
        {
            _boxDragging = true;
            _boxDragStart = pos;
            Canvas.SetLeft(BoxSelectionRect, pos.X);
            Canvas.SetTop(BoxSelectionRect, pos.Y);
            BoxSelectionRect.Width = 0;
            BoxSelectionRect.Height = 0;
            BoxSelectionRect.Visibility = Visibility.Visible;
            GlControl.CaptureMouse();
            e.Handled = true;
            return;
        }

        // Region vertex-edit: left press starts a rubber-band (shared with BB). A tiny rect on release
        // is a single-vertex toggle; a real rect bulk-edits every enclosed vertex. Takes precedence over
        // orbit + light arrows like the other pick modes.
        if (e.ChangedButton == MouseButton.Left && _vm.IsRegionVertexEditMode)
        {
            _vertexEditDragging = true;
            _vertexEditDragStart = pos;
            Canvas.SetLeft(BoxSelectionRect, pos.X);
            Canvas.SetTop(BoxSelectionRect, pos.Y);
            BoxSelectionRect.Width = 0;
            BoxSelectionRect.Height = 0;
            BoxSelectionRect.Visibility = Visibility.Visible;
            GlControl.CaptureMouse();
            e.Handled = true;
            return;
        }

        // Arrow picking: left-click on a light gizmo selects that light for
        // editing instead of starting a camera orbit.
        if (e.ChangedButton == MouseButton.Left && _vm.ShowLightControls)
        {
            var source = PresentationSource.FromVisual(GlControl);
            double dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            int w = (int)(GlControl.ActualWidth * dpiScaleX);
            int h = (int)(GlControl.ActualHeight * dpiScaleY);
            int hit = _vm.HitTestLightArrow(
                (float)(pos.X * dpiScaleX), (float)(pos.Y * dpiScaleY),
                w, h);
            if (hit > 0)
            {
                _vm.SelectedLightIndex = hit;
                e.Handled = true;
                return;
            }
        }

        _vm.Camera.OnMouseDown(
            (float)pos.X, (float)pos.Y,
            leftButton: e.ChangedButton == MouseButton.Left,
            middleButton: e.ChangedButton == MouseButton.Middle);
        GlControl.CaptureMouse();
    }

    /// <summary>Updates the rubber-band rect while painting a BB/vertex-edit selection, otherwise drives camera orbit/pan and restarts the hover dwell timer when not actively orbiting.</summary>
    private void GlControl_MouseMove(object sender, MouseEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;

        var pos = e.GetPosition(GlControl);

        // While painting a BB or a vertex-edit rect, keep the rubber-band rect in sync and suppress the
        // orbit/pan update + hover tooltip. Camera state stays untouched.
        if (_boxDragging || _vertexEditDragging)
        {
            var dragStart = _boxDragging ? _boxDragStart : _vertexEditDragStart;
            double x = Math.Min(dragStart.X, pos.X);
            double y = Math.Min(dragStart.Y, pos.Y);
            double w = Math.Abs(pos.X - dragStart.X);
            double h = Math.Abs(pos.Y - dragStart.Y);
            Canvas.SetLeft(BoxSelectionRect, x);
            Canvas.SetTop(BoxSelectionRect, y);
            BoxSelectionRect.Width = w;
            BoxSelectionRect.Height = h;
            HideHoverTooltip();
            return;
        }

        _vm.Camera.OnMouseMove((float)pos.X, (float)pos.Y);

        // Restart hover dwell timer only when the user is not actively orbiting
        _lastMousePos = pos;
        bool isOrbiting = e.LeftButton == MouseButtonState.Pressed
                       || e.MiddleButton == MouseButtonState.Pressed;
        if (isOrbiting)
        {
            HideHoverTooltip();
        }
        else
        {
            _hoverTimer.Stop();
            _hoverTimer.Start();
        }
    }

    /// <summary>Completes a region vertex-edit or bounding-box selection (resolving the painted rect into a vertex pick or mesh-local AABB), otherwise ends the camera drag.</summary>
    private void GlControl_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;

        // Region vertex-edit release: a tiny rect = single-vertex ray-pick toggle; a real rect =
        // bulk-edit every enclosed vertex. Either way the affected vertices fan out through
        // NotifyRegionVertexEdited (with the current Add/Remove direction) to the editor VM.
        if (_vertexEditDragging && e.ChangedButton == MouseButton.Left)
        {
            _vertexEditDragging = false;
            BoxSelectionRect.Visibility = Visibility.Collapsed;
            GlControl.ReleaseMouseCapture();

            var end = e.GetPosition(GlControl);
            float vw = (float)GlControl.ActualWidth, vh = (float)GlControl.ActualHeight;
            double dragW = Math.Abs(end.X - _vertexEditDragStart.X);
            double dragH = Math.Abs(end.Y - _vertexEditDragStart.Y);

            VM_CharacterViewer.RegionVertexEditPick? pick;
            if (dragW < 4 && dragH < 4)
            {
                // Click → ray-pick the nearest vertex under the cursor.
                var hit = _vm.HitTestKeyVertex((float)end.X, (float)end.Y, vw, vh);
                pick = hit.HasValue ? _vm.BuildRegionVertexEdit(hit.Value) : null;
            }
            else
            {
                pick = _vm.BuildRegionVertexEditFromScreenRect(
                    (float)_vertexEditDragStart.X, (float)_vertexEditDragStart.Y, (float)end.X, (float)end.Y, vw, vh);
            }
            if (pick.HasValue) _vm.NotifyRegionVertexEdited(pick.Value);

            e.Handled = true;
            return;
        }

        // BB drag release: resolve the painted screen rect into a mesh-local AABB via
        // the VM and fan out through NotifyKeyVertexBoxPicked. Tiny/degenerate drags are
        // filtered by ComputeBoxFromScreenRect itself (returns null).
        if (_boxDragging && e.ChangedButton == MouseButton.Left)
        {
            _boxDragging = false;
            BoxSelectionRect.Visibility = Visibility.Collapsed;
            GlControl.ReleaseMouseCapture();

            var end = e.GetPosition(GlControl);
            var pick = _vm.ComputeBoxFromScreenRect(
                (float)_boxDragStart.X, (float)_boxDragStart.Y,
                (float)end.X, (float)end.Y,
                (float)GlControl.ActualWidth, (float)GlControl.ActualHeight,
                _vm.PendingBoxCriterion);
            // Seed the pending-box edit panel + wireframe instead of firing the pick
            // immediately. The user tweaks the six min/max values (or clicks
            // Shrink-to-Camera-Half), then Confirm fans out through
            // NotifyKeyVertexBoxPicked; Cancel discards.
            if (pick.HasValue) _vm.BeginPendingBox(pick.Value);

            e.Handled = true;
            return;
        }

        _vm.Camera.OnMouseUp();
        GlControl.ReleaseMouseCapture();
    }

    /// <summary>Zooms the camera by the wheel delta and hides any hover tooltip.</summary>
    private void GlControl_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;

        HideHoverTooltip();
        _vm.Camera.OnMouseWheel(e.Delta);
    }

    /// <summary>Hides the hover tooltip when the cursor leaves the GL control.</summary>
    private void GlControl_MouseLeave(object sender, MouseEventArgs e)
    {
        HideHoverTooltip();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  RIGHT-CLICK → MESH PICKING & TEXTURE TOGGLE CONTEXT MENU
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Right-click handler: hit-tests the mesh under the cursor and opens a context menu of per-slot texture toggles, mesh visibility, and "show all / reset all" actions for it.</summary>
    private void GlControl_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;

        var pos = e.GetPosition(GlControl);

        var hitMesh = _vm.HitTest(
            (float)pos.X, (float)pos.Y,
            (float)GlControl.ActualWidth, (float)GlControl.ActualHeight);

        if (hitMesh == null) return;

        var menu = new ContextMenu();

        // Header: shape name
        var header = new MenuItem
        {
            Header = hitMesh.ShapeName + " (" + hitMesh.BodyPart + ")",
            IsEnabled = false,
            FontWeight = FontWeights.Bold
        };
        menu.Items.Add(header);
        menu.Items.Add(new Separator());

        // Build toggle items for each texture slot that is active on this shape
        var toggles = new List<(string Label, bool HasTexture, Func<bool> Getter, Action<bool> Setter)>
        {
            ("Diffuse",      hitMesh.DiffuseTexture != 0,  () => hitMesh.DiffuseEnabled,   v => hitMesh.DiffuseEnabled = v),
            ("Normal Map",   hitMesh.HasNormalMap,          () => hitMesh.NormalEnabled,     v => hitMesh.NormalEnabled = v),
            ("Skin/SSS",     hitMesh.HasSkinMap,            () => hitMesh.SkinEnabled,       v => hitMesh.SkinEnabled = v),
            ("Specular",     hitMesh.HasSpecular,           () => hitMesh.SpecularEnabled,   v => hitMesh.SpecularEnabled = v),
            ("Face Tint",    hitMesh.HasFaceTintMap,        () => hitMesh.FaceTintEnabled,   v => hitMesh.FaceTintEnabled = v),
            ("Detail Map",   hitMesh.HasDetailMap,          () => hitMesh.DetailEnabled,     v => hitMesh.DetailEnabled = v),
            ("Environment",  hitMesh.HasEnvironmentMap,     () => hitMesh.EnvMapEnabled,     v => hitMesh.EnvMapEnabled = v),
            ("Emissive",     hitMesh.HasEmissive,           () => hitMesh.EmissiveEnabled,   v => hitMesh.EmissiveEnabled = v),
            (TintColorLabel(hitMesh), hitMesh.HasTintColor,     () => hitMesh.TintColorEnabled,  v => hitMesh.TintColorEnabled = v),
        };

        bool anyAdded = false;
        foreach (var (label, hasTexture, getter, setter) in toggles)
        {
            if (!hasTexture) continue;
            anyAdded = true;

            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = getter(),
                StaysOpenOnClick = true
            };
            // Capture setter in closure
            var localSetter = setter;
            item.Click += (_, _) => localSetter(item.IsChecked);
            menu.Items.Add(item);
        }

        if (!anyAdded)
        {
            menu.Items.Add(new MenuItem { Header = "(no textures)", IsEnabled = false });
        }

        // "Show Mesh" toggle
        menu.Items.Add(new Separator());
        var visItem = new MenuItem
        {
            Header = "Show Mesh",
            IsCheckable = true,
            IsChecked = hitMesh.IsRendering,
            StaysOpenOnClick = true
        };
        visItem.Click += (_, _) => hitMesh.IsRendering = visItem.IsChecked;
        menu.Items.Add(visItem);

        // "Show All Meshes" to reset visibility
        var showAllItem = new MenuItem { Header = "Show All Meshes" };
        showAllItem.Click += (_, _) =>
        {
            foreach (var mesh in _vm.Renderer.Meshes)
                mesh.IsRendering = true;
        };
        menu.Items.Add(showAllItem);

        // "Reset All Textures" to re-enable all toggles
        var resetItem = new MenuItem { Header = "Reset All Textures" };
        resetItem.Click += (_, _) =>
        {
            foreach (var mesh in _vm.Renderer.Meshes)
            {
                mesh.DiffuseEnabled = true;
                mesh.NormalEnabled = true;
                mesh.SkinEnabled = true;
                mesh.SpecularEnabled = true;
                mesh.FaceTintEnabled = true;
                mesh.DetailEnabled = true;
                mesh.EnvMapEnabled = true;
                mesh.EmissiveEnabled = true;
                mesh.TintColorEnabled = true;
            }
        };
        menu.Items.Add(resetItem);

        GlControl.ContextMenu = menu;
        menu.IsOpen = true;

        e.Handled = true;
    }

    /// <summary>Builds the context-menu label for a mesh's tint-color toggle, appending the RGB values when a tint colour is present.</summary>
    private static string TintColorLabel(GlMesh mesh)
    {
        if (!mesh.HasTintColor) return "Tint Color";
        int r = (int)(mesh.TintColor.X * 255);
        int g = (int)(mesh.TintColor.Y * 255);
        int b = (int)(mesh.TintColor.Z * 255);
        return "Tint Color (" + r + ", " + g + ", " + b + ")";
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TOOLBAR HANDLERS
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly (float R, float G, float B)[] BgColors =
    {
        (105f/255f, 105f/255f, 105f/255f), // Dim Gray
        (51f/255f,  51f/255f,  51f/255f),  // Dark Gray
        (0f, 0f, 0f),                       // Black
        (1f, 1f, 1f),                       // White
        (74f/255f,  106f/255f, 138f/255f), // Steel Blue
        (45f/255f,  90f/255f,  39f/255f),  // Forest
    };

    /// <summary>Applies the chosen viewport background colour to the renderer's clear colour.</summary>
    private void BgColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null || BgColorCombo.SelectedIndex < 0 || BgColorCombo.SelectedIndex >= BgColors.Length)
            return;

        var (r, g, b) = BgColors[BgColorCombo.SelectedIndex];
        _vm.Renderer.ClearColor = new OpenTK.Mathematics.Vector3(r, g, b);
        // Also drive BackgroundColor so the software fallback preview (which reads it via the
        // scene snapshot) re-renders with the new background. Harmless in live mode.
        _vm.BackgroundColor = Color.FromRgb((byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f));
    }

    /// <summary>Resets the camera to its default position.</summary>
    private void ResetViewButton_Click(object sender, RoutedEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        _vm?.Camera.Reset();
    }

    /// <summary>Logs the current lighting settings to the viewer diagnostic log.</summary>
    private void LogLightingButton_Click(object sender, RoutedEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        _vm?.LogLightingSettings();
    }

    /// <summary>Clears all key-vertex pick markers from the scene.</summary>
    private void ClearKeyVertexMarkersButton_Click(object sender, RoutedEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        _vm?.ClearKeyVertexMarkers();
    }

    /// <summary>Selects the mirror-image counterparts of the current picks.</summary>
    private void SelectMirrorPicksButton_Click(object sender, RoutedEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        _vm?.SelectMirrorPicks();
    }

    /// <summary>Projects the most recent pick across the mirror axis to create its pair.</summary>
    private void PairAcrossAxisButton_Click(object sender, RoutedEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        _vm?.ProjectLastPickAcrossAxis();
    }

    /// <summary>Handles a selection in either of the BB-pick Criterion popups. The XAML sets
    /// TreeView.Tag = "Pending" (BB-pick toolbar) or "PendingFinal" (pending-box confirm bar)
    /// to dispatch the write to the correct VM property; both popups otherwise share the same
    /// tree data and template. Category headers (non-leaf nodes) are ignored so clicking a
    /// header doesn't blank the current selection. The popup is closed by walking the logical
    /// tree (Popup is in the logical tree but not the visual tree, so VisualTreeHelper
    /// wouldn't find it).</summary>
    private void BoxCriterionSelectionTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (sender is not System.Windows.Controls.TreeView tree) return;
        if (e.NewValue is not BoxCriterionSelectionLeaf leaf) return;
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;

        switch (tree.Tag as string)
        {
            case "Pending":      _vm.PendingBoxCriterion      = leaf.Value; break;
            case "PendingFinal": _vm.PendingBoxFinalCriterion = leaf.Value; break;
            default: return;
        }

        DependencyObject? parent = tree;
        while (parent != null && parent is not System.Windows.Controls.Primitives.Popup)
            parent = LogicalTreeHelper.GetParent(parent);
        if (parent is System.Windows.Controls.Primitives.Popup popup) popup.IsOpen = false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HOVER TOOLTIP — MESH & TEXTURE SOURCE PATHS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Dwell-timer callback: shows a tooltip for the measurement line (priority) or mesh under the cursor, rebuilding its content only when the hover target changes.</summary>
    private void HoverTimer_Tick(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();

        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null || GlControl.ActualWidth <= 0 || GlControl.ActualHeight <= 0)
        {
            HideHoverTooltip();
            return;
        }

        // Measurement-line tooltip takes priority over the mesh tooltip — the lines render
        // on top of the body with depth test disabled (see GlRenderer.DrawMeasurementLines),
        // so visually they ARE the topmost thing under the cursor when one is close enough.
        // Falling through to the mesh hover only when no labeled segment is within the
        // threshold keeps the rest of the existing hover behavior unchanged.
        string? measurementLabel = _vm.HitTestMeasurementLine(
            (float)_lastMousePos.X, (float)_lastMousePos.Y,
            (float)GlControl.ActualWidth, (float)GlControl.ActualHeight,
            MeasurementLineHoverThresholdPx);

        if (measurementLabel != null)
        {
            if (!string.Equals(measurementLabel, _currentHoverMeasurementLabel, StringComparison.Ordinal))
            {
                _currentHoverMeasurementLabel = measurementLabel;
                _currentHoverMesh = null; // forces mesh-content rebuild on next mesh hit
                _hoverTooltip.Content = BuildMeasurementHoverTooltipContent(measurementLabel);
            }
            if (!_hoverTooltip.IsOpen)
                _hoverTooltip.IsOpen = true;
            return;
        }

        var hit = _vm.HitTest(
            (float)_lastMousePos.X, (float)_lastMousePos.Y,
            (float)GlControl.ActualWidth, (float)GlControl.ActualHeight);

        if (hit == null)
        {
            HideHoverTooltip();
            return;
        }

        if (!ReferenceEquals(hit, _currentHoverMesh))
        {
            _currentHoverMesh = hit;
            _currentHoverMeasurementLabel = null; // forces measurement-content rebuild next time
            _hoverTooltip.Content = BuildHoverTooltipContent(hit);
        }

        if (!_hoverTooltip.IsOpen)
            _hoverTooltip.IsOpen = true;
    }

    /// <summary>Stops the dwell timer, clears the tracked hover target, and closes the tooltip.</summary>
    private void HideHoverTooltip()
    {
        _hoverTimer.Stop();
        _currentHoverMesh = null;
        _currentHoverMeasurementLabel = null;
        if (_hoverTooltip.IsOpen)
            _hoverTooltip.IsOpen = false;
    }

    /// <summary>Minimal tooltip content for a measurement-line hover — just the label,
    /// bold, in the same monospace font the mesh tooltip uses for visual consistency.
    /// Single-line; multi-row layout isn't needed for a short identifier and would just
    /// add visual weight when several labels flash by during a sweep.</summary>
    private static TextBlock BuildMeasurementHoverTooltipContent(string label) => new()
    {
        Text = label,
        FontFamily = new FontFamily("Consolas, Courier New, monospace"),
        FontSize = 11,
        FontWeight = FontWeights.Bold,
    };

    /// <summary>Builds the mesh hover tooltip: a shape/body-part header plus the resolved mesh and per-slot texture asset sources.</summary>
    private static TextBlock BuildHoverTooltipContent(GlMesh mesh)
    {
        var tb = new TextBlock
        {
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 11,
        };

        // Header: shape + body part
        string header = string.IsNullOrEmpty(mesh.BodyPart)
            ? mesh.ShapeName
            : mesh.ShapeName + "  (" + mesh.BodyPart + ")";
        tb.Inlines.Add(new System.Windows.Documents.Bold(
            new System.Windows.Documents.Run(header)));
        tb.Inlines.Add(new System.Windows.Documents.LineBreak());

        // Mesh source
        tb.Inlines.Add(new System.Windows.Documents.LineBreak());
        tb.Inlines.Add(new System.Windows.Documents.Bold(
            new System.Windows.Documents.Run("Mesh:")));
        tb.Inlines.Add(new System.Windows.Documents.LineBreak());
        AppendAssetSource(tb, mesh.MeshSource);

        // Textures
        if (mesh.TextureSources.Count > 0)
        {
            tb.Inlines.Add(new System.Windows.Documents.LineBreak());
            tb.Inlines.Add(new System.Windows.Documents.LineBreak());
            tb.Inlines.Add(new System.Windows.Documents.Bold(
                new System.Windows.Documents.Run("Textures:")));
            foreach (var (slotLabel, source) in mesh.TextureSources)
            {
                tb.Inlines.Add(new System.Windows.Documents.LineBreak());
                tb.Inlines.Add(new System.Windows.Documents.Bold(
                    new System.Windows.Documents.Run(slotLabel + ":")));
                tb.Inlines.Add(new System.Windows.Documents.LineBreak());
                AppendAssetSource(tb, source);
            }
        }

        return tb;
    }

    /// <summary>Appends an asset source's game path and origin (loose file / BSA / not found) to a tooltip text block.</summary>
    private static void AppendAssetSource(TextBlock tb, AssetSource? source)
    {
        if (source == null)
        {
            tb.Inlines.Add(new System.Windows.Documents.Run("  (unknown)"));
            return;
        }

        tb.Inlines.Add(new System.Windows.Documents.Run("  " + source.GamePath));

        switch (source.Kind)
        {
            case AssetOriginKind.Loose:
                tb.Inlines.Add(new System.Windows.Documents.LineBreak());
                tb.Inlines.Add(new System.Windows.Documents.Run(
                    "    └─ loose file: " + (source.LoosePath ?? "")));
                break;
            case AssetOriginKind.Bsa:
                tb.Inlines.Add(new System.Windows.Documents.LineBreak());
                tb.Inlines.Add(new System.Windows.Documents.Run(
                    "    └─ BSA: " + (source.BsaPath ?? "(unknown)")));
                break;
            case AssetOriginKind.NotFound:
                tb.Inlines.Add(new System.Windows.Documents.LineBreak());
                tb.Inlines.Add(new System.Windows.Documents.Run("    └─ (not found)"));
                break;
        }
    }

    /// <summary>Shows/hides the lighting settings panel and updates the toggle button caption.</summary>
    private void LightSettingsToggleButton_Click(object sender, RoutedEventArgs e)
    {
        bool show = LightingPanel.Visibility != Visibility.Visible;
        LightingPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LightSettingsToggleButton.Content = show ? "Hide Light Settings" : "Show Light Settings";
    }

    /// <summary>Shows/hides the render settings panel and updates the toggle button caption.</summary>
    private void RenderSettingsToggleButton_Click(object sender, RoutedEventArgs e)
    {
        bool show = RenderPanel.Visibility != Visibility.Visible;
        RenderPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        RenderSettingsToggleButton.Content = show ? "Hide Render Settings" : "Show Render Settings";
    }

    /// <summary>Opens a message window summarizing the 3D viewer's mouse and toolbar controls.</summary>
    private void ControlsButton_Click(object sender, RoutedEventArgs e)
    {
        var msg = new VM_MessageWindowOK(
            "3D Viewer Controls",
            "Left Mouse Drag:      Orbit camera around the model\n" +
            "Middle Mouse Drag:   Pan camera up/down/left/right\n" +
            "Scroll Wheel:             Zoom in and out\n" +
            "Reset View:                Return camera to default position\n\n" +
            "Toolbar:\n" +
            "  Background:   Change viewport background color\n" +
            "  Ambient:         Ambient light intensity\n" +
            "  Key Light:       Main directional light intensity\n" +
            "  Azimuth:         Horizontal angle of key light\n" +
            "  Elevation:       Vertical angle of key light");
        msg.Show();
    }
}
