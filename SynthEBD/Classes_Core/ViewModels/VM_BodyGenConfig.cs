using Mutagen.Bethesda.Skyrim;
using Noggog;
using Noggog.WPF;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Printing;
using System.Windows.Input;
using static SynthEBD.VM_BodyShapeDescriptor;

namespace SynthEBD;

[DebuggerDisplay("{Label}")]
public class VM_BodyGenConfig : VM, IHasAttributeGroupMenu, IHasRaceGroupingEditor
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
    private readonly Func<VM_SettingsTexMesh> _texMeshSettings;

    public VM_BodyGenConfig(
        ObservableCollection<VM_BodyGenConfig> parentCollection,
        VM_Settings_General generalSettingsVM,
        Func<VM_SettingsTexMesh> texMeshSettings,
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
        VM_BodyGenTemplate.Factory templateFactory,
        VM_RaceGroupingEditor.Factory raceGroupingEditorFactory)
    {
        _logger = logger;
        _texMeshSettings = texMeshSettings;
        _raceMenuHandler = raceMenuHandler;
        _attributeGroupMenuFactory = attributeGroupMenuFactory;
        _bodyGenIO = bodyGenIO;
        _groupMappingMenuFactory = groupMappingMenuFactory;
        _descriptorCreator = descriptorCreator;
        _templateMenuFactory = templateMenuFactory;
        _mappingFactory = mappingFactory;
        _templateFactory = templateFactory;

        RaceGroupingEditor = raceGroupingEditorFactory(this, true);
        GroupUI = new VM_BodyGenGroupsMenu(this);
        GroupMappingUI = _groupMappingMenuFactory(GroupUI, RaceGroupingEditor.RaceGroupings);
        DescriptorUI = bodyShapeDescriptorCreationMenuFactory(this, UpdateState, OnDescriptorValueDeletion, OnDescriptorCategoryDeletion);
        TemplateMorphUI = _templateMenuFactory(this, RaceGroupingEditor.RaceGroupings);
        DisplayedUI = TemplateMorphUI;
        AttributeGroupMenu = _attributeGroupMenuFactory(generalSettingsVM.AttributeGroupMenu, true);
        MiscMenu = new(_logger, _raceMenuHandler);
        ParentCollection = parentCollection;

        if (TemplateMorphUI.Templates.Any())
        {
            TemplateMorphUI.SelectedPlaceHolder = TemplateMorphUI.Templates.First();
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
        ClickRaceGroupingsMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = RaceGroupingEditor
        );
        ClickMiscMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = MiscMenu
        );
        ClickDelete = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (MessageWindow.DisplayNotificationYesNo("Confirmation", "Are you sure you want to permanently delete this BodyGen Config?"))
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
                _bodyGenIO.SaveBodyGenConfig(DumpViewModelToModel(), out bool saveSuccess);
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
    public VM_RaceGroupingEditor RaceGroupingEditor { get; set; }
    public string SourcePath { get; set; }
    public ICommand ClickTemplateMenu { get; }
    public ICommand ClickGroupMappingMenu { get; }
    public ICommand ClickDescriptorMenu { get; }
    public ICommand ClickGroupsMenu { get; }
    public ICommand ClickAttributeGroupsMenu { get; }
    public ICommand ClickRaceGroupingsMenu { get; }
    public ICommand ClickMiscMenu { get; }
    public ICommand ClickDelete { get; }
    public RelayCommand Save { get; }
    public string Label { get; set; } = "";
    public Gender Gender { get; set; } = Gender.Female;
    public DescriptorMatchMode AllowedDescriptorMatchMode { get; set; } = DescriptorMatchMode.All;
    public DescriptorMatchMode DisallowedDescriptorMatchMode { get; set; } = DescriptorMatchMode.Any;

    public bool IsLoadingFromViewModel { get; set; } = false;

    public void CopyInViewModelFromModel(BodyGenConfig model, ObservableCollection<VM_RaceGrouping> mainRaceGroupings)
    {
        IsLoadingFromViewModel = true;
        Label = model.Label;
        Gender = model.Gender;
        AllowedDescriptorMatchMode = model.AllowedDescriptorMatchMode;
        DisallowedDescriptorMatchMode = model.DisallowedDescriptorMatchMode;   

        RaceGroupingEditor.CopyInFromModel(model.RaceGroupings, mainRaceGroupings);
        //AddFallBackRaceGroupings(model, RaceGroupingEditor.RaceGroupings, mainRaceGroupings); // local RaceGroupings were introduced in v0.9. Prior to that, RaceGroupings were loaded from General Settings. To make sure not to wipe old settings, scan model for old race groupings and add then from General Settings if available.

        GroupUI.TemplateGroups = new ObservableCollection<VM_CollectionMemberString>();
        foreach (string group in model.TemplateGroups)
        {
            GroupUI.TemplateGroups.Add(new VM_CollectionMemberString(group, GroupUI.TemplateGroups));
        }

        foreach (var RTG in model.RacialTemplateGroupMap)
        {
            GroupMappingUI.RacialTemplateGroupMap.Add(VM_BodyGenRacialMapping.GetViewModelFromModel(RTG, GroupUI, RaceGroupingEditor.RaceGroupings, _mappingFactory, _logger));
        }
        
        if (GroupMappingUI.RacialTemplateGroupMap.Any())
        {
            GroupMappingUI.DisplayedMapping = GroupMappingUI.RacialTemplateGroupMap.First();
        }

        DescriptorUI.CopyInViewModelsFromModels(model.TemplateDescriptors);

        foreach (var descriptor in model.TemplateDescriptors)
        {
            _logger.LogStartupEventStart("Generating UI for BodyShape Descriptor " + descriptor.ID);
            var subVm = _descriptorCreator.CreateNew(
                _descriptorCreator.CreateNewShell(new ObservableCollection<VM_BodyShapeDescriptorShell>(), RaceGroupingEditor.RaceGroupings, this, DescriptorUI.ResponseToChange, DescriptorUI.ResponseToValueDeletion),  // May want to update this later to perform its own check
                RaceGroupingEditor.RaceGroupings, 
                this,
                DescriptorUI.ResponseToChange,
                DescriptorUI.ResponseToValueDeletion);
            subVm.CopyInViewModelFromModel(descriptor, RaceGroupingEditor.RaceGroupings, this);
            DescriptorUI.TemplateDescriptorList.Add(subVm);
            _logger.LogStartupEventEnd("Generating UI for BodyShape Descriptor " + descriptor.ID);
        }

        foreach (var template in model.Templates)
        {
            TemplateMorphUI.Templates.Add(new VM_BodyGenTemplatePlaceHolder(template, TemplateMorphUI.Templates));
        }

        AttributeGroupMenu.CopyInViewModelFromModels(model.AttributeGroups);

        SourcePath = model.FilePath;
        IsLoadingFromViewModel = false;
        // set the Template "other tempate groups" fields now that all templates and groups have loaded in.
        //_logger.LogStartupEventStart("Updating available partner lists for BodyGen Templates");
        //foreach (var tempateVM in TemplateMorphUI.Templates)
        //{
        //    tempateVM.UpdateThisOtherGroupsTemplateCollection();
        //}
        _logger.LogStartupEventEnd("Updating available partner lists for BodyGen Templates");
    }

    public void UpdateState((string, string) previousDescriptor, (string, string) newDescriptor)
    {
        if (previousDescriptor.Item2.IsNullOrWhitespace())
        {
            return;
        }

        var oldCategory = previousDescriptor.Item1;
        var oldValue = previousDescriptor.Item2;
        var newCategory = newDescriptor.Item1;
        var newValue = newDescriptor.Item2;

        foreach (var morph in TemplateMorphUI.Templates)
        {
            UpdateDescriptors(morph.AssociatedModel.BodyShapeDescriptors, oldCategory, oldValue, newCategory, newValue);
        }

        foreach (var subgroup in _texMeshSettings().AssetPacks.SelectMany(x => x.GetAllSubgroups()).ToArray())
        {
            UpdateDescriptors(subgroup.AssociatedModel.AllowedBodyGenDescriptors, oldCategory, oldValue, newCategory, newValue);
            UpdateDescriptors(subgroup.AssociatedModel.DisallowedBodyGenDescriptors, oldCategory, oldValue, newCategory, newValue);
        }
    }

    private void UpdateDescriptors<T>(ICollection<T> descriptors, string oldCategory, string oldValue, string newCategory, string newValue)
        where T : BodyShapeDescriptor.LabelSignature
    {
        foreach (var descriptor in descriptors)
        {
            if (descriptor.Category == oldCategory)
            {
                descriptor.Category = newCategory;

                if (descriptor.Value == oldValue)
                {
                    descriptor.Value = newValue;
                }
            }
        }
    }

    public void OnDescriptorCategoryDeletion(string category)
    {
        if (MessageWindow.DisplayNotificationYesNo("", "Would you like to delete all " + category + " Descriptors from all BodySlides and Config Files that reference it?"))
        {
            TemplateMorphUI.StashAndNullDisplayedMorph();

            foreach (var morph in TemplateMorphUI.Templates)
            {
                morph.AssociatedModel.BodyShapeDescriptors.RemoveWhere(x => x.Category == category);
            }

            foreach (var ap in _texMeshSettings().AssetPacks)
            {
                var subgroups = ap.GetAllSubgroups();
                foreach (var sg in subgroups)
                {
                    sg.AssociatedModel.AllowedBodyGenDescriptors.RemoveWhere(x => x.Category == category);
                    sg.AssociatedModel.DisallowedBodyGenDescriptors.RemoveWhere(x => x.Category == category);
                }
            }

            TemplateMorphUI.RestoreStashedMorph();
        }
    }

    public void OnDescriptorValueDeletion(string decriptorSignature)
    {
        if (MessageWindow.DisplayNotificationYesNo("", "Would you like to delete all " + decriptorSignature + " Descriptors from all BodySlides and Config Files that reference it?"))
        {
            TemplateMorphUI.StashAndNullDisplayedMorph();

            foreach (var morph in TemplateMorphUI.Templates)
            {
                morph.AssociatedModel.BodyShapeDescriptors.RemoveWhere(x => x.ToString() == decriptorSignature);
            }

            foreach (var ap in _texMeshSettings().AssetPacks)
            {
                var subgroups = ap.GetAllSubgroups();
                foreach (var sg in subgroups)
                {
                    sg.AssociatedModel.AllowedBodySlideDescriptors.RemoveWhere(x => x.ToString() == decriptorSignature);
                    sg.AssociatedModel.DisallowedBodySlideDescriptors.RemoveWhere(x => x.ToString() == decriptorSignature);
                    sg.AssociatedModel.PrioritizedBodySlideDescriptors.RemoveWhere(x => x.ToString() == decriptorSignature);
                }
            }

            TemplateMorphUI.RestoreStashedMorph();
        }
    }

    public BodyGenConfig DumpViewModelToModel()
    {
        BodyGenConfig model = new BodyGenConfig();
        model.Label = Label;
        model.Gender = Gender;
        model.AllowedDescriptorMatchMode = AllowedDescriptorMatchMode;
        model.DisallowedDescriptorMatchMode = DisallowedDescriptorMatchMode;
        model.TemplateGroups = GroupUI.TemplateGroups.Select(x => x.Content).ToHashSet();
        foreach (var RTG in GroupMappingUI.RacialTemplateGroupMap)
        {
            model.RacialTemplateGroupMap.Add(VM_BodyGenRacialMapping.DumpViewModelToModel(RTG));
        }
        model.TemplateDescriptors = DescriptorUI.DumpToViewModels();

        if (TemplateMorphUI.CurrentlyDisplayedTemplate != null)
        {
            TemplateMorphUI.CurrentlyDisplayedTemplate.DumpViewModelToModel();
        }

        foreach (var template in TemplateMorphUI.Templates)
        {
            model.Templates.Add(template.AssociatedModel);
        }
        VM_AttributeGroupMenu.DumpViewModelToModels(AttributeGroupMenu, model.AttributeGroups);
        model.RaceGroupings = RaceGroupingEditor.DumpToModel();
        model.FilePath = SourcePath;
        return model;
    }

    public void AddFallBackRaceGroupings(BodyGenConfig model, ObservableCollection<VM_RaceGrouping> existingGroupings, ObservableCollection<VM_RaceGrouping> fallBackGroupings)
    {
        HashSet<RaceGrouping> addedRaceGroups = new();

        HashSet<string> existingGroupNames = model.RaceGroupings.Select(x => x.Label).ToHashSet();
        HashSet<string> fallBackGroupNames = fallBackGroupings.Select(x => x.Label).ToHashSet();

        foreach (var template in model.Templates)
        {
            foreach (var groupLabel in template.AllowedRaceGroupings)
            {
                if (!existingGroupNames.Contains(groupLabel) && fallBackGroupNames.Contains(groupLabel))
                {
                    existingGroupings.Add(fallBackGroupings.Where(x => x.Label == groupLabel).First());
                }
            }

            foreach (var groupLabel in template.DisallowedRaceGroupings)
            {
                if (!existingGroupNames.Contains(groupLabel) && fallBackGroupNames.Contains(groupLabel))
                {
                    existingGroupings.Add(fallBackGroupings.Where(x => x.Label == groupLabel).First());
                }
            }
        }
    }
}