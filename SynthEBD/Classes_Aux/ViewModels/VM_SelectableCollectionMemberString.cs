using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using Noggog;

namespace SynthEBD;

/// <summary>View model wrapping a <see cref="VM_CollectionMemberString"/> with a checkbox selection state, notifying its parent on content/selection changes.</summary>
public class VM_SelectableCollectionMemberString : VM
{
    /// <summary>Creates the row, wiring the delete command and parent notifications on content/selection change.</summary>
    /// <param name="subscribedString">The wrapped string VM.</param>
    /// <param name="parentCollection">The owning collection parent.</param>
    public VM_SelectableCollectionMemberString(VM_CollectionMemberString subscribedString, ICollectionParent parentCollection)
    {
        SubscribedString = subscribedString;
        Parent = parentCollection;
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => this.Parent.CollectionMemberStrings.Remove(this));

        this.WhenAnyValue(x => x.SubscribedString.Content).Skip(1).Subscribe(x => TriggerParentMemberChanged()).DisposeWith(this);
        this.WhenAnyValue(x => x.IsSelected).Skip(1).Subscribe(x => TriggerParentMemberChanged()).DisposeWith(this);
    }
    public VM_CollectionMemberString SubscribedString { get; }
    public ICollectionParent Parent { get; }
    public bool IsSelected { get; set; } = false;
    public RelayCommand DeleteCommand { get; }

    /// <summary>Notifies the parent collection that a member's content or selection changed.</summary>
    public void TriggerParentMemberChanged()
    {
        Parent.CollectionMemberChangedAction();
    }
}

/// <summary>Implemented by view models that host a selectable-string collection and react to member changes.</summary>
public interface ICollectionParent
{
    /// <summary>The selectable string rows.</summary>
    ObservableCollection<VM_SelectableCollectionMemberString> CollectionMemberStrings { get; set; }
    /// <summary>Comma-joined caption of the selected members.</summary>
    string Header { get; set; }
    /// <summary>Invoked when any member's content or selection changes.</summary>
    void CollectionMemberChangedAction();
}

/// <summary>View model for a checkbox list of strings synced to a master collection, maintaining a comma-joined header of the selected items.</summary>
public class VM_CollectionMemberStringCheckboxList : VM, ICollectionParent
{
    /// <summary>Creates the list from a master string collection, building a selectable row per member and refreshing on master-list changes.</summary>
    /// <param name="actualMasterList">The master string collection to mirror.</param>
    public VM_CollectionMemberStringCheckboxList(ObservableCollection<VM_CollectionMemberString> actualMasterList)
    {
        SubscribedMasterList = actualMasterList;

        foreach (var cms in SubscribedMasterList)
        {
            CollectionMemberStrings.Add(new VM_SelectableCollectionMemberString(cms, this));
        }

        SubscribedMasterList.ToObservableChangeSet()
            .QueryWhenChanged(currentList => currentList)
            .Subscribe(x => CollectionMemberChangedAction()).DisposeWith(this);
    }
    public ObservableCollection<VM_SelectableCollectionMemberString> CollectionMemberStrings { get; set; } = new();
    public string Header { get; set; }
    public ObservableCollection<VM_CollectionMemberString> SubscribedMasterList { get; } = new(); // to fire the CollectionChanged event

    /// <summary>Adds rows for newly-added master items and removes rows for removed ones.</summary>
    /// <param name="masterList">The current master string collection.</param>
    void RefreshCheckList(ObservableCollection<VM_CollectionMemberString> masterList)
    {
        if (masterList == null) { return; }

        //if a new item has been added to the subscribed list, add it to the checklist
        foreach (var item in masterList)
        {
            if (!CollectionMemberStrings.Where(x => x.SubscribedString == item).Any())
            {
                var newItem = new VM_SelectableCollectionMemberString(item, this);
                CollectionMemberStrings.Add(newItem);
            }
        }

        // if an existing item has been removed from the subscribed list, remove it from the checklist
        for (int i = 0; i < CollectionMemberStrings.Count; i++)
        {
            var item = CollectionMemberStrings[i];
            if (!masterList.Where(x => item.SubscribedString == x).Any())
            {
                CollectionMemberStrings.RemoveAt(i);
                i--;
            }
        }
    }

    /// <summary>Refreshes the rows against the master list and rebuilds the comma-joined selected-items header.</summary>
    public void CollectionMemberChangedAction()
    {
        RefreshCheckList(SubscribedMasterList);

        string header = "";
        foreach (var selection in CollectionMemberStrings)
        {
            if (selection.IsSelected)
            {
                header += selection.SubscribedString.Content + ", ";
            }
        }

        if (header != "")
        {
            header = header.Remove(header.Length - 2, 2);
        }
        Header = header;
    }

    /// <summary>Sets each row's selection based on whether its content is in the given set.</summary>
    /// <param name="selectedStrings">Contents that should be checked.</param>
    public void InitializeFromHashSet(HashSet<string> selectedStrings)
    {
        foreach (var cms in CollectionMemberStrings)
        {
            if (selectedStrings.Contains(cms.SubscribedString.Content))
            {
                cms.IsSelected = true;
            }
            else
            {
                cms.IsSelected = false;
            }    
        }
    }
}

/// <summary>Minimal selectable string row (content + selection + delete) for simple list scenarios.</summary>
public class VM_SimpleSelectableCollectionMemberString : VM
{
    /// <summary>Creates the row with a delete command.</summary>
    /// <param name="content">The string value.</param>
    /// <param name="parentCollection">The collection this row belongs to.</param>
    public VM_SimpleSelectableCollectionMemberString(string content, ObservableCollection<VM_SimpleSelectableCollectionMemberString> parentCollection)
    {
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));

        Content = content;
    }
    public string Content { get; set; }
    public ObservableCollection<VM_SimpleSelectableCollectionMemberString> parentCollection { get; }
    public bool IsSelected { get; set; } = false;
    public RelayCommand DeleteCommand { get; }
}