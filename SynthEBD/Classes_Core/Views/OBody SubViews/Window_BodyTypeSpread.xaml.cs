using System.Windows;

namespace SynthEBD;

/// <summary>
/// Interaction logic for Window_BodyTypeSpread.xaml — the Show Spread window opened from the
/// Body Type Profile editor's Rules and Match Presets tabs. The only code-behind job is
/// disposing the view model on close, which cancels any renders still queued for it.
/// </summary>
public partial class Window_BodyTypeSpread : Window
{
    public Window_BodyTypeSpread()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            if (DataContext is VM_BodyTypeSpread vm) vm.Dispose();
        };
    }
}
