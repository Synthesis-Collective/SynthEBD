using System.Collections.ObjectModel;
using ReactiveUI;

namespace SynthEBD;

/// <summary>View model wrapping a single editable string in an observable collection, with a delete command.</summary>
public class VM_CollectionMemberString : VM
{
    /// <summary>Creates the row VM for <paramref name="content"/> with a command to remove it from <paramref name="parentCollection"/>.</summary>
    /// <param name="content">The string value.</param>
    /// <param name="parentCollection">The collection this row belongs to.</param>
    public VM_CollectionMemberString(string content, ObservableCollection<VM_CollectionMemberString> parentCollection)
    {
        this.Content = content;
        this.ParentCollection = parentCollection;
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
    }
    public string Content { get; set; }
    public ObservableCollection<VM_CollectionMemberString> ParentCollection { get; set; }
    public RelayCommand DeleteCommand { get; }

    /// <summary>Builds a new observable collection of row VMs from a source string collection.</summary>
    /// <param name="source">Source strings.</param>
    /// <returns>A new collection of row view models.</returns>
    public static ObservableCollection<VM_CollectionMemberString> InitializeObservableCollectionFromICollection(ICollection<string> source)
    {
        ObservableCollection<VM_CollectionMemberString> parentCollection = new ObservableCollection<VM_CollectionMemberString>();
        foreach (string s in source)
        {
            var newCMS = new VM_CollectionMemberString(s, parentCollection);
            parentCollection.Add(newCMS);
        }
        return parentCollection;
    }

    /// <summary>Appends row VMs for each source string into an existing collection.</summary>
    /// <param name="source">Source strings.</param>
    /// <param name="parentCollection">Collection to append into.</param>
    public static void CopyInObservableCollectionFromICollection(ICollection<string> source, ObservableCollection<VM_CollectionMemberString> parentCollection)
    {
        foreach (string s in source)
        {
            var newCMS = new VM_CollectionMemberString(s, parentCollection);
            parentCollection.Add(newCMS);
        }
    }
}