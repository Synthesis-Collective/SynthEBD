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
    /// Interaction logic for Window_ConfigPackager.xaml
    /// </summary>
    public partial class Window_ConfigPackager : Window
    {
        public Window_ConfigPackager()
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
}
