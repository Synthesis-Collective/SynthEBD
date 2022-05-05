using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SynthEBD;

public class VM_BodyGenGroupMappingMenu : INotifyPropertyChanged
{
    public VM_BodyGenGroupMappingMenu(VM_BodyGenGroupsMenu groupsMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
        this.RacialTemplateGroupMap = new ObservableCollection<VM_BodyGenRacialMapping>();
        this.DisplayedMapping = new VM_BodyGenRacialMapping(groupsMenu, raceGroupingVMs);

        AddMapping = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.RacialTemplateGroupMap.Add(new VM_BodyGenRacialMapping(groupsMenu, raceGroupingVMs))
        );

        RemoveMapping = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => this.RacialTemplateGroupMap.Remove((VM_BodyGenRacialMapping)x)
        );
    }
    public ObservableCollection<VM_BodyGenRacialMapping> RacialTemplateGroupMap { get; set; }
    public VM_BodyGenRacialMapping DisplayedMapping { get; set; }
    public RelayCommand AddMapping { get; }
    public RelayCommand RemoveMapping { get; }
    public event PropertyChangedEventHandler PropertyChanged;
}

public class VM_BodyGenRacialMapping : INotifyPropertyChanged
{
    public VM_BodyGenRacialMapping(VM_BodyGenGroupsMenu groupsMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
        this.Label = "";
        this.Races = new ObservableCollection<FormKey>();
        this.RaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        this.Combinations = new ObservableCollection<VM_BodyGenCombination>();
        this.MonitoredGroupsMenu = groupsMenu;

        AddCombination = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.Combinations.Add(new VM_BodyGenCombination(groupsMenu, this))
        );

        RemoveCombination = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x =>  this.Combinations.Remove((VM_BodyGenCombination)x)
        );

        this.lk = PatcherEnvironmentProvider.Environment.LinkCache;
        this.RacePickerFormKeys = typeof(IRaceGetter).AsEnumerable();
    }
    public string Label { get; set; }
    public ObservableCollection<FormKey> Races { get; set; }
    public VM_RaceGroupingCheckboxList RaceGroupings { get; set; }
    public ObservableCollection<VM_BodyGenCombination> Combinations { get; set; }

    public VM_BodyGenGroupsMenu MonitoredGroupsMenu { get; set; }

    public ILinkCache lk { get; set; }
    public IEnumerable<Type> RacePickerFormKeys { get; set; }
    public RelayCommand AddCombination { get; }
    public RelayCommand RemoveCombination { get; }

    public event PropertyChangedEventHandler PropertyChanged;

    public static VM_BodyGenRacialMapping GetViewModelFromModel(BodyGenConfig.RacialMapping model, VM_BodyGenGroupsMenu groupsMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
        VM_BodyGenRacialMapping viewModel = new VM_BodyGenRacialMapping(groupsMenu, raceGroupingVMs);

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
        model.RaceGroupings = viewModel.RaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet();
        foreach (var combination in viewModel.Combinations)
        {
            model.Combinations.Add(VM_BodyGenCombination.DumpViewModelToModel(combination));
        }
        return model;
    }
}
public class VM_BodyGenCombination : INotifyPropertyChanged
{
    public VM_BodyGenCombination(VM_BodyGenGroupsMenu groupsMenu, VM_BodyGenRacialMapping parent)
    {
        this.Members = new ObservableCollection<string>();
        this.ProbabilityWeighting = 1;

        this.MonitoredGroups = groupsMenu.TemplateGroups;

        this.Parent = parent;

        RemoveMember = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => { this.Members.Remove((string)x); CheckForEmptyCombination(); }
        );

        AddMember = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.Members.Add("")
        );

        this.Members.CollectionChanged += CheckForEmptyCombination;
    }
    public ObservableCollection<string> Members { get; set; }
    public double ProbabilityWeighting { get; set; }

    public ObservableCollection<VM_CollectionMemberString> MonitoredGroups { get; set; }

    public VM_BodyGenRacialMapping Parent { get; set; }

    public RelayCommand RemoveMember { get; }

    public RelayCommand AddMember { get; }

    public event PropertyChangedEventHandler PropertyChanged;

    public static VM_BodyGenCombination GetViewModelFromModel(BodyGenConfig.RacialMapping.BodyGenCombination model, VM_BodyGenGroupsMenu groupsMenu, VM_BodyGenRacialMapping parent)
    {
        VM_BodyGenCombination viewModel = new VM_BodyGenCombination(groupsMenu, parent);
        viewModel.ProbabilityWeighting = model.ProbabilityWeighting;
        viewModel.Members = new ObservableCollection<string>(model.Members);
        return viewModel;
    }

    public static BodyGenConfig.RacialMapping.BodyGenCombination DumpViewModelToModel(VM_BodyGenCombination viewModel)
    {
        BodyGenConfig.RacialMapping.BodyGenCombination model = new BodyGenConfig.RacialMapping.BodyGenCombination();
        model.ProbabilityWeighting = viewModel.ProbabilityWeighting;
        model.Members = viewModel.Members.ToHashSet();
        return model;
    }

    public void CheckForEmptyCombination(object sender, NotifyCollectionChangedEventArgs e)
    {
        CheckForEmptyCombination();
    }

    public void CheckForEmptyCombination()
    {
        if (this.Members.Count == 0)
        {
            this.Parent.Combinations.Remove(this);
        }
    }
}