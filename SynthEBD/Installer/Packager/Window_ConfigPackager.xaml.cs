using Noggog.WPF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SynthEBD
{
    /// <summary>
    /// Code-behind for the config packager window.
    /// </summary>
    public partial class Window_ConfigPackager : Window
    {
        /// <summary>Initializes the view's XAML components.</summary>
        public Window_ConfigPackager()
        {
            InitializeComponent();
        }

        /// <summary>Intercepts the preview mouse-down on a tree node so selection is deferred to the mouse-up, enabling click-and-drag from other nodes.</summary>
        private void HandleSelectPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // intercept the down click to make sure the treeview node doesn't get changed until the subsequent upclick. This enables click & drag from other nodes.
            return;
        }

        /// <summary>Completes deferred tree-node selection on mouse-up by focusing the ancestor <see cref="TreeViewItem"/>.</summary>
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
}
