using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
                ) ;

            AddAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => Attributes.Add(VM_NPCAttribute.CreateNewFromUI(Attributes, true)) 
                );
        }

        public string Label { get; set; }
        public ObservableCollection<VM_NPCAttribute> Attributes { get; set; }
        public VM_AttributeGroupMenu ParentMenu { get; set; }

        public RelayCommand Remove { get; }
        public RelayCommand AddAttribute { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_AttributeGroup GetViewModelFromModel(AttributeGroup model, VM_AttributeGroupMenu parentMenu)
        {
            VM_AttributeGroup vm = new VM_AttributeGroup(parentMenu);
            vm.Label = model.Label;
            vm.Attributes = VM_NPCAttribute.GetViewModelsFromModels(model.Attributes, true);
            return vm;
        }

        public static AttributeGroup DumpViewModelToModel(VM_AttributeGroup viewModel)
        {
            AttributeGroup model = new AttributeGroup();
            model.Label = viewModel.Label;
            model.Attributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.Attributes);
            return model;
        }
    }
}
