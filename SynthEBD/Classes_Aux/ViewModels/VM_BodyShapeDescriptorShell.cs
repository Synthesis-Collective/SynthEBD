using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_BodyShapeDescriptorShell : VM
{
    public VM_BodyShapeDescriptorShell(ObservableCollection<VM_BodyShapeDescriptorShell> parentCollection, ObservableCollection<VM_RaceGrouping> raceGroupings, IHasAttributeGroupMenu parentConfig)
    {
        this.ParentCollection = parentCollection;

        AddTemplateDescriptorValue = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.Descriptors.Add(new VM_BodyShapeDescriptor(this, raceGroupings, parentConfig))
        );
    }

    public string Category { get; set; } = "";
    public ObservableCollection<VM_BodyShapeDescriptor> Descriptors { get; set; } = new();
    public ObservableCollection<VM_BodyShapeDescriptorShell> ParentCollection { get; set; }
    public RelayCommand AddTemplateDescriptorValue { get; }


    public static ObservableCollection<VM_BodyShapeDescriptorShell> GetViewModelsFromModels(HashSet<BodyShapeDescriptor> models, ObservableCollection<VM_RaceGrouping> raceGroupings, IHasAttributeGroupMenu parentConfig, IHasDescriptorRules parentDescriptorConfig)
    {
        ObservableCollection<VM_BodyShapeDescriptorShell> viewModels = new ObservableCollection<VM_BodyShapeDescriptorShell>();
        VM_BodyShapeDescriptorShell shellViewModel = new VM_BodyShapeDescriptorShell(viewModels, raceGroupings, parentConfig);
        VM_BodyShapeDescriptor viewModel = new VM_BodyShapeDescriptor(shellViewModel, raceGroupings, parentConfig);
        List<string> usedCategories = new List<string>();

        foreach (var model in models)
        {
            viewModel = VM_BodyShapeDescriptor.GetViewModelFromModel(model, raceGroupings, parentConfig, parentDescriptorConfig);

            if (!usedCategories.Contains(model.Category))
            {
                shellViewModel = new VM_BodyShapeDescriptorShell(viewModels, raceGroupings, parentConfig);
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

    public static HashSet<BodyShapeDescriptor> DumpViewModelsToModels(ObservableCollection<VM_BodyShapeDescriptorShell> viewModels, HashSet<BodyShapeDescriptorRules> configDescriptorRules)
    {
        HashSet<BodyShapeDescriptor> models = new HashSet<BodyShapeDescriptor>();

        foreach (var categoryVM in viewModels)
        {
            foreach (var descriptor in categoryVM.Descriptors)
            {
                models.Add(VM_BodyShapeDescriptor.DumpViewModeltoModel(descriptor, configDescriptorRules));
            }
        }

        return models;
    }
}