using System.Collections.ObjectModel;
using static SynthEBD.VM_BodyShapeDescriptor;

namespace SynthEBD;

public class VM_BodyShapeDescriptorShell : VM
{
    private VM_BodyShapeDescriptorCreator _creator;
    public delegate VM_BodyShapeDescriptorShell Factory(ObservableCollection<VM_BodyShapeDescriptorShell> parentCollection, ObservableCollection<VM_RaceGrouping> raceGroupings, IHasAttributeGroupMenu parentConfig);
    public VM_BodyShapeDescriptorShell(ObservableCollection<VM_BodyShapeDescriptorShell> parentCollection, ObservableCollection<VM_RaceGrouping> raceGroupings, IHasAttributeGroupMenu parentConfig, VM_BodyShapeDescriptorCreator creator)
    {
        _creator = creator;

        ParentCollection = parentCollection;

        AddTemplateDescriptorValue = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Descriptors.Add(_creator.CreateNew(this, raceGroupings, parentConfig))
        );
    }

    public string Category { get; set; } = "";
    public ObservableCollection<VM_BodyShapeDescriptor> Descriptors { get; set; } = new();
    public ObservableCollection<VM_BodyShapeDescriptorShell> ParentCollection { get; set; }
    public RelayCommand AddTemplateDescriptorValue { get; }


    public static ObservableCollection<VM_BodyShapeDescriptorShell> GetViewModelsFromModels(HashSet<BodyShapeDescriptor> models, ObservableCollection<VM_RaceGrouping> raceGroupings, IHasAttributeGroupMenu parentConfig, VM_BodyShapeDescriptorCreator creator)
    {
        ObservableCollection<VM_BodyShapeDescriptorShell> viewModels = new ObservableCollection<VM_BodyShapeDescriptorShell>();
        VM_BodyShapeDescriptorShell shellViewModel = creator.CreateNewShell(viewModels, raceGroupings, parentConfig);
        List<string> usedCategories = new List<string>();

        foreach (var model in models)
        {
            VM_BodyShapeDescriptor subVm = creator.CreateNew(
                creator.CreateNewShell(
                    new ObservableCollection<VM_BodyShapeDescriptorShell>(),
                    raceGroupings, parentConfig), 
                raceGroupings, 
                parentConfig);

            subVm.CopyInViewModelFromModel(model, raceGroupings, parentConfig);

            if (!usedCategories.Contains(model.ID.Category))
            {
                shellViewModel = creator.CreateNewShell(viewModels, raceGroupings, parentConfig);
                shellViewModel.Category = model.ID.Category;
                subVm.ParentShell = shellViewModel;
                shellViewModel.Descriptors.Add(subVm);
                viewModels.Add(shellViewModel);
                usedCategories.Add(model.ID.Category);
            }
            else
            {
                int index = usedCategories.IndexOf(model.ID.Category);
                subVm.ParentShell = viewModels[index];
                viewModels[index].Descriptors.Add(subVm);
            }
        }

        return viewModels;
    }

    public static HashSet<BodyShapeDescriptor> DumpViewModelsToModels(ObservableCollection<VM_BodyShapeDescriptorShell> viewModels)
    {
        HashSet<BodyShapeDescriptor> models = new();

        foreach (var categoryVM in viewModels)
        {
            foreach (var descriptor in categoryVM.Descriptors)
            {
                models.Add(VM_BodyShapeDescriptor.DumpViewModeltoModel(descriptor));
            }
        }

        return models;
    }
}