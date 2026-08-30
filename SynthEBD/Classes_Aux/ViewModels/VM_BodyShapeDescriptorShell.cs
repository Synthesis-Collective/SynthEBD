using Noggog;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Linq;
using static SynthEBD.VM_BodyShapeDescriptor;

namespace SynthEBD;

/// <summary>
/// View model for one body-shape descriptor category "shell" — groups the <see cref="VM_BodyShapeDescriptor"/>
/// value rows under a <see cref="Category"/>, with a command to add a new value.
/// </summary>
[DebuggerDisplay("{Category}: {Descriptors.Count} Values")]
public class VM_BodyShapeDescriptorShell : VM
{
    private VM_BodyShapeDescriptorCreator _creator;
    /// <summary>Autofac factory delegate for constructing a shell within a parent collection.</summary>
    public delegate VM_BodyShapeDescriptorShell Factory(ObservableCollection<VM_BodyShapeDescriptorShell> parentCollection, ObservableCollection<VM_RaceGrouping> raceGroupings, IHasAttributeGroupMenu parentConfig, Action<(string, string), (string, string)> responseToChange, Action<string> responseToDeletion);
    /// <summary>Creates the shell view model and wires the "add descriptor value" command.</summary>
    /// <param name="parentCollection">The collection of shells this belongs to.</param>
    /// <param name="raceGroupings">Race groupings available to descriptor rules.</param>
    /// <param name="parentConfig">The owning config exposing the attribute-group menu.</param>
    /// <param name="creator">Factory for creating descriptor value VMs.</param>
    /// <param name="responseToChange">Callback invoked when a descriptor's (category, value) changes.</param>
    /// <param name="responseToDeletion">Callback invoked when a descriptor is deleted.</param>
    public VM_BodyShapeDescriptorShell(ObservableCollection<VM_BodyShapeDescriptorShell> parentCollection, ObservableCollection<VM_RaceGrouping> raceGroupings, IHasAttributeGroupMenu parentConfig, VM_BodyShapeDescriptorCreator creator, Action<(string, string), (string, string)> responseToChange, Action<string> responseToDeletion)
    {
        _creator = creator;

        ParentCollection = parentCollection;

        AddTemplateDescriptorValue = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Descriptors.Add(_creator.CreateNew(this, raceGroupings, parentConfig, responseToChange, responseToDeletion))
        );
    }

    public string Category { get; set; } = "";
    public string CategoryDescription { get; set; } = "";

    /// <summary>Mirrors <see cref="BodyShapeDescriptorShell.IsRulesOnly"/>: keep this category in the
    /// Body Type Profile editor (Rules tree + DescriptorRef pickers) but hide it from the
    /// distribution-facing descriptor pickers used by asset-pack subgroups and BodyGen templates.</summary>
    public bool IsRulesOnly { get; set; } = false;

    public ObservableCollection<VM_BodyShapeDescriptor> Descriptors { get; set; } = new();
    public ObservableCollection<VM_BodyShapeDescriptorShell> ParentCollection { get; set; }
    public RelayCommand AddTemplateDescriptorValue { get; }
}