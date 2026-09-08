using System.Windows;

namespace SynthEBD;

/// <summary>
/// Interaction logic for Window_BodySlideCompare.xaml — the two-pane BodySlide comparison
/// window opened from the Compare button on any OBody-menu CharacterViewer.
///
/// <para>The only code-behind responsibility is disposing the view model on close. That
/// matters more than usual here: each pane owns a live <see cref="VM_CharacterViewer"/> with
/// its own GL context, shaders, VAOs and decoded textures, and nothing else in the app holds
/// a reference to them once the window is gone. Leaving them to finalization would strand two
/// full scenes' worth of GPU memory for as long as the app runs.</para>
/// </summary>
public partial class Window_BodySlideCompare : Window
{
    /// <summary>Calls InitializeComponent and hooks Closed to dispose the view model (and with it both panes' GL scenes).</summary>
    public Window_BodySlideCompare()
    {
        InitializeComponent();

        // Closed, not Closing: Closing is cancellable, and tearing down the GL contexts of a
        // window that then stays open would leave two dead viewports behind.
        Closed += (_, _) =>
        {
            if (DataContext is VM_BodySlideCompare vm) vm.Dispose();
        };
    }
}
