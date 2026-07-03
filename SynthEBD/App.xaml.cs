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
using ReactiveUI;
using ReactiveUI.Builder;

namespace SynthEBD;

/// <summary>
/// WPF application entry point. Registers the SynthEBD callbacks with the Mutagen
/// <see cref="SynthesisPipeline"/> and routes startup into one of three paths:
/// standalone UI (<see cref="StandaloneOpen"/>), settings UI launched from Synthesis
/// (<see cref="OpenForSettings"/>), and the patch run (<see cref="RunPatch"/>), plus a
/// runnability check (<see cref="CanRunPatch"/>). Each path builds its own Autofac
/// container from <see cref="MainModule"/> with a mode-specific environment provider.
/// </summary>
public partial class App : Application
{
    private PatcherSettingsSourceProvider _settingsSourceProvider;
    private PatcherEnvironmentSourceProvider _environmentSourceProvider;
    private IEnvironmentStateProvider _environmentStateProvider;
    private PatcherState _patcherState;
    private Logger _logger;

    /// <summary>
    /// Wires the Synthesis pipeline callbacks (settings/runnability/patch/standalone),
    /// switches it to WPF mode, and runs it against the process arguments, blocking
    /// until it completes.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        // Last-ditch crash logging to CrashLog.txt next to the exe. The rich XAML-wired
        // DispatcherUnhandledException handler (Application_DispatcherUnhandledException) needs the
        // fully built container/state and only sees UI-thread exceptions; these two backstops also
        // catch background-thread and pre-UI crashes (notably under a mod manager's VFS, where a
        // startup crash otherwise leaves no window and no log). Both handlers coexist.
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => LogCrash("AppDomain.UnhandledException", ev.ExceptionObject as Exception);
        this.DispatcherUnhandledException += (_, ev) => LogCrash("DispatcherUnhandledException", ev.Exception);

        base.OnStartup(e);

        // ReactiveUI 20+ no longer self-initializes on assembly load (older ReactiveUI did, which is
        // why SynthEBD never needed this before). Its WPF platform services (IActivationForViewFetcher,
        // binding converters, the dispatcher scheduler) must be registered via the RxAppBuilder before
        // the first view/ViewModel is created — otherwise every WhenAnyValue/WhenActivated throws
        // "ReactiveUI has not been initialized". All three Synthesis entry points below (standalone UI,
        // OpenForSettings, and even the headless RunPatch/CanRunPatch — which resolve settings VMs via
        // SaveLoader) construct ReactiveObjects, so initialize here once, before the pipeline runs.
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithWpf()
            .BuildApp();

        // CRITICAL — set before any ReactiveCommand/WhenAnyValue is created below. ReactiveCommand and
        // POCOObservableForProperty capture RxSchedulers.MainThreadScheduler at creation time. The
        // RxAppBuilder leaves it as DefaultScheduler (the thread pool; WithWpf()'s WaitForDispatcherScheduler
        // does not stick), so off-thread emissions would read WPF DependencyObjects from a pool thread and
        // throw cross-thread InvalidOperationExceptions. Force the real UI dispatcher (resolvable from any thread).
        RxSchedulers.MainThreadScheduler = new System.Reactive.Concurrency.DispatcherScheduler(Application.Current.Dispatcher);

        SynthesisPipeline.Instance
            .SetOpenForSettings(OpenForSettings)
            .AddRunnabilityCheck(CanRunPatch)
            .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
            .SetTypicalOpen(StandaloneOpen)
            .SetForWpf()
            .Run(e.Args)
            .Wait();
    }

    /// <summary>
    /// Last-ditch crash logger: appends an unhandled exception to CrashLog.txt next to the exe so
    /// failures that occur before (or instead of) any UI — notably when launched under a mod
    /// manager's virtual file system — leave a diagnosable trace instead of silently vanishing.
    /// Complements the detailed <see cref="Application_DispatcherUnhandledException"/> report.
    /// </summary>
    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "CrashLog.txt"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}:{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* nothing more we can do */ }
    }

    /// <summary>
    /// Standalone startup path: builds the container with a
    /// <see cref="StandaloneRunEnvironmentStateProvider"/>, resolves settings/environment
    /// source providers (rooted under the install directory), the <see cref="PatcherState"/>,
    /// the <see cref="MainWindow_ViewModel"/>, and shows the main window on the main menu.
    /// </summary>
    /// <returns>0 to signal success to the Synthesis pipeline.</returns>
    public int StandaloneOpen()
    {
        ThemeManager.ApplyTheme(ThemeManager.DefaultThemeName);
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

    /// <summary>
    /// Synthesis "open for settings" path: builds the container with an
    /// <see cref="OpenForSettingsWrapper"/> over <paramref name="state"/>, sources settings
    /// from the Synthesis ExtraSettingsDataPath, and shows the settings UI. The environment
    /// source provider is resolved only to satisfy <see cref="SaveLoader"/>.
    /// </summary>
    /// <param name="state">Synthesis-supplied settings-state context.</param>
    /// <returns>0 to signal success to the Synthesis pipeline.</returns>
    public int OpenForSettings(IOpenForSettingsState state)
    {
        ThemeManager.ApplyTheme(ThemeManager.DefaultThemeName);
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

    /// <summary>
    /// Synthesis runnability check: builds a throwaway container with a
    /// <see cref="RunnabilitySettingsWrapper"/>, loads all settings via
    /// <see cref="SaveLoader.LoadAllSettings"/>, and runs <see cref="PreRunValidation"/>.
    /// Throws if validation fails so Synthesis reports the patcher as not runnable.
    /// </summary>
    /// <param name="state">Synthesis-supplied runnability context.</param>
    /// <exception cref="Exception">Thrown when patcher-state validation fails.</exception>
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

    /// <summary>
    /// Synthesis patch-run path: builds the container with a <see cref="PatcherStateWrapper"/>,
    /// loads all settings, runs body-shape annotation validation (BodySlide or BodyGen) and
    /// returns early if it fails, sets the output data folder from settings when valid, then
    /// invokes <see cref="Patcher.RunPatcher"/>.
    /// </summary>
    /// <param name="state">Synthesis-supplied patcher state (load order, link cache, paths).</param>
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

    /// <summary>
    /// Global WPF unhandled-exception handler. Composes a detailed crash report (exception
    /// stack, version, run mode, settings/environment creation logs, patcher state, and the
    /// current NPC's override order and saved report), writes it to a timestamped crash log,
    /// shows it to the user, marks the exception handled, and closes the main window.
    /// </summary>
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
            sb.AppendLine("Installation Location: " + (Assembly.GetEntryAssembly()?.Location ?? "Failed to locate."));
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

        // _settingsSourceProvider may not have resolved yet if the crash happened early in startup; fall back to the
        // app base directory so the crash log still gets written (the other crash-handler fields are guarded similarly).
        var path = BuildCrashLogPath(_settingsSourceProvider?.GetCurrentSettingsRootPath(), AppContext.BaseDirectory, DateTime.Now);


        // Deliberately synchronous (do NOT rewrite as `await`): this DispatcherUnhandledException handler must finish
        // writing the crash log and reach `e.Handled = true` below before it returns to the dispatcher -- awaiting would
        // return at the await point with e.Handled still false, so WPF would treat the exception as unhandled and tear
        // the app down before the dialog/Handled run. Task.Run runs the async WriteTextFile (and its continuations) on a
        // pool thread so .Wait() on the UI thread cannot deadlock on a continuation that needs the UI thread.
        Task.Run(() => PatcherIO.WriteTextFile(path, errorMessage, _logger)).Wait();

        MessageWindow.DisplayNotificationOK("SynthEBD has crashed.", errorMessage);

        e.Handled = true;

        Application.Current.MainWindow.Close();
    }

    /// <summary>
    /// Builds the crash-log file path ("&lt;root&gt;/Logs/Crash Logs/&lt;yyyy-MM-dd-HH-mm&gt;.txt"). Falls back to
    /// <paramref name="fallbackRootPath"/> when <paramref name="settingsRootPath"/> is null/blank, so a crash before the
    /// settings source provider resolves still writes its log.
    /// </summary>
    public static string BuildCrashLogPath(string? settingsRootPath, string fallbackRootPath, DateTime timestamp)
    {
        var root = string.IsNullOrWhiteSpace(settingsRootPath) ? fallbackRootPath : settingsRootPath;
        return Path.Combine(root, "Logs", "Crash Logs", timestamp.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture) + ".txt");
    }
}