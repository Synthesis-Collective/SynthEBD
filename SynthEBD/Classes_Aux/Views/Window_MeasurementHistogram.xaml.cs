using System.Windows;

namespace SynthEBD;

/// <summary>
/// Interaction logic for Window_MeasurementHistogram.xaml.
/// <para>The only code-behind responsibility is forwarding the chart-area's actual
/// height to <see cref="VM_MeasurementHistogram.RescaleBars"/> whenever the layout
/// settles or the window is resized. Without this hook, bars stay locked at their
/// initial 280px regardless of how big the chart area grows — visible as wasted
/// vertical space after the user maximizes the window.</para>
/// </summary>
public partial class Window_MeasurementHistogram : Window
{
    public Window_MeasurementHistogram()
    {
        InitializeComponent();
    }

    private void ChartArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // ActualHeight is read off the ItemsControl (the sender) rather than the Border
        // around it because the Border adds 1px of BorderThickness + 2px of internal
        // margin that we don't want included in the bar normalization. e.NewSize already
        // excludes both, but reading ActualHeight off the sender stays robust if either
        // value gets tweaked later.
        if (sender is not FrameworkElement fe) return;
        if (DataContext is VM_MeasurementHistogram vm)
        {
            vm.RescaleBars(fe.ActualHeight);
        }
    }
}
