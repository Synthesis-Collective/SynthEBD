using System.Collections.ObjectModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_CollectionMemberString : VM
{
    public VM_CollectionMemberString(string content, ObservableCollection<VM_CollectionMemberString> parentCollection)
    {
        this.Content = content;
        this.ParentCollection = parentCollection;
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
    }
    public string Content { get; set; }
    public ObservableCollection<VM_CollectionMemberString> ParentCollection { get; set; }
    public RelayCommand DeleteCommand { get; }

    public static ObservableCollection<VM_CollectionMemberString> InitializeCollectionFromHashSet(HashSet<string> source)
    {
        ObservableCollection<VM_CollectionMemberString> parentCollection = new ObservableCollection<VM_CollectionMemberString>();
        foreach (string s in source)
        {
            var newCMS = new VM_CollectionMemberString(s, parentCollection);
            parentCollection.Add(newCMS);
        }
        return parentCollection;
    }
}