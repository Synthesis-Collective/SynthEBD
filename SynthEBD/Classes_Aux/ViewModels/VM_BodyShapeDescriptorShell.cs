using System.Collections.ObjectModel;
using static SynthEBD.VM_BodyShapeDescriptor;

namespace SynthEBD;

public class VM_BodyShapeDescriptorShell : VM
{
    private VM_BodyShapeDescriptorCreator _creator;
    public delegate VM_BodyShapeDescriptorShell Factory(ObservableCollection<VM_BodyShapeDescriptorShell> parentCollection, ObservableCollection<VM_RaceGrouping> raceGroupings, IHasAttributeGroupMenu parentConfig);
    public VM_BodyShapeDescriptorShell(ObservableCollection<VM_BodyShapeDescriptorShell> parentCollection, ObservableCollection<VM_RaceGrouping> raceGroupings, IHasAttributeGroupMenu parentConfig, VM_BodyShapeDescriptorCreator creator)
    {
        _creator = creator;

        ParentCollection = parentCollection;

        AddTemplateDescriptorValue = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Descriptors.Add(_creator.CreateNew(this, raceGroupings, parentConfig))
        );
    }

    public string Category { get; set; } = "";
    public ObservableCollection<VM_BodyShapeDescriptor> Descriptors { get; set; } = new();
    public ObservableCollection<VM_BodyShapeDescriptorShell> ParentCollection { get; set; }
    public RelayCommand AddTemplateDescriptorValue { get; }

}