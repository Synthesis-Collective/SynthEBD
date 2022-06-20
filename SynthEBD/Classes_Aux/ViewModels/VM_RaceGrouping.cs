using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_RaceGrouping : VM
{
    public VM_RaceGrouping(PatcherEnvironmentProvider patcherEnvironmentProvider)
    {
        _patcherEnvironmentProvider = patcherEnvironmentProvider;
    }
    public VM_RaceGrouping(RaceGrouping raceGrouping, VM_Settings_General parentVM)
    {
        this.Label = raceGrouping.Label;
        this.Races = new ObservableCollection<FormKey>(raceGrouping.Races);
        
        _patcherEnvironmentProvider.WhenAnyValue(x => x.Environment.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.RaceGroupings.Remove(this));
    }
    public string Label { get; set; }
    public ObservableCollection<FormKey> Races { get; set; }
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();
    public ILinkCache lk { get; private set; }
    public VM_Settings_General ParentVM { get; set; }
    public RelayCommand DeleteCommand { get; }
    private readonly PatcherEnvironmentProvider _patcherEnvironmentProvider;

    public static ObservableCollection<VM_RaceGrouping> GetViewModelsFromModels(List<RaceGrouping> models, VM_Settings_General parentVM)
    {
        var RGVM = new ObservableCollection<VM_RaceGrouping>();

        foreach (var x in models)
        {
            var y = new VM_RaceGrouping(x, parentVM);
            RGVM.Add(y);
        }

        return RGVM;
    }
    public static RaceGrouping DumpViewModelToModel(VM_RaceGrouping viewModel)
    {
        RaceGrouping model = new RaceGrouping();
        model.Label = viewModel.Label;
        model.Races = viewModel.Races.ToHashSet();

        return model;
    }
}