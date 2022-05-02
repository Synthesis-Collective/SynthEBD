using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Binding;
using Noggog;
using ReactiveUI;

namespace SynthEBD
{
    public class VM_AttributeGroup : INotifyPropertyChanged
    {
        public VM_AttributeGroup(VM_AttributeGroupMenu parent)
        {
            Label = "";
            Attributes = new ObservableCollection<VM_NPCAttribute>();
            ParentMenu = parent;

            Remove = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => ParentMenu.Groups.Remove(this)
                );

            AddAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => Attributes.Add(VM_NPCAttribute.CreateNewFromUI(Attributes, false, true, parent.Groups))
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

        public string Label { get; set; }
        public ObservableCollection<VM_NPCAttribute> Attributes { get; set; }
        public VM_AttributeGroupMenu ParentMenu { get; set; }

        public RelayCommand Remove { get; }
        public RelayCommand AddAttribute { get; }

        public ObservableCollection<VM_NPCAttribute> Attributes_Bak { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

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

        public static VM_AttributeGroup GetViewModelFromModel(AttributeGroup model, VM_AttributeGroupMenu parentMenu)
        {
            VM_AttributeGroup vm = new VM_AttributeGroup(parentMenu);
            vm.Label = model.Label;
            vm.Attributes = VM_NPCAttribute.GetViewModelsFromModels(model.Attributes, parentMenu.Groups, false, true);
            vm.Attributes_Bak = new ObservableCollection<VM_NPCAttribute>(vm.Attributes);
            return vm;
        }

        public static AttributeGroup DumpViewModelToModel(VM_AttributeGroup viewModel)
        {
            AttributeGroup model = new AttributeGroup();
            model.Label = viewModel.Label;
            model.Attributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.Attributes);
            return model;
        }

        public static VM_AttributeGroup Copy(VM_AttributeGroup toCopy, VM_AttributeGroupMenu newParentMenu)
        {
            var model = DumpViewModelToModel(toCopy);
            var copy = GetViewModelFromModel(model, newParentMenu);
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

                    foreach (var label in groupAttribute.AttributeCheckList.AttributeSelections.Where(x => x.IsSelected).Select(x => x.Label))
                    {
                        var subGroup = allGroups.Where(x => x.Label == label).FirstOrDefault();
                        if (subGroup != null)
                        {
                            if (referencedGroups.Contains(subGroup.Label))
                            {
                                //test
                                var toUnSet = groupAttribute.AttributeCheckList.AttributeSelections.Where(x => x.Label == label).First();
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
}
