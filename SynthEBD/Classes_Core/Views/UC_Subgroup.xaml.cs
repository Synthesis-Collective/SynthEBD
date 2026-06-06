using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Code-behind for the asset pack subgroup user control.
/// </summary>
public partial class UC_Subgroup : UserControl
{
    /// <summary>Initializes the view's XAML components.</summary>
    public UC_Subgroup()
    {
        InitializeComponent();
    }
        

    /// <summary>Text-input filter that rejects non-numeric keystrokes on the source TextBox.</summary>
    //https://stackoverflow.com/questions/4085471/allow-only-numeric-entry-in-wpf-text-box
    private void NumericOnly(System.Object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        var senderTextBox = (System.Windows.Controls.TextBox)sender;
        e.Handled = !IsNumeric.IsTextNumeric(senderTextBox, e.Text);
    }

}