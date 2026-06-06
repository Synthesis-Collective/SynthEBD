using Noggog.WPF;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SynthEBD;

/// <summary>
/// Code-behind for the asset pack editor user control.
/// </summary>
public partial class UC_AssetPack : UserControl
{
    /// <summary>Initializes the view's XAML components.</summary>
    public UC_AssetPack()
    {
        InitializeComponent();
    }

    /// <summary>Swallows the tree node's preview mouse-down so selection isn't committed until
    /// the matching mouse-up, enabling click-and-drag from other nodes.</summary>
    private void HandleSelectPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // intercept the down click to make sure the treeview node doesn't get changed until the subsequent upclick. This enables click & drag from other nodes.
        return;
    }

    /// <summary>On preview mouse-up, focuses the ancestor TreeViewItem so the deferred
    /// selection (from <see cref="HandleSelectPreviewMouseDown"/>) lands on the clicked node.</summary>
    private void HandleSelectPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        var dep = sender as DependencyObject;
        if (dep.TryGetAncestor<TreeViewItem>(out var treeViewItem))
        {
            treeViewItem.Focus();
            e.Handled = true;
        }
    }
}