using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_FilePathReplacement
    {
        public VM_FilePathReplacement(ObservableCollection<VM_FilePathReplacement> parentCollection)
        {
            this.Source = "";
            this.Destination = "";

            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
        }

        public string Source { get; set; }
        public string Destination { get; set; }

        public RelayCommand DeleteCommand { get; }

        public static VM_FilePathReplacement GetViewModelFromModel(FilePathReplacement model, ObservableCollection<VM_FilePathReplacement> parentCollection)
        {
            VM_FilePathReplacement viewModel = new VM_FilePathReplacement(parentCollection);
            viewModel.Source = model.Source;
            viewModel.Destination = model.Destination;

            return viewModel;
        }

        public static ObservableCollection<VM_FilePathReplacement> GetViewModelsFromModels(HashSet<FilePathReplacement> models)
        {
            ObservableCollection<VM_FilePathReplacement> oc = new ObservableCollection<VM_FilePathReplacement>();
            foreach (var m in models)
            {
                oc.Add(GetViewModelFromModel(m, oc));
            }
            return oc;
        }
    }
}
