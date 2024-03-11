namespace SynthEBD;

public class VM_ConfigInstaller : VM
{
    private readonly Func<VM_SettingsModManager> _modManagerVM;
    private readonly VM_DestinationFolderSelector.Factory _destinationFolderSelector;
    private readonly Window_ConfigInstaller _window;
    public delegate VM_ConfigInstaller Factory(Manifest manifest, Window_ConfigInstaller window, string tempFolderPath);

    public VM_ConfigInstaller(Manifest manifest, Window_ConfigInstaller window, string tempFolderPath, Func<VM_SettingsModManager> modManagerVM, VM_DestinationFolderSelector.Factory destinationFolderSelector)
    {
        _window = window;
        _modManagerVM = modManagerVM;
        _destinationFolderSelector = destinationFolderSelector;
        TempFolderPath = tempFolderPath;
        SelectorMenu = new VM_ConfigSelector(manifest, window, this);
        DestinationFolderSelector = _destinationFolderSelector(manifest, this);
        DisplayedViewModel = SelectorMenu;
    }

    public object DisplayedViewModel { get; set; }
    public VM_ConfigSelector SelectorMenu { get; set; }
    public VM_DownloadCoordinator DownloadMenu { get; set; } = null;
    public VM_DestinationFolderSelector DestinationFolderSelector { get; set; }
    public string TempFolderPath { get; }
    public bool Cancelled { get; set; } = false;
    public bool Completed { get; set; } = false;
    public string InstallationMessage { get; set; } = string.Empty;

    public void ConcludeInstallation()
    {
        if (Cancelled || DestinationFolderSelector.IsFinalized || _modManagerVM().ModManagerType == ModManager.None)
        {
            Close();   
        }
        else
        {
            DisplayedViewModel = DestinationFolderSelector;
        }
    }

    public void Close()
    {
        _window.Close();
        return;
    }
}