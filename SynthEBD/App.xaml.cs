using Autofac;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Synthesis.WPF;
using Noggog.WPF;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;

namespace SynthEBD;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private PatcherSettingsSourceProvider _settingsSourceProvider;
    private PatcherEnvironmentSourceProvider _environmentSourceProvider;
    private PatcherState _patcherState;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        SynthesisPipeline.Instance
            .SetOpenForSettings(OpenForSettings)
            .AddRunnabilityCheck(CanRunPatch)
            .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
            .SetTypicalOpen(StandaloneOpen)
            .SetForWpf()
            .Run(e.Args)
            .Wait();
    }

    public int StandaloneOpen()
    {
        var assembly = Assembly.GetEntryAssembly() ?? throw new ArgumentNullException();
        var rootPath = Path.GetDirectoryName(assembly.Location);

        var builder = new ContainerBuilder();
        builder.RegisterType<PatcherSettingsSourceProvider>().AsSelf().SingleInstance();
        builder.RegisterType<PatcherEnvironmentSourceProvider>().AsSelf().SingleInstance();

        builder.RegisterType<StandaloneRunEnvironmentStateProvider>().AsSelf().AsImplementedInterfaces().SingleInstance();
        builder.RegisterModule<MainModule>();

        var container = builder.Build();
        _settingsSourceProvider = container.Resolve<PatcherSettingsSourceProvider>(new NamedParameter("sourcePath", Path.Combine(rootPath, SynthEBDPaths.StandaloneSourceDirName, SynthEBDPaths.SettingsSourceFileName)));
        // TEST_settingsSourceProvider.SettingsRootPath = Path.Combine(rootPath, SynthEBDPaths.StandaloneSourceDirName);
        _environmentSourceProvider = container.Resolve<PatcherEnvironmentSourceProvider>(new NamedParameter("sourcePath", Path.Combine(rootPath, SynthEBDPaths.StandaloneSourceDirName, SynthEBDPaths.EnvironmentSourceDirName)));
        var state = container.Resolve<StandaloneRunEnvironmentStateProvider>(new NamedParameter("environmentSourceProvider", _environmentSourceProvider));

        //builder.RegisterInstance(state).AsImplementedInterfaces();

        var mainVM = container.Resolve<MainWindow_ViewModel>();
        mainVM.Init();
        var window = new MainWindow();
        window.DataContext = mainVM;
        window.Show();

        var navPanel = container.Resolve<VM_NavPanel>();
        navPanel.GoToMainMenu();

        var customMessages = container.Resolve<CustomMessageBox>();
        customMessages.AllowMessageDisplay();

        //special case - MaterialMessageBox causes main window to close if it's called before window.Show(), so have to call these functions now
        var updateHandler = container.Resolve<UpdateHandler>();
        var texMeshVM = container.Resolve<VM_SettingsTexMesh>();
        updateHandler.PostWindowShowFunctions(texMeshVM);

        return 0;
    }

    public int OpenForSettings(IOpenForSettingsState state)
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule<MainModule>();
        builder.RegisterType<PatcherSettingsSourceProvider>().AsSelf().SingleInstance();
        builder.RegisterType<PatcherEnvironmentSourceProvider>().AsSelf().SingleInstance();
        builder.RegisterInstance(new OpenForSettingsWrapper(state)).AsSelf().AsImplementedInterfaces().SingleInstance();
        var container = builder.Build();

        _settingsSourceProvider = container.Resolve<PatcherSettingsSourceProvider>(new NamedParameter("sourcePath", Path.Combine(state.ExtraSettingsDataPath, SynthEBDPaths.SettingsSourceFileName)));
        // TEST_settingsSourceProvider.SettingsRootPath = state.ExtraSettingsDataPath;
        _environmentSourceProvider = container.Resolve<PatcherEnvironmentSourceProvider>(new NamedParameter("sourcePath", Path.Combine(state.ExtraSettingsDataPath, SynthEBDPaths.StandaloneSourceDirName, SynthEBDPaths.EnvironmentSourceDirName))); // resolved only to satisfy SaveLoader; not needed for Synthesis runs

        var window = new MainWindow();
        var mainVM = container.Resolve<MainWindow_ViewModel>();
        window.DataContext = mainVM;
        mainVM.Init();
        window.Show();

        var customMessages = container.Resolve<CustomMessageBox>();
        customMessages.AllowMessageDisplay();

        //special case - MaterialMessageBox causes main window to close if it's called before window.Show(), so have to call these functions now
        var updateHandler = container.Resolve<UpdateHandler>();
        var texMeshVM = container.Resolve<VM_SettingsTexMesh>();
        updateHandler.PostWindowShowFunctions(texMeshVM);

        return 0;
    }

    private static void CanRunPatch(IRunnabilityState state)
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule<MainModule>();
        builder.RegisterType<PatcherSettingsSourceProvider>().AsSelf().SingleInstance();
        builder.RegisterType<PatcherEnvironmentSourceProvider>().AsSelf().SingleInstance();
        builder.RegisterInstance(new RunnabilitySettingsWrapper(state)).AsSelf().AsImplementedInterfaces().SingleInstance();
        var container = builder.Build();

        container.Resolve<PatcherSettingsSourceProvider>(new NamedParameter("sourcePath", Path.Combine(state.ExtraSettingsDataPath, SynthEBDPaths.SettingsSourceFileName)));
        container.Resolve<PatcherEnvironmentSourceProvider>(new NamedParameter("sourcePath", Path.Combine(state.ExtraSettingsDataPath, SynthEBDPaths.StandaloneSourceDirName, SynthEBDPaths.EnvironmentSourceDirName))); // resolved only to satisfy SaveLoader; not needed for Synthesis runs
        var saveLoader = container.Resolve<SaveLoader>();
        saveLoader.LoadAllSettings();

        var validation = container.Resolve<PreRunValidation>();
        if (!validation.ValidatePatcherState())
        {
            throw new Exception("SynthEBD Validation Failed. See logged messages above.");
        }
    }

    private async Task RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<PatcherSettingsSourceProvider>().AsSelf().SingleInstance();
        builder.RegisterType<PatcherEnvironmentSourceProvider>().AsSelf().SingleInstance();
        builder.RegisterInstance(new PatcherStateWrapper(state)).AsSelf().AsImplementedInterfaces().SingleInstance();
        builder.RegisterModule<MainModule>();
        var container = builder.Build();

        _settingsSourceProvider = container.Resolve<PatcherSettingsSourceProvider>(new NamedParameter("sourcePath", Path.Combine(state.ExtraSettingsDataPath, SynthEBDPaths.SettingsSourceFileName)));
        _environmentSourceProvider = container.Resolve<PatcherEnvironmentSourceProvider>(new NamedParameter("sourcePath", Path.Combine(state.ExtraSettingsDataPath, SynthEBDPaths.StandaloneSourceDirName, SynthEBDPaths.EnvironmentSourceDirName))); // resolved only to satisfy SaveLoader; not needed for Synthesis runs

        var saveLoader = container.Resolve<SaveLoader>();
        saveLoader.LoadAllSettings();

        var miscValidation = container.Resolve<MiscValidation>();
        _patcherState = container.Resolve<PatcherState>();

        // these are handled explicitly in RunPatch rather than CanRunPatch so users don't get unsolicited popups when Synthesis starts up and automatically performs runnability checks on its patchers
        if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodySlide && !miscValidation.VerifyBodySlideAnnotations(_patcherState.OBodySettings))
        {
            return;
        }
        else if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodyGen && !miscValidation.VerifyBodyGenAnnotations(_patcherState.AssetPacks, _patcherState.BodyGenConfigs))
        {
            return;
        }
        //

        var patcher = container.Resolve<Patcher>();
        await patcher.RunPatcher();
    }

    private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        StringBuilder sb = new();
        sb.AppendLine("SynthEBD has crashed with the following error:");
        sb.AppendLine(ExceptionLogger.GetExceptionStack(e.Exception));
        sb.AppendLine();
        sb.AppendLine("Patcher Settings Creation Log:");
        sb.AppendLine(PatcherSettingsSourceProvider.SettingsLog.ToString());
        sb.AppendLine();
        sb.AppendLine("Patcher Environment Creation Log:");
        sb.AppendLine(PatcherEnvironmentSourceProvider.SettingsLog.ToString());

        var errorMessage = sb.ToString();
        CustomMessageBox.DisplayNotificationOK("SynthEBD has crashed.", errorMessage);

        var path = Path.Combine(_settingsSourceProvider.SettingsRootPath, "Logs", "Crash Logs", DateTime.Now.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture) + ".txt");
        PatcherIO.WriteTextFileStatic(path, errorMessage).Wait();

        e.Handled = true;
    }

}