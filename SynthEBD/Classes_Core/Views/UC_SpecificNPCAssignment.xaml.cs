using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Interaction logic for UC_SpecificNPCAssignment.xaml
/// </summary>
public partial class UC_SpecificNPCAssignment : UserControl
{
    private VM_SpecificNPCAssignmentsUI? _parentVM;

    public UC_SpecificNPCAssignment()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Find the parent VM for preview settings
        var parentUC = this.FindAncestor<UC_SpecificNPCAssignmentsUI>();
        _parentVM = parentUC?.DataContext as VM_SpecificNPCAssignmentsUI;

        if (_parentVM != null)
        {
            // Initialize column width from persisted setting
            var width = _parentVM.PreviewerWidth;
            if (width > 0)
            {
                PreviewerColumn.Width = new GridLength(width);
            }

            // Apply initial visibility
            ApplyPreviewVisibility(_parentVM.Show3DPreview);

            // Subscribe to Show3DPreview changes
            _parentVM.PropertyChanged += OnParentVMPropertyChanged;
        }

        // Listen for splitter drag to persist width
        PreviewerColumn.NotifyDragDelta(this, OnSplitterDragCompleted);
    }

    private void OnParentVMPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM_SpecificNPCAssignmentsUI.Show3DPreview) && _parentVM != null)
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

    //https://stackoverflow.com/questions/4085471/allow-only-numeric-entry-in-wpf-text-box
    private void NumericOnly(System.Object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        var senderTextBox = (System.Windows.Controls.TextBox)sender;
        e.Handled = !IsNumeric.IsTextNumeric(senderTextBox, e.Text);
    }
}

internal static class VisualTreeHelpers
{
    public static T? FindAncestor<T>(this DependencyObject child) where T : DependencyObject
    {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T typed)
                return typed;
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    /// <summary>
    /// Monitors GridSplitter drag on a ColumnDefinition by listening for SizeChanged.
    /// Calls the callback when the width stabilizes after a splitter drag.
    /// </summary>
    public static void NotifyDragDelta(this ColumnDefinition column, UIElement scope, Action callback)
    {
        // Listen for GridSplitter DragCompleted events bubbling up
        scope.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
            new System.Windows.Controls.Primitives.DragCompletedEventHandler((s, e) => callback()));
    }
}
