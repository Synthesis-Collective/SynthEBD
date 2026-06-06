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
    /// <summary>Calls InitializeComponent and hooks the window's Closed event to persist the histogram's bin-count settings to the process-static cache ("last to close wins").</summary>
    public Window_MeasurementHistogram()
    {
        InitializeComponent();

        // Push the current PersistBinCount + BinCount into the process-static cache when
        // the window closes, so the next histogram opens with the same settings. "Last to
        // close wins" — if multiple histograms are open, whichever closes last overwrites
        // whatever earlier closures left behind. Hooked via the Closed event (fires once
        // per window after the user dismisses it) rather than Closing (cancellable, and
        // we don't want to do the work twice).
        Closed += (_, _) =>
        {
            if (DataContext is VM_MeasurementHistogram vm) vm.CommitPersistedSettings();
        };
    }

    /// <summary>Forwards the chart area's actual height to <see cref="VM_MeasurementHistogram.RescaleBars"/> so the bars re-normalize when the layout settles or the window is resized.</summary>
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
