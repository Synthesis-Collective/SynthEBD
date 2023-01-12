using System.ComponentModel;
using System.Windows;
using ReactiveUI;

namespace SynthEBD;

public class MainWindow_ViewModel : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly SaveLoader _saveLoader;
    private readonly VM_Settings_General _settingsGeneral;
    private readonly VM_NavPanel _navPanel;
    private readonly Logger _logger;
    public readonly SynthEBDPaths _paths; // must be accessible to App.xaml.cs for crash logging
    private readonly CustomMessageBox _customMessageBox;

    public DisplayedItemVm Display { get; }
    public VM_RunButton RunButtonVM { get; }
    public VM_NavPanel NavViewModel { get; }
    public VM_StatusBar StatusBarVM { get; }
    public static bool EvalMessageTriggered {get; set;} = false;
    public MainWindow_ViewModel(
        IEnvironmentStateProvider environmentProvider,
        Logger logger,
        SaveLoader saveLoader,
        VM_Settings_General settingsGeneral,
        DisplayedItemVm display,
        VM_StatusBar statusBar,
        VM_NavPanel navPanel,
        Func<VM_RunButton> getRunButton,
        SynthEBDPaths paths,
        CustomMessageBox customMessageBox)
    {
        _environmentProvider = environmentProvider;
        _saveLoader = saveLoader;
        _settingsGeneral = settingsGeneral;
        _navPanel = navPanel;
        _logger = logger;
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
        // Load settings
        //_saveLoader.Reinitialize();
        _saveLoader.TrackRootFolder(); // respond to portable settings folder updates now that initial settings are loaded.

        Application.Current.Exit += MainWindow_Closing;

        ValidateEval();
    }

    void MainWindow_Closing(object sender, ExitEventArgs e)
    {
        _saveLoader.SaveEntireState();
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
            _customMessageBox.DisplayEvalErrorMessage();
        }
    }

    
}