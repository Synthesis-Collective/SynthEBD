/*
using System.Text;
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
    [Reactive] public SkyrimMod OutputMod { get; set; }
    public IGameEnvironment<ISkyrimMod, ISkyrimModGetter> Environment { get; private set; }
    public StringBuilder EnvironmentLog { get; } = new();
    public readonly Logger _logger;

    private void LogEnvironmentEvent(string logString)
    {
        EnvironmentLog.AppendLine(logString);
    }

    public PatcherEnvironmentProvider(PatcherSettingsSourceProvider settingsProvider, Logger logger)
    {
        _logger = logger;
        var sourceSettings = settingsProvider.SettingsSource.Value;
        // initialize paths
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

        this.WhenAnyValue(
                x => x.SkyrimVersion,
                x => x.PatchFileName)
            .Subscribe(_ => UpdateEnvironment());
    }

    public void UpdateEnvironment()
    {
        LogEnvironmentEvent("Creating Patcher Environment:");
        OutputMod = null;
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
            string notificationStr = "";
            Environment = builder
                .TransformModListings(x =>
                    x.OnlyEnabledAndExisting().
                    RemoveModAndDependents(PatchFileName, verbose: true, out notificationStr))
                    .WithOutputMod(OutputMod)
                .Build();
            built = true;
            if (!notificationStr.IsNullOrEmpty())
            {
                LogEnvironmentEvent(notificationStr);
            }
            LogEnvironmentEvent("Environment created successfully");
        }
        catch (Exception ex)
        {
            LogEnvironmentEvent("Environment was NOT successfully created");
            LogEnvironmentEvent(ExceptionLogger.GetExceptionStack(ex));
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

*/