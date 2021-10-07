using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class VM_BodyGenGroupMappingMenu : INotifyPropertyChanged
    {
        public VM_BodyGenGroupMappingMenu()
        {
            this.RacialTemplateGroupMap = new ObservableCollection<VM_BodyGenRacialMapping>();
        }
        public ObservableCollection<VM_BodyGenRacialMapping> RacialTemplateGroupMap { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class VM_BodyGenRacialMapping : INotifyPropertyChanged
    {
        public VM_BodyGenRacialMapping()
        {
            this.Label = "";
            this.Races = new ObservableCollection<FormKey>();
            this.RaceGroupings = new ObservableCollection<string>();
            this.Combinations = new ObservableCollection<VM_BodyGenCombination>();
        }
        public string Label { get; set; }
        public ObservableCollection<FormKey> Races { get; set; }
        public ObservableCollection<string> RaceGroupings { get; set; }
        public ObservableCollection<VM_BodyGenCombination> Combinations { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_BodyGenRacialMapping GetViewModelFromModel(BodyGenConfig.RacialMapping model)
        {
            VM_BodyGenRacialMapping viewModel = new VM_BodyGenRacialMapping();

            viewModel.Label = model.Label;
            viewModel.Races = new ObservableCollection<FormKey>(model.Races);
            viewModel.RaceGroupings = new ObservableCollection<string>(model.RaceGroupings);
            foreach (var combination in model.Combinations)
            {
                viewModel.Combinations.Add(VM_BodyGenCombination.GetViewModelFromModel(combination));
            }
            return viewModel;
        }

        public class VM_BodyGenCombination : INotifyPropertyChanged
        {
            public VM_BodyGenCombination()
            {
                this.Members = new ObservableCollection<string>();
                this.ProbabilityWeighting = 1;
            }
            public ObservableCollection<string> Members { get; set; }
            public int ProbabilityWeighting { get; set; }

            public event PropertyChangedEventHandler PropertyChanged;

            public static VM_BodyGenCombination GetViewModelFromModel(BodyGenConfig.RacialMapping.BodyGenCombination model)
            {
                VM_BodyGenCombination viewModel = new VM_BodyGenCombination();
                viewModel.ProbabilityWeighting = model.ProbabilityWeighting;
                viewModel.Members = new ObservableCollection<string>(model.Members);
                return viewModel;
            }
        }
    }
}
