using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Code-behind for the specific NPC assignment user control. Manages the collapsible 3D
/// previewer column (persisted width, Show3DPreview toggle, splitter-drag write-back) and
/// filters numeric text input.
/// </summary>
public partial class UC_SpecificNPCAssignment : UserControl
{
    private VM_SpecificNPCAssignmentsUI? _parentVM;

    /// <summary>Initializes the view's XAML components and defers preview-column setup to Loaded.</summary>
    public UC_SpecificNPCAssignment()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>On load, locates the parent <see cref="VM_SpecificNPCAssignmentsUI"/>, restores
    /// the previewer column width, applies its visibility, and wires up the splitter-drag and
    /// Show3DPreview change listeners.</summary>
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

    /// <summary>Re-applies preview-column visibility whenever the parent VM's Show3DPreview changes.</summary>
    private void OnParentVMPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM_SpecificNPCAssignmentsUI.Show3DPreview) && _parentVM != null)
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

    /// <summary>Text-input filter that rejects non-numeric keystrokes on the source TextBox.</summary>
    //https://stackoverflow.com/questions/4085471/allow-only-numeric-entry-in-wpf-text-box
    private void NumericOnly(System.Object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        var senderTextBox = (System.Windows.Controls.TextBox)sender;
        e.Handled = !IsNumeric.IsTextNumeric(senderTextBox, e.Text);
    }
}

/// <summary>Visual-tree extension helpers shared by the assignment views: ancestor lookup
/// and GridSplitter drag-completed notification.</summary>
internal static class VisualTreeHelpers
{
    /// <summary>Walks up the visual tree from <paramref name="child"/> to find the nearest
    /// ancestor of type <typeparamref name="T"/>, or null if none exists.</summary>
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
    /// Invokes <paramref name="callback"/> whenever a GridSplitter drag completes anywhere within
    /// <paramref name="scope"/>, by handling the bubbling <see cref="System.Windows.Controls.Primitives.Thumb.DragCompletedEvent"/>.
    /// (The <paramref name="column"/> parameter is currently unused — see review notes.)
    /// </summary>
    public static void NotifyDragDelta(this ColumnDefinition column, UIElement scope, Action callback)
    {
        // Listen for GridSplitter DragCompleted events bubbling up
        scope.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
            new System.Windows.Controls.Primitives.DragCompletedEventHandler((s, e) => callback()));
    }
}
