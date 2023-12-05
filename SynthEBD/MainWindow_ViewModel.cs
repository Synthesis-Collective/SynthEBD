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
    private readonly Logger _logger;

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
        Logger logger)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _viewModelLoader = viewModelLoader;
        _settingsGeneral = settingsGeneral;
        _navPanel = navPanel;
        _logger = logger;

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

    public void Init()
    {
        Application.Current.Exit += MainWindow_Closing;
        ValidateEval();
    }

    void MainWindow_Closing(object sender, ExitEventArgs e)
    {
        _viewModelLoader.SaveViewModelsToDrive();
    }

    void ValidateEval() // users should never see this but this will remind developer to update the Eval-Expression NuGet when the monthly trial expires. Unfortunately this function doesn't seeem to work - hence the static trigger that allows the Eval function in RecordPathParser to trigger the message box if necessary.
    {
        bool trueVar = false;
        List<dynamic> evalParameters = new List<dynamic>() { true, true };
        string evalExpression = "{0} == {1}";

        try
        {
            trueVar = Z.Expressions.Eval.Execute<bool>(evalExpression, evalParameters.ToArray());
        }
        catch (Exception ex)
        {
            //pass
        }

        if (!trueVar)
        {
            MessageWindow.DisplayNotificationOK("Eval-Expression License Expired", EvalExpiredMessage);
        }
    }

    public const string EvalExpiredMessage = "SynthEBD's asset distribution functionality depends on a month-to-month license of Eval-Expression.NET, and it appears this license has expired for the current build of SynthEBD. Please check the GitHub or Nexus page to see if an updated version has been released, and install the update if so. Otherwise, please contact Piranha91 or another member of the Synthesis Collective to refresh this license by updating the Eval-Expression NuGet package.";
}