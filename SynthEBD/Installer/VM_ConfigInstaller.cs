namespace SynthEBD;

public class VM_ConfigInstaller : VM
{
    public VM_ConfigInstaller(Manifest manifest, Window_ConfigInstaller window)
    {
        SelectorMenu = new VM_ConfigSelector(manifest, window, this);
        DisplayedViewModel = SelectorMenu;
    }
    public Window_ConfigInstaller Window { get; set; }
    public object DisplayedViewModel { get; set; }
    public VM_ConfigSelector SelectorMenu { get; set; }
    public VM_DownloadCoordinator DownloadMenu { get; set; } = null;
    public bool Cancelled { get; set; } = false;
    public bool Completed { get; set; } = false;
    public string InstallationMessage { get; set; } = string.Empty;
}