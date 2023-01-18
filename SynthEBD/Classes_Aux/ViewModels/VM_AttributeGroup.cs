using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using Mutagen.Bethesda.Plugins.Records;
using ReactiveUI;
using static SynthEBD.VM_NPCAttribute;
using Noggog;

namespace SynthEBD;

public class VM_AttributeGroup : VM
{
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly Logger _logger;
    public VM_AttributeGroup(VM_AttributeGroupMenu parent, VM_NPCAttributeCreator attributeCreator, Logger logger)
    {
        ParentMenu = parent;
        _attributeCreator = attributeCreator;
        _logger = logger;

        Remove = new RelayCommand(
            canExecute: _ => true,
            execute: _ => ParentMenu.Groups.Remove(this)
        );

        AddAttribute = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Attributes.Add(_attributeCreator.CreateNewFromUI(Attributes, false, true, parent.Groups))
        );

        SubscribeToCircularReferenceCheck();
    }

    public string Label { get; set; } = "";
    public ObservableCollection<VM_NPCAttribute> Attributes { get; set; } = new();
    public VM_AttributeGroupMenu ParentMenu { get; set; }

    public RelayCommand Remove { get; }
    public RelayCommand AddAttribute { get; }

    private void SubscribeToCircularReferenceCheck()
    {
        Attributes
            .ToObservableChangeSet()
            .Transform(x =>
                x.WhenAnyObservable(y => y.NeedsRefresh)
                .Subscribe(_ => CheckGroupForCircularReferences()))
            .DisposeMany() // Dispose subscriptions related to removed attributes
            .Subscribe()  // Execute my instructions
            .DisposeWith(this);
    }

    public void CopyInViewModelFromModel(AttributeGroup model, VM_AttributeGroupMenu parentMenu)
    {
        Label = model.Label;
        Attributes = _attributeCreator.GetViewModelsFromModels(model.Attributes, parentMenu.Groups, false, true);
        SubscribeToCircularReferenceCheck(); // need to call again because this ObservableCollection is a different object than the one subscribed to in the constructor.
    }

    public static AttributeGroup DumpViewModelToModel(VM_AttributeGroup viewModel)
    {
        AttributeGroup model = new AttributeGroup();
        model.Label = viewModel.Label;
        model.Attributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.Attributes);
        return model;
    }

    public VM_AttributeGroup Copy(VM_AttributeGroupMenu newParentMenu)
    {
        var model = DumpViewModelToModel(this);
        var copy = new VM_AttributeGroup(newParentMenu, _attributeCreator, _logger);
        copy.CopyInViewModelFromModel(model, newParentMenu);
        return copy;
    }

    public void CheckGroupForCircularReferences()
    {
        List<string> circularRefs = new() { Label };
        foreach (var attribute in Attributes)
        {
            if (CheckMemberForCircularReference(attribute, circularRefs, ParentMenu.Groups))
            {
                CustomMessageBox.DisplayNotificationOK("Attribute Group Error", "Circular reference detected: " + string.Join(" -> ", circularRefs));
                var groupAttribute = attribute.MostRecentlyEditedShell.Attribute as VM_NPCAttributeGroup;
                if (groupAttribute != null)
                {
                    groupAttribute.MostRecentlyEditedSelection.IsSelected = false;
                }
            }
        }
    }

    private bool CheckMemberForCircularReference(VM_NPCAttribute attribute, List<string> referencedGroups, ObservableCollection<VM_AttributeGroup> allGroups)
    {
        foreach (var subAttributeShell in attribute.GroupedSubAttributes)
        {
            if (subAttributeShell.Type == NPCAttributeType.Group && subAttributeShell.Attribute as VM_NPCAttributeGroup != null)
            {
                var groupAttribute = (VM_NPCAttributeGroup)subAttributeShell.Attribute;
                
                foreach (var label in groupAttribute.SelectableAttributeGroups.Where(x => x.IsSelected).Select(x => x.SubscribedAttributeGroup.Label))
                {
                    var selectedSubGroup = allGroups.Where(x => x.Label == label).FirstOrDefault();
                    if (selectedSubGroup != null)
                    {
                        if (referencedGroups.Contains(selectedSubGroup.Label))
                        {
                            referencedGroups.Add(selectedSubGroup.Label);
                            return true;
                        }

                        referencedGroups.Add(selectedSubGroup.Label);
                        foreach (var subAttribute in selectedSubGroup.Attributes)
                        {
                            if(CheckMemberForCircularReference(subAttribute, referencedGroups, allGroups))
                            {
                                return true;
                            }
                        }
                        referencedGroups.RemoveAt(referencedGroups.Count - 1); // remove current label from "check against" list before moving on to next one
                    }
                }
            }
        }
        return false;
    }
}