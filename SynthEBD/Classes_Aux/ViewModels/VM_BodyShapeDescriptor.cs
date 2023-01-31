using ReactiveUI;
using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_BodyShapeDescriptor : VM
{
    private VM_BodyShapeDescriptorRules.Factory _rulesFactory;
    public delegate VM_BodyShapeDescriptor Factory(VM_BodyShapeDescriptorShell parentShell, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig);
    public VM_BodyShapeDescriptor(VM_BodyShapeDescriptorShell parentShell, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, VM_BodyShapeDescriptorRules.Factory rulesFactory)
    {
        _rulesFactory = rulesFactory;
        ParentShell = parentShell;
        AssociatedRules = _rulesFactory(this, raceGroupingVMs, parentConfig);

        RemoveDescriptorValue = new RelayCommand(
            canExecute: _ => true,
            execute: _ => ParentShell.Descriptors.Remove(this)
        );
    }
    public string Value { get; set; } = "";
    public string Signature { get; set; } = "";
    public VM_BodyShapeDescriptorRules AssociatedRules { get; set; }

    public VM_BodyShapeDescriptorShell ParentShell { get; set; }

    public RelayCommand RemoveDescriptorValue { get; }

    public class VM_BodyShapeDescriptorCreator
    {
        private readonly VM_BodyShapeDescriptor.Factory _descriptorFactory;
        private readonly VM_BodyShapeDescriptorShell.Factory _shellFactory;
        private readonly VM_BodyShapeDescriptorRules.Factory _rulesFactory;

        public VM_BodyShapeDescriptorCreator(Factory factory, VM_BodyShapeDescriptorShell.Factory shellFactory, VM_BodyShapeDescriptorRules.Factory rulesFactory)
        {
            _descriptorFactory = factory;
            _shellFactory = shellFactory;
            _rulesFactory = rulesFactory;
        }
        public VM_BodyShapeDescriptor CreateNew(VM_BodyShapeDescriptorShell parentShell, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig)
        {
            return _descriptorFactory(parentShell, raceGroupingVMs, parentConfig);
        }
        public VM_BodyShapeDescriptorShell CreateNewShell(ObservableCollection<VM_BodyShapeDescriptorShell> parentCollection, ObservableCollection<VM_RaceGrouping> raceGroupings, IHasAttributeGroupMenu parentConfig)
        {
            return _shellFactory(parentCollection, raceGroupings, parentConfig);
        }
    }

    public void CopyInViewModelFromModel(BodyShapeDescriptor model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig)
    {
        Value = model.ID.Value;
        AssociatedRules = _rulesFactory(this, raceGroupingVMs, parentConfig);
        AssociatedRules.CopyInViewModelFromModel(model.AssociatedRules, raceGroupingVMs);
    }

    public BodyShapeDescriptor DumpViewModeltoModel()
    {
        BodyShapeDescriptor model = new BodyShapeDescriptor(); 
        model.ID = new() { Category = ParentShell.Category, Value = Value };
        model.AssociatedRules = AssociatedRules.DumpViewModelToModel();
        return model;
    }

    public bool MapsTo(BodyShapeDescriptor descriptor)
    {
        return MapsTo(descriptor.ID);    
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