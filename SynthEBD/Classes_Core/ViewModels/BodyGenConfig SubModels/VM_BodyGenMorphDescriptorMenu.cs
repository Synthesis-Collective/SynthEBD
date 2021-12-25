using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_BodyGenMorphDescriptorMenu : INotifyPropertyChanged
    {
        public VM_BodyGenMorphDescriptorMenu()
        {
            this.TemplateDescriptors = new ObservableCollection<VM_BodyGenMorphDescriptorShell>();
            this.TemplateDescriptorList = new ObservableCollection<VM_BodyGenMorphDescriptor>();
            this.CurrentlyDisplayedTemplateDescriptorShell = new VM_BodyGenMorphDescriptorShell(new ObservableCollection<VM_BodyGenMorphDescriptorShell>());

            AddTemplateDescriptorShell = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.TemplateDescriptors.Add(new VM_BodyGenMorphDescriptorShell(this.TemplateDescriptors))
                );

            RemoveTemplateDescriptorShell = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.TemplateDescriptors.Remove((VM_BodyGenMorphDescriptorShell)x)
                );
        }

        public ObservableCollection<VM_BodyGenMorphDescriptorShell> TemplateDescriptors { get; set; }
        public ObservableCollection<VM_BodyGenMorphDescriptor> TemplateDescriptorList { get; set; } // hidden flattened list of TemplateDescriptors for presentation to VM_Subgroup and VM_BodyGenTemplate. Needs to be synced with TemplateDescriptors on update.

        public VM_BodyGenMorphDescriptorShell CurrentlyDisplayedTemplateDescriptorShell { get; set; }

        public RelayCommand AddTemplateDescriptorShell { get; }
        public RelayCommand RemoveTemplateDescriptorShell { get; }
        public event PropertyChangedEventHandler PropertyChanged;
    }




    public class VM_BodyGenMorphDescriptorShell : INotifyPropertyChanged
    {
        public VM_BodyGenMorphDescriptorShell(ObservableCollection<VM_BodyGenMorphDescriptorShell> parentCollection)
        {
            this.Category = "";
            this.Descriptors = new ObservableCollection<VM_BodyGenMorphDescriptor>();
            this.ParentCollection = parentCollection;

            AddTemplateDescriptorValue = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.Descriptors.Add(new VM_BodyGenMorphDescriptor(this))
                );
        }

        public string Category { get; set; }
        public ObservableCollection<VM_BodyGenMorphDescriptor> Descriptors { get; set; }
        public ObservableCollection<VM_BodyGenMorphDescriptorShell> ParentCollection { get; set; }
        public RelayCommand AddTemplateDescriptorValue { get; }
        public event PropertyChangedEventHandler PropertyChanged;

        public static ObservableCollection<VM_BodyGenMorphDescriptorShell> GetViewModelsFromModels(HashSet<BodyGenConfig.MorphDescriptor> models)
        {
            ObservableCollection<VM_BodyGenMorphDescriptorShell> viewModels = new ObservableCollection<VM_BodyGenMorphDescriptorShell>();
            VM_BodyGenMorphDescriptorShell shellViewModel = new VM_BodyGenMorphDescriptorShell(viewModels);
            VM_BodyGenMorphDescriptor viewModel = new VM_BodyGenMorphDescriptor(shellViewModel);
            List<string> usedCategories = new List<string>();

            foreach (var model in models)
            {
                viewModel = VM_BodyGenMorphDescriptor.GetViewModelFromModel(model);

                if (!usedCategories.Contains(model.Category))
                {
                    shellViewModel = new VM_BodyGenMorphDescriptorShell(viewModels);
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

        public static HashSet<BodyGenConfig.MorphDescriptor> DumpViewModelsToModels(ObservableCollection<VM_BodyGenMorphDescriptorShell> viewModels)
        {
            HashSet<BodyGenConfig.MorphDescriptor> models = new HashSet<BodyGenConfig.MorphDescriptor>();

            foreach (var categoryVM in viewModels)
            {
                foreach (var descriptor in categoryVM.Descriptors)
                {
                    models.Add(new BodyGenConfig.MorphDescriptor() { Category = categoryVM.Category, Value = descriptor.Value, DispString = descriptor.DispString });
                }
            }

            return models;
        }
    }

    public class VM_BodyGenMorphDescriptor : INotifyPropertyChanged
    {
        public VM_BodyGenMorphDescriptor(VM_BodyGenMorphDescriptorShell parentShell)
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

        public VM_BodyGenMorphDescriptorShell ParentShell { get; set; }

        public RelayCommand RemoveDescriptorValue { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_BodyGenMorphDescriptor GetViewModelFromModel(BodyGenConfig.MorphDescriptor model)
        {
            VM_BodyGenMorphDescriptor viewModel = new VM_BodyGenMorphDescriptor(new VM_BodyGenMorphDescriptorShell(new ObservableCollection<VM_BodyGenMorphDescriptorShell>()));
            viewModel.Category = model.Category;
            viewModel.Value = model.Value;
            viewModel.DispString = model.DispString;
            return viewModel;
        }
    }
}
