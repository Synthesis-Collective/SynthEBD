using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Code-behind for the config distribution-rules editor user control. The only logic is
/// constraining numeric fields to numeric input.
/// </summary>
public partial class UC_ConfigDistributionRules : UserControl
{
    public UC_ConfigDistributionRules()
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