using DynamicData.Binding;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ReactiveUI;
using Noggog;

namespace SynthEBD;

public class VM_RaceGroupingCheckboxList : VM
{
    public VM_RaceGroupingCheckboxList(ObservableCollection<VM_RaceGrouping> RaceGroupingVMs)
    {
        SubscribedMasterList = RaceGroupingVMs;
        
        foreach (var rgvm in SubscribedMasterList)
        {
            RaceGroupingSelections.Add(new RaceGroupingSelection(rgvm, this));
        }

        BuildHeaderCaption();
        SubscribedMasterList.ToObservableChangeSet().Subscribe(x => RefreshCheckList()).DisposeWith(this);
    }

    public ObservableCollection<VM_RaceGrouping> SubscribedMasterList { get; set; } // to fire the CollectionChanged event
    void RefreshCheckList()
    {
        var holdingList = new ObservableCollection<RaceGroupingSelection>(RaceGroupingSelections);

        RaceGroupingSelections.Clear();

        foreach (var masterListing in SubscribedMasterList)
        {
            var existingSelection = holdingList.Where(x => x.SubscribedMasterRaceGrouping.Label == masterListing.Label).FirstOrDefault();
            if (existingSelection is null)
            {
                RaceGroupingSelections.Add(new RaceGroupingSelection(masterListing, this));
            }
            else
            {
                RaceGroupingSelections.Add(existingSelection);
            }
        }
        holdingList.Clear();
        BuildHeaderCaption();
    }

    public ObservableCollection<RaceGroupingSelection> RaceGroupingSelections { get; set; } = new();
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

    public void BuildHeaderCaption()
    {
        List<string> selections = new();
        foreach (var selection in RaceGroupingSelections)
        {
            if (selection.IsSelected)
            {
                selections.Add(selection.SubscribedMasterRaceGrouping.Label);
            }
        }
        HeaderCaption = string.Join(", ", selections);
    }

    public void CopyInRaceGroupingsByLabel(HashSet<string> groupingStrings, ObservableCollection<VM_RaceGrouping> allRaceGroupings)
    {
        foreach (string s in groupingStrings) // loop through all of the RaceGrouping labels stored in the models
        {
            foreach (var raceGroupingSelection in RaceGroupingSelections) // loop through all available RaceGroupings
            {
                if (raceGroupingSelection.SubscribedMasterRaceGrouping.Label == s)
                {
                    raceGroupingSelection.IsSelected = true;
                    break;
                }
            }
        }
    }

    public class RaceGroupingSelection : VM
    {
        public RaceGroupingSelection(VM_RaceGrouping raceGroupingVM, VM_RaceGroupingCheckboxList parent)
        {
            SubscribedMasterRaceGrouping = raceGroupingVM;
            ParentCheckList = parent;
            this.WhenAnyValue(x => x.IsSelected).Subscribe(x => ParentCheckList.BuildHeaderCaption()).DisposeWith(this);
        }
        public bool IsSelected { get; set; } = false;

        public VM_RaceGrouping SubscribedMasterRaceGrouping { get; set; } // to fire the PropertyChanged event

        public VM_RaceGroupingCheckboxList ParentCheckList { get; set; }

        public RaceGroupingSelection Clone()
        {
            RaceGroupingSelection clone = new RaceGroupingSelection(SubscribedMasterRaceGrouping, ParentCheckList);
            clone.IsSelected = IsSelected;
            return clone;
        }
    }

    public void ActivateSelectedRaceGroupings(IEnumerable<VM_RaceGrouping> groupings)
    {
        foreach (var group in groupings)
        {
            var matchedSelection = RaceGroupingSelections.Where(x => x.SubscribedMasterRaceGrouping == group).FirstOrDefault();
            if (matchedSelection != null)
            {
                matchedSelection.IsSelected = true;
            }
        }
    }
}