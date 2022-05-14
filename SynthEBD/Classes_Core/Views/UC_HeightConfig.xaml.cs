using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Interaction logic for UC_HeightConfig.xaml
/// </summary>
public partial class UC_HeightConfig : UserControl
{
    public UC_HeightConfig()
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