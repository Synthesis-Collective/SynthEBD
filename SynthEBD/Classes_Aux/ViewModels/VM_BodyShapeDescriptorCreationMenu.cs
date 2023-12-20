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
    public delegate VM_BodyShapeDescriptorCreationMenu Factory(IHasAttributeGroupMenu parentConfig, Action<(string, string), (string, string)> responseToChange);
    public Action<(string, string), (string, string)> ResponseToChange { get; set; }

    public VM_BodyShapeDescriptorCreationMenu(IHasAttributeGroupMenu parentConfig, VM_Settings_General generalSettings, Logger logger, VM_BodyShapeDescriptor.VM_BodyShapeDescriptorCreator descriptorCreator, Action<(string, string), (string, string)> responseToChange)
    {
        _generalSettings = generalSettings;
        _logger = logger;
        _descriptorCreator = descriptorCreator;
        _parentConfig = parentConfig;
        ResponseToChange = responseToChange;

        CurrentlyDisplayedTemplateDescriptorShell = descriptorCreator.CreateNewShell(new ObservableCollection<VM_BodyShapeDescriptorShell>(), generalSettings.RaceGroupingEditor.RaceGroupings, parentConfig, ResponseToChange);

        AddTemplateDescriptorShell = new RelayCommand(
            canExecute: _ => true,
            execute: _ => TemplateDescriptors.Add(descriptorCreator.CreateNewShell(TemplateDescriptors, generalSettings.RaceGroupingEditor.RaceGroupings, parentConfig, ResponseToChange))
        );

        RemoveTemplateDescriptorShell = new RelayCommand(
            canExecute: _ => true,
            execute: x => TemplateDescriptors.Remove((VM_BodyShapeDescriptorShell)x)
        );
    }

    public void CopyInViewModelsFromModels(HashSet<BodyShapeDescriptor> models)
    {
        MergeInMissingModels(models, DescriptorRulesMergeMode.Overwrite, new List<string>());
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
                shell = _descriptorCreator.CreateNewShell(TemplateDescriptors, _generalSettings.RaceGroupingEditor.RaceGroupings, _parentConfig, ResponseToChange);
                shell.Category = model.ID.Category;
                shell.CategoryDescription = model.CategoryDescription;
                TemplateDescriptors.Add(shell);
            }

            var descriptor = shell.Descriptors.Where(x => x.Value == model.ID.Value).FirstOrDefault();
            if (descriptor == null)
            {
                descriptor = _descriptorCreator.CreateNew(shell, _generalSettings.RaceGroupingEditor.RaceGroupings, _parentConfig, ResponseToChange);
                descriptor.Value = model.ID.Value;
                descriptor.ValueDescription = model.ValueDescription;
                descriptor.AssociatedRules.CopyInViewModelFromModel(model.AssociatedRules, _generalSettings.RaceGroupingEditor.RaceGroupings);
                shell.Descriptors.Add(descriptor);
                TemplateDescriptorList.Add(descriptor);
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