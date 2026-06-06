using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;
using System.Diagnostics;

namespace SynthEBD;

/// <summary>View model for a named race grouping (label + race set), with a delete command.</summary>
[DebuggerDisplay("{Label} ({Races.Count})")]
public class VM_RaceGrouping : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    /// <summary>Autofac factory delegate for constructing a grouping under a race-grouping owner.</summary>
    public delegate VM_RaceGrouping Factory(RaceGrouping raceGrouping, IHasRaceGroupingVMs parentVM);
    private readonly VM_RaceGrouping.Factory _selfFactory;
    /// <summary>Creates the grouping VM from a model and wires the delete command and link-cache tracking.</summary>
    /// <param name="raceGrouping">The grouping model to edit.</param>
    /// <param name="parentVM">The owner holding the grouping collection.</param>
    /// <param name="environmentProvider">Supplies the link cache for the race picker.</param>
    /// <param name="selfFactory">Factory used by <see cref="Copy"/>.</param>
    public VM_RaceGrouping(RaceGrouping raceGrouping, IHasRaceGroupingVMs parentVM, IEnvironmentStateProvider environmentProvider, VM_RaceGrouping.Factory selfFactory)
    {
        _environmentProvider = environmentProvider;
        _selfFactory = selfFactory;
        Label = raceGrouping.Label;
        Races = new ObservableCollection<FormKey>(raceGrouping.Races);
        
        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.RaceGroupings.Remove(this));
    }
    public string Label { get; set; }
    public ObservableCollection<FormKey> Races { get; set; }
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();
    public ILinkCache lk { get; private set; }
    public IHasRaceGroupingVMs ParentVM { get; set; }
    public RelayCommand DeleteCommand { get; }

    /// <summary>Builds an observable collection of grouping VMs from a list of models.</summary>
    /// <param name="models">The grouping models.</param>
    /// <param name="parentVM">The owner holding the grouping collection.</param>
    /// <param name="factory">Factory used to construct each VM.</param>
    /// <returns>A collection of grouping view models.</returns>
    public static ObservableCollection<VM_RaceGrouping> GetViewModelsFromModels(List<RaceGrouping> models, IHasRaceGroupingVMs parentVM, VM_RaceGrouping.Factory factory)
    {
        var RGVM = new ObservableCollection<VM_RaceGrouping>();

        foreach (var x in models)
        {
            var y = factory(x, parentVM);
            RGVM.Add(y);
        }

        return RGVM;
    }
    /// <summary>Projects this grouping VM back into a <see cref="RaceGrouping"/> model.</summary>
    /// <returns>The populated model.</returns>
    public RaceGrouping DumpViewModelToModel()
    {
        RaceGrouping model = new RaceGrouping();
        model.Label = Label;
        model.Races = Races.ToHashSet();

        return model;
    }

    /// <summary>Copies this grouping into another owner by round-tripping through its model.</summary>
    /// <param name="destination">The owner to attach the copy to.</param>
    /// <returns>The copied grouping VM.</returns>
    public VM_RaceGrouping Copy(IHasRaceGroupingVMs destination)
    {
        return _selfFactory(DumpViewModelToModel(), destination);
    }

    /// <summary>Returns the groupings whose race set exactly equals the given race collection.</summary>
    /// <param name="collection">The race FormKeys to match.</param>
    /// <param name="groupings">The groupings to test.</param>
    /// <returns>The matching groupings (empty when none match exactly).</returns>
    /// <remarks>The inline comment ("returns true if…") predates the change to returning the matched set. Uses an O(n²) nested-loop comparison; see review notes.</remarks>
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

/// <summary>Implemented by view models that own an editable collection of race groupings.</summary>
public interface IHasRaceGroupingVMs
{
    /// <summary>The owned race-grouping view models.</summary>
    public ObservableCollection<VM_RaceGrouping> RaceGroupings { get; set; }
}