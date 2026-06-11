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
}