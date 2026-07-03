using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Interaction logic for UC_SettingsDestandalone.xaml (the Destandalone patching page; state lives
/// on the shared <see cref="VM_SettingsTexMesh"/> exposed through <see cref="VM_SettingsDestandalone"/>).
/// </summary>
public partial class UC_SettingsDestandalone : UserControl
{
    public UC_SettingsDestandalone()
    {
        InitializeComponent();
    }
}
