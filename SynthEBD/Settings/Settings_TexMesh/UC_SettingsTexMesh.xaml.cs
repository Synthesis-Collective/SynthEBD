using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Code-behind for the texture/mesh settings page.
/// </summary>
public partial class UC_SettingsTexMesh : UserControl
{
    /// <summary>Initializes the view's XAML components.</summary>
    public UC_SettingsTexMesh()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Text-input handler that rejects keystrokes which would make the target
    /// text box's contents non-numeric, restricting entry to numeric values.
    /// </summary>
    //https://stackoverflow.com/questions/4085471/allow-only-numeric-entry-in-wpf-text-box
    private void NumericOnly(System.Object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        var senderTextBox = (System.Windows.Controls.TextBox)sender;
        e.Handled = !IsNumeric.IsTextNumeric(senderTextBox, e.Text);
    }
}