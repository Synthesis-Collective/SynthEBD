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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SynthEBD
{
    /// <summary>
    /// Code-behind for the head part editor user control.
    /// </summary>
    public partial class UC_HeadPart : UserControl
    {
        /// <summary>Initializes the view's XAML components.</summary>
        public UC_HeadPart()
        {
            InitializeComponent();
        }
        /// <summary>Text-input filter that rejects non-numeric keystrokes on the source TextBox.</summary>
        private void NumericOnly(System.Object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            var senderTextBox = (System.Windows.Controls.TextBox)sender;
            e.Handled = !IsNumeric.IsTextNumeric(senderTextBox, e.Text);
        }
    }
}
