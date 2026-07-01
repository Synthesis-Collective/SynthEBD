using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using System.Reflection;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System.Text;
using System.IO;
using System;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace SynthEBD;

/// <summary>
/// Abstraction over the Mutagen/Synthesis game environment that the rest of SynthEBD depends on:
/// the load order, link cache, key data/settings/output paths, run mode, and logging targets.
/// Implemented separately for standalone runs and for the three Synthesis entry points
/// (settings UI, runnability check, and patch execution).
/// </summary>
public interface IEnvironmentStateProvider
{
    // core properties "seeded" by Noggog
    /// <summary>The active Skyrim load order.</summary>
    ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> LoadOrder { get; }
    /// <summary>Link cache used to resolve records across the load order.</summary>
    ILinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache { get; }
    /// <summary>Folder for user-facing extra settings data.</summary>
    DirectoryPath ExtraSettingsDataPath { get; }
    /// <summary>Folder for SynthEBD's bundled internal data (e.g. 7-Zip, defaults).</summary>
    DirectoryPath InternalDataPath { get; }
    //Additional properties needed by SynthEBD
    /// <summary>The game's Data folder. Settable so a custom location can be supplied.</summary>
    DirectoryPath DataFolderPath { get; set; }
    /// <summary>Whether SynthEBD is running standalone or under Synthesis.</summary>
    EnvironmentMode RunMode { get; }
    /// <summary>Whether log output goes to the in-app pane or the console.</summary>
    LogMode LoggerMode { get; }
    /// <summary>Folder where log files are written.</summary>
    public string LogFolderPath { get; }
    /// <summary>File name of the output plugin SynthEBD generates.</summary>
    public string OutputModName { get; set; }
    /// <summary>The Skyrim release (SE/AE/VR) being targeted.</summary>
    public SkyrimRelease SkyrimVersion { get; }
    // Additional properties (for logging only)
    /// <summary>Path to the Creation Club listings file (logging only).</summary>
    public string CreationClubListingsFilePath { get; }
    /// <summary>Path to the load-order file (logging only).</summary>
    public string LoadOrderFilePath { get; }
    /// <summary>Accumulated startup-log lines, surfaced once the logger is available.</summary>
    public List<string> StartUpLog { get; set; }
}

/// <summary>Whether SynthEBD is running as its own application or hosted inside the Synthesis pipeline.</summary>
public enum EnvironmentMode
{
    /// <summary>Running as a standalone WPF application.</summary>
    Standalone,
    /// <summary>Running inside the Synthesis patcher pipeline.</summary>
    Synthesis
}

/// <summary>An <see cref="IEnvironmentStateProvider"/> that also exposes a writable output mod (for run modes that emit a patch).</summary>
public interface IOutputEnvironmentStateProvider : IEnvironmentStateProvider
{
    /// <summary>The output plugin that generated records are written into.</summary>
    ISkyrimMod OutputMod { get; }
}

/// <summary>
/// <see cref="IEnvironmentStateProvider"/> for standalone runs: builds a Mutagen game environment from
/// the detected (or user-specified) install, rebuilding it reactively when the Skyrim version, output
/// mod name, or data folder changes, and prompting for a custom environment if detection fails.
/// </summary>
public class StandaloneRunEnvironmentStateProvider : VM, IOutputEnvironmentStateProvider
{
    // "Core" state properties and fields
    private IGameEnvironment<ISkyrimMod, ISkyrimModGetter> _environment;
    public ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> LoadOrder => _environment.LoadOrder;
    public ILinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache => _environment.LinkCache;
    [Reactive] public SkyrimRelease SkyrimVersion { get; set; }
    public DirectoryPath ExtraSettingsDataPath { get; set; }
    public DirectoryPath InternalDataPath { get; set; }
    [Reactive] public DirectoryPath DataFolderPath { get; set; }
    public ISkyrimMod OutputMod { get; set; }
    public EnvironmentMode RunMode { get; } = EnvironmentMode.Standalone;
    public LogMode LoggerMode { get; } = LogMode.SynthEBD;

    // Additional properties for customization
    [Reactive] public string OutputModName { get; set; }
    public StringBuilder EnvironmentLog { get; } = new();
    public string LogFolderPath { get; }
    // Additional properties (for logging only)
    public string CreationClubListingsFilePath { get; set; }
    public string LoadOrderFilePath { get; set; }
    public List<string> StartUpLog { get; set; } = new();

    /// <summary>Initializes paths from the executable location, seeds version/data-folder/output-mod from the source provider, builds the initial environment, and subscribes to rebuild it on relevant property changes.</summary>
    /// <param name="environmentSourceProvider">Supplies the initial Skyrim version, game directory, and output mod name.</param>
    /// <exception cref="Exception">Thrown when the running assembly location cannot be determined.</exception>
    public StandaloneRunEnvironmentStateProvider(PatcherEnvironmentSourceProvider environmentSourceProvider)
    {
        StartUpLog.Add(Logger.FormatTimeStamp(DateTime.Now) + "Initializing Standalone SynthEBD Environment");
        System.Diagnostics.Stopwatch sw = new();
        sw.Start();

        string? exeLocation = null;
        var assembly = Assembly.GetEntryAssembly();
        if (assembly != null)
        {
            exeLocation = Path.GetDirectoryName(assembly.Location);
        }
        else
        {
            throw new Exception("Could not locate running assembly");
        }

        LogFolderPath = Path.Combine(exeLocation, "Logs");
        ExtraSettingsDataPath = Path.Combine(exeLocation, "Settings");
        InternalDataPath = Path.Combine(exeLocation, "InternalData");

        SkyrimVersion = environmentSourceProvider.EnvironmentSource.Value.SkyrimVersion;
        if (!environmentSourceProvider.EnvironmentSource.Value.GameEnvironmentDirectory.IsNullOrWhitespace())
        {
            DataFolderPath = environmentSourceProvider.EnvironmentSource.Value.GameEnvironmentDirectory;
        }
        if (!environmentSourceProvider.EnvironmentSource.Value.OutputModName.IsNullOrWhitespace())
        {
            OutputModName = environmentSourceProvider.EnvironmentSource.Value.OutputModName;
        }

        StartUpLog.Add(Logger.FormatTimeStamp(DateTime.Now) + "Building Game Environment");
        UpdateEnvironment();

        this.WhenAnyValue(
                x => x.SkyrimVersion,
                x => x.OutputModName,
                x => x.DataFolderPath)
            .Subscribe(_ => UpdateEnvironment())
            .DisposeWith(this);

        sw.Stop();
        StartUpLog.Add(Logger.FormatTimeStamp(DateTime.Now) + "Generated Standalone SynthEBD Environment in: " + string.Format("{0:D2}:{1:D2}:{2:D2}", sw.Elapsed.Hours, sw.Elapsed.Minutes, sw.Elapsed.Seconds));
    }

    /// <summary>Appends a line to the in-memory environment-build log.</summary>
    /// <param name="logString">The line to record.</param>
    private void LogEnvironmentEvent(string logString)
    {
        EnvironmentLog.AppendLine(logString);
    }

    /// <summary>(Re)builds the Mutagen game environment for the current version/data-folder/output-mod, excluding the output mod and any plugins mastered to a stale copy of it; falls back to a user prompt if the environment is invalid.</summary>
    /// <remarks>Creates a fresh output <see cref="SkyrimMod"/>, records resolved paths back into this provider, and logs success/failure. An environment whose listed order contains only the output mod is treated as invalid.</remarks>
    public void UpdateEnvironment()
    {
        LogEnvironmentEvent("Creating Patcher Environment:");
        var builder = GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(SkyrimVersion.ToGameRelease());
        if (!DataFolderPath.ToString().IsNullOrWhitespace())
        {
            builder = builder.WithTargetDataFolder(DataFolderPath);
            LogEnvironmentEvent("Game Data Directory: " + DataFolderPath.ToString());
        }
        else
        {
            LogEnvironmentEvent("Game Data Directory: Default");
        }

        LogEnvironmentEvent("Skyrim Version: " + SkyrimVersion.ToString());

        OutputMod = new SkyrimMod(ModKey.FromName(OutputModName, ModType.Plugin), SkyrimVersion);
        LogEnvironmentEvent("Output mod: " + OutputMod.ModKey.ToString());

        var built = false;

        try
        {
            string notificationStr = "";
            _environment = builder
                .TransformModListings(x =>
                    x.OnlyEnabledAndExisting().
                    RemoveModAndDependents(OutputModName, verbose: true, out notificationStr))
                    .WithOutputMod(OutputMod)
                .Build();
            
            // Mutagen 0.54 made IGameEnvironment.LoadOrderFilePath nullable (FilePath?), which no
            // longer exposes .Exists/.Path directly. Resolve it to its string path (the same implicit
            // FilePath? -> string conversion the LoadOrderFilePath field assignment below relies on)
            // and validate that.
            string loadOrderFilePath = _environment.LoadOrderFilePath;
            if (string.IsNullOrEmpty(loadOrderFilePath) || !System.IO.File.Exists(loadOrderFilePath))
            {
                throw new Exception("Load order file path at " + loadOrderFilePath + " does not exist"); // prevent successful initialization in the wrong mode.
            }

            built = true;

            if (!notificationStr.IsNullOrEmpty())
            {
                LogEnvironmentEvent(notificationStr);
            }
            LogEnvironmentEvent("Environment created successfully");
            CreationClubListingsFilePath = _environment.CreationClubListingsFilePath;
            LoadOrderFilePath = _environment.LoadOrderFilePath;
            DataFolderPath = _environment.DataFolderPath; // If a custom data folder path was provided it will not change. If no custom data folder path was provided, this will set it to the default path.
        }
        catch (Exception ex)
        {
            LogEnvironmentEvent("Environment was NOT successfully created");
            LogEnvironmentEvent(ExceptionLogger.GetExceptionStack(ex));
            built = false;
        }

        if (!built || _environment.LinkCache.ListedOrder.Count == 1) // invalid environment directory (ListedOrder only contains output mod)
        {
            SelectUserSpecifiedGameEnvironment("SynthEBD was unable to create an environment from any default installation directory. This can occur if your game is installed in a non-default location.");
        }
    }

    /// <summary>Shows the custom-environment dialog so the user can point SynthEBD at a valid game install, then rebuilds the environment; exits the application if the user cancels.</summary>
    /// <param name="message">Explanatory message shown in the dialog.</param>
    private void SelectUserSpecifiedGameEnvironment(string message)
    {
        var customEnvWindow = new Window_CustomEnvironment();
        var customEnvVM = new VM_CustomEnvironment(customEnvWindow, message, SkyrimVersion, DataFolderPath);
        customEnvWindow.DataContext = customEnvVM;
        customEnvWindow.ShowDialog();

        if (customEnvVM.IsValidated)
        {
            DataFolderPath = customEnvVM.TrialEnvironment.DataFolderPath;
            SkyrimVersion = customEnvVM.SkyrimRelease;
            customEnvVM.TrialEnvironment.LoadOrder.Dispose();
            customEnvVM.TrialEnvironment.LinkCache.Dispose();
            customEnvVM.TrialEnvironment.Dispose(); // free up the output file if it is active in the load order
            UpdateEnvironment();
        }
        else
        {
            System.Windows.Application.Current.Shutdown();
            System.Environment.Exit(1);
        }
    }
}

/// <summary>
/// <see cref="IEnvironmentStateProvider"/> backing the Synthesis "open for settings" entry point;
/// wraps an <see cref="IOpenForSettingsState"/> and lazily materializes the game environment.
/// </summary>
public class OpenForSettingsWrapper : IEnvironmentStateProvider
{
    private readonly IOpenForSettingsState _state;
    private readonly Lazy<IGameEnvironment<ISkyrimMod, ISkyrimModGetter>> _env;

    /// <summary>Captures the Synthesis settings state and prepares a lazily-built game environment.</summary>
    /// <param name="state">The Synthesis open-for-settings state.</param>
    public OpenForSettingsWrapper(IOpenForSettingsState state)
    {
        StartUpLog.Add(Logger.FormatTimeStamp(DateTime.Now) + "Initializing Synthesis SynthEBD Environment");
        System.Diagnostics.Stopwatch sw = new();
        sw.Start();

        _state = state;
        _env = new Lazy<IGameEnvironment<ISkyrimMod, ISkyrimModGetter>>(
            () => state.GetEnvironmentState<ISkyrimMod, ISkyrimModGetter>());
        DataFolderPath = _state.DataFolderPath;

        sw.Stop();
        StartUpLog.Add(Logger.FormatTimeStamp(DateTime.Now) + "Generated Synthesis SynthEBD Environment in: " + string.Format("{0:D2}:{1:D2}:{2:D2}", sw.Elapsed.Hours, sw.Elapsed.Minutes, sw.Elapsed.Seconds));
    }

    public ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> LoadOrder => _env.Value.LoadOrder;
    public ILinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache => _env.Value.LinkCache;
    public DirectoryPath ExtraSettingsDataPath => _state.ExtraSettingsDataPath ?? throw new Exception("Could not locate Extra Settings Data Path");
    public DirectoryPath InternalDataPath => _state.InternalDataPath ?? throw new Exception("Could not locate Internal Data Path");
    public DirectoryPath DataFolderPath { get; set; }
    public EnvironmentMode RunMode { get; } = EnvironmentMode.Synthesis;
    public LogMode LoggerMode { get; } = LogMode.SynthEBD;
    public string LogFolderPath => Path.Combine(_state.ExtraSettingsDataPath, "Logs");
    public SkyrimRelease SkyrimVersion => _state.GameRelease.ToSkyrimRelease();
    public string LoadOrderFilePath => _state.LoadOrderFilePath;
    public string CreationClubListingsFilePath => _env.Value?.CreationClubListingsFilePath ?? "Not Available";
    public string OutputModName { get; set; } = "Not Available";
    public List<string> StartUpLog { get; set; } = new();
}

/// <summary>
/// <see cref="IEnvironmentStateProvider"/> backing the Synthesis runnability (CanRunPatch) check;
/// wraps an <see cref="IRunnabilityState"/> and lazily materializes the game environment. Logs to the console.
/// </summary>
public class RunnabilitySettingsWrapper : IEnvironmentStateProvider
{
    private readonly IRunnabilityState _state;
    private readonly Lazy<IGameEnvironment<ISkyrimMod, ISkyrimModGetter>> _env;

    /// <summary>Captures the Synthesis runnability state and prepares a lazily-built game environment.</summary>
    /// <param name="state">The Synthesis runnability state.</param>
    public RunnabilitySettingsWrapper(IRunnabilityState state)
    {
        _state = state;
        _env = new Lazy<IGameEnvironment<ISkyrimMod, ISkyrimModGetter>>(
            () => state.GetEnvironmentState<ISkyrimMod, ISkyrimModGetter>());
        DataFolderPath = _state.DataFolderPath;
    }

    public ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> LoadOrder => _env.Value.LoadOrder;
    public ILinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache => _env.Value.LinkCache;
    public DirectoryPath ExtraSettingsDataPath => _state.ExtraSettingsDataPath ?? throw new Exception("Could not locate Extra Settings Data Path");
    public DirectoryPath InternalDataPath => _state.InternalDataPath ?? throw new Exception("Could not locate Internal Data Path");
    public DirectoryPath DataFolderPath { get; set; }
    public EnvironmentMode RunMode { get; } = EnvironmentMode.Synthesis;
    public LogMode LoggerMode { get; } = LogMode.Synthesis;
    public string LogFolderPath => Path.Combine(_state.ExtraSettingsDataPath, "Logs");
    public SkyrimRelease SkyrimVersion => _state.GameRelease.ToSkyrimRelease();
    public string LoadOrderFilePath => _state.LoadOrderFilePath;
    public string CreationClubListingsFilePath => _env.Value?.CreationClubListingsFilePath ?? "Not Available";
    public string OutputModName { get; set; } = "Not Available";
    public List<string> StartUpLog { get; set; } = new();
}

/// <summary>
/// <see cref="IOutputEnvironmentStateProvider"/> backing the Synthesis patch-execution entry point;
/// wraps an <see cref="IPatcherState{TMod, TModGetter}"/> and exposes its load order, link cache, and patch mod.
/// </summary>
public class PatcherStateWrapper : IOutputEnvironmentStateProvider
{
    private readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> _state;

    /// <summary>Captures the Synthesis patcher state and its output patch mod.</summary>
    /// <param name="state">The Synthesis patcher state.</param>
    public PatcherStateWrapper(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
    {
        _state = state;
        DataFolderPath = _state.DataFolderPath;
        OutputModName = _state.PatchMod.ModKey.FileName;
        OutputMod = _state.PatchMod;
    }

    public ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> LoadOrder => _state.LoadOrder;
    public ILinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache => _state.LinkCache;
    public DirectoryPath ExtraSettingsDataPath => _state.ExtraSettingsDataPath ?? throw new Exception("Could not locate Extra Settings Data Path");
    public DirectoryPath InternalDataPath => _state.InternalDataPath ?? throw new Exception("Could not locate Internal Data Path");
    public DirectoryPath DataFolderPath { get; set; }
    public EnvironmentMode RunMode { get; } = EnvironmentMode.Synthesis;
    public LogMode LoggerMode { get; } = LogMode.Synthesis;
    public string LogFolderPath => Path.Combine(_state.ExtraSettingsDataPath, "Logs");
    public SkyrimRelease SkyrimVersion => _state.GameRelease.ToSkyrimRelease();
    public string LoadOrderFilePath => _state.LoadOrderFilePath;
    public string CreationClubListingsFilePath => "Not Available";
    public string OutputModName { get; set; }
    public ISkyrimMod OutputMod { get; set; }
    public List<string> StartUpLog { get; set; } = new();
}

/// <summary>Load-order helpers used when constructing the standalone game environment.</summary>
public static class LoadOrderExtensions
{
    /// <summary>Filters a load order to exclude the output mod itself and any plugins that (directly or transitively) master to it, preventing SynthEBD from patching against a stale copy of its own output.</summary>
    /// <param name="listedOrder">The mods to filter.</param>
    /// <param name="outputModName">File name of SynthEBD's output plugin.</param>
    /// <param name="verbose">When <c>true</c>, records a human-readable reason for each excluded mod.</param>
    /// <param name="notificationStr">Receives the newline-joined exclusion messages (empty when nothing was excluded).</param>
    /// <returns>The filtered load order.</returns>
    /// <remarks>Transitive exclusion relies on listing order: a dependent is only detected if the mod it masters to was already removed earlier in the enumeration.</remarks>
    public static IEnumerable<IModListingGetter<ISkyrimModGetter>> RemoveModAndDependents(this IEnumerable<IModListingGetter<ISkyrimModGetter>> listedOrder, string outputModName, bool verbose, out string notificationStr)
    {
        List<IModListingGetter<ISkyrimModGetter>> filteredLoadOrder = new List<IModListingGetter<ISkyrimModGetter>>();
        HashSet<string> removedMods = new HashSet<string>();
        List<string> notifications = new();
        foreach (var mod in listedOrder)
        {
            if (mod.ModKey.FileName == outputModName) { continue; }

            var masterFiles = mod.Mod.ModHeader.MasterReferences.Select(x => x.Master.ToString()).ToArray();

            if (masterFiles.Contains(outputModName, StringComparer.OrdinalIgnoreCase))
            {
                if (verbose) { notifications.Add(mod.ModKey.FileName.String + " will not be patched because it is mastered to a previous version of " + outputModName); };
                removedMods.Add(mod.ModKey.FileName.String);
                continue;
            }

            bool isRemovedDependent = false;
            foreach (var removedMod in removedMods)
            {
                if (masterFiles.Contains(removedMod, StringComparer.OrdinalIgnoreCase))
                {
                    isRemovedDependent = true;
                    break;
                }
            }
            if (isRemovedDependent)
            {
                if (verbose) { notifications.Add(mod.ModKey.FileName + " will not be patched because it is mastered to a mod which is mastered to a previous version of " + outputModName); }
                continue;
            }

            filteredLoadOrder.Add(mod);
        }

        notificationStr = string.Join(Environment.NewLine, notifications);
        return filteredLoadOrder;
    }
}
