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
    public delegate VM_AttributeWeightModifier Factory(ObservableCollection<VM_AttributeWeightModifier> parentCollection, ObservableCollection<VM_AttributeGroup> attributeGroups);

    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly ObservableCollection<VM_AttributeGroup> _attributeGroups;

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

    private VM_NPCAttribute CreateHostedBox()
    {
        // displayForceIfOption=false hides the ForceIf/ForceMode UI (a modifier is a plain match test).
        var box = _attributeCreator.CreateNewFromUI(AttributeContainer, false, null, _attributeGroups);
        box.DisplayORButton = false;
        box.DisplayRemoveButton = false;
        return box;
    }

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

    public AttributeWeightModifier DumpViewModelToModel()
    {
        var attribute = AttributeContainer.FirstOrDefault()?.DumpViewModelToModel() ?? new NPCAttribute();
        return new AttributeWeightModifier { Attribute = attribute, Factor = Factor };
    }
}
