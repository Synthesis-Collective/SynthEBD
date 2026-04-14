using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using HelixToolkit.Wpf.SharpDX;

namespace SynthEBD;

public partial class UC_CharacterViewer : UserControl
{
    public UC_CharacterViewer()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private VM_CharacterViewer? _vm;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe from old VM
        if (_vm != null)
            _vm.MeshModels.CollectionChanged -= OnMeshModelsChanged;

        _vm = DataContext as VM_CharacterViewer;

        if (_vm != null)
        {
            _vm.MeshModels.CollectionChanged += OnMeshModelsChanged;
            // Add any models that are already loaded
            SyncAllModels();
        }
    }

    private void OnMeshModelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null)
                    foreach (MeshGeometryModel3D model in e.NewItems)
                        Viewport.Items.Add(model);
                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                    foreach (MeshGeometryModel3D model in e.OldItems)
                        Viewport.Items.Remove(model);
                break;

            case NotifyCollectionChangedAction.Reset:
                RemoveAllMeshModels();
                break;
        }
    }

    private void SyncAllModels()
    {
        RemoveAllMeshModels();
        if (_vm == null) return;

        foreach (var model in _vm.MeshModels)
            Viewport.Items.Add(model);
    }

    private void RemoveAllMeshModels()
    {
        // Remove only MeshGeometryModel3D items (preserve lights)
        for (int i = Viewport.Items.Count - 1; i >= 0; i--)
        {
            if (Viewport.Items[i] is MeshGeometryModel3D)
                Viewport.Items.RemoveAt(i);
        }
    }

    private static readonly System.Windows.Media.Color[] BgColors =
    {
        System.Windows.Media.Color.FromRgb(105, 105, 105), // Dim Gray
        System.Windows.Media.Color.FromRgb(51, 51, 51),    // Dark Gray
        System.Windows.Media.Color.FromRgb(0, 0, 0),       // Black
        System.Windows.Media.Color.FromRgb(255, 255, 255), // White
        System.Windows.Media.Color.FromRgb(74, 106, 138),  // Steel Blue
        System.Windows.Media.Color.FromRgb(45, 90, 39),    // Forest
    };

    private void BgColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Viewport != null && BgColorCombo.SelectedIndex >= 0 && BgColorCombo.SelectedIndex < BgColors.Length)
        {
            Viewport.BackgroundColor = BgColors[BgColorCombo.SelectedIndex];
        }
    }

    private void ResetViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (Viewport?.Camera is HelixToolkit.Wpf.SharpDX.PerspectiveCamera cam)
        {
            cam.Position = new System.Windows.Media.Media3D.Point3D(0, 100, -300);
            cam.LookDirection = new System.Windows.Media.Media3D.Vector3D(0, -0.2, 1);
            cam.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
            cam.FieldOfView = 45;
        }
    }

    private void BenchmarkButton_Click(object sender, RoutedEventArgs e)
    {
        (_vm ?? DataContext as VM_CharacterViewer)?.BenchmarkTextureStrategies();
    }

    private void LogLightingButton_Click(object sender, RoutedEventArgs e)
    {
        (_vm ?? DataContext as VM_CharacterViewer)?.LogLightingSettings();
    }
}
