using Noggog;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Linq;

namespace SynthEBD;

/// <summary>View model for a single body-shape descriptor value (Category from the parent shell + Value), its associated rules, and a remove command.</summary>
[DebuggerDisplay("{Value}")]
public class VM_BodyShapeDescriptor : VM, IHasValueString
{
    private VM_BodyShapeDescriptorRules.Factory _rulesFactory;
    /// <summary>Autofac factory delegate for constructing a descriptor value under a shell.</summary>
    public delegate VM_BodyShapeDescriptor Factory(VM_BodyShapeDescriptorShell parentShell, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, Action<(string, string), (string, string)> responseToChange, Action<string> ResponseToDeletion);
    /// <summary>Creates the descriptor VM, building its rules and wiring the remove command and a debounced change notification.</summary>
    /// <param name="parentShell">The owning category shell.</param>
    /// <param name="raceGroupingVMs">Race groupings available to the rules.</param>
    /// <param name="parentConfig">The owning config (attribute-group menu).</param>
    /// <param name="rulesFactory">Factory for the rules VM.</param>
    /// <param name="responseToChange">Callback invoked (debounced) when this descriptor's (category, value) changes.</param>
    /// <param name="ResponseToDeletion">Callback invoked when this descriptor is removed.</param>
    public VM_BodyShapeDescriptor(VM_BodyShapeDescriptorShell parentShell, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, VM_BodyShapeDescriptorRules.Factory rulesFactory, Action<(string, string), (string, string)> responseToChange, Action<string> ResponseToDeletion)
    {
        _rulesFactory = rulesFactory;
        ParentShell = parentShell;
        AssociatedRules = _rulesFactory(this, raceGroupingVMs, parentConfig);

        RemoveDescriptorValue = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                ParentShell.Descriptors.Remove(this);
                ResponseToDeletion(Signature);
            }
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

    /// <summary>Factory helper that builds descriptor-value and category-shell VMs by wrapping their Autofac factories.</summary>
    public class VM_BodyShapeDescriptorCreator
    {
        private readonly VM_BodyShapeDescriptor.Factory _descriptorFactory;
        private readonly VM_BodyShapeDescriptorShell.Factory _shellFactory;
        private readonly VM_BodyShapeDescriptorRules.Factory _rulesFactory;

        /// <summary>Captures the descriptor/shell/rules factories.</summary>
        public VM_BodyShapeDescriptorCreator(Factory factory, VM_BodyShapeDescriptorShell.Factory shellFactory, VM_BodyShapeDescriptorRules.Factory rulesFactory)
        {
            _descriptorFactory = factory;
            _shellFactory = shellFactory;
            _rulesFactory = rulesFactory;
        }
        /// <summary>Creates a new descriptor-value VM under a shell.</summary>
        public VM_BodyShapeDescriptor CreateNew(VM_BodyShapeDescriptorShell parentShell, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, Action<(string, string), (string, string)> responseToChange, Action<string> responseToDeletion)
        {
            return _descriptorFactory(parentShell, raceGroupingVMs, parentConfig, responseToChange, responseToDeletion);
        }
        /// <summary>Creates a new category-shell VM under a collection.</summary>
        public VM_BodyShapeDescriptorShell CreateNewShell(ObservableCollection<VM_BodyShapeDescriptorShell> parentCollection, ObservableCollection<VM_RaceGrouping> raceGroupings, IHasAttributeGroupMenu parentConfig, Action<(string, string), (string, string)> responseToChange, Action<string> responseToDeletion)
        {
            return _shellFactory(parentCollection, raceGroupings, parentConfig, responseToChange, responseToDeletion);
        }
    }

    /// <summary>Populates this VM (value, description, rules) from a <see cref="BodyShapeDescriptor"/> model.</summary>
    /// <param name="model">The model to load.</param>
    /// <param name="raceGroupingVMs">Race groupings available to the rebuilt rules.</param>
    /// <param name="parentConfig">The owning config (attribute-group menu).</param>
    public void CopyInViewModelFromModel(BodyShapeDescriptor model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig)
    {
        Value = model.ID.Value;
        ValueDescription = model.ValueDescription;
        AssociatedRules = _rulesFactory(this, raceGroupingVMs, parentConfig);
        AssociatedRules.CopyInViewModelFromModel(model.AssociatedRules, raceGroupingVMs);
    }

    /// <summary>Projects this VM back into a <see cref="BodyShapeDescriptor"/> model (Category comes from the parent shell; CategoryDescription now lives on the shell).</summary>
    /// <returns>The populated model.</returns>
    public BodyShapeDescriptor DumpViewModelToModel()
    {
        // CategoryDescription is no longer written here — it lives on the owning
        // BodyShapeDescriptorShell now. The shell-aware dump in
        // VM_BodyShapeDescriptorCreationMenu.DumpToViewModels emits one CategoryDescription
        // per category instead of duplicating it across every value.
        BodyShapeDescriptor model = new BodyShapeDescriptor();
        model.ID = new() { Category = ParentShell.Category, Value = Value };
        model.ValueDescription = ValueDescription;
        model.AssociatedRules = AssociatedRules.DumpViewModelToModel();
        return model;
    }

    /// <summary>Whether this descriptor's Category+Value matches the given descriptor.</summary>
    public bool MapsTo(BodyShapeDescriptor descriptor)
    {
        return MapsTo(descriptor.ID);
    }
    /// <summary>Whether this descriptor's Category+Value matches the given label signature.</summary>
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

/// <summary>Implemented by view models that expose an editable Value string.</summary>
public interface IHasValueString
{
    /// <summary>The value string.</summary>
    public string Value { get; set; }
}