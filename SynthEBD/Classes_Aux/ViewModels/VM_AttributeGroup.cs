using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
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

        Attributes.ToObservableChangeSet().TransformMany(x => x.GroupedSubAttributes).Transform(
            x =>
            {
                var unSubDisposable = x.WhenAnyValue(x => x.Attribute.NeedsRefresh).Switch().Subscribe(_ => CheckForCircularReferences());
                return unSubDisposable;
            }).DisposeMany().Subscribe();

        Attributes.CollectionChanged += CheckForCircularReferences;

        ParentMenu.Groups.CollectionChanged += RefreshAttributeWatch; // this is needed because the Subscription set in this constructor will not follow other attribute groups added to the parent collection after the current one is loaded
    }

    public string Label { get; set; } = "";
    public ObservableCollection<VM_NPCAttribute> Attributes { get; set; } = new();
    public VM_AttributeGroupMenu ParentMenu { get; set; }

    public RelayCommand Remove { get; }
    public RelayCommand AddAttribute { get; }

    public ObservableCollection<VM_NPCAttribute> Attributes_Bak { get; set; }

    public void RefreshAttributeWatch(object sender, NotifyCollectionChangedEventArgs e)
    {
        Attributes.ToObservableChangeSet().TransformMany(x => x.GroupedSubAttributes).Transform(
            x =>
            {
                var unSubDisposable = x.WhenAnyValue(x => x.Attribute.NeedsRefresh).Switch().Subscribe(_ => CheckForCircularReferences());
                return unSubDisposable;
            }).DisposeMany().Subscribe();
    }

    public void CheckForCircularReferences()
    {
        if (CheckForCircularReferences(this, new HashSet<string>()))
        {
            CustomMessageBox.DisplayNotificationOK("Attribute Group Error", "Circular reference detected.");
            Attributes = new ObservableCollection<VM_NPCAttribute>(Attributes_Bak);
        }
        else
        {
            Attributes_Bak = new ObservableCollection<VM_NPCAttribute>(Attributes);
        }
    }

    public void CopyInViewModelFromModel(AttributeGroup model, VM_AttributeGroupMenu parentMenu)
    {
        this.Label = model.Label;
        this.Attributes = VM_NPCAttribute.GetViewModelsFromModels(model.Attributes, parentMenu.Groups, false, true, _attributeCreator, _logger);
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
      

    public void CheckForCircularReferences(object sender, PropertyChangedEventArgs e)
    {
        if (CheckForCircularReferences(this, new HashSet<string>()))
        {
            CustomMessageBox.DisplayNotificationOK("Attribute Group Error", "Circular reference detected.");
            Attributes = new ObservableCollection<VM_NPCAttribute>(Attributes_Bak);
        }
        else
        {
            Attributes_Bak = new ObservableCollection<VM_NPCAttribute>(Attributes);
        }
    }

    public void CheckForCircularReferences(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (CheckForCircularReferences(this, new HashSet<string>()))
        {
            CustomMessageBox.DisplayNotificationOK("Attribute Group Error", "Circular reference detected.");
            Attributes = new ObservableCollection<VM_NPCAttribute>(Attributes_Bak);
        }
        else
        {
            Attributes_Bak = new ObservableCollection<VM_NPCAttribute>(Attributes);
        }
    }

    private static bool CheckForCircularReferences(VM_AttributeGroup attGroup, HashSet<string> referencedGroups)
    {
        foreach (var attribute in attGroup.Attributes)
        {
            if (CheckForCircularReference(attribute, referencedGroups, attGroup.ParentMenu.Groups))
            {
                return true;
            }
        }
        return false;
    }

    private static bool CheckForCircularReference(VM_NPCAttribute attribute, HashSet<string> referencedGroups, ObservableCollection<VM_AttributeGroup> allGroups)
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
                            //test
                            var toUnSet = groupAttribute.SelectableAttributeGroups.Where(x => x.SubscribedAttributeGroup.Label == label).First();
                            toUnSet.IsSelected = false;
                            //
                            return true;
                        }
                        referencedGroups.Add(subGroup.Label);
                        return CheckForCircularReferences(subGroup, referencedGroups);
                    }
                }
            }
        }
        return false;
    }
}