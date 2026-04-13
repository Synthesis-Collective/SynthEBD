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
}
