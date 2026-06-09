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

/// <summary>
/// View model for a named attribute group — a reusable set of attribute conditions referenceable by
/// other rules. Detects circular references between groups and round-trips to/from <see cref="AttributeGroup"/>.
/// </summary>
public class VM_AttributeGroup : VM
{
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly Logger _logger;
    /// <summary>Autofac factory delegate for constructing a group under the attribute-group menu.</summary>
    public delegate VM_AttributeGroup Factory(VM_AttributeGroupMenu parent);
    /// <summary>Creates the group VM, wiring remove/add-attribute commands and circular-reference checking.</summary>
    /// <param name="parent">The owning attribute-group menu.</param>
    /// <param name="attributeCreator">Factory for attribute VMs.</param>
    /// <param name="logger">Logger for diagnostics.</param>
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

    /// <summary>Subscribes to the attribute collection so any edit re-runs the circular-reference check (disposing per-attribute subscriptions as attributes are removed).</summary>
    private void SubscribeToCircularReferenceCheck()
    {
        Attributes
            .ToObservableChangeSet()
            .Transform(x =>
                x.WhenAnyObservable(y => y.NeedsRefresh)
                .Subscribe(_ => CheckGroupForCircularReferences())
                .DisposeWith(this))
            .DisposeMany() // Dispose subscriptions related to removed attributes
            .Subscribe()  // Execute my instructions
            .DisposeWith(this);
    }

    /// <summary>Populates this VM from a persisted <see cref="AttributeGroup"/> model, re-subscribing the circular-reference check to the repopulated collection.</summary>
    /// <param name="model">The model to load.</param>
    public void CopyInViewModelFromModel(AttributeGroup model)
    {
        Label = model.Label;
        _attributeCreator.CopyInFromModels(model.Attributes, Attributes, ParentMenu.Groups, false, true);
        SubscribeToCircularReferenceCheck(); // need to call again because this ObservableCollection is a different object than the one subscribed to in the constructor.
    }

    /// <summary>Projects a group VM back into an <see cref="AttributeGroup"/> model.</summary>
    /// <param name="viewModel">The view model to project.</param>
    /// <returns>The populated model.</returns>
    public static AttributeGroup DumpViewModelToModel(VM_AttributeGroup viewModel)
    {
        AttributeGroup model = new AttributeGroup();
        model.Label = viewModel.Label;
        model.Attributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.Attributes);
        return model;
    }

    /// <summary>Copies this group into another menu by round-tripping through its model.</summary>
    /// <param name="newParentMenu">The menu to attach the copy to.</param>
    /// <returns>The copied group VM.</returns>
    public VM_AttributeGroup Copy(VM_AttributeGroupMenu newParentMenu)
    {
        var model = DumpViewModelToModel(this);
        var copy = new VM_AttributeGroup(newParentMenu, _attributeCreator, _logger);
        copy.CopyInViewModelFromModel(model);
        return copy;
    }

    /// <summary>Checks every attribute for a circular group reference, warning the user and de-selecting the offending reference when one is found.</summary>
    public void CheckGroupForCircularReferences()
    {
        List<string> circularRefs = new() { Label };
        foreach (var attribute in Attributes)
        {
            if (CheckMemberForCircularReference(attribute, circularRefs, ParentMenu.Groups))
            {
                MessageWindow.DisplayNotificationOK("Attribute Group Error", "Circular reference detected: " + string.Join(" -> ", circularRefs));
                var groupAttribute = attribute.MostRecentlyEditedShell.Attribute as VM_NPCAttributeGroup;
                if (groupAttribute != null)
                {
                    groupAttribute.MostRecentlyEditedSelection.IsSelected = false;
                }
            }
        }
    }

    /// <summary>Recursively walks group references reachable from an attribute, returning true if any path revisits an already-referenced group.</summary>
    /// <param name="attribute">The attribute whose group sub-attributes are followed.</param>
    /// <param name="referencedGroups">The chain of group labels visited so far (used as the cycle stack).</param>
    /// <param name="allGroups">All groups, for resolving referenced labels.</param>
    /// <returns><c>true</c> if a circular reference is detected.</returns>
    private bool CheckMemberForCircularReference(VM_NPCAttribute attribute, List<string> referencedGroups, ObservableCollection<VM_AttributeGroup> allGroups)
    {
        foreach (var subAttributeShell in attribute.GroupedSubAttributes)
        {
            if (subAttributeShell.Type == NPCAttributeType.Group && subAttributeShell.Attribute as VM_NPCAttributeGroup != null)
            {
                var groupAttribute = (VM_NPCAttributeGroup)subAttributeShell.Attribute;
                
                foreach (var label in groupAttribute.SelectableAttributeGroups.Where(x => x.IsSelected).Select(x => x.SubscribedAttributeGroup.Label).ToArray())
                {
                    var selectedSubGroup = allGroups.FirstOrDefault(x => x.Label == label);
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