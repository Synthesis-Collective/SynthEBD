using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using Mutagen.Bethesda.Plugins.Records;
using ReactiveUI;
using static SynthEBD.VM_NPCAttribute;

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

        Observable.CombineLatest(
                Attributes.ToObservableChangeSet(),
                ParentMenu.Groups.ToObservableChangeSet(),
                (_, _) => { return 0; })
            .Subscribe(_ => {
                RefreshCircularReferenceWatch();
            });
    }

    public string Label { get; set; } = "";
    public ObservableCollection<VM_NPCAttribute> Attributes { get; set; } = new();
    public VM_AttributeGroupMenu ParentMenu { get; set; }

    public RelayCommand Remove { get; }
    public RelayCommand AddAttribute { get; }

    public ObservableCollection<VM_NPCAttribute> Attributes_Bak { get; set; }

    public void RefreshCircularReferenceWatch()
    {
        Attributes.ToObservableChangeSet().TransformMany(x => x.GroupedSubAttributes).Transform(
                x =>
                {
                    var unSubDisposable = x.WhenAnyValue(x => x.Attribute.NeedsRefresh).Switch().Subscribe(_ => CheckGroupForCircularReferences());
                    return unSubDisposable;
                }).DisposeMany().Subscribe();

        foreach (var group in ParentMenu.Groups.Where(x => x != this))
        {
            group.Attributes.ToObservableChangeSet().TransformMany(x => x.GroupedSubAttributes).Transform(
                x =>
                {
                    var unSubDisposable = x.WhenAnyValue(x => x.Attribute.NeedsRefresh).Switch().Subscribe(_ => CheckGroupForCircularReferences());
                    return unSubDisposable;
                }).DisposeMany().Subscribe();
        }
    }

    public void CopyInViewModelFromModel(AttributeGroup model, VM_AttributeGroupMenu parentMenu)
    {
        this.Label = model.Label;
        this.Attributes = _attributeCreator.GetViewModelsFromModels(model.Attributes, parentMenu.Groups, false, true);
        this.Attributes_Bak = new ObservableCollection<VM_NPCAttribute>(this.Attributes);
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
                Attributes.Clear();
                Attributes.AddRange(Attributes_Bak);
            }
            else
            {
                Attributes_Bak = new ObservableCollection<VM_NPCAttribute>(Attributes);
            }
        }
    }

    private bool CheckMemberForCircularReference(VM_NPCAttribute attribute, List<string> referencedGroups, ObservableCollection<VM_AttributeGroup> allGroups)
    {
        foreach (var subAttributeShell in attribute.GroupedSubAttributes)
        {
            if (subAttributeShell.Type == NPCAttributeType.Group)
            {
                var groupAttribute = (VM_NPCAttributeGroup)subAttributeShell.Attribute;

                foreach (var label in groupAttribute.SelectableAttributeGroups.Where(x => x.IsSelected).Select(x => x.SubscribedAttributeGroup.Label))
                {
                    var subGroup = allGroups.Where(x => x.Label == label).FirstOrDefault();
                    if (subGroup != null)
                    {
                        if (referencedGroups.Contains(subGroup.Label))
                        {
                            var toUnSet = groupAttribute.SelectableAttributeGroups.Where(x => x.SubscribedAttributeGroup.Label == label).First();
                            toUnSet.IsSelected = false;
                            referencedGroups.Add(subGroup.Label);
                            return true;
                        }

                        referencedGroups.Add(subGroup.Label);
                        foreach (var subAttribute in subGroup.Attributes)
                        {
                            return CheckMemberForCircularReference(subAttribute, referencedGroups, allGroups);
                        }
                        referencedGroups.RemoveAt(-1);
                    }
                }
            }
        }
        return false;
    }
}