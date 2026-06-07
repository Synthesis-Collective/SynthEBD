namespace SynthEBD;

/// <summary>
/// Root view model for the config-install wizard window (<see cref="Window_ConfigInstaller"/>), shown by
/// <see cref="ConfigInstaller.InstallConfigFile"/> after a manifest is parsed. Hosts the wizard pages
/// (option selection, dependency download coordination, destination-folder selection) and swaps them through
/// <see cref="DisplayedViewModel"/>, tracking whether the user completed or cancelled the flow.
/// </summary>
public class VM_ConfigInstaller : VM
{
    private readonly Func<VM_SettingsModManager> _modManagerVM;
    private readonly VM_DestinationFolderSelector.Factory _destinationFolderSelector;
    private readonly Window_ConfigInstaller _window;
    /// <summary>Autofac factory binding the per-install <paramref name="manifest"/>, host window, and temp extraction folder.</summary>
    public delegate VM_ConfigInstaller Factory(Manifest manifest, Window_ConfigInstaller window, string tempFolderPath);

    /// <summary>Wires the child wizard pages: builds the option <see cref="SelectorMenu"/> and the
    /// <see cref="DestinationFolderSelector"/>, and shows the selector first.</summary>
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

    /// <summary>Currently displayed wizard page VM; the view routes it to the matching DataTemplate.</summary>
    public object DisplayedViewModel { get; set; }
    public VM_ConfigSelector SelectorMenu { get; set; }
    /// <summary>Dependency-archive page; constructed by <see cref="VM_ConfigSelector"/> after the option chain is finalized.</summary>
    public VM_DownloadCoordinator DownloadMenu { get; set; } = null;
    public VM_DestinationFolderSelector DestinationFolderSelector { get; set; }
    /// <summary>Folder the archive was extracted into; consumed by the installer engine to locate source files.</summary>
    public string TempFolderPath { get; }
    public bool Cancelled { get; set; } = false;
    public bool Completed { get; set; } = false;
    public string InstallationMessage { get; set; } = string.Empty;

    /// <summary>Advances past the download page: closes the window if cancelled, already finalized, or no mod
    /// manager is configured; otherwise shows the destination-folder page.</summary>
    public void ConcludeInstallation()
    {
        if (Cancelled || DestinationFolderSelector.IsFinalized || _modManagerVM().ModManagerType == ModManager.None)
        {
            Close();   
        }
        else
        {
            DestinationFolderSelector.InitializeDisplay();
            DisplayedViewModel = DestinationFolderSelector;
        }
    }

    /// <summary>Closes the host wizard window.</summary>
    public void Close()
    {
        _window.Close();
        return;
    }
}