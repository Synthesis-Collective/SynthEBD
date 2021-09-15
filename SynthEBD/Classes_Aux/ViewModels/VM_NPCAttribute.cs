using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_NPCAttribute
    {
        public VM_NPCAttribute(ObservableCollection<VM_NPCAttribute> parentCollection)
        {
            this.Path = "";
            this.Value = "";

            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
        }

        public string Path { get; set; }
        public string Value { get; set; }

        public RelayCommand DeleteCommand { get; }

        public static VM_NPCAttribute GetViewModelFromModel(NPCAttribute model, ObservableCollection<VM_NPCAttribute> parentCollection)
        {
            VM_NPCAttribute viewModel = new VM_NPCAttribute(parentCollection);
            viewModel.Path = model.Path;
            viewModel.Value = model.Value;

            return viewModel;
        }

        public static ObservableCollection<VM_NPCAttribute> GetViewModelsFromModels(HashSet<NPCAttribute> models)
        {
            ObservableCollection<VM_NPCAttribute> oc = new ObservableCollection<VM_NPCAttribute>();
            foreach (var m in models)
            {
                oc.Add(GetViewModelFromModel(m, oc));
            }
            return oc;
        }
    }
    
}
