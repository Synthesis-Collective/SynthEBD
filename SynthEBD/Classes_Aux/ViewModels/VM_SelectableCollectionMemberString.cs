using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using Noggog;

namespace SynthEBD;

public class VM_SelectableCollectionMemberString : VM
{
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

    public void TriggerParentMemberChanged()
    {
        Parent.CollectionMemberChangedAction();
    }
}

public interface ICollectionParent
{
    ObservableCollection<VM_SelectableCollectionMemberString> CollectionMemberStrings { get; set; }
    string Header { get; set; }
    void CollectionMemberChangedAction();
}

public class VM_CollectionMemberStringCheckboxList : VM, ICollectionParent
{
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