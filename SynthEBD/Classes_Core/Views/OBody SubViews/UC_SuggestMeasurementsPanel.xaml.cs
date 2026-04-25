using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Code-behind for <see cref="VM_SuggestMeasurementsPanel"/>. Pure binding host -- no
/// imperative logic needed since the dynamic per-Category UI is driven by the VM's
/// <see cref="VM_SuggestMeasurementsPanel.Groups"/> collection through DataTemplates.
/// </summary>
public partial class UC_SuggestMeasurementsPanel : UserControl
{
    public UC_SuggestMeasurementsPanel()
    {
        InitializeComponent();
    }
}
