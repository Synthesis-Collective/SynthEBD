using System.Windows;

namespace SynthEBD.CLI;

/// <summary>
/// XAML-defined <see cref="Application"/> used only by the <c>ui-screenshot</c> verb. Its XAML
/// carries a copy of SynthEBD's App.xaml resource block, loaded through the same
/// InitializeComponent/LoadComponent mechanism the GUI uses — the only way MahApps' deferred
/// StaticResource references resolve (see the note in UiHarnessApp.xaml).
/// </summary>
public partial class UiHarnessApp : Application
{
    public UiHarnessApp()
    {
        InitializeComponent();
    }
}
