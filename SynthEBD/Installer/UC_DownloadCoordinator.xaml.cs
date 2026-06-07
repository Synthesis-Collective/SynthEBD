using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace SynthEBD;

/// <summary>
/// Code-behind for the download-coordinator wizard page that guides the user through fetching required config downloads.
/// </summary>
public partial class UC_DownloadCoordinator : UserControl
{
    /// <summary>Initializes the view's XAML components.</summary>
    public UC_DownloadCoordinator()
    {
        InitializeComponent();
    }

    /// <summary>Opens the clicked hyperlink's target in the default handler via Explorer and marks the event handled.</summary>
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) //https://stackoverflow.com/questions/10238694/example-using-hyperlink-in-wpf
    {
        Hyperlink hl = (Hyperlink)sender;
        string navigateUri = hl.NavigateUri.ToString();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{navigateUri}\"",
            UseShellExecute = false
        });
        e.Handled = true;
    }
}