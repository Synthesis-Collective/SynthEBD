using System.Collections.ObjectModel;
using System.Windows.Input;
using Noggog.WPF;

namespace SynthEBD;

public class VM_BodyGenConfig : ViewModel, IHasAttributeGroupMenu
{
    public VM_BodyGenConfig(VM_Settings_General generalSettingsVM, ObservableCollection<VM_BodyGenConfig> parentCollection, VM_SettingsBodyGen bodyGenSettingsVM)
    {
        this.GroupMappingUI = new VM_BodyGenGroupMappingMenu(this.GroupUI, generalSettingsVM.RaceGroupings);
        this.GroupUI = new VM_BodyGenGroupsMenu(this);
        this.DescriptorUI = new VM_BodyShapeDescriptorCreationMenu(generalSettingsVM.RaceGroupings, this);
        this.TemplateMorphUI = new VM_BodyGenTemplateMenu(this, generalSettingsVM.RaceGroupings);
        this.DisplayedUI = this.TemplateMorphUI;
        this.AttributeGroupMenu = new VM_AttributeGroupMenu(generalSettingsVM.AttributeGroupMenu, true);
        this.ParentCollection = parentCollection;

        ClickTemplateMenu = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.DisplayedUI = this.TemplateMorphUI
        );

        ClickGroupMappingMenu = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.DisplayedUI = this.GroupMappingUI
        );
        ClickDescriptorMenu = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.DisplayedUI = this.DescriptorUI
        );
        ClickGroupsMenu = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.DisplayedUI = this.GroupUI
        );
        ClickAttributeGroupsMenu = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.DisplayedUI = this.AttributeGroupMenu
        );
        ClickMiscMenu = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.DisplayedUI = this.MiscMenu
        );
        ClickDelete = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (CustomMessageBox.DisplayNotificationYesNo("Confirmation", "Are you sure you want to permanently delete this BodyGen Config?"))
                {
                    try
                    {
                        System.IO.File.Delete(this.SourcePath);
                        if (ParentCollection.Contains(this)) // false if user tries to delete a new blank view model
                        {
                            ParentCollection.Remove(this);
                            bool updateDisplayedConfig = false;
                            if (bodyGenSettingsVM.CurrentMaleConfig == this)
                            {
                                if (bodyGenSettingsVM.CurrentlyDisplayedConfig == this) {  updateDisplayedConfig = true; }

                                if (ParentCollection.Any())
                                {
                                    bodyGenSettingsVM.CurrentMaleConfig = ParentCollection.First();
                                }
                                else
                                {
                                    bodyGenSettingsVM.CurrentMaleConfig = new VM_BodyGenConfig(generalSettingsVM, ParentCollection, bodyGenSettingsVM);
                                }

                                if (updateDisplayedConfig) {  bodyGenSettingsVM.CurrentlyDisplayedConfig = bodyGenSettingsVM.CurrentMaleConfig; }
                            }
                            else if (bodyGenSettingsVM.CurrentFemaleConfig == this)
                            {
                                if (bodyGenSettingsVM.CurrentlyDisplayedConfig == this) { updateDisplayedConfig = true; }

                                if (ParentCollection.Any())
                                {
                                    bodyGenSettingsVM.CurrentFemaleConfig = ParentCollection.First();
                                }
                                else
                                {
                                    bodyGenSettingsVM.CurrentFemaleConfig = new VM_BodyGenConfig(generalSettingsVM, ParentCollection, bodyGenSettingsVM);
                                }

                                if (updateDisplayedConfig) { bodyGenSettingsVM.CurrentlyDisplayedConfig = bodyGenSettingsVM.CurrentFemaleConfig; }
                            }
                        }
                    }
                    catch
                    {
                        Logger.LogError("Could not delete file at " + this.SourcePath);
                        Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not delete BodyGen Config", ErrorType.Error, 5);
                    }
                }
            }
        );

        Save = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                SettingsIO_BodyGen.SaveBodyGenConfig(DumpViewModelToModel(this), out bool saveSuccess);
                if (saveSuccess)
                {
                    Logger.CallTimedNotifyStatusUpdateAsync(Label + " Saved.", 2, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Yellow));
                }
                else
                {
                    Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save " + Label + ".", ErrorType.Error, 5);
                    Logger.SwitchViewToLogDisplay();
                }
            }
        );
    }

    public object DisplayedUI { get; set; }
    public VM_BodyGenGroupMappingMenu GroupMappingUI { get; set; }
    public VM_BodyGenGroupsMenu GroupUI { get; set; }
    public VM_BodyShapeDescriptorCreationMenu DescriptorUI { get; set; }
    public VM_BodyGenTemplateMenu TemplateMorphUI { get; set; }

    public VM_AttributeGroupMenu AttributeGroupMenu { get; set; }
    public VM_BodyGenMiscMenu MiscMenu { get; set; } = new();
    public ObservableCollection<VM_BodyGenConfig> ParentCollection { get; set; }

    public string SourcePath { get; set; }

    public ICommand ClickTemplateMenu { get; }
    public ICommand ClickGroupMappingMenu { get; }
    public ICommand ClickDescriptorMenu { get; }
    public ICommand ClickGroupsMenu { get; }
    public ICommand ClickAttributeGroupsMenu { get; }
    public ICommand ClickMiscMenu { get; }
    public ICommand ClickDelete { get; }
    public RelayCommand Save { get; }

    public string Label { get; set; } = "";
    public Gender Gender { get; set; } = Gender.Female;

    public static VM_BodyGenConfig GetViewModelFromModel(BodyGenConfig model, VM_Settings_General generalSettingsVM, ObservableCollection<VM_BodyGenConfig> parentCollection, VM_SettingsBodyGen bodyGenSettingsVM)
    {
        VM_BodyGenConfig viewModel = new VM_BodyGenConfig(generalSettingsVM, parentCollection, bodyGenSettingsVM);
        viewModel.Label = model.Label;
        viewModel.Gender = model.Gender;

        viewModel.GroupUI.TemplateGroups = new ObservableCollection<VM_CollectionMemberString>();
        foreach (string group in model.TemplateGroups)
        {
            viewModel.GroupUI.TemplateGroups.Add(new VM_CollectionMemberString(group, viewModel.GroupUI.TemplateGroups));
        }

        foreach (var RTG in model.RacialTemplateGroupMap)
        {
            viewModel.GroupMappingUI.RacialTemplateGroupMap.Add(VM_BodyGenRacialMapping.GetViewModelFromModel(RTG, viewModel.GroupUI, generalSettingsVM.RaceGroupings));
        }

        viewModel.DescriptorUI.TemplateDescriptors = VM_BodyShapeDescriptorShell.GetViewModelsFromModels(model.TemplateDescriptors, generalSettingsVM.RaceGroupings, viewModel, model);

        foreach (var descriptor in model.TemplateDescriptors)
        {
            viewModel.DescriptorUI.TemplateDescriptorList.Add(VM_BodyShapeDescriptor.GetViewModelFromModel(descriptor, generalSettingsVM.RaceGroupings, viewModel, model));
        }

        foreach (var template in model.Templates)
        {
            var templateVM = new VM_BodyGenTemplate(viewModel.GroupUI.TemplateGroups, viewModel.DescriptorUI, generalSettingsVM.RaceGroupings, viewModel.TemplateMorphUI.Templates, viewModel);
            VM_BodyGenTemplate.GetViewModelFromModel(template, templateVM, viewModel.DescriptorUI, generalSettingsVM.RaceGroupings);
            viewModel.TemplateMorphUI.Templates.Add(templateVM);
        }

        VM_AttributeGroupMenu.GetViewModelFromModels(model.AttributeGroups, viewModel.AttributeGroupMenu);

        viewModel.SourcePath = model.FilePath;

        return viewModel;
    }

    public static BodyGenConfig DumpViewModelToModel(VM_BodyGenConfig viewModel)
    {
        BodyGenConfig model = new BodyGenConfig();
        model.Label = viewModel.Label;
        model.Gender = viewModel.Gender;
        model.TemplateGroups = viewModel.GroupUI.TemplateGroups.Select(x => x.Content).ToHashSet();
        foreach (var RTG in viewModel.GroupMappingUI.RacialTemplateGroupMap)
        {
            model.RacialTemplateGroupMap.Add(VM_BodyGenRacialMapping.DumpViewModelToModel(RTG));
        }
        model.TemplateDescriptors = VM_BodyShapeDescriptorShell.DumpViewModelsToModels(viewModel.DescriptorUI.TemplateDescriptors, model.DescriptorRules);
        foreach (var template in viewModel.TemplateMorphUI.Templates)
        {
            model.Templates.Add(VM_BodyGenTemplate.DumpViewModelToModel(template));
        }
        VM_AttributeGroupMenu.DumpViewModelToModels(viewModel.AttributeGroupMenu, model.AttributeGroups);
        model.FilePath = viewModel.SourcePath;
        return model;
    }
}