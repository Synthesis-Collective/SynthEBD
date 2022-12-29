using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_RaceGrouping : VM
{
    private readonly IStateProvider _stateProvider;
    public delegate VM_RaceGrouping Factory(RaceGrouping raceGrouping, VM_Settings_General parentVM);
    public VM_RaceGrouping(RaceGrouping raceGrouping, VM_Settings_General parentVM, IStateProvider stateProvider)
    {
        _stateProvider = stateProvider;
        Label = raceGrouping.Label;
        Races = new ObservableCollection<FormKey>(raceGrouping.Races);
        
        _stateProvider.WhenAnyValue(x => x.LinkCache)
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

    public static ObservableCollection<VM_RaceGrouping> GetViewModelsFromModels(List<RaceGrouping> models, VM_Settings_General parentVM, VM_RaceGrouping.Factory factory)
    {
        var RGVM = new ObservableCollection<VM_RaceGrouping>();

        foreach (var x in models)
        {
            var y = factory(x, parentVM);
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

    public static HashSet<VM_RaceGrouping> CollectionMatchesRaceGrouping(IEnumerable<FormKey> collection, IEnumerable<VM_RaceGrouping> groupings) // returns true if a collection of Race formkeys is identical to an existing race grouping
    {
        HashSet<VM_RaceGrouping> matchedGroupings = new();
        foreach (var group in groupings)
        {
            if (collection.Count() != group.Races.Count()) {  continue; }
            bool allRacesFound = true;
            foreach (var collectionRace in collection)
            {
                bool raceFound = false;
                foreach (var groupRace in group.Races)
                {
                    if (groupRace.Equals(collectionRace)) { raceFound = true; break; }
                }
                if (!raceFound) { allRacesFound = false; break; }
            }
            if (allRacesFound)
            {
                matchedGroupings.Add(group);
            }
        }
        return matchedGroupings;
    }
}