using System.Collections.ObjectModel;
using Noggog.WPF;

namespace SynthEBD;

public class VM_BodyShapeDescriptor : ViewModel
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

    public static VM_BodyShapeDescriptor GetViewModelFromModel(BodyShapeDescriptor model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, IHasDescriptorRules parentDescriptorConfig)
    {
        VM_BodyShapeDescriptor viewModel = new VM_BodyShapeDescriptor(new VM_BodyShapeDescriptorShell(new ObservableCollection<VM_BodyShapeDescriptorShell>(), raceGroupingVMs, parentConfig), raceGroupingVMs, parentConfig);
        viewModel.Value = model.Value;
        viewModel.Signature = model.Category + ": " + model.Value;

        var descriptorRules = parentDescriptorConfig.DescriptorRules.Where(x => x.DescriptorSignature == viewModel.Signature).FirstOrDefault();
        if (descriptorRules != null)
        {
            viewModel.AssociatedRules = VM_BodyShapeDescriptorRules.GetViewModelFromModel(descriptorRules, viewModel, parentConfig, raceGroupingVMs);
        }
        else
        {
            viewModel.AssociatedRules = new VM_BodyShapeDescriptorRules(viewModel, raceGroupingVMs, parentConfig);
        }

        return viewModel;
    }

    public static BodyShapeDescriptor DumpViewModeltoModel(VM_BodyShapeDescriptor viewModel, HashSet<BodyShapeDescriptorRules> descriptorRules)
    {
        BodyShapeDescriptor model = new BodyShapeDescriptor() { Category = viewModel.ParentShell.Category, Value = viewModel.Value, Signature = viewModel.Signature };
        var matchedDescriptorModel = descriptorRules.Where(x => x.DescriptorSignature == model.Signature).FirstOrDefault();
        if (matchedDescriptorModel is not null)
        {
            descriptorRules.Remove(matchedDescriptorModel); // remove current model and replace with new one
        }
        descriptorRules.Add(VM_BodyShapeDescriptorRules.DumpViewModelToModel(viewModel.AssociatedRules));
        return model;
    }
}