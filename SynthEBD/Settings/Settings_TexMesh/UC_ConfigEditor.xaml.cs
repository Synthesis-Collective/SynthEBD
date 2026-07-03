using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Interaction logic for UC_ConfigEditor.xaml (the asset-pack Config Editor page; state lives on
/// the shared <see cref="VM_SettingsTexMesh"/> exposed through <see cref="VM_ConfigEditor"/>).
/// </summary>
public partial class UC_ConfigEditor : UserControl
{
    public UC_ConfigEditor()
    {
        InitializeComponent();
    }
}
