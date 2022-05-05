using System.ComponentModel;

namespace SynthEBD;

public class VM_ConfigInstaller  : INotifyPropertyChanged
{
    public VM_ConfigInstaller(Manifest manifest, Window_ConfigInstaller window)
    {
        SelectorMenu = new VM_ConfigSelector(manifest, window, this);
        SelectorMenu.SelectedOption = SelectorMenu; // shows the top-level choices
        DownloadMenu = null;
        DisplayedViewModel = SelectorMenu;

        Cancelled = false;
        Completed = false;
    }
    public Window_ConfigInstaller Window { get; set; }
    public object DisplayedViewModel { get; set; }
    public VM_ConfigSelector SelectorMenu { get; set; }
    public VM_DownloadCoordinator DownloadMenu { get; set; }
    public bool Cancelled { get; set; }
    public bool Completed { get; set; }

    public event PropertyChangedEventHandler PropertyChanged;
}