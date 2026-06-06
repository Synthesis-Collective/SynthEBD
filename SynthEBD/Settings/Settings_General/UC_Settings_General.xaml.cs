using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Code-behind for the general settings page.
/// </summary>
public partial class UC_Settings_General : UserControl
{
    private bool _isDragging;

    /// <summary>Initializes the view's XAML components.</summary>
    public UC_Settings_General()
    {
        InitializeComponent();
    }
}