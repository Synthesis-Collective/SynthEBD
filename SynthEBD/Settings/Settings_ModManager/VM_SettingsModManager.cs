using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using ReactiveUI;
using System.IO;

namespace SynthEBD;

public class VM_SettingsModManager : VM
{
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    public delegate VM_SettingsModManager Factory();
    public VM_SettingsModManager(PatcherState patcherState, Logger logger)
    {
        _patcherState = patcherState;
        _logger = logger;

        SelectTempFolder = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFolder(TempFolder, out var tmpFolder))
                {
                    TempFolder = tmpFolder;
                }
            }
        );

        this.WhenAnyValue(x => x.ModManagerType).Subscribe(x => UpdateDisplayedVM()).DisposeWith(this);
        this.WhenAnyValue(x => x.ModManagerType).Subscribe(x =>
        {
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
    public RelayCommand SelectTempFolder { get; set; }

    public void UpdateDisplayedVM()
    {
        switch(ModManagerType)
        {
            case ModManager.None: DisplayedSubVM = null; break;
            case ModManager.ModOrganizer2: DisplayedSubVM = MO2IntegrationVM; break;
            case ModManager.Vortex: DisplayedSubVM = VortexIntegrationVM; break;
        }
    }

    public void UpdatePatcherSettings()
    {
        if (this != null)
        {
            _patcherState.ModManagerSettings = DumpViewModelToModel();
        }
    }

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
        FilePathLimit = model.FilePathLimit;
        _logger.LogStartupEventEnd("Loading Mod Manager Settings UI");
    }

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

        model.FilePathLimit = FilePathLimit;
        return model;
    }
}

public class VM_MO2Integration : VM
{
    public VM_MO2Integration()
    {
        FindModFolder = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFolder("", out var modFolder))
                {
                    ModFolderPath = modFolder;
                }
            }
        );

        FindExecutable = new SynthEBD.RelayCommand(
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

    public void SetDefaultModDirPath()
    {
        string mo2Dir = Path.GetDirectoryName(ExecutablePath);
        string defaultPath = Path.Combine(mo2Dir, "mods");
        if (Directory.Exists(defaultPath))
        {
            ModFolderPath = defaultPath;
        }
    }

    public void GetViewModelFromModel(Settings_ModManager.MO2 model)
    {
        ModFolderPath = model.ModFolderPath;
        ExecutablePath = model.ExecutablePath;
        FilePathLimit = model.FilePathLimit;
    }
    public Settings_ModManager.MO2 DumpViewModelToModel()
    {
        Settings_ModManager.MO2 model = new();
        model.ModFolderPath = ModFolderPath;
        model.ExecutablePath = ExecutablePath;
        model.FilePathLimit = FilePathLimit;
        return model;
    }
}

public class VM_VortexIntergation : VM
{
    public VM_VortexIntergation()
    {
        FindStagingFolder = new SynthEBD.RelayCommand(
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

    public void GetViewModelFromModel(Settings_ModManager.Vortex model)
    {
        StagingFolderPath = model.StagingFolderPath;
        FilePathLimit = model.FilePathLimit;
    }
    public Settings_ModManager.Vortex DumpViewModelToModel()
    {
        Settings_ModManager.Vortex model = new();
        model.StagingFolderPath = StagingFolderPath;
        model.FilePathLimit = FilePathLimit;
        return model;
    }
}

public class PathLimitVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool visibility = false;
        if (value is ModManager)
        {
            visibility = (ModManager)value == ModManager.None;
        }
        return visibility ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        System.Windows.Visibility visibility = (System.Windows.Visibility)value;
        return (visibility == System.Windows.Visibility.Visible);
    }
}