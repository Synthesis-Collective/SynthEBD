using System.Collections.ObjectModel;
using System.Printing;
using System.Windows.Input;
using static SynthEBD.VM_BodyShapeDescriptor;

namespace SynthEBD;

public class VM_BodyGenConfig : VM, IHasAttributeGroupMenu
{
    public delegate VM_BodyGenConfig Factory(ObservableCollection<VM_BodyGenConfig> parentCollection);

    private readonly VM_AttributeGroupMenu.Factory _attributeGroupMenuFactory;
    private readonly Logger _logger;
    private readonly RaceMenuIniHandler _raceMenuHandler;
    private readonly SettingsIO_BodyGen _bodyGenIO;
    private readonly VM_BodyShapeDescriptorCreator _descriptorCreator;
    private readonly VM_BodyGenGroupMappingMenu.Factory _groupMappingMenuFactory;
    private readonly VM_BodyGenTemplateMenu.Factory _templateMenuFactory;
    private readonly VM_BodyGenRacialMapping.Factory _mappingFactory;
    private readonly VM_BodyGenTemplate.Factory _templateFactory;
    public VM_BodyGenConfig(
        ObservableCollection<VM_BodyGenConfig> parentCollection,
        VM_Settings_General generalSettingsVM,
        VM_BodyGenConfig.Factory bodyGenConfigFactory,
        VM_BodyShapeDescriptorCreationMenu.Factory bodyShapeDescriptorCreationMenuFactory,
        VM_SettingsBodyGen bodyGenSettingsVM,
        VM_AttributeGroupMenu.Factory attributeGroupMenuFactory,
        Logger logger,
        RaceMenuIniHandler raceMenuHandler,
        SettingsIO_BodyGen bodyGenIO,
        VM_BodyGenGroupMappingMenu.Factory groupMappingMenuFactory,
        VM_BodyShapeDescriptorCreator descriptorCreator,
        VM_BodyGenTemplateMenu.Factory templateMenuFactory,
        VM_BodyGenRacialMapping.Factory mappingFactory,
        VM_BodyGenTemplate.Factory templateFactory)
    {
        _logger = logger;
        _raceMenuHandler = raceMenuHandler;
        _attributeGroupMenuFactory = attributeGroupMenuFactory;
        _bodyGenIO = bodyGenIO;
        _groupMappingMenuFactory = groupMappingMenuFactory;
        _descriptorCreator = descriptorCreator;
        _templateMenuFactory = templateMenuFactory;
        _mappingFactory = mappingFactory;
        _templateFactory = templateFactory;

        GroupUI = new VM_BodyGenGroupsMenu(this);
        GroupMappingUI = _groupMappingMenuFactory(GroupUI, generalSettingsVM.RaceGroupings);
        DescriptorUI = bodyShapeDescriptorCreationMenuFactory(this);
        TemplateMorphUI = _templateMenuFactory(this, generalSettingsVM.RaceGroupings);
        DisplayedUI = TemplateMorphUI;
        AttributeGroupMenu = _attributeGroupMenuFactory(generalSettingsVM.AttributeGroupMenu, true);
        MiscMenu = new(_logger, _raceMenuHandler);
        ParentCollection = parentCollection;

        if (TemplateMorphUI.Templates.Any())
        {
            TemplateMorphUI.CurrentlyDisplayedTemplate = TemplateMorphUI.Templates.First();
        }

        ClickTemplateMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = TemplateMorphUI
        );

        ClickGroupMappingMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = GroupMappingUI
        );
        ClickDescriptorMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = DescriptorUI
        );
        ClickGroupsMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = GroupUI
        );
        ClickAttributeGroupsMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = AttributeGroupMenu
        );
        ClickMiscMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = MiscMenu
        );
        ClickDelete = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (CustomMessageBox.DisplayNotificationYesNo("Confirmation", "Are you sure you want to permanently delete this BodyGen Config?"))
                {
                    try
                    {
                        if (System.IO.File.Exists(SourcePath))
                        {
                            System.IO.File.Delete(SourcePath);
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
                        _logger.LogError("Could not delete file at " + SourcePath);
                        _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not delete BodyGen Config", ErrorType.Error, 5);
                    }
                }
            }
        );

        Save = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                _bodyGenIO.SaveBodyGenConfig(DumpViewModelToModel(this), out bool saveSuccess);
                if (saveSuccess)
                {
                    _logger.CallTimedNotifyStatusUpdateAsync(Label + " Saved.", 2, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Yellow));
                }
                else
                {
                    _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save " + Label + ".", ErrorType.Error, 5);
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
    public VM_BodyGenMiscMenu MiscMenu { get; set; }
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

    public bool IsLoadingFromViewModel { get; set; } = false;

    public void CopyInViewModelFromModel(BodyGenConfig model, VM_Settings_General generalSettingsVM)
    {
        IsLoadingFromViewModel = true;
        Label = model.Label;
        Gender = model.Gender;

        GroupUI.TemplateGroups = new ObservableCollection<VM_CollectionMemberString>();
        foreach (string group in model.TemplateGroups)
        {
            GroupUI.TemplateGroups.Add(new VM_CollectionMemberString(group, GroupUI.TemplateGroups));
        }

        foreach (var RTG in model.RacialTemplateGroupMap)
        {
            GroupMappingUI.RacialTemplateGroupMap.Add(VM_BodyGenRacialMapping.GetViewModelFromModel(RTG, GroupUI, generalSettingsVM.RaceGroupings, _mappingFactory));
        }
        
        if (GroupMappingUI.RacialTemplateGroupMap.Any())
        {
            GroupMappingUI.DisplayedMapping = GroupMappingUI.RacialTemplateGroupMap.First();
        }

        DescriptorUI.TemplateDescriptors = VM_BodyShapeDescriptorShell.GetViewModelsFromModels(model.TemplateDescriptors, generalSettingsVM.RaceGroupings, this, _descriptorCreator);

        foreach (var descriptor in model.TemplateDescriptors)
        {
            var subVm = _descriptorCreator.CreateNew(
                _descriptorCreator.CreateNewShell(
                    new ObservableCollection<VM_BodyShapeDescriptorShell>(), generalSettingsVM.RaceGroupings, this),
                generalSettingsVM.RaceGroupings, 
                this);
            subVm.CopyInViewModelFromModel(descriptor, generalSettingsVM.RaceGroupings, this);
            DescriptorUI.TemplateDescriptorList.Add(subVm);
        }

        foreach (var template in model.Templates)
        {
            var templateVM = _templateFactory(GroupUI.TemplateGroups, DescriptorUI, generalSettingsVM.RaceGroupings, TemplateMorphUI.Templates, this);
            templateVM.CopyInViewModelFromModel(template, DescriptorUI, generalSettingsVM.RaceGroupings);
            TemplateMorphUI.Templates.Add(templateVM);
        }

        AttributeGroupMenu.CopyInViewModelFromModels(model.AttributeGroups);

        SourcePath = model.FilePath;
        IsLoadingFromViewModel = false;
        // set the Template "other tempate groups" fields now that all templates and groups have loaded in.
        foreach (var tempateVM in TemplateMorphUI.Templates)
        {
            tempateVM.UpdateThisOtherGroupsTemplateCollection();
        }
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