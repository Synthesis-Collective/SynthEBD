using Noggog.WPF;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SynthEBD;

/// <summary>
/// Interaction logic for UC_AssetPackSettings.xaml
/// </summary>
public partial class UC_AssetPack : UserControl
{
    public UC_AssetPack()
    {
        InitializeComponent();
    }

    private void HandleSelectPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // intercept the down click to make sure the treeview node doesn't get changed until the subsequent upclick. This enables click & drag from other nodes.
        return;
    }

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