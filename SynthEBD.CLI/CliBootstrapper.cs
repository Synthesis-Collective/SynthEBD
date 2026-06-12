using System.IO;
using Autofac;
using Noggog;

namespace SynthEBD.CLI;

/// <summary>
/// Stands up the full SynthEBD object graph headlessly for CLI verbs, mirroring the container wiring of
/// <c>App.StandaloneOpen</c> and the integration-test harness: construct the settings/environment source
/// providers for the target SynthEBD instance, register a <see cref="CliEnvironmentStateProvider"/> behind
/// the environment interfaces, add <see cref="MainModule"/>, then load all settings via
/// <see cref="SaveLoader"/>.
///
/// <para>The settings tree is located either like the GUI does it (<c>--synthebd-path</c>: read
/// <c>Settings\SettingsSource.json</c> under the install folder, honoring any portable-settings redirect it
/// declares) or pinned directly to a tree (<c>--settings-root</c>: force portable mode rooted at that
/// folder, the same pattern <c>PatcherTestHarness</c> uses for the demo settings). The settings source must
/// be configured before <see cref="SynthEBDPaths"/> first resolves, because its constructor reads the
/// source to compute the root path — satisfied here by registering fully-constructed source instances.</para>
/// </summary>
public sealed class CliBootstrapper : IDisposable
{
    public IContainer Container { get; }
    public CliEnvironmentStateProvider EnvironmentProvider { get; }
    public PatcherState PatcherState { get; }
    public SynthEBDPaths Paths { get; }
    public string SettingsRootPath { get; }

    private CliBootstrapper(IContainer container, CliEnvironmentStateProvider environmentProvider,
        PatcherState patcherState, SynthEBDPaths paths, string settingsRootPath)
    {
        Container = container;
        EnvironmentProvider = environmentProvider;
        PatcherState = patcherState;
        Paths = paths;
        SettingsRootPath = settingsRootPath;
    }

    /// <summary>
    /// Builds the container and loads all settings for the instance described by <paramref name="options"/>.
    /// Returns <c>false</c> with a human-readable reason if the game environment cannot be constructed.
    /// </summary>
    public static bool TryCreate(CliOptions options, out CliBootstrapper? bootstrapper, out string failureReason)
    {
        bootstrapper = null;

        string settingsSourcePath;
        string environmentSourcePath;
        if (options.SettingsRoot != null)
        {
            // Source files at the root level so DefaultSettingsRootPath equals the tree itself (mirrors
            // PatcherTestHarness; keeps SettingsIO fallback paths inside the same tree).
            settingsSourcePath = Path.Combine(options.SettingsRoot, SynthEBDPaths.SettingsSourceFileName);
            environmentSourcePath = Path.Combine(options.SettingsRoot, SynthEBDPaths.EnvironmentSourceDirName);
        }
        else
        {
            // Mirrors App.StandaloneOpen's construction relative to the install folder.
            settingsSourcePath = Path.Combine(options.SynthEbdPath, SynthEBDPaths.StandaloneSourceDirName, SynthEBDPaths.SettingsSourceFileName);
            environmentSourcePath = Path.Combine(options.SynthEbdPath, SynthEBDPaths.StandaloneSourceDirName, SynthEBDPaths.EnvironmentSourceDirName);
        }

        var settingsSource = new PatcherSettingsSourceProvider(settingsSourcePath);
        if (options.SettingsRoot != null)
        {
            settingsSource.Initialized = true;
            settingsSource.UsePortableSettings = true;
            settingsSource.PortableSettingsFolder = options.SettingsRoot;
        }

        var environmentSource = new PatcherEnvironmentSourceProvider(environmentSourcePath);
        var sourced = environmentSource.EnvironmentSource.Value;
        var skyrimVersion = options.SkyrimVersion ?? sourced.SkyrimVersion;
        var dataFolder = options.DataFolder
            ?? (sourced.GameEnvironmentDirectory.IsNullOrWhitespace() ? null : sourced.GameEnvironmentDirectory);
        var outputModName = options.OutputModName
            ?? (sourced.OutputModName.IsNullOrWhitespace() ? "SynthEBD" : sourced.OutputModName);

        Console.WriteLine("Skyrim version: " + skyrimVersion);
        Console.WriteLine("Settings root: " + settingsSource.GetCurrentSettingsRootPath());

        if (!CliEnvironmentStateProvider.TryBuild(skyrimVersion, dataFolder, outputModName,
                settingsSource.GetCurrentSettingsRootPath(), out var environmentProvider, out failureReason)
            || environmentProvider == null)
        {
            return false;
        }
        Console.WriteLine("Data folder: " + environmentProvider.DataFolderPath);

        var builder = new ContainerBuilder();
        builder.RegisterModule<MainModule>();
        // Registered AFTER MainModule so these pre-configured instances win over the RegisterType
        // registrations MainModule itself declares for the source providers (last registration wins).
        builder.RegisterInstance(settingsSource).AsSelf().SingleInstance();
        builder.RegisterInstance(environmentSource).AsSelf().SingleInstance();
        builder.RegisterInstance(environmentProvider).AsSelf().AsImplementedInterfaces().SingleInstance();
        var container = builder.Build();

        var paths = container.Resolve<SynthEBDPaths>();
        container.Resolve<SaveLoader>().LoadAllSettings();
        var patcherState = container.Resolve<PatcherState>();

        Console.WriteLine("Loaded " + patcherState.AssetPacks.Count + " asset pack config(s), "
            + patcherState.RecordTemplatePlugins.Count + " record template plugin(s).");

        bootstrapper = new CliBootstrapper(container, environmentProvider, patcherState, paths,
            settingsSource.GetCurrentSettingsRootPath());
        return true;
    }

    /// <summary>
    /// Copies the loaded General settings model into the <see cref="VM_Settings_General"/> singleton
    /// (the first step of <c>ViewModelLoader.LoadInitialSettingsViewModels</c>). Required before any
    /// feature that imports race groupings or attribute groups "from General settings" — those imports
    /// read the General settings <i>view model</i>, which a headless bootstrap otherwise leaves empty.
    /// Deliberately avoids resolving <c>ViewModelLoader</c> itself, whose construction triggers a full
    /// reload including backward-compatibility migrations.
    /// </summary>
    public void PopulateGeneralSettingsViewModel()
    {
        var generalVM = Container.Resolve<VM_Settings_General>();
        var raceAliasFactory = Container.Resolve<VM_RaceAlias.Factory>();
        var linkedNPCFactory = Container.Resolve<VM_LinkedNPCGroup.Factory>();
        generalVM.CopyInFromModel(PatcherState.GeneralSettings, raceAliasFactory, linkedNPCFactory,
            EnvironmentProvider.LinkCache);
    }

    public void Dispose()
    {
        // Transitively-resolved view models queue deferred dispatcher callbacks that resolve from the
        // container. Drain the queue while the container is still alive (mirrors PatcherTestHarness.Dispose)
        // so those callbacks can't throw ObjectDisposedException after disposal. On the dispatcher thread,
        // Invoke at ContextIdle priority pumps all higher-priority queued work via a nested frame.
        try
        {
            if (System.Windows.Threading.Dispatcher.FromThread(Thread.CurrentThread) is { } dispatcher)
            {
                dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        }
        catch
        {
            // Best-effort; Program's DispatcherUnhandledException handler swallows any stragglers.
        }

        Container.Dispose();
    }
}
