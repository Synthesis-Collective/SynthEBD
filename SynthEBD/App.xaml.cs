using Autofac;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Synthesis.WPF;
using Noggog.WPF;
using System.Text;
using System.Windows;

namespace SynthEBD;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        SynthesisPipeline.Instance
            .SetOpenForSettings(OpenForSettings)
            .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
            .SetTypicalOpen(StandaloneOpen)
            .SetForWpf()
            .Run(e.Args)
            .Wait();
    }

    public static int OpenForSettings(IOpenForSettingsState state)
    {
        /*
        var builder = new ContainerBuilder();
        builder.RegisterModule<MainModule>();
        var container = builder.Build();
        PatcherEnvironmentProvider.Instance = container.Resolve<PatcherEnvironmentProvider>();
        var mvm = container.Resolve<MainWindow_ViewModel>();
        this.DataContext = mvm;
        mvm.Init();
        */
        var builder = new ContainerBuilder();
        builder.RegisterModule<MainModule>();
        builder.RegisterInstance(new OpenForSettingsWrapper(state)).AsImplementedInterfaces();
        var container = builder.Build();

        var window = new MainWindow();
        var mainVM = container.Resolve<MainWindow_ViewModel>();
        window.DataContext = mainVM;

        //PatcherSettings.Paths.SetRootPath("");

        window.Show();
        window.CenterAround(state.RecommendedOpenLocation);

        return 0;
    }

    public static int StandaloneOpen()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule<MainModule>();
        builder.RegisterType<StandaloneRunStateProvider>().AsImplementedInterfaces();
        var container = builder.Build();

        var window = new MainWindow();
        window.DataContext = container.Resolve<MainWindow_ViewModel>();
        window.Show();

        return 0;
    }

    private async Task RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
    {
        /*
         * 
         * 
         */
    }
    
    /*
    void App_Startup(object sender, StartupEventArgs e)
    {
        // Application is running
        // Process command line args
        bool startMinimized = false;
        for (int i = 0; i != e.Args.Length; ++i)
        {
            if (e.Args[i] == "/StartMinimized")
            {
                startMinimized = true;
            }
        }

        // Create main application window, starting minimized if specified
        MainWindow mainWindow = new MainWindow();
        if (startMinimized)
        {
            mainWindow.WindowState = WindowState.Minimized;
        }
        mainWindow.Show();
    }
    */
    private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        StringBuilder sb = new();
        sb.AppendLine("SynthEBD has crashed with the following error:");
        sb.AppendLine(ExceptionLogger.GetExceptionStack(e.Exception, ""));
        sb.AppendLine();
        sb.AppendLine("Patcher Settings Creation Log:");
        sb.AppendLine(PatcherSettingsProvider.SettingsLog.ToString());
        sb.AppendLine();
        sb.AppendLine("Patcher Environment Creation Log:");
        sb.AppendLine(PatcherEnvironmentProvider.Instance.EnvironmentLog.ToString());

        var errorMessage = sb.ToString();
        CustomMessageBox.DisplayNotificationOK("SynthEBD has crashed.", errorMessage);

        var path = System.IO.Path.Combine(PatcherSettings.Paths.LogFolderPath, "Crash Logs", DateTime.Now.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture) + ".txt");
        PatcherIO.WriteTextFile(path, errorMessage).Wait();

        e.Handled = true;
    }

}