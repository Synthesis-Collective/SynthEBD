using System.Collections.ObjectModel;
using System.Linq;
using ReactiveUI;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

/// <summary>
/// View model for a single Probability Modifier row: one NPC attribute condition paired with a
/// multiplicative <see cref="Factor"/>. The attribute is hosted in a one-element collection so the
/// existing <c>UC_NPCAttribute</c> control renders it unchanged; the box's per-condition "OR" and
/// "Remove Attribute" buttons are hidden because the row itself owns add/remove. Round-trips to/from
/// <see cref="AttributeWeightModifier"/>.
/// </summary>
public class VM_AttributeWeightModifier : VM
{
    /// <summary>Autofac factory delegate for constructing a modifier row within a parent collection.</summary>
    public delegate VM_AttributeWeightModifier Factory(ObservableCollection<VM_AttributeWeightModifier> parentCollection, ObservableCollection<VM_AttributeGroup> attributeGroups);

    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly ObservableCollection<VM_AttributeGroup> _attributeGroups;

    /// <summary>Creates the modifier row, hosting a single attribute box and wiring the delete command.</summary>
    /// <param name="parentCollection">The collection of modifier rows this belongs to.</param>
    /// <param name="attributeGroups">Attribute groups available to the hosted attribute box.</param>
    /// <param name="attributeCreator">Factory for the hosted <see cref="VM_NPCAttribute"/> box.</param>
    public VM_AttributeWeightModifier(ObservableCollection<VM_AttributeWeightModifier> parentCollection, ObservableCollection<VM_AttributeGroup> attributeGroups, VM_NPCAttributeCreator attributeCreator)
    {
        _attributeCreator = attributeCreator;
        _attributeGroups = attributeGroups;
        ParentCollection = parentCollection;

        AttributeContainer.Add(CreateHostedBox());

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => parentCollection.Remove(this)
        );
    }

    public ObservableCollection<VM_NPCAttribute> AttributeContainer { get; } = new(); // hosts exactly one box so UC_NPCAttribute renders unchanged
    public double Factor { get; set; } = 1.0;
    public ObservableCollection<VM_AttributeWeightModifier> ParentCollection { get; }
    public RelayCommand DeleteCommand { get; }

    /// <summary>Creates the single hosted attribute box with the ForceIf/OR/Remove UI suppressed (a modifier is a plain match test).</summary>
    /// <returns>The configured attribute box.</returns>
    private VM_NPCAttribute CreateHostedBox()
    {
        // displayForceIfOption=false hides the ForceIf/ForceMode UI (a modifier is a plain match test).
        var box = _attributeCreator.CreateNewFromUI(AttributeContainer, false, null, _attributeGroups);
        box.DisplayORButton = false;
        box.DisplayRemoveButton = false;
        return box;
    }

    /// <summary>Builds a modifier row VM from a persisted <see cref="AttributeWeightModifier"/>, populating the hosted attribute box and factor.</summary>
    /// <param name="model">The model to project.</param>
    /// <param name="parentCollection">The collection the new row belongs to.</param>
    /// <param name="attributeGroups">Attribute groups available to the hosted box.</param>
    /// <param name="factory">Factory used to construct the row.</param>
    /// <param name="creator">Factory used to populate the hosted attribute box from the model.</param>
    /// <returns>The populated row view model.</returns>
    public static VM_AttributeWeightModifier GetViewModelFromModel(AttributeWeightModifier model, ObservableCollection<VM_AttributeWeightModifier> parentCollection, ObservableCollection<VM_AttributeGroup> attributeGroups, Factory factory, VM_NPCAttributeCreator creator)
    {
        var vm = factory(parentCollection, attributeGroups);
        vm.AttributeContainer.Clear(); // drop the empty starter box created in the ctor
        creator.CopyInFromModels(new HashSet<NPCAttribute> { model.Attribute }, vm.AttributeContainer, attributeGroups, false, null);
        foreach (var box in vm.AttributeContainer)
        {
            box.DisplayORButton = false;
            box.DisplayRemoveButton = false;
        }
        vm.Factor = model.Factor;
        return vm;
    }

    /// <summary>Projects this row back into an <see cref="AttributeWeightModifier"/> model (its hosted attribute plus the factor).</summary>
    /// <returns>The populated model.</returns>
    public AttributeWeightModifier DumpViewModelToModel()
    {
        var attribute = AttributeContainer.FirstOrDefault()?.DumpViewModelToModel() ?? new NPCAttribute();
        return new AttributeWeightModifier { Attribute = attribute, Factor = Factor };
    }
}
