using System.Collections.ObjectModel;
using static SynthEBD.VM_BodyShapeDescriptor;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

public class VM_SettingsOBody : VM, IHasAttributeGroupMenu
{
    private readonly Logger _logger;
    private readonly VM_AttributeGroupMenu.Factory _attributeGroupFactory;
    private readonly VM_BodySlidesMenu.Factory _bodySlidesMenuFactory;

    public VM_SettingsOBody(
        VM_Settings_General generalSettingsVM,
        Logger logger,
        VM_BodyShapeDescriptorCreationMenu.Factory bodyShapeDescriptorCreationMenuFactory,
        VM_BodySlidesMenu.Factory bodySlidesMenuFactory,
        VM_OBodyMiscSettings.Factory miscSettingsFactory,
        VM_AttributeGroupMenu.Factory attributeGroupFactory
        )
    {
        _logger = logger;
        _attributeGroupFactory = attributeGroupFactory;
        _bodySlidesMenuFactory = bodySlidesMenuFactory;

        DescriptorUI = bodyShapeDescriptorCreationMenuFactory(this);
        BodySlidesUI = _bodySlidesMenuFactory(generalSettingsVM.RaceGroupingEditor.RaceGroupings);
        AttributeGroupMenu = _attributeGroupFactory(generalSettingsVM.AttributeGroupMenu, true);
        MiscUI = miscSettingsFactory();

        DisplayedUI = BodySlidesUI;

        ClickBodySlidesMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = BodySlidesUI
        );

        ClickDescriptorsMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = DescriptorUI
        );

        ClickAttributeGroupsMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = AttributeGroupMenu
        );

        ClickMiscMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = MiscUI
        );
    }

    public object DisplayedUI { get; set; }
    public VM_BodyShapeDescriptorCreationMenu DescriptorUI { get; set; }
    public VM_BodySlidesMenu BodySlidesUI { get; set; }
    public VM_AttributeGroupMenu AttributeGroupMenu { get; set; }
    public VM_OBodyMiscSettings MiscUI { get; set; }
    public RelayCommand ClickBodySlidesMenu { get; }
    public RelayCommand ClickDescriptorsMenu { get; }
    public RelayCommand ClickAttributeGroupsMenu { get; }
    public RelayCommand ClickMiscMenu { get; }
    public HashSet<string> CurrentlyExistingBodySlides { get; set; } = new(); // storage variable - keeps data from model to pass back to model on dump

    public void CopyInViewModelFromModel(Settings_OBody model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_BodyShapeDescriptorCreator descriptorCreator, VM_OBodyMiscSettings.Factory miscSettingsFactory, VM_BodySlideSetting.Factory bodySlideFactory, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory, VM_NPCAttributeCreator attCreator, Logger logger)
    {
        if (model == null)
        {
            return;
        }
        _logger.LogStartupEventStart("Loading OBody Menu UI");
        AttributeGroupMenu.CopyInViewModelFromModels(model.AttributeGroups); // get this first so other properties can reference it

        DescriptorUI.CopyInViewModelsFromModels(model.TemplateDescriptors);

        BodySlidesUI.CurrentlyExistingBodySlides = model.CurrentlyExistingBodySlides; // must load before presets

        BodySlidesUI.BodySlidesMale.Clear();
        BodySlidesUI.BodySlidesFemale.Clear();

        foreach (var preset in model.BodySlidesMale)
        {
            _logger.LogStartupEventStart("Loading UI for BodySlide " + preset.Label);
            var presetVM = bodySlideFactory(raceGroupingVMs, BodySlidesUI.BodySlidesMale);
            BodySlidesUI.BodySlidesMale.Add(presetVM);
            Task.Run(() => presetVM.CopyInViewModelFromModel(preset));       
            _logger.LogStartupEventEnd("Loading UI for BodySlide " + preset.Label);
        }

        var existingPresets = new HashSet<string>();
        foreach (var presetVM in BodySlidesUI.BodySlidesMale)
        {
            if (existingPresets.Contains(presetVM.Label))
            {
                presetVM.RenameByIndex();
            }
            existingPresets.Add(presetVM.Label);
        }

        existingPresets.Clear();
        foreach (var preset in model.BodySlidesFemale)
        {
            _logger.LogStartupEventStart("Loading UI for BodySlide " + preset.Label);
            var presetVM = bodySlideFactory(raceGroupingVMs, BodySlidesUI.BodySlidesFemale);
            BodySlidesUI.BodySlidesFemale.Add(presetVM);
            Task.Run(() => presetVM.CopyInViewModelFromModel(preset));
            _logger.LogStartupEventEnd("Loading UI for BodySlide " + preset.Label);
        }

        foreach (var presetVM in BodySlidesUI.BodySlidesFemale)
        {
            if (existingPresets.Contains(presetVM.Label))
            {
                presetVM.RenameByIndex();
            }
            existingPresets.Add(presetVM.Label);
        }

        MiscUI = MiscUI.GetViewModelFromModel(model);

        CurrentlyExistingBodySlides = model.CurrentlyExistingBodySlides;
        _logger.LogStartupEventEnd("Loading OBody Menu UI");
    }

    public Settings_OBody DumpViewModelToModel()
    {
        Settings_OBody model = new();
        model.TemplateDescriptors = DescriptorUI.DumpToViewModels();

        model.BodySlidesMale.Clear();
        model.BodySlidesFemale.Clear();

        foreach (var preset in BodySlidesUI.BodySlidesMale)
        {
            model.BodySlidesMale.Add(VM_BodySlideSetting.DumpViewModelToModel(preset));
        }
        foreach (var preset in BodySlidesUI.BodySlidesFemale)
        {
            model.BodySlidesFemale.Add(VM_BodySlideSetting.DumpViewModelToModel(preset));
        }
        VM_AttributeGroupMenu.DumpViewModelToModels(AttributeGroupMenu, model.AttributeGroups);

        MiscUI.DumpViewModelToModel(model);
        model.CurrentlyExistingBodySlides = CurrentlyExistingBodySlides;
        return model;
    }
}