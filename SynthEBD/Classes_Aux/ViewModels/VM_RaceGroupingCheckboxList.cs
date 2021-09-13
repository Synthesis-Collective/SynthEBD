using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
                this.RaceGroupingSelections.Add(new RaceGroupingSelection(rgvm.RaceGrouping));
            }
        }

        public ObservableCollection<RaceGroupingSelection> RaceGroupingSelections { get; set; }
        
        public class RaceGroupingSelection : INotifyPropertyChanged
        {
            public RaceGroupingSelection(RaceGrouping raceGrouping)
            {
                this.Label = raceGrouping.Label;
                this.IsSelected = false;
            }
            public string Label { get; set; }
            public bool IsSelected { get; set; }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
