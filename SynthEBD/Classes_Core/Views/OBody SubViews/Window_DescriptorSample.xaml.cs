using System.Windows;
using System.Windows.Input;

namespace SynthEBD;

/// <summary>
/// Interaction logic for Window_DescriptorSample.xaml — the Rules tab's Show Sample window. Code-behind
/// disposes the view model on close (cancelling any renders still queued for it and unhooking it from
/// the editor) and routes Ctrl + wheel to the cell zoom.
/// </summary>
public partial class Window_DescriptorSample : Window
{
    public Window_DescriptorSample()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            if (DataContext is VM_DescriptorSample vm) vm.Dispose();
        };
    }

    /// <summary>Ctrl + wheel zooms the cells instead of scrolling; a plain wheel scrolls as usual.</summary>
    private void OnCellsPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || DataContext is not VM_DescriptorSample vm) return;
        vm.ZoomCells(e.Delta);
        e.Handled = true;
    }
}
