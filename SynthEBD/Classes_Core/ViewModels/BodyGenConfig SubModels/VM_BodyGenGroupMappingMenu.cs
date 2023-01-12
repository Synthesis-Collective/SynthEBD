using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;
using DynamicData.Binding;

namespace SynthEBD;

public class VM_BodyGenGroupMappingMenu : VM
{
    private readonly VM_BodyGenRacialMapping.Factory _mappingFactory;
    public delegate VM_BodyGenGroupMappingMenu Factory(VM_BodyGenGroupsMenu groupsMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs);
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

public class VM_BodyGenRacialMapping : VM
{
    private readonly IEnvironmentStateProvider _stateProvider;
    public delegate VM_BodyGenRacialMapping Factory(VM_BodyGenGroupsMenu groupsMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs);
    public VM_BodyGenRacialMapping(VM_BodyGenGroupsMenu groupsMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IEnvironmentStateProvider stateProvider)
    {
        _stateProvider = stateProvider;
        RaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        MonitoredGroupsMenu = groupsMenu;
        
        _stateProvider.WhenAnyValue(x => x.LinkCache)
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
        });
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

    public static VM_BodyGenRacialMapping GetViewModelFromModel(BodyGenConfig.RacialMapping model, VM_BodyGenGroupsMenu groupsMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_BodyGenRacialMapping.Factory mappingFactory)
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
public class VM_BodyGenCombination : VM
{
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

        Members.ToObservableChangeSet().Subscribe(x => CheckForEmptyCombination());
    }
    public ObservableCollection<VM_CollectionMemberString> Members { get; set; } = new();
    public double ProbabilityWeighting { get; set; } = 1;

    public ObservableCollection<VM_CollectionMemberString> MonitoredGroups { get; set; }

    public VM_BodyGenRacialMapping Parent { get; set; }

    public RelayCommand RemoveMember { get; }

    public RelayCommand AddMember { get; }

    public static VM_BodyGenCombination GetViewModelFromModel(BodyGenConfig.RacialMapping.BodyGenCombination model, VM_BodyGenGroupsMenu groupsMenu, VM_BodyGenRacialMapping parent)
    {
        VM_BodyGenCombination viewModel = new VM_BodyGenCombination(groupsMenu, parent);
        viewModel.ProbabilityWeighting = model.ProbabilityWeighting;
        viewModel.Members = VM_CollectionMemberString.InitializeObservableCollectionFromICollection(model.Members);
        return viewModel;
    }

    public static BodyGenConfig.RacialMapping.BodyGenCombination DumpViewModelToModel(VM_BodyGenCombination viewModel)
    {
        BodyGenConfig.RacialMapping.BodyGenCombination model = new BodyGenConfig.RacialMapping.BodyGenCombination();
        model.ProbabilityWeighting = viewModel.ProbabilityWeighting;
        model.Members = viewModel.Members.Select(x => x.Content).ToHashSet();
        return model;
    }

    public void CheckForEmptyCombination()
    {
        if (this.Members.Count == 0)
        {
            this.Parent.Combinations.Remove(this);
        }
    }
}