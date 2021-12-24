using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_AttributeGroupMenu : INotifyPropertyChanged
    {
        public VM_AttributeGroupMenu()
        {
            Groups = new ObservableCollection<VM_AttributeGroup>();
            DisplayedGroup = null;

            AddGroup = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.Groups.Add(new VM_AttributeGroup(this))
                );
        }
        public ObservableCollection<VM_AttributeGroup> Groups { get; set; }
        public VM_AttributeGroup DisplayedGroup { get; set; }

        public RelayCommand AddGroup { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static void GetViewModelFromModels(HashSet<AttributeGroup> models, VM_AttributeGroupMenu viewModel)
        {
            foreach (var model in models)
            {
                viewModel.Groups.Add(VM_AttributeGroup.GetViewModelFromModel(model, viewModel));
            }
        }

        public static void DumpViewModelToModels(VM_AttributeGroupMenu viewModel, HashSet<AttributeGroup> models)
        {
            models.Clear();
            foreach (var subVM in viewModel.Groups)
            {
                var model = VM_AttributeGroup.DumpViewModelToModel(subVM);
                if (model.Attributes.Any())
                {
                    models.Add(model);
                }
            }
        }
    }
}
