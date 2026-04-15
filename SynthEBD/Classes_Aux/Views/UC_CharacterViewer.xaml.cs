using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenTK.Wpf;

namespace SynthEBD;

public partial class UC_CharacterViewer : UserControl
{
    public UC_CharacterViewer()
    {
        InitializeComponent();

        // GLWpfControl requires explicit Start() before it will fire Render events.
        // Use a safe framerate; the control only redraws when invalidated or on timer.
        Loaded += OnLoaded;
    }

    private VM_CharacterViewer? _vm;
    private bool _glStarted;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as VM_CharacterViewer;

        if (!_glStarted)
        {
            var settings = new GLWpfControlSettings
            {
                MajorVersion = 3,
                MinorVersion = 3,
                RenderContinuously = true
            };
            GlControl.Start(settings);
            _glStarted = true;
        }

        // Wire mouse events for orbit camera
        GlControl.MouseDown += GlControl_MouseDown;
        GlControl.MouseMove += GlControl_MouseMove;
        GlControl.MouseUp += GlControl_MouseUp;
        GlControl.MouseWheel += GlControl_MouseWheel;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  GL RENDER CALLBACK
    // ═══════════════════════════════════════════════════════════════════════

    private void GlControl_OnRender(TimeSpan delta)
    {
        _vm ??= DataContext as VM_CharacterViewer;
        if (_vm == null) return;

        // Lazy-init GL on first render (context is guaranteed ready here)
        if (!_vm.IsGlInitialized)
        {
            string shaderDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Classes_Aux", "Rendering", "Shaders");
            _vm.InitializeGl(shaderDir);
        }

        // Update background color from renderer
        int w = (int)GlControl.ActualWidth;
        int h = (int)GlControl.ActualHeight;
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
}
