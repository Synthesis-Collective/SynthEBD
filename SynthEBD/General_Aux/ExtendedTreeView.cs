using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SynthEBD
{
    // https://stackoverflow.com/a/3535089
    /// <summary>
    /// A <see cref="TreeView"/> subclass that exposes the selected item via a bindable dependency
    /// property, working around <see cref="TreeView.SelectedItem"/> being read-only and thus not a
    /// valid binding target.
    /// </summary>
    public class ExtendedTreeView : TreeView
    {
        /// <summary>Creates the tree view and routes selection changes into <see cref="SelectedItem_"/>.</summary>
        public ExtendedTreeView()
            : base()
        {
            SelectedItemChanged += new RoutedPropertyChangedEventHandler<object>(___ICH);
        }

        /// <summary>Selection-changed handler that mirrors the current <see cref="TreeView.SelectedItem"/> into the bindable dependency property.</summary>
        void ___ICH(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (SelectedItem != null)
            {
                SetValue(SelectedItem_Property, SelectedItem);
            }
        }

        /// <summary>Gets or sets the selected item. Unlike <see cref="TreeView.SelectedItem"/>, this property can be a binding target.</summary>
        public object SelectedItem_
        {
            get { return (object)GetValue(SelectedItem_Property); }
            set { SetValue(SelectedItem_Property, value); }
        }
        /// <summary>Backing dependency property for <see cref="SelectedItem_"/>.</summary>
        public static readonly DependencyProperty SelectedItem_Property = DependencyProperty.Register("SelectedItem_", typeof(object), typeof(ExtendedTreeView), new UIPropertyMetadata(null));
    }
}
