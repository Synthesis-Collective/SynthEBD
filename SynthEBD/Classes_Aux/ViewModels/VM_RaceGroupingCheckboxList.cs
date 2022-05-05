using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SynthEBD;

public class VM_RaceGroupingCheckboxList : INotifyPropertyChanged
{
    public VM_RaceGroupingCheckboxList(ObservableCollection<VM_RaceGrouping> RaceGroupingVMs)
    {
        this.RaceGroupingSelections = new ObservableCollection<RaceGroupingSelection>();
        foreach (var rgvm in RaceGroupingVMs)
        {
            this.RaceGroupingSelections.Add(new RaceGroupingSelection(rgvm, this));
        }

        this.HeaderCaption = BuildHeaderCaption(this);

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
    public string HeaderCaption { get; set; }

    public VM_RaceGroupingCheckboxList Clone()
    {
        var clone = new VM_RaceGroupingCheckboxList(SubscribedMasterList);
        clone.RaceGroupingSelections.Clear();
        foreach (var entry in RaceGroupingSelections)
        {
            clone.RaceGroupingSelections.Add(entry.Clone());
        }
        return clone;
    }

    public static string BuildHeaderCaption(VM_RaceGroupingCheckboxList checkList)
    {
        string header = "";
        foreach (var selection in checkList.RaceGroupingSelections)
        {
            if (selection.IsSelected)
            {
                header += selection.Label + ", ";
            }
        }

        if (header != "")
        {
            header = header.Remove(header.Length - 2, 2);
        }
        return header;
    }

    public static VM_RaceGroupingCheckboxList GetRaceGroupingsByLabel(HashSet<string> groupingStrings, ObservableCollection<VM_RaceGrouping> allRaceGroupings)
    {
        VM_RaceGroupingCheckboxList checkBoxList = new VM_RaceGroupingCheckboxList(allRaceGroupings);

        foreach (var raceGroupingSelection in checkBoxList.RaceGroupingSelections) // loop through all available RaceGroupings
        {
            foreach (string s in groupingStrings) // loop through all of the RaceGrouping labels stored in the models
            {
                if (raceGroupingSelection.Label == s)
                {
                    raceGroupingSelection.IsSelected = true;
                    break;
                }
            }
        }

        return checkBoxList;
    }

    public class RaceGroupingSelection : INotifyPropertyChanged
    {
        public RaceGroupingSelection(VM_RaceGrouping raceGroupingVM, VM_RaceGroupingCheckboxList parent)
        {
            this.Label = raceGroupingVM.Label;
            this.IsSelected = false;
            this.SubscribedMasterRaceGrouping = raceGroupingVM;
            this.SubscribedMasterRaceGrouping.PropertyChanged += RefreshRaceGroupingName;

            this.ParentCheckList = parent;
            this.PropertyChanged += RefreshHeaderCaption;
        }
        public string Label { get; set; }
        public bool IsSelected { get; set; }

        public VM_RaceGrouping SubscribedMasterRaceGrouping { get; set; } // to fire the PropertyChanged event
        public event PropertyChangedEventHandler PropertyChanged;
        public void RefreshRaceGroupingName(object sender, PropertyChangedEventArgs e)
        {
            VM_RaceGrouping updatedMasterRaceGrouping = (VM_RaceGrouping)sender;
            var updatedSelection = new RaceGroupingSelection(updatedMasterRaceGrouping, this.ParentCheckList);
            this.Label = updatedMasterRaceGrouping.Label;
        }

        public VM_RaceGroupingCheckboxList ParentCheckList { get; set; }

        public void RefreshHeaderCaption(object sender, PropertyChangedEventArgs e)
        {
            ParentCheckList.HeaderCaption = BuildHeaderCaption(ParentCheckList);
        }

        public RaceGroupingSelection Clone()
        {
            RaceGroupingSelection clone = new RaceGroupingSelection(SubscribedMasterRaceGrouping, ParentCheckList);
            clone.Label = Label;
            clone.IsSelected = IsSelected;
            return clone;
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
}