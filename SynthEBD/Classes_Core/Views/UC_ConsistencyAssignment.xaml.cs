using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Code-behind for the consistency assignment user control. Manages the collapsible 3D
/// previewer column: restores its persisted width, toggles it with the parent VM's
/// Show3DPreview flag, and writes the width back on splitter drag.
/// </summary>
public partial class UC_ConsistencyAssignment : UserControl
{
    private VM_ConsistencyUI? _parentVM;

    /// <summary>Initializes the view's XAML components and defers preview-column setup to Loaded.</summary>
    public UC_ConsistencyAssignment()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>On load, locates the parent <see cref="VM_ConsistencyUI"/>, restores the
    /// previewer column width, applies its visibility, and wires up the splitter-drag and
    /// Show3DPreview change listeners.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var parentUC = this.FindAncestor<UC_ConsistencyUI>();
        _parentVM = parentUC?.DataContext as VM_ConsistencyUI;

        if (_parentVM != null)
        {
            var width = _parentVM.PreviewerWidth;
            if (width > 0)
            {
                PreviewerColumn.Width = new GridLength(width);
            }

            ApplyPreviewVisibility(_parentVM.Show3DPreview);

            // idempotent in case Loaded is raised more than once
            _parentVM.PropertyChanged -= OnParentVMPropertyChanged;
            _parentVM.PropertyChanged += OnParentVMPropertyChanged;
        }

        PreviewerColumn.NotifyDragDelta(this, OnSplitterDragCompleted);
    }

    /// <summary>Detaches the parent-VM PropertyChanged listener when this control leaves the visual tree, so
    /// the long-lived menu VM does not keep this control (and its 3D previewer) alive across navigation.</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_parentVM != null)
        {
            _parentVM.PropertyChanged -= OnParentVMPropertyChanged;
        }
    }

    /// <summary>Re-applies preview-column visibility whenever the parent VM's Show3DPreview changes.</summary>
    private void OnParentVMPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM_ConsistencyUI.Show3DPreview) && _parentVM != null)
        {
            ApplyPreviewVisibility(_parentVM.Show3DPreview);
        }
    }

    /// <summary>Shows the previewer column at its persisted (or default 525px) width, or
    /// collapses it to zero width when the preview is hidden.</summary>
    private void ApplyPreviewVisibility(bool show)
    {
        if (show)
        {
            var width = _parentVM?.PreviewerWidth ?? 525;
            if (width <= 0) width = 525;
            PreviewerColumn.Width = new GridLength(width);
        }
        else
        {
            PreviewerColumn.Width = new GridLength(0);
        }
    }

    /// <summary>Persists the current previewer column width back to the parent VM after a
    /// splitter drag completes.</summary>
    private void OnSplitterDragCompleted()
    {
        if (_parentVM != null && PreviewerColumn.ActualWidth > 0)
        {
            _parentVM.PreviewerWidth = PreviewerColumn.ActualWidth;
        }
    }
}
