using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenTK.Wpf;

namespace SynthEBD;

public partial class UC_CharacterViewer : UserControl
{
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

        // Start GL when the control gets a valid size (handles deferred layout)
        GlControl.SizeChanged += (_, _) => TryStartGl();
        Loaded += (_, _) => { _vm ??= DataContext as VM_CharacterViewer; TryStartGl(); };
    }

    private VM_CharacterViewer? _vm;
    private bool _glStarted;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _vm = DataContext as VM_CharacterViewer;
        TryStartGl();
    }

    private void TryStartGl()
    {
        if (_glStarted) return;
        if (!IsLoaded) return;
        if (GlControl.ActualWidth <= 0 || GlControl.ActualHeight <= 0) return;

        var settings = new GLWpfControlSettings
        {
            MajorVersion = 3,
            MinorVersion = 3,
            RenderContinuously = true
        };
        GlControl.Start(settings);
        _glStarted = true;

        // GLWpfControl bug: Start() registers CompositionTarget.Rendering only
        // inside IsVisibleChanged, but if the control is already visible when
        // Start() is called, that event never fires. Force continuous rendering
        // by toggling visibility to trigger the handler.
        GlControl.Visibility = Visibility.Collapsed;
        GlControl.Visibility = Visibility.Visible;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  GL RENDER CALLBACK
    // ═══════════════════════════════════════════════════════════════════════

    private void GlControl_OnRender(TimeSpan delta)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;

        // Initialize GL on first render — context is guaranteed current here
        if (!_vm.IsGlInitialized)
        {
            string shaderDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Classes_Aux", "Rendering", "Shaders");
            _vm.InitializeGl(shaderDir);
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
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MOUSE → ORBIT CAMERA
    // ═══════════════════════════════════════════════════════════════════════

    private void GlControl_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;

        var pos = e.GetPosition(GlControl);
        _vm.Camera.OnMouseDown(
            (float)pos.X, (float)pos.Y,
            leftButton: e.ChangedButton == MouseButton.Left,
            middleButton: e.ChangedButton == MouseButton.Middle);
        GlControl.CaptureMouse();
    }

    private void GlControl_MouseMove(object sender, MouseEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;

        var pos = e.GetPosition(GlControl);
        _vm.Camera.OnMouseMove((float)pos.X, (float)pos.Y);
    }

    private void GlControl_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;

        _vm.Camera.OnMouseUp();
        GlControl.ReleaseMouseCapture();
    }

    private void GlControl_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;

        _vm.Camera.OnMouseWheel(e.Delta);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  RIGHT-CLICK → MESH PICKING & TEXTURE TOGGLE CONTEXT MENU
    // ═══════════════════════════════════════════════════════════════════════

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

    private void BgColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null || BgColorCombo.SelectedIndex < 0 || BgColorCombo.SelectedIndex >= BgColors.Length)
            return;

        var (r, g, b) = BgColors[BgColorCombo.SelectedIndex];
        _vm.Renderer.ClearColor = new OpenTK.Mathematics.Vector3(r, g, b);
    }

    private void ResetViewButton_Click(object sender, RoutedEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        _vm?.Camera.Reset();
    }

    private void LogLightingButton_Click(object sender, RoutedEventArgs e)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        _vm?.LogLightingSettings();
    }

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
