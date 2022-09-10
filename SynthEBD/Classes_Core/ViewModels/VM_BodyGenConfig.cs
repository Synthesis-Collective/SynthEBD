using System.Collections.ObjectModel;
using System.Windows.Input;

namespace SynthEBD;

public class VM_BodyGenConfig : VM, IHasAttributeGroupMenu
{
    public delegate VM_BodyGenConfig Factory(ObservableCollection<VM_BodyGenConfig> parentCollection);
    
    public VM_BodyGenConfig(
        ObservableCollection<VM_BodyGenConfig> parentCollection,
        VM_Settings_General generalSettingsVM,
        VM_BodyGenConfig.Factory bodyGenConfigFactory,
        VM_BodyShapeDescriptorCreationMenu.Factory bodyShapeDescriptorCreationMenuFactory,
        VM_SettingsBodyGen bodyGenSettingsVM)
    {
        this.GroupUI = new VM_BodyGenGroupsMenu(this);
        this.GroupMappingUI = new VM_BodyGenGroupMappingMenu(this.GroupUI, generalSettingsVM.RaceGroupings);
        this.DescriptorUI = bodyShapeDescriptorCreationMenuFactory(this);
        this.TemplateMorphUI = new VM_BodyGenTemplateMenu(this, generalSettingsVM.RaceGroupings);
        this.DisplayedUI = this.TemplateMorphUI;
        this.AttributeGroupMenu = new VM_AttributeGroupMenu(generalSettingsVM.AttributeGroupMenu, true);
        this.ParentCollection = parentCollection;

        if (TemplateMorphUI.Templates.Any())
        {
            TemplateMorphUI.CurrentlyDisplayedTemplate = TemplateMorphUI.Templates.First();
        }

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
                        if (System.IO.File.Exists(this.SourcePath))
                        {
                            System.IO.File.Delete(this.SourcePath);
                        }
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
                                    bodyGenSettingsVM.CurrentMaleConfig = bodyGenConfigFactory(ParentCollection);
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
                                    bodyGenSettingsVM.CurrentFemaleConfig = bodyGenConfigFactory(ParentCollection);
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

    public void CopyInViewModelFromModel(BodyGenConfig model, VM_Settings_General generalSettingsVM)
    {
        Label = model.Label;
        Gender = model.Gender;

        GroupUI.TemplateGroups = new ObservableCollection<VM_CollectionMemberString>();
        foreach (string group in model.TemplateGroups)
        {
            GroupUI.TemplateGroups.Add(new VM_CollectionMemberString(group, GroupUI.TemplateGroups));
        }

        foreach (var RTG in model.RacialTemplateGroupMap)
        {
            GroupMappingUI.RacialTemplateGroupMap.Add(VM_BodyGenRacialMapping.GetViewModelFromModel(RTG, GroupUI, generalSettingsVM.RaceGroupings));
        }
        
        if (GroupMappingUI.RacialTemplateGroupMap.Any())
        {
            GroupMappingUI.DisplayedMapping = GroupMappingUI.RacialTemplateGroupMap.First();
        }

        DescriptorUI.TemplateDescriptors = VM_BodyShapeDescriptorShell.GetViewModelsFromModels(model.TemplateDescriptors, generalSettingsVM.RaceGroupings, this);

        foreach (var descriptor in model.TemplateDescriptors)
        {
            var subVm = new VM_BodyShapeDescriptor(
                new VM_BodyShapeDescriptorShell(
                    new ObservableCollection<VM_BodyShapeDescriptorShell>(), generalSettingsVM.RaceGroupings, this),
                generalSettingsVM.RaceGroupings, 
                this);
            subVm.CopyInViewModelFromModel(descriptor, generalSettingsVM.RaceGroupings, this);
            DescriptorUI.TemplateDescriptorList.Add(subVm);
        }

        foreach (var template in model.Templates)
        {
            var templateVM = new VM_BodyGenTemplate(GroupUI.TemplateGroups, DescriptorUI, generalSettingsVM.RaceGroupings, TemplateMorphUI.Templates, this);
            templateVM.CopyInViewModelFromModel(template, DescriptorUI, generalSettingsVM.RaceGroupings);
            TemplateMorphUI.Templates.Add(templateVM);
        }

        AttributeGroupMenu.CopyInViewModelFromModels(model.AttributeGroups);

        SourcePath = model.FilePath;
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
        model.TemplateDescriptors = VM_BodyShapeDescriptorShell.DumpViewModelsToModels(viewModel.DescriptorUI.TemplateDescriptors);
        foreach (var template in viewModel.TemplateMorphUI.Templates)
        {
            model.Templates.Add(VM_BodyGenTemplate.DumpViewModelToModel(template));
        }
        VM_AttributeGroupMenu.DumpViewModelToModels(viewModel.AttributeGroupMenu, model.AttributeGroups);
        model.FilePath = viewModel.SourcePath;
        return model;
    }
}