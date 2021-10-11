using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_BodyGenGroupMappingMenu : INotifyPropertyChanged
    {
        public VM_BodyGenGroupMappingMenu(VM_BodyGenGroupsMenu groupsMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
        {
            this.RacialTemplateGroupMap = new ObservableCollection<VM_BodyGenRacialMapping>();
        }
        public ObservableCollection<VM_BodyGenRacialMapping> RacialTemplateGroupMap { get; set; }
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

            this.lk = new GameEnvironmentProvider().MyEnvironment.LinkCache;
            this.RacePickerFormKeys = typeof(IRaceGetter).AsEnumerable();
        }
        public string Label { get; set; }
        public ObservableCollection<FormKey> Races { get; set; }
        public VM_RaceGroupingCheckboxList RaceGroupings { get; set; }
        public ObservableCollection<VM_BodyGenCombination> Combinations { get; set; }

        public VM_BodyGenGroupsMenu MonitoredGroupsMenu { get; set; }

        public ILinkCache lk { get; set; }
        public IEnumerable<Type> RacePickerFormKeys { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_BodyGenRacialMapping GetViewModelFromModel(BodyGenConfig.RacialMapping model, VM_BodyGenGroupsMenu groupsMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
        {
            VM_BodyGenRacialMapping viewModel = new VM_BodyGenRacialMapping(groupsMenu, raceGroupingVMs);

            viewModel.Label = model.Label;
            viewModel.Races = new ObservableCollection<FormKey>(model.Races);
            viewModel.RaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
            foreach (var combination in model.Combinations)
            {
                viewModel.Combinations.Add(VM_BodyGenCombination.GetViewModelFromModel(combination, groupsMenu));
            }
            return viewModel;
        }
    }
    public class VM_BodyGenCombination : INotifyPropertyChanged
    {
        public VM_BodyGenCombination(VM_BodyGenGroupsMenu groupsMenu)
        {
            this.Members = new ObservableCollection<string>();
            this.ProbabilityWeighting = 1;

            this.MonitoredGroups = groupsMenu.TemplateGroups;
        }
        public ObservableCollection<string> Members { get; set; }
        public int ProbabilityWeighting { get; set; }

        public ObservableCollection<VM_CollectionMemberString> MonitoredGroups { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_BodyGenCombination GetViewModelFromModel(BodyGenConfig.RacialMapping.BodyGenCombination model, VM_BodyGenGroupsMenu groupsMenu)
        {
            VM_BodyGenCombination viewModel = new VM_BodyGenCombination(groupsMenu);
            viewModel.ProbabilityWeighting = model.ProbabilityWeighting;
            viewModel.Members = new ObservableCollection<string>(model.Members);
            return viewModel;
        }
    }
}
