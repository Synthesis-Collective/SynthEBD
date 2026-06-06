using Mutagen.Bethesda.Oblivion;
using Noggog.WPF;
using System.Collections.ObjectModel;

namespace SynthEBD;

/// <summary>
/// View model for the body-shape descriptor creation/editing menu: an editable tree of category shells
/// and their descriptor values, with import/merge support and several dump variants (all values, only
/// selected values).
/// </summary>
public class VM_BodyShapeDescriptorCreationMenu : VM
{
    private readonly VM_Settings_General _generalSettings;
    private readonly Logger _logger;
    private readonly VM_BodyShapeDescriptor.VM_BodyShapeDescriptorCreator _descriptorCreator;
    private readonly IHasAttributeGroupMenu _parentConfig;
    /// <summary>Autofac factory delegate for constructing the menu, supplying the change/deletion callbacks.</summary>
    public delegate VM_BodyShapeDescriptorCreationMenu Factory(IHasAttributeGroupMenu parentConfig, Action<(string, string), (string, string)> responseToChange, Action<string> responseToValueDeletion, Action<string> repsonseToCategoryDeletion);
    public Action<(string, string), (string, string)> ResponseToChange { get; set; }
    public Action<string> ResponseToValueDeletion { get; set; }
    public Action<string> RepsonseToCategoryDeletion { get; set; }

    /// <summary>Creates the menu, seeding a blank category shell and wiring the add/remove-shell commands.</summary>
    /// <param name="parentConfig">The owning config (attribute-group menu source).</param>
    /// <param name="generalSettings">General settings (race-grouping source).</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="descriptorCreator">Factory for shell and descriptor VMs.</param>
    /// <param name="responseToChange">Callback invoked when a descriptor's (category, value) changes.</param>
    /// <param name="responseToValueDeletion">Callback invoked when a descriptor value is deleted.</param>
    /// <param name="repsonseToCategoryDeletion">Callback invoked when a category shell is deleted.</param>
    public VM_BodyShapeDescriptorCreationMenu(IHasAttributeGroupMenu parentConfig, VM_Settings_General generalSettings, Logger logger, VM_BodyShapeDescriptor.VM_BodyShapeDescriptorCreator descriptorCreator, Action<(string, string), (string, string)> responseToChange, Action<string> responseToValueDeletion, Action<string> repsonseToCategoryDeletion)
    {
        _generalSettings = generalSettings;
        _logger = logger;
        _descriptorCreator = descriptorCreator;
        _parentConfig = parentConfig;
        ResponseToChange = responseToChange;
        ResponseToValueDeletion = responseToValueDeletion;
        RepsonseToCategoryDeletion = repsonseToCategoryDeletion;

        CurrentlyDisplayedTemplateDescriptorShell = descriptorCreator.CreateNewShell(new ObservableCollection<VM_BodyShapeDescriptorShell>(), generalSettings.RaceGroupingEditor.RaceGroupings, parentConfig, ResponseToChange, ResponseToValueDeletion);

        AddTemplateDescriptorShell = new RelayCommand(
            canExecute: _ => true,
            execute: _ => TemplateDescriptors.Add(descriptorCreator.CreateNewShell(TemplateDescriptors, generalSettings.RaceGroupingEditor.RaceGroupings, parentConfig, ResponseToChange, ResponseToValueDeletion))
        );

        RemoveTemplateDescriptorShell = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                var shell = (VM_BodyShapeDescriptorShell)x;
                RepsonseToCategoryDeletion(shell.Category);
                TemplateDescriptors.Remove(shell);
            }
        );
    }

    /// <summary>Replaces the menu's descriptors with those from the given shells (overwrite merge).</summary>
    /// <param name="models">The shell models to load.</param>
    public void CopyInViewModelsFromModels(List<BodyShapeDescriptorShell> models)
    {
        MergeInMissingModels(models, DescriptorRulesMergeMode.Overwrite, new List<string>());
    }

    /// <summary>Projects the full descriptor tree into shell models, emitting one CategoryDescription per category.</summary>
    /// <returns>The shell models.</returns>
    public List<BodyShapeDescriptorShell> DumpToViewModels()
    {
        // Emit one shell per category VM with one CategoryDescription, then each value VM's
        // descriptor model under it. The per-descriptor model no longer carries
        // CategoryDescription (lives on the shell), so the JSON only stores it once per category.
        var models = new List<BodyShapeDescriptorShell>();
        foreach (var categoryVM in TemplateDescriptors)
        {
            var shell = new BodyShapeDescriptorShell
            {
                Category = categoryVM.Category,
                CategoryDescription = categoryVM.CategoryDescription,
            };
            foreach (var descriptor in categoryVM.Descriptors)
            {
                shell.Descriptors.Add(descriptor.DumpViewModeltoModel());
            }
            models.Add(shell);
        }
        return models;
    }

    /// <summary>Projects only the named (Category, Value) descriptors into shell models, omitting categories with no selected values.</summary>
    /// <param name="selectedDescriptors">The descriptor signatures to include.</param>
    /// <returns>The filtered shell models.</returns>
    public List<BodyShapeDescriptorShell> DumpSelectedToViewModels(IEnumerable<BodyShapeDescriptor.LabelSignature> selectedDescriptors)
    {
        // Same shape as DumpToViewModels, but filtered to (Category, Value) pairs the caller
        // explicitly named. Empty shells (no selected descriptors in the category) are omitted
        // so the exported file is minimal.
        var selectedSignatures = selectedDescriptors.Select(x => x.ToString()).ToHashSet(StringComparer.Ordinal);
        var models = new List<BodyShapeDescriptorShell>();
        foreach (var categoryVM in TemplateDescriptors)
        {
            BodyShapeDescriptorShell shell = null;
            foreach (var descriptor in categoryVM.Descriptors)
            {
                if (!selectedSignatures.Contains(descriptor.Signature)) continue;
                if (shell == null)
                {
                    shell = new BodyShapeDescriptorShell
                    {
                        Category = categoryVM.Category,
                        CategoryDescription = categoryVM.CategoryDescription,
                    };
                }
                shell.Descriptors.Add(descriptor.DumpViewModeltoModel());
            }
            if (shell != null) models.Add(shell);
        }
        return models;
    }

    /// <summary>Merges shells into the menu: adds missing categories and values, and for already-present values applies the given rules merge mode.</summary>
    /// <param name="shells">The shell models to merge in.</param>
    /// <param name="mode">How to combine rules for descriptors that already exist (skip/overwrite/merge).</param>
    /// <param name="mergedDescriptors">Receives the signatures of descriptors whose rules were merged.</param>
    public void MergeInMissingModels(List<BodyShapeDescriptorShell> shells, DescriptorRulesMergeMode mode, List<string> mergedDescriptors)
    {
        mergedDescriptors.Clear();
        if (shells == null) return;

        foreach (var shellModel in shells)
        {
            if (shellModel == null) continue;
            var shell = TemplateDescriptors.Where(x => x.Category == shellModel.Category).FirstOrDefault();
            if (shell == null)
            {
                shell = _descriptorCreator.CreateNewShell(TemplateDescriptors, _generalSettings.RaceGroupingEditor.RaceGroupings, _parentConfig, ResponseToChange, ResponseToValueDeletion);
                shell.Category = shellModel.Category;
                shell.CategoryDescription = shellModel.CategoryDescription;
                TemplateDescriptors.Add(shell);
            }
            else if (string.IsNullOrEmpty(shell.CategoryDescription) && !string.IsNullOrEmpty(shellModel.CategoryDescription))
            {
                // Existing shell without a description picks up the imported one.
                // We don't overwrite an existing non-empty description — that would surprise
                // the user, and the merge-mode parameter is about rules, not descriptions.
                shell.CategoryDescription = shellModel.CategoryDescription;
            }

            foreach (var model in shellModel.Descriptors ?? new List<BodyShapeDescriptor>())
            {
                if (model?.ID == null) continue;
                var descriptor = shell.Descriptors.Where(x => x.Value == model.ID.Value).FirstOrDefault();
                if (descriptor == null)
                {
                    descriptor = _descriptorCreator.CreateNew(shell, _generalSettings.RaceGroupingEditor.RaceGroupings, _parentConfig, ResponseToChange, ResponseToValueDeletion);
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
    }

    public ObservableCollection<VM_BodyShapeDescriptorShell> TemplateDescriptors { get; set; } = new();
    public ObservableCollection<VM_BodyShapeDescriptor> TemplateDescriptorList { get; set; } = new(); // hidden flattened list of TemplateDescriptors for presentation to VM_Subgroup and VM_BodyGenTemplate. Needs to be synced with TemplateDescriptors on update.

    public VM_BodyShapeDescriptorShell CurrentlyDisplayedTemplateDescriptorShell { get; set; }

    public RelayCommand AddTemplateDescriptorShell { get; }
    public RelayCommand RemoveTemplateDescriptorShell { get; }
}

/// <summary>How imported descriptor rules combine with existing ones during a merge.</summary>
public enum DescriptorRulesMergeMode
{
    /// <summary>Keep the existing rules unchanged.</summary>
    Skip,
    /// <summary>Replace the existing rules with the imported ones.</summary>
    Overwrite,
    /// <summary>Merge the imported rules into the existing ones.</summary>
    Merge
}