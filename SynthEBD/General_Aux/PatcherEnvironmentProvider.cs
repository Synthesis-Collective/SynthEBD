using System.ComponentModel;
using System.Reactive.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Noggog.WPF;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace SynthEBD;

public class PatcherEnvironmentProvider : INotifyPropertyChanged
{
    public static PatcherEnvironmentProvider Instance = new();
    public string PatchFileName { get; set; } = "SynthEBD.esp";
    public SkyrimRelease SkyrimVersion { get; set; } = SkyrimRelease.SkyrimSE;
    public string OutputDataFolder { get; set; } = "";
    public RelayCommand SelectOutputFolder { get; }
    
    public IGameEnvironment<ISkyrimMod, ISkyrimModGetter> Environment { get; private set; }

    public event PropertyChangedEventHandler PropertyChanged;

    private PatcherEnvironmentProvider()
    {
        SelectOutputFolder = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFolder(Environment.DataFolderPath, out var tmpFolder))
                {
                    OutputDataFolder = tmpFolder;
                }
            }
        );
        
        Observable.CombineLatest(
                this.WhenAnyValue(x => x.PatchFileName),
                this.WhenAnyValue(x => x.SkyrimVersion),
                this.WhenAnyValue(x => x.OutputDataFolder),
                (PatchFileName, Release, DataFolder) => (PatchFileName, Release, DataFolder))
            .Select(inputs =>
            {
                var builder = GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(inputs.Release.ToGameRelease());
                if (!inputs.DataFolder.IsNullOrWhitespace())
                {
                    builder = builder.WithTargetDataFolder(inputs.DataFolder);
                }
                return builder
                    .TransformModListings(x =>
                        x.OnlyEnabledAndExisting().RemoveModAndDependents(inputs.PatchFileName + ".esp", verbose: true))
                    .Build();
            })
            .Subscribe(x => Environment = x);
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