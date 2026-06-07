using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using ReactiveUI;
using System.IO;

namespace SynthEBD;

/// <summary>
/// View model for the Mod Manager Integration settings tab, backing the
/// <see cref="Settings_ModManager"/> model. Selects the active <see cref="ModManager"/>
/// (None / MO2 / Vortex), exposes the matching sub-VM, and tracks the temp-extraction
/// folder and effective file-path length limit used during config installation.
/// </summary>
public class VM_SettingsModManager : VM
{
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    /// <summary>Autofac factory delegate for constructing a <see cref="VM_SettingsModManager"/>.</summary>
    public delegate VM_SettingsModManager Factory();
    /// <summary>
    /// Wires the temp-folder picker command and subscriptions that refresh the displayed
    /// sub-VM, path limit, and patcher settings when the mod-manager type changes, and that
    /// warn when the temp-folder path is excessively deep (&gt;100 chars).
    /// </summary>
    public VM_SettingsModManager(PatcherState patcherState, Logger logger)
    {
        _patcherState = patcherState;
        _logger = logger;

        SelectTempFolder = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFolder(TempFolder, out var tmpFolder))
                {
                    TempFolder = tmpFolder;
                }
            }
        );

        this.WhenAnyValue(x => x.ModManagerType).Subscribe(x =>
        {
            UpdateDisplayedVM();
            UpdateFilePathLimit();
            UpdatePatcherSettings();
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.TempFolder).Subscribe(folder =>
        {
            if (folder.Length > 100)
            {
                MessageWindow.DisplayNotificationOK("Warning", "Your SynthEBD Temp Folder is currently buried very deep: the folder path is " + folder.Length + " characters long. This can cause issues during config file installation. It is recommended that you change the Temp Folder in the Mod Manager Integration menu to a less deep folder.");
            }
        }).DisposeWith(this);
    }

    public ModManager ModManagerType { get; set; } = ModManager.None;
    public VM_MO2Integration MO2IntegrationVM { get; set; } = new();
    public VM_VortexIntergation VortexIntegrationVM { get; set; } = new();

    public object DisplayedSubVM { get; set; } = null;

    public string TempFolder { get; set; } = "";
    public int FilePathLimit { get; set; } = 260;
    public int FilePathLimit_NoModManager { get; set; } = 260;
    public RelayCommand SelectTempFolder { get; set; }

    /// <summary>Selects <see cref="DisplayedSubVM"/> (null / MO2 / Vortex) to match <see cref="ModManagerType"/>.</summary>
    public void UpdateDisplayedVM()
    {
        switch(ModManagerType)
        {
            case ModManager.None: DisplayedSubVM = null; break;
            case ModManager.ModOrganizer2: DisplayedSubVM = MO2IntegrationVM; break;
            case ModManager.Vortex: DisplayedSubVM = VortexIntegrationVM; break;
        }
    }

    /// <summary>Dumps the current VM state into <see cref="PatcherState.ModManagerSettings"/> to keep runtime state synced.</summary>
    public void UpdatePatcherSettings()
    {
        _patcherState.ModManagerSettings = DumpViewModelToModel();
    }

    /// <summary>Sets <see cref="FilePathLimit"/> from the active mod manager's configured limit.</summary>
    private void UpdateFilePathLimit()
    {
        switch (ModManagerType)
        {
            case ModManager.None: FilePathLimit = FilePathLimit_NoModManager; break;
            case ModManager.ModOrganizer2: FilePathLimit = MO2IntegrationVM.FilePathLimit; break;
            case ModManager.Vortex: FilePathLimit = VortexIntegrationVM.FilePathLimit; break;
        }
    }

    /// <summary>Model → VM: loads sub-VMs, temp folder, manager type, and path limit, and resolves the current install folder.</summary>
    public void CopyInViewModelFromModel(Settings_ModManager model)
    {
        if (model == null)
        {
            return;
        }
        _logger.LogStartupEventStart("Loading Mod Manager Settings UI");
        MO2IntegrationVM.GetViewModelFromModel(model.MO2Settings);
        VortexIntegrationVM.GetViewModelFromModel(model.VortexSettings);
        TempFolder = model.TempExtractionFolder;
        ModManagerType = model.ModManagerType;
        switch(model.ModManagerType)
        {
            case ModManager.None: model.CurrentInstallationFolder = model.DefaultInstallationFolder; break;
            case ModManager.ModOrganizer2: model.CurrentInstallationFolder = model.MO2Settings.ModFolderPath; break;
            case ModManager.Vortex: model.CurrentInstallationFolder = model.VortexSettings.StagingFolderPath; break;
        }
        FilePathLimit_NoModManager = model.FilePathLimit;
        UpdateFilePathLimit();
        _logger.LogStartupEventEnd("Loading Mod Manager Settings UI");
    }

    /// <summary>VM → Model: writes manager type, sub-VM settings, temp folder, install folder, and path limit to a new model.</summary>
    public Settings_ModManager DumpViewModelToModel()
    {
        Settings_ModManager model = new();
        model.ModManagerType = ModManagerType;
        model.MO2Settings = MO2IntegrationVM.DumpViewModelToModel();
        model.VortexSettings = VortexIntegrationVM.DumpViewModelToModel();
        model.TempExtractionFolder = TempFolder;

        switch (model.ModManagerType)
        {
            case ModManager.None: model.CurrentInstallationFolder = model.DefaultInstallationFolder; break;
            case ModManager.ModOrganizer2: model.CurrentInstallationFolder = model.MO2Settings.ModFolderPath; break;
            case ModManager.Vortex: model.CurrentInstallationFolder = model.VortexSettings.StagingFolderPath; break;
        }

        model.FilePathLimit = FilePathLimit_NoModManager;
        return model;
    }
}

/// <summary>
/// Sub-VM for Mod Organizer 2 integration, backing <see cref="Settings_ModManager.MO2"/>.
/// Holds the mod folder, MO2 executable path, and path limit, and auto-derives the mod
/// folder from ModOrganizer.ini when the executable is chosen.
/// </summary>
public class VM_MO2Integration : VM
{
    /// <summary>Wires the folder/executable picker commands and refreshes the mod folder when the executable path changes.</summary>
    public VM_MO2Integration()
    {
        FindModFolder = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFolder("", out var modFolder))
                {
                    ModFolderPath = modFolder;
                }
            }
        );

        FindExecutable = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFile("", "Executable files (*.exe)|*.exe", "Select the MO2 executable", out var execPath))
                {
                    ExecutablePath = execPath;
                }
            }
        );

        this.WhenAnyValue(x => x.ExecutablePath).Subscribe(x =>
        {
            UpdateModFolderPath();
        }).DisposeWith(this);
    }
    public string ModFolderPath { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public int FilePathLimit { get; set; } = 220;
    public RelayCommand FindModFolder { get; set; }
    public RelayCommand FindExecutable { get; set; }

    /// <summary>
    /// Derives <see cref="ModFolderPath"/> by parsing the <c>mod_directory</c> entry of the
    /// ModOrganizer.ini next to the executable; falls back to the default "mods" subfolder.
    /// No-op if the mod folder is already set and exists, or the executable path is invalid.
    /// </summary>
    public void UpdateModFolderPath()
    {
        if (!ModFolderPath.IsNullOrEmpty() && Directory.Exists(ModFolderPath))
        {
            return;
        }
        if (ExecutablePath.IsNullOrEmpty() || !File.Exists(ExecutablePath))
        {
            return;
        }
        string mo2Dir = Path.GetDirectoryName(ExecutablePath);
        string mo2iniPath = Path.Combine(mo2Dir, "ModOrganizer.ini");
        if (!File.Exists(mo2iniPath))
        {
            SetDefaultModDirPath();
            return;
        }
        var iniLines = IO_Aux.ReadFileToList(mo2iniPath, out bool success);
        if (!success)
        {
            SetDefaultModDirPath();
            return;
        }
        string dirLine = iniLines.Where(x => x.StartsWith("mod_directory", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        if (dirLine == null || dirLine.IsNullOrEmpty())
        {
            SetDefaultModDirPath();
            return;
        }
        int eqPos = dirLine.IndexOf('=');
        if (eqPos > -1)
        {
            var dirPath = dirLine.Substring(eqPos + 1);
            if (Directory.Exists(dirPath))
            {
                ModFolderPath = Path.GetFullPath(dirPath); // convert // in ini file to \\
            }
            else
            {
                SetDefaultModDirPath();
            }
        }
    }

    /// <summary>Sets <see cref="ModFolderPath"/> to the "mods" folder beside the executable when it exists.</summary>
    public void SetDefaultModDirPath()
    {
        string mo2Dir = Path.GetDirectoryName(ExecutablePath);
        string defaultPath = Path.Combine(mo2Dir, "mods");
        if (Directory.Exists(defaultPath))
        {
            ModFolderPath = defaultPath;
        }
    }

    /// <summary>Model → VM: loads the MO2 mod folder, executable path, and path limit.</summary>
    public void GetViewModelFromModel(Settings_ModManager.MO2 model)
    {
        ModFolderPath = model.ModFolderPath;
        ExecutablePath = model.ExecutablePath;
        FilePathLimit = model.FilePathLimit;
    }
    /// <summary>VM → Model: writes the MO2 mod folder, executable path, and path limit to a new model.</summary>
    public Settings_ModManager.MO2 DumpViewModelToModel()
    {
        Settings_ModManager.MO2 model = new();
        model.ModFolderPath = ModFolderPath;
        model.ExecutablePath = ExecutablePath;
        model.FilePathLimit = FilePathLimit;
        return model;
    }
}

/// <summary>
/// Sub-VM for Vortex integration, backing <see cref="Settings_ModManager.Vortex"/>.
/// Holds the staging folder path and path limit.
/// </summary>
public class VM_VortexIntergation : VM
{
    /// <summary>Wires the staging-folder picker command.</summary>
    public VM_VortexIntergation()
    {
        FindStagingFolder = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFolder("", out var stagingFolder))
                {
                    StagingFolderPath = stagingFolder;
                }
            }
        );
    }
    public string StagingFolderPath { get; set; } = "";
    public int FilePathLimit { get; set; } = 220;
    public RelayCommand FindStagingFolder { get; set; }

    /// <summary>Model → VM: loads the Vortex staging folder and path limit.</summary>
    public void GetViewModelFromModel(Settings_ModManager.Vortex model)
    {
        StagingFolderPath = model.StagingFolderPath;
        FilePathLimit = model.FilePathLimit;
    }
    /// <summary>VM → Model: writes the Vortex staging folder and path limit to a new model.</summary>
    public Settings_ModManager.Vortex DumpViewModelToModel()
    {
        Settings_ModManager.Vortex model = new();
        model.StagingFolderPath = StagingFolderPath;
        model.FilePathLimit = FilePathLimit;
        return model;
    }
}

/// <summary>
/// WPF value converter that shows the no-mod-manager file-path-limit control only when the
/// bound <see cref="ModManager"/> value is <see cref="ModManager.None"/>.
/// </summary>
public class PathLimitVisibilityConverter : System.Windows.Data.IValueConverter
{
    /// <summary>Returns Visible when the value is <see cref="ModManager.None"/>, otherwise Collapsed.</summary>
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool visibility = false;
        if (value is ModManager)
        {
            visibility = (ModManager)value == ModManager.None;
        }
        return visibility ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
    /// <summary>Maps Visibility back to a bool (true when Visible).</summary>
    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        System.Windows.Visibility visibility = (System.Windows.Visibility)value;
        return (visibility == System.Windows.Visibility.Visible);
    }
}