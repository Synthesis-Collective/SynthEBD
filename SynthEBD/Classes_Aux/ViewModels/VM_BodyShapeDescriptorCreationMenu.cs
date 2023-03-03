using Mutagen.Bethesda.Oblivion;
using Noggog.WPF;
using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_BodyShapeDescriptorCreationMenu : VM
{
    private readonly VM_Settings_General _generalSettings;
    private readonly Logger _logger;
    private readonly VM_BodyShapeDescriptor.VM_BodyShapeDescriptorCreator _descriptorCreator;
    private readonly IHasAttributeGroupMenu _parentConfig;
    public delegate VM_BodyShapeDescriptorCreationMenu Factory(IHasAttributeGroupMenu parentConfig);
    
    public VM_BodyShapeDescriptorCreationMenu(IHasAttributeGroupMenu parentConfig, VM_Settings_General generalSettings, Logger logger, VM_BodyShapeDescriptor.VM_BodyShapeDescriptorCreator descriptorCreator)
    {
        _generalSettings = generalSettings;
        _logger = logger;
        _descriptorCreator = descriptorCreator;
        _parentConfig = parentConfig;

        CurrentlyDisplayedTemplateDescriptorShell = descriptorCreator.CreateNewShell(new ObservableCollection<VM_BodyShapeDescriptorShell>(), generalSettings.RaceGroupingEditor.RaceGroupings, parentConfig);

        AddTemplateDescriptorShell = new RelayCommand(
            canExecute: _ => true,
            execute: _ => TemplateDescriptors.Add(descriptorCreator.CreateNewShell(TemplateDescriptors, generalSettings.RaceGroupingEditor.RaceGroupings, parentConfig))
        );

        RemoveTemplateDescriptorShell = new RelayCommand(
            canExecute: _ => true,
            execute: x => TemplateDescriptors.Remove((VM_BodyShapeDescriptorShell)x)
        );
    }

    public void CopyInViewModelsFromModels(HashSet<BodyShapeDescriptor> models)
    {
        _logger.LogStartupEventStart("Generating BodyShape Descriptor Creator UI");
        List<string> usedCategories = new List<string>();

        foreach (var model in models)
        {
            VM_BodyShapeDescriptor subVm = _descriptorCreator.CreateNew(
                _descriptorCreator.CreateNewShell(
                    new ObservableCollection<VM_BodyShapeDescriptorShell>(),
                    _generalSettings.RaceGroupingEditor.RaceGroupings, _parentConfig),
                _generalSettings.RaceGroupingEditor.RaceGroupings,
                _parentConfig);

            subVm.CopyInViewModelFromModel(model, _generalSettings.RaceGroupingEditor.RaceGroupings, _parentConfig);

            if (!usedCategories.Contains(model.ID.Category))
            {
                var shellViewModel = _descriptorCreator.CreateNewShell(TemplateDescriptors, _generalSettings.RaceGroupingEditor.RaceGroupings, _parentConfig);
                shellViewModel.Category = model.ID.Category;
                subVm.ParentShell = shellViewModel;
                shellViewModel.Descriptors.Add(subVm);
                TemplateDescriptors.Add(shellViewModel);
                usedCategories.Add(model.ID.Category);
            }
            else
            {
                int index = usedCategories.IndexOf(model.ID.Category);
                subVm.ParentShell = TemplateDescriptors[index];
                TemplateDescriptors[index].Descriptors.Add(subVm);
            }
        }
        _logger.LogStartupEventEnd("Generating BodyShape Descriptor Creator UI");
    }

    public HashSet<BodyShapeDescriptor> DumpToViewModels()
    {
        HashSet<BodyShapeDescriptor> models = new();

        foreach (var categoryVM in TemplateDescriptors)
        {
            foreach (var descriptor in categoryVM.Descriptors)
            {
                models.Add(descriptor.DumpViewModeltoModel());
            }
        }

        return models;
    }

    public HashSet<BodyShapeDescriptor> DumpSelectedToViewModels(IEnumerable<BodyShapeDescriptor.LabelSignature> selectedDescriptors)
    {
        HashSet<BodyShapeDescriptor> models = new();
        var selectedSignatures = selectedDescriptors.Select(x => x.ToString()).ToArray();

        foreach (var categoryVM in TemplateDescriptors)
        {
            foreach (var descriptor in categoryVM.Descriptors)
            {
                if (selectedSignatures.Contains(descriptor.Signature))
                {
                    models.Add(descriptor.DumpViewModeltoModel());
                }
            }
        }

        return models;
    }

    public void MergeInMissingModels(HashSet<BodyShapeDescriptor> models, DescriptorRulesMergeMode mode, List<string> mergedDescriptors)
    {
        mergedDescriptors.Clear();

        foreach (var model in models)
        {
            var shell = TemplateDescriptors.Where(x => x.Category == model.ID.Category).FirstOrDefault();
            if (shell == null)
            {
                shell = _descriptorCreator.CreateNewShell(TemplateDescriptors, _generalSettings.RaceGroupingEditor.RaceGroupings, _parentConfig);
                shell.Category = model.ID.Category;
                TemplateDescriptors.Add(shell);
            }

            var descriptor = shell.Descriptors.Where(x => x.Value == model.ID.Value).FirstOrDefault();
            if (descriptor == null)
            {
                descriptor = _descriptorCreator.CreateNew(shell, _generalSettings.RaceGroupingEditor.RaceGroupings, _parentConfig);
                descriptor.Value = model.ID.Value;
                descriptor.AssociatedRules.CopyInViewModelFromModel(model.AssociatedRules, _generalSettings.RaceGroupingEditor.RaceGroupings);
                shell.Descriptors.Add(descriptor);
            }
            else
            {
                switch (mode)
                {
                    case DescriptorRulesMergeMode.Skip: break;
                    case DescriptorRulesMergeMode.Overwrite: descriptor.AssociatedRules.CopyInViewModelFromModel(model.AssociatedRules, _generalSettings.RaceGroupingEditor.RaceGroupings); break;
                    case DescriptorRulesMergeMode.Merge: 
                        descriptor.AssociatedRules.MergeInViewModelFromModel(model.AssociatedRules, _generalSettings.RaceGroupingEditor.RaceGroupings);
                        mergedDescriptors.Add(descriptor.Signature);
                        break;

                }
            }
        }
    }

    public ObservableCollection<VM_BodyShapeDescriptorShell> TemplateDescriptors { get; set; } = new();
    public ObservableCollection<VM_BodyShapeDescriptor> TemplateDescriptorList { get; set; } = new(); // hidden flattened list of TemplateDescriptors for presentation to VM_Subgroup and VM_BodyGenTemplate. Needs to be synced with TemplateDescriptors on update.

    public VM_BodyShapeDescriptorShell CurrentlyDisplayedTemplateDescriptorShell { get; set; }

    public RelayCommand AddTemplateDescriptorShell { get; }
    public RelayCommand RemoveTemplateDescriptorShell { get; }


}

public enum DescriptorRulesMergeMode
{
    Skip,
    Overwrite,
    Merge
}