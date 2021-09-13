using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_RaceGroupingCheckboxList : INotifyPropertyChanged
    {
        public VM_RaceGroupingCheckboxList(ObservableCollection<VM_RaceGrouping> RaceGroupingVMs)
        {
            this.RaceGroupingSelections = new ObservableCollection<RaceGroupingSelection>();
            foreach (var rgvm in RaceGroupingVMs)
            {
                this.RaceGroupingSelections.Add(new RaceGroupingSelection(rgvm));
            }
            this.SubscribedMasterList = RaceGroupingVMs;
            this.SubscribedMasterList.CollectionChanged += RefreshCheckList;
        }

        public ObservableCollection<VM_RaceGrouping> SubscribedMasterList { get; set; } // to fire the CollectionChanged event
        void RefreshCheckList(object sender, NotifyCollectionChangedEventArgs e)
        {
            var updatedMasterList = (ObservableCollection<VM_RaceGrouping>)sender;
            var newCheckList = new VM_RaceGroupingCheckboxList(updatedMasterList);
            this.RaceGroupingSelections = newCheckList.RaceGroupingSelections;
        }

        public ObservableCollection<RaceGroupingSelection> RaceGroupingSelections { get; set; }

        public class RaceGroupingSelection : INotifyPropertyChanged
        {
            public RaceGroupingSelection(VM_RaceGrouping raceGroupingVM)
            {
                this.Label = raceGroupingVM.RaceGrouping.Label;
                this.IsSelected = false;
                this.SubscribedMasterRaceGrouping = raceGroupingVM;
                this.SubscribedMasterRaceGrouping.PropertyChanged += RefreshRaceGroupingName;
            }
            public string Label { get; set; }
            public bool IsSelected { get; set; }

            public VM_RaceGrouping SubscribedMasterRaceGrouping { get; set; } // to fire the PropertyChanged event
            public event PropertyChangedEventHandler PropertyChanged;
            public void RefreshRaceGroupingName(object sender, PropertyChangedEventArgs e)
            {
                VM_RaceGrouping updatedMasterRaceGrouping = (VM_RaceGrouping)sender;
                var updatedSelection = new RaceGroupingSelection(updatedMasterRaceGrouping);
                this.Label = updatedMasterRaceGrouping.RaceGrouping.Label;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
