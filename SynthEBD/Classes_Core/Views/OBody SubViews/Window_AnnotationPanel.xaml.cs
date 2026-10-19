using System.Windows;
using System.Windows.Input;

namespace SynthEBD;

/// <summary>
/// Interaction logic for Window_AnnotationPanel.xaml — the annotation queue's Panel, opened from the
/// queue's Open Panel button. Code-behind disposes the view model on close (cancelling any renders
/// still queued for it and unhooking it from the queue and editor) and routes Ctrl + wheel to the
/// cell zoom.
/// </summary>
public partial class Window_AnnotationPanel : Window
{
    public Window_AnnotationPanel()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            if (DataContext is VM_AnnotationPanel vm) vm.Dispose();
        };
    }

    /// <summary>Ctrl + wheel zooms the cells instead of scrolling; a plain wheel scrolls as usual.</summary>
    private void OnCellsPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || DataContext is not VM_AnnotationPanel vm) return;
        vm.ZoomCells(e.Delta);
        e.Handled = true;
    }
}
