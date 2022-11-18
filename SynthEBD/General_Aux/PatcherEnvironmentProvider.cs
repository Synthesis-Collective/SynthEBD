using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace SynthEBD;

public class PatcherEnvironmentProvider : Noggog.WPF.ViewModel
{
    public static PatcherEnvironmentProvider Instance;
    [Reactive] public string PatchFileName { get; set; } = "SynthEBD";
    [Reactive] public SkyrimRelease SkyrimVersion { get; set; } = SkyrimRelease.SkyrimSE;
    [Reactive] public string GameDataFolder { get; set; } = "";
    public RelayCommand SelectGameDataFolder { get; }
    public RelayCommand ClearGameDataFolder { get; }
    public SkyrimMod OutputMod { get; set; }
    public IGameEnvironment<ISkyrimMod, ISkyrimModGetter> Environment { get; private set; }
    public StringBuilder EnvironmentLog { get; } = new();

    private void LogEnvironmentEvent(string logString)
    {
        EnvironmentLog.AppendLine(logString);
    }

    public PatcherEnvironmentProvider(PatcherSettingsProvider settingsProvider)
    {
        // initialize paths
        var sourceSettings = settingsProvider.SourceSettings.Value;
        if (sourceSettings.Initialized)
        {
            if (!string.IsNullOrWhiteSpace(sourceSettings.GameEnvironmentDirectory))
            {
                GameDataFolder = sourceSettings.GameEnvironmentDirectory;
            }
            SkyrimVersion = sourceSettings.SkyrimVersion;
        }

        UpdateEnvironment(); // create an initial environment

        SelectGameDataFolder = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                SelectUserSpecifiedGameEnvironment(String.Empty);
            }
        );

        this.WhenAnyValue(x => x.SkyrimVersion).Subscribe(_ => UpdateEnvironment());
    }

    public void UpdateEnvironment()
    {
        LogEnvironmentEvent("Creating Patcher Environment:");
        OutputMod = new SkyrimMod(ModKey.FromName(PatchFileName, ModType.Plugin), SkyrimVersion);
        LogEnvironmentEvent("Output mod: " + OutputMod.ModKey.ToString());
        LogEnvironmentEvent("Skyrim Version: " + SkyrimVersion.ToString());
        var builder = GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(SkyrimVersion.ToGameRelease());
        if (!GameDataFolder.IsNullOrWhitespace())
        {
            builder = builder.WithTargetDataFolder(GameDataFolder);
            LogEnvironmentEvent("Game Data Directory: " + GameDataFolder.ToString());
        }
        else
        {
            LogEnvironmentEvent("Game Data Directory: Default");
        }

        var built = false;

        try
        {
            Environment = builder
                .TransformModListings(x =>
                    x.OnlyEnabledAndExisting().
                    RemoveModAndDependents(PatchFileName, verbose: true))
                    .WithOutputMod(OutputMod)
                .Build();
            built = true;
            LogEnvironmentEvent("Environment created successfully");
        }
        catch
        {
            LogEnvironmentEvent("Environment was NOT successfully created");
            built = false;
        }

        if (!built || Environment.LinkCache.ListedOrder.Count == 1) // invalid environment directory (ListedOrder only contains output mod)
        {
            SelectUserSpecifiedGameEnvironment("SynthEBD was unable to create an environment from any default installation directory. This can occur if your game is installed in a non-default location.");
        }
    }

    private void SelectUserSpecifiedGameEnvironment(string message)
    {
        var customEnvWindow = new Window_CustomEnvironment();
        var customEnvVM = new VM_CustomEnvironment(customEnvWindow, message, SkyrimVersion, GameDataFolder);
        customEnvWindow.DataContext = customEnvVM;
        customEnvWindow.ShowDialog();

        if (customEnvVM.IsValidated)
        {
            SkyrimVersion = customEnvVM.SkyrimRelease;
            GameDataFolder = customEnvVM.CustomGameDataDir;
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

public static class LoadOrderExtensions
{
    public static IEnumerable<IModListingGetter<ISkyrimModGetter>> RemoveModAndDependents(this IEnumerable<IModListingGetter<ISkyrimModGetter>> listedOrder, string outputModName, bool verbose)
    {
        List<IModListingGetter<ISkyrimModGetter>> filteredLoadOrder = new List<IModListingGetter<ISkyrimModGetter>>();
        HashSet<string> removedMods = new HashSet<string>();
        foreach (var mod in listedOrder)
        {
            if (mod.ModKey.FileName == outputModName) { continue; }

            var masterFiles = mod.Mod.ModHeader.MasterReferences.Select(x => x.Master.ToString());

            if (masterFiles.Contains(outputModName, StringComparer.OrdinalIgnoreCase)) 
            {
                if (verbose) { Logger.CallTimedLogErrorWithStatusUpdateAsync(mod.ModKey.FileName.String + " will not be patched because it is mastered to a previous version of " + outputModName, ErrorType.Warning, 2); };
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
                if (verbose) { Logger.CallTimedLogErrorWithStatusUpdateAsync(mod.ModKey.FileName + " will not be patched because it is mastered to a mod which is mastered to a previous version of " + outputModName, ErrorType.Warning, 2); }
                continue;
            }

            filteredLoadOrder.Add(mod);
        }
        return filteredLoadOrder;
    }
}