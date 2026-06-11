using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Noggog.WPF;

namespace SynthEBD;

/// <summary>
/// Attached behavior for tree-node elements that defers selection from the preview mouse-down to the
/// mouse-up, enabling click-and-drag from other nodes. Setting
/// <c>TreeNodeDeferredSelectBehavior.IsEnabled="True"</c> swallows
/// <see cref="UIElement.PreviewMouseLeftButtonDown"/> and, on <see cref="UIElement.MouseLeftButtonUp"/>,
/// focuses the ancestor <see cref="TreeViewItem"/> so the deferred selection lands on the clicked node.
/// Replaces the per-view, byte-identical HandleSelectPreviewMouseDown/HandleSelectPreviewMouseUp code-behind
/// handlers (R6).
/// </summary>
public static class TreeNodeDeferredSelectBehavior
{
    /// <summary>Attached property; set to <c>true</c> on a tree-node element to defer its selection to mouse-up.</summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(TreeNodeDeferredSelectBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Gets the <see cref="IsEnabledProperty"/> value.</summary>
    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    /// <summary>Sets the <see cref="IsEnabledProperty"/> value.</summary>
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            element.MouseLeftButtonUp += OnMouseLeftButtonUp;
        }
        else
        {
            element.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            element.MouseLeftButtonUp -= OnMouseLeftButtonUp;
        }
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Intercept the down click so the treeview node isn't changed until the subsequent up click.
        // This enables click & drag from other nodes.
        e.Handled = true;
    }

    private static void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var dep = sender as DependencyObject;
        if (dep.TryGetAncestor<TreeViewItem>(out var treeViewItem))
        {
            treeViewItem.Focus();
            e.Handled = true;
        }
    }
}
