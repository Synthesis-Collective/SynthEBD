using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SynthEBD;

/// <summary>
/// Code-behind for <see cref="VM_AnnotationQueue"/>'s view. Carries only focus management.
///
/// <para>The panel's shortcuts (digits to toggle a value, Enter to commit and advance, Backspace
/// to go back, S to skip) are declared as <c>InputBindings</c> on the UserControl rather than on an
/// ancestor, because they are unmodified keys: an ancestor binding on <c>D1</c> would swallow the
/// digit before a focused TextBox elsewhere in the tab ever saw it, breaking the weight-slot box
/// and the preset filter. Scoping them here fixes that but introduces the opposite problem -- a
/// UserControl only sees key input while focus is inside it, and clicking any button in the panel
/// moves focus to that button, while clicking a descriptor checkbox in the sibling annotation
/// editor moves it out of the panel entirely.</para>
///
/// <para>So every command button returns focus to the panel after it runs. That is what makes the
/// advertised workflow -- press a digit, press Enter, next body loads -- actually hold for more
/// than one slice.</para>
/// </summary>
public partial class UC_AnnotationQueue : UserControl
{
    public UC_AnnotationQueue()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Returns keyboard focus to the panel after a command button runs, so the keyboard shortcuts
    /// keep working without a click in between.
    /// <para>Deferred to <see cref="DispatcherPriority.Input"/>: WPF sets focus to the clicked
    /// button as part of the same input pass, so focusing synchronously here would be overwritten
    /// on the way back out.</para>
    /// </summary>
    private void OnCommandButtonClick(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new System.Action(() => Keyboard.Focus(this)));
    }
}
