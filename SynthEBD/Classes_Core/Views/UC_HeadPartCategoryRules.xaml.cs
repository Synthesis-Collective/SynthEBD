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
    /// Interaction logic for UC_HeadPartCategoryRules.xaml
    /// </summary>
    public partial class UC_HeadPartCategoryRules : UserControl
    {
        public UC_HeadPartCategoryRules()
        {
            InitializeComponent();
            this.MaxHeight = (System.Windows.SystemParameters.PrimaryScreenHeight * 0.5);
        }
        private void NumericOnly(System.Object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            var senderTextBox = (System.Windows.Controls.TextBox)sender;
            e.Handled = !IsNumeric.IsTextNumeric(senderTextBox, e.Text);
        }
    }
}
