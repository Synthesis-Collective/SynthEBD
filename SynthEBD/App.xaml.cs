using Autofac;
using K4os.Compression.LZ4.Internal;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Synthesis.WPF;
using Noggog.WPF;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using static System.Formats.Asn1.AsnWriter;
using static System.Windows.Forms.AxHost;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace SynthEBD;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private PatcherSettingsSourceProvider _settingsSourceProvider;
    private PatcherEnvironmentSourceProvider _environmentSourceProvider;

    private readonly string _standaloneSourceDirName = "Settings";
    private readonly string _settingsSourceName = "SettingsSource.json";
    private readonly string _environmentSourceName = "EnvironmentSource.json";

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


    public int OpenForSettings(IOpenForSettingsState state)
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule<MainModule>();

        builder.RegisterType<PatcherSettingsSourceProvider>();
        builder.RegisterInstance(new OpenForSettingsWrapper(state)).AsImplementedInterfaces().SingleInstance();
        var container = builder.Build();

        _settingsSourceProvider = container.Resolve<PatcherSettingsSourceProvider>(new NamedParameter("sourcePath", Path.Combine(state.ExtraSettingsDataPath, _settingsSourceName)));

        var window = new MainWindow();
        var mainVM = container.Resolve<MainWindow_ViewModel>();
        window.DataContext = mainVM;
        mainVM.Init();
        window.Show();
        window.CenterAround(state.RecommendedOpenLocation);

        //special case - MaterialMessageBox causes main window to close if it's called before window.Show(), so have to call this function now
        var updateHandler = container.Resolve<UpdateHandler>();
        var texMeshVM = container.Resolve<VM_SettingsTexMesh>();
        updateHandler.PostWindowShowFunctions(texMeshVM);

        return 0;
    }

    public int StandaloneOpen()
    {
        var assembly = Assembly.GetEntryAssembly() ?? throw new ArgumentNullException();
        var rootPath = Path.GetDirectoryName(assembly.Location);

        var builder = new ContainerBuilder();
        builder.RegisterType<PatcherSettingsSourceProvider>().AsSelf().SingleInstance();
        builder.RegisterType<PatcherEnvironmentSourceProvider>().AsSelf().SingleInstance();
        //builder.RegisterType<StandaloneRunStateProvider>().AsSelf().SingleInstance();
        builder.RegisterType<StandaloneRunStateProvider>().AsSelf().AsImplementedInterfaces().SingleInstance();
        builder.RegisterModule<MainModule>();

        var container = builder.Build();
        _settingsSourceProvider = container.Resolve<PatcherSettingsSourceProvider>(new NamedParameter("sourcePath", Path.Combine(rootPath, _standaloneSourceDirName, _settingsSourceName)));
        _settingsSourceProvider.SettingsRootPath = Path.Combine(rootPath, _standaloneSourceDirName);
        _environmentSourceProvider = container.Resolve<PatcherEnvironmentSourceProvider>(new NamedParameter("sourcePath", Path.Combine(rootPath, _standaloneSourceDirName, _environmentSourceName)));
        var state = container.Resolve<StandaloneRunStateProvider>(new NamedParameter("environmentSourceProvider", _environmentSourceProvider));

        //builder.RegisterInstance(state).AsImplementedInterfaces();

        var mainVM = container.Resolve<MainWindow_ViewModel>();
        mainVM.Init();
        var window = new MainWindow();
        window.DataContext = mainVM;
        window.Show();

        //special case - MaterialMessageBox causes main window to close if it's called before window.Show(), so have to call this function now
        var updateHandler = container.Resolve<UpdateHandler>();
        var texMeshVM = container.Resolve<VM_SettingsTexMesh>();
        updateHandler.PostWindowShowFunctions(texMeshVM);

        return 0;
    }
    
    private async Task RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
    {

    }

    
    private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        StringBuilder sb = new();
        sb.AppendLine("SynthEBD has crashed with the following error:");
        sb.AppendLine(ExceptionLogger.GetExceptionStack(e.Exception));
        sb.AppendLine();
        sb.AppendLine("Patcher Environment Creation Log:");
        sb.AppendLine(PatcherEnvironmentSourceProvider.SettingsLog.ToString());
        sb.AppendLine();
        sb.AppendLine("Patcher Settings Creation Log:");
        sb.AppendLine(PatcherSettingsSourceProvider.SettingsLog.ToString());

        var errorMessage = sb.ToString();
        CustomMessageBox.DisplayNotificationOK("SynthEBD has crashed.", errorMessage);

        var path = Path.Combine(_settingsSourceProvider.SettingsRootPath, "Logs", "Crash Logs", DateTime.Now.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture) + ".txt");
        PatcherIO.WriteTextFileStatic(path, errorMessage).Wait();

        e.Handled = true;
    }

}