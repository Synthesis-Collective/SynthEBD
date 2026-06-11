using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Code-behind for the body-shape descriptor rules editor user control. The only logic is
/// constraining the weight-range fields to numeric input.
/// </summary>
public partial class UC_BodyShapeDescriptorRules : UserControl
{
    public UC_BodyShapeDescriptorRules()
    {
        InitializeComponent();
    }
}