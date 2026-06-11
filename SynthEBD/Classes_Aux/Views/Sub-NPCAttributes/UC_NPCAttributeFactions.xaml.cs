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
}