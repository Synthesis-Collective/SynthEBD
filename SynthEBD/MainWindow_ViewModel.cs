using System.ComponentModel;
using System.Windows;
using Mutagen.Bethesda.Synthesis;
using ReactiveUI;

namespace SynthEBD;

public class MainWindow_ViewModel : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly ViewModelLoader _viewModelLoader;
    private readonly VM_Settings_General _settingsGeneral;
    private readonly VM_NavPanel _navPanel;
    public readonly SynthEBDPaths _paths; // must be accessible to App.xaml.cs for crash logging
    private readonly CustomMessageBox _customMessageBox;

    public DisplayedItemVm Display { get; }
    public VM_RunButton RunButtonVM { get; }
    public VM_NavPanel NavViewModel { get; }
    public VM_StatusBar StatusBarVM { get; }
    public static bool EvalMessageTriggered {get; set;} = false;
    public MainWindow_ViewModel(
        IEnvironmentStateProvider environmentProvider,
        PatcherState patcherState,
        ViewModelLoader viewModelLoader,
        VM_Settings_General settingsGeneral,
        DisplayedItemVm display,
        VM_StatusBar statusBar,
        VM_NavPanel navPanel,
        Func<VM_RunButton> getRunButton,
        SynthEBDPaths paths,
        CustomMessageBox customMessageBox)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _viewModelLoader = viewModelLoader;
        _settingsGeneral = settingsGeneral;
        _navPanel = navPanel;
        _customMessageBox = customMessageBox;
        Display = display;
        StatusBarVM = statusBar;
        if (_environmentProvider.RunMode == EnvironmentMode.Standalone)
        {
            RunButtonVM = getRunButton();
        }
        NavViewModel = _navPanel;
        _paths = paths;

        // Start on the settings VM
        Display.DisplayedViewModel = _settingsGeneral;
    }

    public void Init()
    {
        Application.Current.Exit += MainWindow_Closing;
    }

    void MainWindow_Closing(object sender, ExitEventArgs e)
    {
        _viewModelLoader.SaveViewModelsToDrive();
    }
}