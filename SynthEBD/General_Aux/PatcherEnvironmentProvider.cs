using System.Reactive.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

public class PatcherEnvironmentProvider : VM
{
    public static PatcherEnvironmentProvider Instance = new();
    public string PatchFileName { get; set; } = "SynthEBD";
    public SkyrimRelease SkyrimVersion { get; set; } = SkyrimRelease.SkyrimSE;
    public string GameDataFolder { get; set; } = "";
    public RelayCommand SelectGameDataFolder { get; }
    public RelayCommand ClearGameDataFolder { get; }
    public SkyrimMod OutputMod { get; set; }

    public IGameEnvironment<ISkyrimMod, ISkyrimModGetter> Environment { get; private set; }

    private PatcherEnvironmentProvider()
    {
        SelectGameDataFolder = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFolder(Environment.DataFolderPath, out var tmpFolder))
                {
                    GameDataFolder = tmpFolder;
                }
            }
        );

        ClearGameDataFolder = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                GameDataFolder = String.Empty;
            }
        );

        Observable.CombineLatest(
                this.WhenAnyValue(x => x.PatchFileName),
                this.WhenAnyValue(x => x.SkyrimVersion),
                this.WhenAnyValue(x => x.GameDataFolder),
                (PatchFileName, Release, DataFolder) => (PatchFileName, Release, DataFolder))
            .Select(inputs =>
            {
                ModKey.TryFromName(PatchFileName, ModType.Plugin, out var outputModKey);
                OutputMod = new SkyrimMod(outputModKey, SkyrimVersion);

                var builder = GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(inputs.Release.ToGameRelease());
                if (!inputs.DataFolder.IsNullOrWhitespace())
                {
                    builder = builder.WithTargetDataFolder(inputs.DataFolder);
                }

                var build = builder
                    .TransformModListings(x =>
                        x.OnlyEnabledAndExisting().
                        RemoveModAndDependents(inputs.PatchFileName, verbose: true))
                        .WithOutputMod(OutputMod)
                    .Build();

                if (build.LinkCache.ListedOrder.Count == 1) // invalid environment directory (ListedOrder only contains output mod)
                {
                    var customEnvWindow = new Window_CustomEnvironment();
                    var customEnvVM = new VM_CustomEnvironment(customEnvWindow);
                    customEnvWindow.DataContext = customEnvVM;
                    customEnvWindow.ShowDialog();

                    if (customEnvVM.IsValidated)
                    {
                        SkyrimVersion = customEnvVM.SkyrimRelease;
                        GameDataFolder = customEnvVM.CustomGamePath;
                        return customEnvVM.Environment;
                    }
                    else
                    {
                        System.Windows.Application.Current.Shutdown();
                        System.Environment.Exit(1);
                    }
                }

                return build;
            })
            .Subscribe(x => Environment = x)
            .DisposeWith(this);
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