using Noggog;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Linq;

namespace SynthEBD;

[DebuggerDisplay("{Value}")]
public class VM_BodyShapeDescriptor : VM, IHasValueString
{
    private VM_BodyShapeDescriptorRules.Factory _rulesFactory;
    public delegate VM_BodyShapeDescriptor Factory(VM_BodyShapeDescriptorShell parentShell, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, Action<(string, string), (string, string)> responseToChange);
    public VM_BodyShapeDescriptor(VM_BodyShapeDescriptorShell parentShell, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, VM_BodyShapeDescriptorRules.Factory rulesFactory, Action<(string, string), (string, string)> responseToChange)
    {
        _rulesFactory = rulesFactory;
        ParentShell = parentShell;
        AssociatedRules = _rulesFactory(this, raceGroupingVMs, parentConfig);

        RemoveDescriptorValue = new RelayCommand(
            canExecute: _ => true,
            execute: _ => ParentShell.Descriptors.Remove(this)
        );

        this.WhenAnyValue(x => x.ParentShell.Category, x => x.Value)
            .Throttle(TimeSpan.FromMilliseconds(500), RxApp.MainThreadScheduler)
            .Buffer(2, 1)
             .Select(b => (Previous: b[0], Current: b[1]))
             .Subscribe(t =>
             {
                 responseToChange(t.Previous, t.Current);
             }).DisposeWith(this);
    }
    public string Value { get; set; } = "";
    public string ValueDescription { get; set; } = "";
    public string Signature => BodyShapeDescriptor.LabelSignature.ToSignatureString(ParentShell.Category, Value);
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
        public VM_BodyShapeDescriptor CreateNew(VM_BodyShapeDescriptorShell parentShell, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, Action<(string, string), (string, string)> responseToChange)
        {
            return _descriptorFactory(parentShell, raceGroupingVMs, parentConfig, responseToChange);
        }
        public VM_BodyShapeDescriptorShell CreateNewShell(ObservableCollection<VM_BodyShapeDescriptorShell> parentCollection, ObservableCollection<VM_RaceGrouping> raceGroupings, IHasAttributeGroupMenu parentConfig, Action<(string, string), (string, string)> responseToChange)
        {
            return _shellFactory(parentCollection, raceGroupings, parentConfig, responseToChange);
        }
    }

    public void CopyInViewModelFromModel(BodyShapeDescriptor model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig)
    {
        Value = model.ID.Value;
        ValueDescription = model.ValueDescription;
        AssociatedRules = _rulesFactory(this, raceGroupingVMs, parentConfig);
        AssociatedRules.CopyInViewModelFromModel(model.AssociatedRules, raceGroupingVMs);
    }

    public BodyShapeDescriptor DumpViewModeltoModel()
    {
        BodyShapeDescriptor model = new BodyShapeDescriptor(); 
        model.ID = new() { Category = ParentShell.Category, Value = Value };
        model.ValueDescription = ValueDescription;
        model.CategoryDescription = ParentShell.CategoryDescription;
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

public interface IHasValueString
{
    public string Value { get; set; }
}