using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Interaction logic for UC_ConsistencyAssignment.xaml
/// </summary>
public partial class UC_ConsistencyAssignment : UserControl
{
    private VM_ConsistencyUI? _parentVM;

    public UC_ConsistencyAssignment()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

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

            _parentVM.PropertyChanged += OnParentVMPropertyChanged;
        }

        PreviewerColumn.NotifyDragDelta(this, OnSplitterDragCompleted);
    }

    private void OnParentVMPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM_ConsistencyUI.Show3DPreview) && _parentVM != null)
        {
            ApplyPreviewVisibility(_parentVM.Show3DPreview);
        }
    }

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

    private void OnSplitterDragCompleted()
    {
        if (_parentVM != null && PreviewerColumn.ActualWidth > 0)
        {
            _parentVM.PreviewerWidth = PreviewerColumn.ActualWidth;
        }
    }
}
