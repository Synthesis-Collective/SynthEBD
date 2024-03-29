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
    /// Interaction logic for UC_DetailedReportNPCSelector.xaml
    /// </summary>
    public partial class UC_DetailedReportNPCSelector : UserControl
    {
        public UC_DetailedReportNPCSelector()
        {
            InitializeComponent();
        }

        //https://stackoverflow.com/questions/4085471/allow-only-numeric-entry-in-wpf-text-box
        private void NumericOnly(System.Object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            var senderTextBox = (System.Windows.Controls.TextBox)sender;
            e.Handled = !IsNumeric.IsTextNumeric(senderTextBox, e.Text);
        }
    }
}
