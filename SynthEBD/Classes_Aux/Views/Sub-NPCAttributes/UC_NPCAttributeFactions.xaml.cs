using System.Windows.Controls;

namespace SynthEBD
{
    /// <summary>
    /// Interaction logic for UC_NPCAttributeFactions.xaml
    /// </summary>
    public partial class UC_NPCAttributeFactions : UserControl
    {
        public UC_NPCAttributeFactions()
        {
            InitializeComponent();
        }

        private void NumericOnly(System.Object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            var senderTextBox = (System.Windows.Controls.TextBox)sender;
            e.Handled = !IsNumeric.IsTextNumeric(senderTextBox, e.Text);
        }
    }
}
