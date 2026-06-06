using DynamicData.Binding;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ReactiveUI;
using Noggog;

namespace SynthEBD;

/// <summary>
/// View model for a checkbox list that lets the user select among a master list of race groupings,
/// keeping a comma-joined header caption of the selected labels in sync.
/// </summary>
public class VM_RaceGroupingCheckboxList : VM
{
    /// <summary>Creates the list from a master grouping collection, building one selection row per grouping and refreshing when the master list changes.</summary>
    /// <param name="RaceGroupingVMs">The master collection of race groupings to offer.</param>
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
    /// <summary>Rebuilds the selection rows to match the master list, preserving existing selections by label.</summary>
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

    /// <summary>Creates a copy of this checkbox list with the same selection states.</summary>
    /// <returns>The cloned list.</returns>
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

    /// <summary>Recomputes <see cref="HeaderCaption"/> as the comma-joined labels of the selected groupings.</summary>
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

    /// <summary>Checks the selection rows whose grouping labels appear in the given set.</summary>
    /// <param name="groupingStrings">Labels of groupings to select.</param>
    /// <param name="allRaceGroupings">Available groupings (unused beyond label matching).</param>
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

    /// <summary>One selectable row: a race grouping plus its checked state, kept in sync with the parent's header caption.</summary>
    public class RaceGroupingSelection : VM
    {
        /// <summary>Creates the row and updates the parent caption whenever its selection changes.</summary>
        /// <param name="raceGroupingVM">The grouping this row represents.</param>
        /// <param name="parent">The owning checkbox list.</param>
        public RaceGroupingSelection(VM_RaceGrouping raceGroupingVM, VM_RaceGroupingCheckboxList parent)
        {
            SubscribedMasterRaceGrouping = raceGroupingVM;
            ParentCheckList = parent;
            this.WhenAnyValue(x => x.IsSelected).Subscribe(x => ParentCheckList.BuildHeaderCaption()).DisposeWith(this);
        }
        public bool IsSelected { get; set; } = false;

        public VM_RaceGrouping SubscribedMasterRaceGrouping { get; set; } // to fire the PropertyChanged event

        public VM_RaceGroupingCheckboxList ParentCheckList { get; set; }

        /// <summary>Creates a copy of this row with the same selection state.</summary>
        /// <returns>The cloned row.</returns>
        public RaceGroupingSelection Clone()
        {
            RaceGroupingSelection clone = new RaceGroupingSelection(SubscribedMasterRaceGrouping, ParentCheckList);
            clone.IsSelected = IsSelected;
            return clone;
        }
    }

    /// <summary>Selects the rows corresponding to the given groupings.</summary>
    /// <param name="groupings">The groupings to mark selected.</param>
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