using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Code-behind for <see cref="VM_PresetAnnotationEditor"/>. The DataTemplate inside the XAML
/// hosts the reused <see cref="UC_BodyShapeDescriptorSelectionMenu"/> via a ContentControl, so
/// no extra logic is needed here -- the partial class only exists to satisfy the InitializeComponent
/// contract.
/// </summary>
public partial class UC_PresetAnnotationEditor : UserControl
{
    public UC_PresetAnnotationEditor()
    {
        InitializeComponent();
    }
}
