using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Code-behind for the Factions NPC-attribute editor (the view for <see cref="VM_NPCAttributeFactions"/>).
/// The only logic is constraining the faction rank fields to numeric input.
/// </summary>
public partial class UC_NPCAttributeFactions : UserControl
{
    public UC_NPCAttributeFactions()
    {
        InitializeComponent();
    }

    /// <summary>Text-input handler that rejects non-numeric keystrokes in a <see cref="System.Windows.Controls.TextBox"/>.</summary>
    private void NumericOnly(System.Object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        var senderTextBox = (System.Windows.Controls.TextBox)sender;
        e.Handled = !IsNumeric.IsTextNumeric(senderTextBox, e.Text);
    }
}