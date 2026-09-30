using System.Windows;

namespace SynthEBD;

/// <summary>
/// Interaction logic for Window_AnnotationPanel.xaml — the annotation queue's Panel, opened from the
/// queue's Open Panel button. The only code-behind job is disposing the view model on close, which
/// cancels any renders still queued for it and unhooks it from the queue and editor.
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
}
