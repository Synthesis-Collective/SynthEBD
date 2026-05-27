using System.Windows;

namespace SynthEBD;

/// <summary>Interaction logic for Window_RuleDeleteExportPicker.xaml. The window itself is a
/// thin shell — its VM (<see cref="VM_RuleDeleteExportPicker"/>) drives every interactive
/// element. The window only needs to translate the VM's <c>RequestClose</c> event into a
/// <see cref="Window.DialogResult"/> + <see cref="Window.Close"/> pair so the caller's
/// <c>ShowDialog()</c> returns appropriately.</summary>
public partial class Window_RuleDeleteExportPicker : Window
{
    public Window_RuleDeleteExportPicker()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Detach from the previous VM (if any) so we don't double-fire on a context swap.
        if (e.OldValue is VM_RuleDeleteExportPicker oldVm)
        {
            oldVm.RequestClose -= HandleRequestClose;
        }
        if (e.NewValue is VM_RuleDeleteExportPicker newVm)
        {
            newVm.RequestClose += HandleRequestClose;
        }
    }

    private void HandleRequestClose(bool confirmed)
    {
        // Setting DialogResult on a non-modal window throws InvalidOperationException; the
        // calling site always uses ShowDialog() so this is safe. Wrapped in a try so a
        // misuse doesn't crash the editor — the Close() path still fires.
        try { DialogResult = confirmed; } catch { }
        Close();
    }
}
