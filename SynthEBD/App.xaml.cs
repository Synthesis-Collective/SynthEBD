using Autofac;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Synthesis.WPF;
using Noggog;
using Noggog.WPF;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using Mutagen.Bethesda;

namespace SynthEBD;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private PatcherSettingsSourceProvider _settingsSourceProvider;
    private PatcherEnvironmentSourceProvider _environmentSourceProvider;
    private IEnvironmentStateProvider _environmentStateProvider;
    private PatcherState _patcherState;
    private Logger _logger;

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
        var window = new MainWindow();

        var assembly = Assembly.GetEntryAssembly() ?? throw new ArgumentNullException();
        var rootPath = Path.GetDirectoryName(assembly.Location);

        var builder = new ContainerBuilder();
        builder.RegisterType<PatcherSettingsSourceProvider>().AsSelf().SingleInstance();
        builder.RegisterType<PatcherEnvironmentSourceProvider>().AsSelf().SingleInstance();

        builder.RegisterType<StandaloneRunEnvironmentStateProvider>().AsSelf().AsImplementedInterfaces().SingleInstance();
        builder.RegisterModule<MainModule>();

        var container = builder.Build();
        _settingsSourceProvider = container.Resolve<PatcherSettingsSourceProvider>(new NamedParameter("sourcePath", Path.Combine(rootPath, SynthEBDPaths.StandaloneSourceDirName, SynthEBDPaths.SettingsSourceFileName)));
        _environmentSourceProvider = container.Resolve<PatcherEnvironmentSourceProvider>(new NamedParameter("sourcePath", Path.Combine(rootPath, SynthEBDPaths.StandaloneSourceDirName, SynthEBDPaths.EnvironmentSourceDirName)));
        _environmentStateProvider = container.Resolve<StandaloneRunEnvironmentStateProvider>(new NamedParameter("environmentSourceProvider", _environmentSourceProvider));
        _patcherState = container.Resolve<PatcherState>();
        _logger = container.Resolve<Logger>();

        var mainVM = container.Resolve<MainWindow_ViewModel>();
        mainVM.Init();
        window.DataContext = mainVM;
        window.Show();

        var navPanel = container.Resolve<VM_NavPanel>();
        navPanel.GoToMainMenu();

        return 0;
    }

    public int OpenForSettings(IOpenForSettingsState state)
    {
        var window = new MainWindow();

        var builder = new ContainerBuilder();
        builder.RegisterModule<MainModule>();
        builder.RegisterType<PatcherSettingsSourceProvider>().AsSelf().SingleInstance();
        builder.RegisterType<PatcherEnvironmentSourceProvider>().AsSelf().SingleInstance();
        builder.RegisterInstance(new OpenForSettingsWrapper(state)).AsSelf().AsImplementedInterfaces().SingleInstance();
        var container = builder.Build();

        _settingsSourceProvider = container.Resolve<PatcherSettingsSourceProvider>(new NamedParameter("sourcePath", Path.Combine(state.ExtraSettingsDataPath, SynthEBDPaths.SettingsSourceFileName)));
        _environmentSourceProvider = container.Resolve<PatcherEnvironmentSourceProvider>(new NamedParameter("sourcePath", Path.Combine(state.ExtraSettingsDataPath, SynthEBDPaths.StandaloneSourceDirName, SynthEBDPaths.EnvironmentSourceDirName))); // resolved only to satisfy SaveLoader; not needed for Synthesis runs
        _environmentStateProvider = container.Resolve<OpenForSettingsWrapper>();
        _patcherState = container.Resolve<PatcherState>();
        _logger = container.Resolve<Logger>();

        var mainVM = container.Resolve<MainWindow_ViewModel>();
        window.DataContext = mainVM;
        mainVM.Init();
        window.Show();

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
        _environmentStateProvider = container.Resolve<PatcherStateWrapper>();
        _logger = container.Resolve<Logger>();

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

        // Output folder setting is handled via an Rx subscription in standalone mode; must be explicitly set in patcher mode
        // patcher must be resolved before _paths.OutputDataFolder is set here; otherwise the constructor resets the OutputDataFolder.
        if (!_patcherState.GeneralSettings.OutputDataFolder.IsNullOrEmpty() && Directory.Exists(_patcherState.GeneralSettings.OutputDataFolder))
        {
            var paths = container.Resolve<SynthEBDPaths>();
            paths.OutputDataFolder = _patcherState.GeneralSettings.OutputDataFolder;
            _logger.LogMessage("Output folder for SynthEBD-associated files: " + paths.OutputDataFolder);
        }
        else
        {
            _logger.LogMessage("Warning: outputting SynthEBD-associated files to data folder because no output folder was found in settings");
        }

        await patcher.RunPatcher();
    }

    private async void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        StringBuilder sb = new();
        sb.AppendLine("SynthEBD has crashed with the following error:");
        sb.AppendLine(ExceptionLogger.GetExceptionStack(e.Exception));
        sb.AppendLine();
        sb.AppendLine("SynthEBD Version: " + PatcherState.Version);
        sb.AppendLine();
        if (_environmentStateProvider != null)
        {
            sb.AppendLine("Run Mode: " + _environmentStateProvider.RunMode);
        }
        else
        {
            sb.AppendLine("Environment State: Null");
        }
        sb.AppendLine();
        try
        {
            sb.AppendLine("Installation Location: " + Assembly.GetEntryAssembly()?.Location ?? "Failed to locate.");
        }
        catch
        {
            sb.AppendLine("Installation Location: GetEntryAssembly() failed.");
        }
        sb.AppendLine();
        sb.AppendLine("Patcher Settings Creation Log:");
        sb.AppendLine(PatcherSettingsSourceProvider.SettingsLog.ToString());
        sb.AppendLine();
        sb.AppendLine("Patcher Environment Creation Log:");
        sb.AppendLine(PatcherEnvironmentSourceProvider.SettingsLog.ToString());
        sb.AppendLine();
        if (_patcherState != null)
        {
            sb.AppendLine("Patcher State:");
            sb.AppendLine(_patcherState.GetStateLogStr());
        }
        else
        {
            sb.AppendLine("Patcher State: Null");
        }

        sb.AppendLine();
        if (_logger != null)
        {
            if (_logger.CurrentNPCInfo != null)
            {
                string id = "No ID";
                if (_logger.CurrentNPCInfo?.LogIDstring != null) { id = _logger.CurrentNPCInfo.LogIDstring; }
                else if (_logger.CurrentNPCInfo?.NPC?.FormKey != null) { id = _logger.CurrentNPCInfo.NPC.FormKey.ToString(); }
                sb.AppendLine("Current NPC: " + id);

                if (_logger.CurrentNPCInfo?.NPC != null && 
                    _environmentStateProvider != null && 
                    _environmentStateProvider?.LinkCache != null)
                {
                    var contexts = _logger.CurrentNPCInfo.NPC.ToLink().ResolveAllContexts<ISkyrimMod, ISkyrimModGetter, INpc, INpcGetter>(_environmentStateProvider.LinkCache).ToArray();
                    var sourcePlugins = "NPC Override Order: " + Environment.NewLine +
                                        string.Join(Environment.NewLine, contexts.Select(x => x.ModKey.ToString()));
                    sb.AppendLine(sourcePlugins);
                }

                if (_logger.CurrentNPCInfo?.Report != null)
                {
                    try
                    {
                        _logger.CurrentNPCInfo.Report.LogCurrentNPC = true;
                        _logger.CurrentNPCInfo.Report.SaveCurrentNPCLog = true;
                        (string savePath, string reportStr) = _logger.SaveReport(_logger.CurrentNPCInfo);
                        if (!reportStr.IsNullOrWhitespace())
                        {
                            sb.AppendLine("Saved NPC report to " + savePath + ". Please include this file if submitting a bug report.");
                        }
                    }
                    catch
                    {
                        sb.AppendLine("Could not save NPC Report");
                    }
                }
            }
            else
            {
                sb.AppendLine("Current NPC Info: Null");
            }
        }
        else
        {
            sb.AppendLine("Logger: Null");
        }

        var errorMessage = sb.ToString();

        var path = Path.Combine(_settingsSourceProvider.GetCurrentSettingsRootPath(), "Logs", "Crash Logs", DateTime.Now.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture) + ".txt");


        Task.Run(() => PatcherIO.WriteTextFile(path, errorMessage, _logger)).Wait();

        MessageWindow.DisplayNotificationOK("SynthEBD has crashed.", errorMessage);

        e.Handled = true;

        Application.Current.MainWindow.Close();
    }
}