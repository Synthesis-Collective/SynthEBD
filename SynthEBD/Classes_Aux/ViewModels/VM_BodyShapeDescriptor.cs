using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_BodyShapeDescriptor : INotifyPropertyChanged
    {
        public VM_BodyShapeDescriptor(VM_BodyShapeDescriptorShell parentShell)
        {
            this.Category = "";
            this.Value = "";
            this.DispString = "";
            this.ParentShell = parentShell;

            RemoveDescriptorValue = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.ParentShell.Descriptors.Remove(this)
                );
        }
        public string Category { get; set; }
        public string Value { get; set; }
        public string DispString { get; set; }

        public VM_BodyShapeDescriptorShell ParentShell { get; set; }

        public RelayCommand RemoveDescriptorValue { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_BodyShapeDescriptor GetViewModelFromModel(BodyShapeDescriptor model)
        {
            VM_BodyShapeDescriptor viewModel = new VM_BodyShapeDescriptor(new VM_BodyShapeDescriptorShell(new ObservableCollection<VM_BodyShapeDescriptorShell>()));
            viewModel.Category = model.Category;
            viewModel.Value = model.Value;
            viewModel.DispString = model.DispString;
            return viewModel;
        }
    }
}
