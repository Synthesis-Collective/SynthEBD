using System.ComponentModel;
using System.Windows;
using Mutagen.Bethesda.Synthesis;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// Shell view model for the main window. Owns the displayed-content host
/// (<see cref="DisplayedItemVm"/>), nav panel, status bar, and (in standalone mode) the run
/// button, and starts the UI on the General Settings view. Wires application shutdown to save
/// the view models to disk.
/// </summary>
public class MainWindow_ViewModel : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly ViewModelLoader _viewModelLoader;
    private readonly VM_Settings_General _settingsGeneral;
    private readonly VM_NavPanel _navPanel;
    public readonly SynthEBDPaths _paths; // must be accessible to App.xaml.cs for crash logging
    private readonly Logger _logger;
    private readonly FaceGenPreviewService _faceGenPreviewService;

    public DisplayedItemVm Display { get; }
    public VM_RunButton RunButtonVM { get; }
    public VM_NavPanel NavViewModel { get; }
    public VM_StatusBar StatusBarVM { get; }
    /// <summary>Process-wide guard so the evaluation-mode message is shown at most once.</summary>
    public static bool EvalMessageTriggered {get; set;} = false;
    /// <summary>
    /// Wires up the child VMs; in standalone mode resolves the run button via its factory,
    /// otherwise sets the Synthesis startup log string. Starts on the General Settings VM.
    /// </summary>
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
        Logger logger,
        FaceGenPreviewService faceGenPreviewService)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _viewModelLoader = viewModelLoader;
        _settingsGeneral = settingsGeneral;
        _navPanel = navPanel;
        _logger = logger;
        _faceGenPreviewService = faceGenPreviewService;

        Display = display;
        StatusBarVM = statusBar;
        if (_environmentProvider.RunMode == EnvironmentMode.Standalone)
        {
            RunButtonVM = getRunButton();
        }
        else
        {
            _logger.SetSynthesisStartupString();
        }
        NavViewModel = _navPanel;
        _paths = paths;

        // Start on the settings VM
        Display.DisplayedViewModel = _settingsGeneral;
    }

    /// <summary>Registers the application Exit handler that persists state on shutdown.</summary>
    public void Init()
    {
        Application.Current.Exit += MainWindow_Closing;
    }

    /// <summary>
    /// Application-exit handler: disposes the FaceGen preview service (logging but swallowing
    /// any failure) and saves all view models to disk.
    /// </summary>
    void MainWindow_Closing(object sender, ExitEventArgs e)
    {
        try
        {
            _faceGenPreviewService.Dispose();
        }
        catch (System.Exception ex)
        {
            _logger.LogMessage("MainWindow_Closing: FaceGen preview cleanup failed: " + ex.Message);
        }

        _viewModelLoader.SaveViewModelsToDrive();
    }
}