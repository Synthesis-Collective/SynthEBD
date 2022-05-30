using DynamicData;
using DynamicData.Binding;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_AttributeGroupCheckList : VM
{
    public VM_AttributeGroupCheckList(ObservableCollection<VM_AttributeGroup> sourceAttributeGroups)
    {
        SubscribedAttributeGroups = sourceAttributeGroups;
        foreach (var attributeGroupVM in sourceAttributeGroups)
        {
            SelectableAttributeGroups.Add(new AttributeGroupSelection(attributeGroupVM));
        }

        SubscribedAttributeGroups.ToObservableChangeSet()
            .QueryWhenChanged(currentList => currentList)
            .Subscribe(x =>
            {
                RefreshCheckList();
                int debugPauseHere = 0;
                }
            );
    }

    public ObservableCollection<VM_AttributeGroup> SubscribedAttributeGroups { get; set; }
    public ObservableCollection<AttributeGroupSelection> SelectableAttributeGroups { get; set; } = new();

    void RefreshCheckList()
    {
        var currentSelections = SelectableAttributeGroups.Where(x => x.IsSelected).Select(x => x.SubscribedAttributeGroup.Label).ToList();

        SelectableAttributeGroups.Clear();
        foreach (var attributeGroupVM in SubscribedAttributeGroups)
        {
            var newSelection = new AttributeGroupSelection(attributeGroupVM);
            if (currentSelections.Contains(attributeGroupVM.Label)) 
            { 
                newSelection.IsSelected = true; 
            }
            SelectableAttributeGroups.Add(newSelection);
        }
    }

    public class AttributeGroupSelection : VM
    {
        public AttributeGroupSelection(VM_AttributeGroup attributeGroupVM)
        {
            SubscribedAttributeGroup = attributeGroupVM;
        }
        
        public bool IsSelected { get; set; } = false;
        public VM_AttributeGroup SubscribedAttributeGroup { get; set; }
    }
}