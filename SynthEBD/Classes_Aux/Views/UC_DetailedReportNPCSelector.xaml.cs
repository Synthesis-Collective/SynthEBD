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
    /// Code-behind for the detailed-report NPC selector user control. The only logic is
    /// constraining numeric fields to numeric input.
    /// </summary>
    public partial class UC_DetailedReportNPCSelector : UserControl
    {
        public UC_DetailedReportNPCSelector()
        {
            InitializeComponent();
        }

        /// <summary>Text-input handler that rejects non-numeric keystrokes in a <see cref="System.Windows.Controls.TextBox"/>.</summary>
        //https://stackoverflow.com/questions/4085471/allow-only-numeric-entry-in-wpf-text-box
        private void NumericOnly(System.Object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            var senderTextBox = (System.Windows.Controls.TextBox)sender;
            e.Handled = !IsNumeric.IsTextNumeric(senderTextBox, e.Text);
        }
    }
}
