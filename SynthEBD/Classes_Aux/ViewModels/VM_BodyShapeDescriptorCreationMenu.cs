using System.Collections.ObjectModel;
using Noggog.WPF;

namespace SynthEBD;

public class VM_BodyShapeDescriptorCreationMenu : ViewModel
{
    public VM_BodyShapeDescriptorCreationMenu(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig)
    {
        this.CurrentlyDisplayedTemplateDescriptorShell = new VM_BodyShapeDescriptorShell(new ObservableCollection<VM_BodyShapeDescriptorShell>(), raceGroupingVMs, parentConfig);

        AddTemplateDescriptorShell = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.TemplateDescriptors.Add(new VM_BodyShapeDescriptorShell(this.TemplateDescriptors, raceGroupingVMs, parentConfig))
        );

        RemoveTemplateDescriptorShell = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => this.TemplateDescriptors.Remove((VM_BodyShapeDescriptorShell)x)
        );
    }

    public ObservableCollection<VM_BodyShapeDescriptorShell> TemplateDescriptors { get; set; } = new();
    public ObservableCollection<VM_BodyShapeDescriptor> TemplateDescriptorList { get; set; } = new(); // hidden flattened list of TemplateDescriptors for presentation to VM_Subgroup and VM_BodyGenTemplate. Needs to be synced with TemplateDescriptors on update.

    public VM_BodyShapeDescriptorShell CurrentlyDisplayedTemplateDescriptorShell { get; set; }

    public RelayCommand AddTemplateDescriptorShell { get; }
    public RelayCommand RemoveTemplateDescriptorShell { get; }
}