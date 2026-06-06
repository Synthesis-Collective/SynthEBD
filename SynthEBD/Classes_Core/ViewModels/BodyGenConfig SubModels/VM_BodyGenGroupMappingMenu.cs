using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;
using DynamicData.Binding;

namespace SynthEBD;

/// <summary>
/// View model for the BodyGen config editor's race-to-template-group mapping menu. Holds the
/// collection of <see cref="VM_BodyGenRacialMapping"/> entries that bind races (or race groupings)
/// to template-group combinations.
/// </summary>
public class VM_BodyGenGroupMappingMenu : VM
{
    private readonly VM_BodyGenRacialMapping.Factory _mappingFactory;
    /// <summary>Autofac factory delegate for <see cref="VM_BodyGenGroupMappingMenu"/>.</summary>
    public delegate VM_BodyGenGroupMappingMenu Factory(VM_BodyGenGroupsMenu groupsMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs);
    /// <summary>Wires up the Add/Remove mapping commands; AddMapping seeds a new mapping with one combination preloaded with the first available template group.</summary>
    public VM_BodyGenGroupMappingMenu(VM_BodyGenGroupsMenu groupsMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_BodyGenRacialMapping.Factory mappingFactory)
    {
        _mappingFactory = mappingFactory;
        AddMapping = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var newMapping = _mappingFactory(groupsMenu, raceGroupingVMs);
                var newCombination = new VM_BodyGenCombination(groupsMenu, newMapping);
                if (groupsMenu.TemplateGroups.Any())
                {
                    newCombination.Members.Add(groupsMenu.TemplateGroups.First());
                }
                newMapping.Combinations.Add(newCombination);
                RacialTemplateGroupMap.Add(newMapping);
                }
        );

        RemoveMapping = new RelayCommand(
            canExecute: _ => true,
            execute: x => RacialTemplateGroupMap.Remove((VM_BodyGenRacialMapping)x)
        );
    }
    public ObservableCollection<VM_BodyGenRacialMapping> RacialTemplateGroupMap { get; set; } = new();
    public VM_BodyGenRacialMapping DisplayedMapping { get; set; }
    public RelayCommand AddMapping { get; }
    public RelayCommand RemoveMapping { get; }
}

/// <summary>
/// View model of a <see cref="BodyGenConfig.RacialMapping"/>: maps a set of races / race groupings
/// to one or more template-group <see cref="VM_BodyGenCombination"/>s. Backs a single mapping entry
/// in the group mapping menu.
/// </summary>
public class VM_BodyGenRacialMapping : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    /// <summary>Autofac factory delegate for <see cref="VM_BodyGenRacialMapping"/>.</summary>
    public delegate VM_BodyGenRacialMapping Factory(VM_BodyGenGroupsMenu groupsMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs);
    /// <summary>Builds the race-grouping checkbox list, tracks the link cache, wires Add/Remove combination commands, and toggles <see cref="ShowAddNew"/> based on whether any combinations exist.</summary>
    public VM_BodyGenRacialMapping(VM_BodyGenGroupsMenu groupsMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IEnvironmentStateProvider environmentProvider, Logger logger)
    {
        _environmentProvider = environmentProvider;
        _logger = logger;
        RaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        MonitoredGroupsMenu = groupsMenu;
        
        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        AddCombination = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var newCombination = new VM_BodyGenCombination(groupsMenu, this);
                if (groupsMenu.TemplateGroups.Any())
                {
                    newCombination.Members.Add(groupsMenu.TemplateGroups.First());
                }
                Combinations.Add(newCombination);
            }
        );

        RemoveCombination = new RelayCommand(
            canExecute: _ => true,
            execute: x =>  Combinations.Remove((VM_BodyGenCombination)x)
        );

        Combinations.ToObservableChangeSet().Subscribe(x =>
        {
            if (Combinations.Any())
            {
                ShowAddNew = false;
            }
            else
            {
                ShowAddNew = true;
            }
        }).DisposeWith(this);
    }
    public string Label { get; set; } = "";
    public ObservableCollection<FormKey> Races { get; set; } = new();
    public VM_RaceGroupingCheckboxList RaceGroupings { get; set; }
    public ObservableCollection<VM_BodyGenCombination> Combinations { get; set; } = new();

    public VM_BodyGenGroupsMenu MonitoredGroupsMenu { get; set; }

    
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();
    public RelayCommand AddCombination { get; }
    public RelayCommand RemoveCombination { get; }
    public bool ShowAddNew { get; set; }

    /// <summary>Builds a <see cref="VM_BodyGenRacialMapping"/> from its persisted model.</summary>
    public static VM_BodyGenRacialMapping GetViewModelFromModel(BodyGenConfig.RacialMapping model, VM_BodyGenGroupsMenu groupsMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_BodyGenRacialMapping.Factory mappingFactory, Logger logger)
    {
        VM_BodyGenRacialMapping viewModel = mappingFactory(groupsMenu, raceGroupingVMs);

        viewModel.Label = model.Label;
        viewModel.Races = new ObservableCollection<FormKey>(model.Races);
        viewModel.RaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        foreach (var combination in model.Combinations)
        {
            viewModel.Combinations.Add(VM_BodyGenCombination.GetViewModelFromModel(combination, groupsMenu, viewModel));
        }
        return viewModel;
    }

    /// <summary>Serializes a <see cref="VM_BodyGenRacialMapping"/> back into its persisted model.</summary>
    public static BodyGenConfig.RacialMapping DumpViewModelToModel(VM_BodyGenRacialMapping viewModel)
    {
        BodyGenConfig.RacialMapping model = new BodyGenConfig.RacialMapping();
        model.Label = viewModel.Label;
        model.Races = viewModel.Races.ToHashSet();
        model.RaceGroupings = viewModel.RaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        foreach (var combination in viewModel.Combinations)
        {
            model.Combinations.Add(VM_BodyGenCombination.DumpViewModelToModel(combination));
        }
        return model;
    }
}
/// <summary>
/// View model of a <see cref="BodyGenConfig.RacialMapping.BodyGenCombination"/>: a weighted set of
/// template-group member strings drawn from the parent mapping. Self-removes from its parent when emptied.
/// </summary>
public class VM_BodyGenCombination : VM
{
    /// <summary>Subscribes to the available template groups, wires Add/Remove member commands, and auto-removes the combination when its member list becomes empty.</summary>
    public VM_BodyGenCombination(VM_BodyGenGroupsMenu groupsMenu, VM_BodyGenRacialMapping parent)
    {
        MonitoredGroups = groupsMenu.TemplateGroups;

        Parent = parent;

        RemoveMember = new RelayCommand(
            canExecute: _ => true,
            execute: x => { Members.Remove((VM_CollectionMemberString)x); CheckForEmptyCombination(); }
        );

        AddMember = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Members.Add(new VM_CollectionMemberString("", Members))
        );

        Members.ToObservableChangeSet().Subscribe(x => CheckForEmptyCombination()).DisposeWith(this);
    }
    public ObservableCollection<VM_CollectionMemberString> Members { get; set; } = new();
    public double ProbabilityWeighting { get; set; } = 1;

    public ObservableCollection<VM_CollectionMemberString> MonitoredGroups { get; set; }

    public VM_BodyGenRacialMapping Parent { get; set; }

    public RelayCommand RemoveMember { get; }

    public RelayCommand AddMember { get; }

    /// <summary>Builds a <see cref="VM_BodyGenCombination"/> from its persisted model.</summary>
    public static VM_BodyGenCombination GetViewModelFromModel(BodyGenConfig.RacialMapping.BodyGenCombination model, VM_BodyGenGroupsMenu groupsMenu, VM_BodyGenRacialMapping parent)
    {
        VM_BodyGenCombination viewModel = new VM_BodyGenCombination(groupsMenu, parent);
        viewModel.ProbabilityWeighting = model.ProbabilityWeighting;
        viewModel.Members = VM_CollectionMemberString.InitializeObservableCollectionFromICollection(model.Members);
        return viewModel;
    }

    /// <summary>Serializes a <see cref="VM_BodyGenCombination"/> back into its persisted model.</summary>
    public static BodyGenConfig.RacialMapping.BodyGenCombination DumpViewModelToModel(VM_BodyGenCombination viewModel)
    {
        BodyGenConfig.RacialMapping.BodyGenCombination model = new BodyGenConfig.RacialMapping.BodyGenCombination();
        model.ProbabilityWeighting = viewModel.ProbabilityWeighting;
        model.Members = viewModel.Members.Select(x => x.Content).ToList();
        return model;
    }

    /// <summary>Removes this combination from its parent mapping if it has no members left.</summary>
    public void CheckForEmptyCombination()
    {
        if (Members.Count == 0)
        {
            Parent.Combinations.Remove(this);
        }
    }
}