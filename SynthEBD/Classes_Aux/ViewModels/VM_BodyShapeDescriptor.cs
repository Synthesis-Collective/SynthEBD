using ReactiveUI;
using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_BodyShapeDescriptor : VM
{
    public VM_BodyShapeDescriptor(VM_BodyShapeDescriptorShell parentShell, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig)
    {
        this.ParentShell = parentShell;
        AssociatedRules = new VM_BodyShapeDescriptorRules(this, raceGroupingVMs, parentConfig);

        RemoveDescriptorValue = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.ParentShell.Descriptors.Remove(this)
        );
    }
    public string Value { get; set; } = "";
    public string Signature { get; set; } = "";
    public VM_BodyShapeDescriptorRules AssociatedRules { get; set; }

    public VM_BodyShapeDescriptorShell ParentShell { get; set; }

    public RelayCommand RemoveDescriptorValue { get; }

    public void CopyInViewModelFromModel(BodyShapeDescriptor model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig)
    {
        Value = model.Signature.Value;
        Signature = model.Signature.Category + ": " + model.Signature.Value;
        AssociatedRules = new VM_BodyShapeDescriptorRules(this, raceGroupingVMs, parentConfig);
        AssociatedRules.CopyInViewModelFromModel(model.AssociatedRules, raceGroupingVMs);
    }

    public static BodyShapeDescriptor DumpViewModeltoModel(VM_BodyShapeDescriptor viewModel)
    {
        BodyShapeDescriptor model = new BodyShapeDescriptor(); 
        model.Signature = new(){ Category = viewModel.ParentShell.Category, Value = viewModel.Value };
        model.AssociatedRules = (VM_BodyShapeDescriptorRules.DumpViewModelToModel(viewModel.AssociatedRules));
        return model;
    }

    public bool MapsTo(BodyShapeDescriptor descriptor)
    {
        return MapsTo(descriptor.Signature);    
    }
    public bool MapsTo(BodyShapeDescriptor.LabelSignature descriptor)
    {
        if (ParentShell.Category == descriptor.Category && Value == descriptor.Value)
        {
            return true;
        }
        else
        {
            return false;
        }
    }
}