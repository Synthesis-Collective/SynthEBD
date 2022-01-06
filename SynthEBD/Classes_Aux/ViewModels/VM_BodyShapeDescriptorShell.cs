using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_BodyShapeDescriptorShell : INotifyPropertyChanged
    {
        public VM_BodyShapeDescriptorShell(ObservableCollection<VM_BodyShapeDescriptorShell> parentCollection)
        {
            this.Category = "";
            this.Descriptors = new ObservableCollection<VM_BodyShapeDescriptor>();
            this.ParentCollection = parentCollection;

            AddTemplateDescriptorValue = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.Descriptors.Add(new VM_BodyShapeDescriptor(this))
                );
        }

        public string Category { get; set; }
        public ObservableCollection<VM_BodyShapeDescriptor> Descriptors { get; set; }
        public ObservableCollection<VM_BodyShapeDescriptorShell> ParentCollection { get; set; }
        public RelayCommand AddTemplateDescriptorValue { get; }
        public event PropertyChangedEventHandler PropertyChanged;

        public static ObservableCollection<VM_BodyShapeDescriptorShell> GetViewModelsFromModels(HashSet<BodyShapeDescriptor> models)
        {
            ObservableCollection<VM_BodyShapeDescriptorShell> viewModels = new ObservableCollection<VM_BodyShapeDescriptorShell>();
            VM_BodyShapeDescriptorShell shellViewModel = new VM_BodyShapeDescriptorShell(viewModels);
            VM_BodyShapeDescriptor viewModel = new VM_BodyShapeDescriptor(shellViewModel);
            List<string> usedCategories = new List<string>();

            foreach (var model in models)
            {
                viewModel = VM_BodyShapeDescriptor.GetViewModelFromModel(model);

                if (!usedCategories.Contains(model.Category))
                {
                    shellViewModel = new VM_BodyShapeDescriptorShell(viewModels);
                    shellViewModel.Category = model.Category;
                    viewModel.ParentShell = shellViewModel;
                    shellViewModel.Descriptors.Add(viewModel);
                    viewModels.Add(shellViewModel);
                    usedCategories.Add(model.Category);
                }
                else
                {
                    int index = usedCategories.IndexOf(model.Category);
                    viewModel.ParentShell = viewModels[index];
                    viewModels[index].Descriptors.Add(viewModel);
                }
            }

            return viewModels;
        }

        public static HashSet<BodyShapeDescriptor> DumpViewModelsToModels(ObservableCollection<VM_BodyShapeDescriptorShell> viewModels)
        {
            HashSet<BodyShapeDescriptor> models = new HashSet<BodyShapeDescriptor>();

            foreach (var categoryVM in viewModels)
            {
                foreach (var descriptor in categoryVM.Descriptors)
                {
                    models.Add(new BodyShapeDescriptor() { Category = categoryVM.Category, Value = descriptor.Value, DispString = descriptor.DispString });
                }
            }

            return models;
        }
    }
}
