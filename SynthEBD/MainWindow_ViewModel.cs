using System.ComponentModel;
using System.Windows;

namespace SynthEBD;

public class MainWindow_ViewModel : VM
{
    private readonly SaveLoader _saveLoader;
    private readonly VM_Settings_General _settingsGeneral;
    private readonly VM_NavPanel _navPanel;

    public DisplayedItemVm Display { get; }
    public VM_RunButton RunButtonVM { get; }
    public object NavViewModel { get; }
    public VM_StatusBar StatusBarVM { get; }
    public static bool EvalMessageTriggered {get; set;} = false;
    public MainWindow_ViewModel(
        Logger logger,
        SaveLoader saveLoader,
        VM_Settings_General settingsGeneral,
        DisplayedItemVm display,
        VM_StatusBar statusBar,
        VM_NavPanel navPanel,
        VM_RunButton runButton,
        Paths paths,
        PatcherEnvironmentProvider patcherEnvironmentProvider)
    {
        _saveLoader = saveLoader;
        _settingsGeneral = settingsGeneral;
        _navPanel = navPanel;
        Logger.Instance = logger;
        Display = display;
        StatusBarVM = statusBar;
        RunButtonVM = runButton;
        NavViewModel = _navPanel;
        PatcherEnvironmentProvider.Instance = patcherEnvironmentProvider;
        PatcherSettings.Paths = paths;

        // Start on the settings VM
        Display.DisplayedViewModel = _settingsGeneral;
    }

    public void Init()
    {
        // Load settings
        _saveLoader.LoadInitialSettingsViewModels();
        _saveLoader.LoadPluginViewModels();
        _saveLoader.LoadFinalSettingsViewModels();

        Application.Current.MainWindow.Closing += new CancelEventHandler(MainWindow_Closing);

        ValidateEval();
    }

    void MainWindow_Closing(object sender, CancelEventArgs e)
    {
        _saveLoader.FinalClosing();
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
            DisplayEvalErrorMessage();
        }
    }

    public static void DisplayEvalErrorMessage()
    {
        Application.Current.Dispatcher.Invoke((Action)delegate { // apparently thread-safe to do it this way - https://stackoverflow.com/questions/2329978/the-calling-thread-must-be-sta-because-many-ui-components-require-this
            CustomMessageBox.DisplayNotificationOK("Eval-Expression License Expired", "SynthEBD's asset distribution functionality depends on a month-to-month license of Eval-Expression.NET, and it appears this license has expired for the current build of SynthEBD. Please contact Piranha91 or another member of the Synthesis Collective to refresh this license by updating the Eval-Expression NuGet package. In the meantime, BodyGen, BodySlide, and Height distribution remain fully functional.");
            EvalMessageTriggered = true;
        });
    }
}