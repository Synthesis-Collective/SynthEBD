using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_BodyShapeDescriptorCreationMenu : VM
{
    public delegate VM_BodyShapeDescriptorCreationMenu Factory(IHasAttributeGroupMenu parentConfig);
    
    public VM_BodyShapeDescriptorCreationMenu(IHasAttributeGroupMenu parentConfig, VM_Settings_General generalSettings, VM_BodyShapeDescriptor.VM_BodyShapeDescriptorCreator descriptorCreator)
    {
        CurrentlyDisplayedTemplateDescriptorShell = descriptorCreator.CreateNewShell(new ObservableCollection<VM_BodyShapeDescriptorShell>(), generalSettings.RaceGroupings, parentConfig);

        AddTemplateDescriptorShell = new RelayCommand(
            canExecute: _ => true,
            execute: _ => TemplateDescriptors.Add(descriptorCreator.CreateNewShell(TemplateDescriptors, generalSettings.RaceGroupings, parentConfig))
        );

        RemoveTemplateDescriptorShell = new RelayCommand(
            canExecute: _ => true,
            execute: x => TemplateDescriptors.Remove((VM_BodyShapeDescriptorShell)x)
        );
    }

    public ObservableCollection<VM_BodyShapeDescriptorShell> TemplateDescriptors { get; set; } = new();
    public ObservableCollection<VM_BodyShapeDescriptor> TemplateDescriptorList { get; set; } = new(); // hidden flattened list of TemplateDescriptors for presentation to VM_Subgroup and VM_BodyGenTemplate. Needs to be synced with TemplateDescriptors on update.

    public VM_BodyShapeDescriptorShell CurrentlyDisplayedTemplateDescriptorShell { get; set; }

    public RelayCommand AddTemplateDescriptorShell { get; }
    public RelayCommand RemoveTemplateDescriptorShell { get; }
}