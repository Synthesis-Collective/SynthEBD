using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Interaction logic for UC_ConfigDistributionRules.xaml
/// </summary>
public partial class UC_ConfigDistributionRules : UserControl
{
    public UC_ConfigDistributionRules()
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