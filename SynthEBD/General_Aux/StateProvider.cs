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

public interface IStateProvider
{
    // core properties "seeded" by Noggog
    ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> LoadOrder { get; }
    ILinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache { get; }
    DirectoryPath ExtraSettingsDataPath { get; }
    DirectoryPath InternalDataPath { get; }
    //Additional properties needed by SynthEBD
    DirectoryPath DataFolderPath { get; set; }
    Mode RunMode { get; }
    public string LogFolderPath { get; }
    public string OutputModName { get; set; }
    public SkyrimRelease SkyrimVersion { get; }
    // Additional properties (for logging only)
    public string CreationClubListingsFilePath { get; }
    public string LoadOrderFilePath { get; }
}

public enum Mode
{
    Standalone,
    Synthesis
}

public interface IOutputStateProvider : IStateProvider
{
    ISkyrimMod OutputMod { get; }
}

public class StandaloneRunStateProvider : VM, IOutputStateProvider
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
    public Mode RunMode { get; set; } = Mode.Standalone;

    // Additional properties for customization
    [Reactive] public string OutputModName { get; set; }
    public StringBuilder EnvironmentLog { get; } = new();
    public string LogFolderPath { get; }
    // Additional properties (for logging only)
    public string CreationClubListingsFilePath { get; set; }
    public string LoadOrderFilePath { get; set; }

    public StandaloneRunStateProvider(PatcherEnvironmentSourceProvider environmentSourceProvider)
    {
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
        InternalDataPath = System.IO.Path.Combine(exeLocation, "InternalData");

        SkyrimVersion = environmentSourceProvider.EnvironmentSource.Value.SkyrimVersion;
        if (!environmentSourceProvider.EnvironmentSource.Value.GameEnvironmentDirectory.IsNullOrWhitespace())
        {
            DataFolderPath = environmentSourceProvider.EnvironmentSource.Value.GameEnvironmentDirectory;
        }
        if (!environmentSourceProvider.EnvironmentSource.Value.OutputModName.IsNullOrWhitespace())
        {
            OutputModName = environmentSourceProvider.EnvironmentSource.Value.OutputModName;
        }
        UpdateEnvironment();

        this.WhenAnyValue(
                x => x.SkyrimVersion,
                x => x.OutputModName,
                x => x.DataFolderPath)
            .Subscribe(_ => UpdateEnvironment());
    }

    private void LogEnvironmentEvent(string logString)
    {
        EnvironmentLog.AppendLine(logString);
    }

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

        OutputMod = null;
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

    private void SelectUserSpecifiedGameEnvironment(string message)
    {
        var customEnvWindow = new Window_CustomEnvironment();
        var customEnvVM = new VM_CustomEnvironment(customEnvWindow, message, SkyrimVersion, DataFolderPath);
        customEnvWindow.DataContext = customEnvVM;
        customEnvWindow.ShowDialog();

        if (customEnvVM.IsValidated)
        {
            DataFolderPath = customEnvVM.CustomGameDataDir;
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

public class OpenForSettingsWrapper : IStateProvider
{
    private readonly IOpenForSettingsState _state;
    private readonly Lazy<IGameEnvironment<ISkyrimMod, ISkyrimModGetter>> _env;

    public OpenForSettingsWrapper(IOpenForSettingsState state)
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
    public Mode RunMode { get; set; } = Mode.Synthesis;
    public string LogFolderPath => Path.Combine(_state.ExtraSettingsDataPath, "Logs");
    public SkyrimRelease SkyrimVersion => _state.GameRelease.ToSkyrimRelease();
    public string LoadOrderFilePath => _state.LoadOrderFilePath;
    public string CreationClubListingsFilePath => _env.Value?.CreationClubListingsFilePath ?? "Not Available";
    public string OutputModName { get; set; } = "Not Available";
}

public class RunnabilitySettingsWrapper : IStateProvider
{
    private readonly IRunnabilityState _state;
    private readonly Lazy<IGameEnvironment<ISkyrimMod, ISkyrimModGetter>> _env;

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
    public Mode RunMode { get; set; } = Mode.Synthesis;
    public string LogFolderPath => Path.Combine(_state.ExtraSettingsDataPath, "Logs");
    public SkyrimRelease SkyrimVersion => _state.GameRelease.ToSkyrimRelease();
    public string LoadOrderFilePath => _state.LoadOrderFilePath;
    public string CreationClubListingsFilePath => _env.Value?.CreationClubListingsFilePath ?? "Not Available";
    public string OutputModName { get; set; } = "Not Available";
}

public class PatcherStateWrapper : IOutputStateProvider
{
    private readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> _state;

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
    public Mode RunMode { get; set; } = Mode.Synthesis;
    public string LogFolderPath => Path.Combine(_state.ExtraSettingsDataPath, "Logs");
    public SkyrimRelease SkyrimVersion => _state.GameRelease.ToSkyrimRelease();
    public string LoadOrderFilePath => _state.LoadOrderFilePath;
    public string CreationClubListingsFilePath => "Not Available";
    public string OutputModName { get; set; }
    public ISkyrimMod OutputMod { get; set; }
}

public static class LoadOrderExtensions
{
    public static IEnumerable<IModListingGetter<ISkyrimModGetter>> RemoveModAndDependents(this IEnumerable<IModListingGetter<ISkyrimModGetter>> listedOrder, string outputModName, bool verbose, out string notificationStr)
    {
        List<IModListingGetter<ISkyrimModGetter>> filteredLoadOrder = new List<IModListingGetter<ISkyrimModGetter>>();
        HashSet<string> removedMods = new HashSet<string>();
        List<string> notifications = new();
        foreach (var mod in listedOrder)
        {
            if (mod.ModKey.FileName == outputModName) { continue; }

            var masterFiles = mod.Mod.ModHeader.MasterReferences.Select(x => x.Master.ToString());

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
