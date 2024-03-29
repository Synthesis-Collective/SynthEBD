using DynamicData.Binding;
using Noggog;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reflection.Emit;
using static SynthEBD.VM_BodyShapeDescriptor;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

public class VM_SettingsOBody : VM, IHasAttributeGroupMenu
{
    private readonly Logger _logger;
    private readonly VM_AttributeGroupMenu.Factory _attributeGroupFactory;
    private readonly VM_BodySlidesMenu.Factory _bodySlidesMenuFactory;
    private readonly VM_BodySlidePlaceHolder.Factory _bodySlidePlaceHolderFactory;
    private readonly VM_BodySlideAnnotator.Factory _bodySlideAnnotatorFactory;

    public VM_SettingsOBody(
        VM_Settings_General generalSettingsVM,
        Logger logger,
        VM_BodyShapeDescriptorCreationMenu.Factory bodyShapeDescriptorCreationMenuFactory,
        VM_BodySlidesMenu.Factory bodySlidesMenuFactory,
        VM_OBodyMiscSettings.Factory miscSettingsFactory,
        VM_AttributeGroupMenu.Factory attributeGroupFactory,
        VM_BodySlidePlaceHolder.Factory bodySlidePlaceHolderFactory,
        VM_BodySlideAnnotator.Factory bodySlideAnnotatorFactory
        )
    {
        _logger = logger;
        _attributeGroupFactory = attributeGroupFactory;
        _bodySlidesMenuFactory = bodySlidesMenuFactory;
        _bodySlidePlaceHolderFactory = bodySlidePlaceHolderFactory;
        _bodySlideAnnotatorFactory = bodySlideAnnotatorFactory;

        DescriptorUI = bodyShapeDescriptorCreationMenuFactory(this);
        BodySlidesUI = _bodySlidesMenuFactory(generalSettingsVM.RaceGroupingEditor.RaceGroupings);
        AttributeGroupMenu = _attributeGroupFactory(generalSettingsVM.AttributeGroupMenu, true);
        MiscUI = miscSettingsFactory();
        AnnotatorUI = _bodySlideAnnotatorFactory(DescriptorUI, BodySlidesUI, MiscUI);

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

        ClickAnnotationMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = AnnotatorUI
        );
    }

    public object DisplayedUI { get; set; }
    public VM_BodyShapeDescriptorCreationMenu DescriptorUI { get; set; }
    public VM_BodySlidesMenu BodySlidesUI { get; set; }
    public VM_AttributeGroupMenu AttributeGroupMenu { get; set; }
    public VM_OBodyMiscSettings MiscUI { get; set; }
    public VM_BodySlideAnnotator AnnotatorUI { get; set; }
    public RelayCommand ClickBodySlidesMenu { get; }
    public RelayCommand ClickDescriptorsMenu { get; }
    public RelayCommand ClickAttributeGroupsMenu { get; }
    public RelayCommand ClickMiscMenu { get; }
    public RelayCommand ClickAnnotationMenu { get; }
    public HashSet<string> CurrentlyExistingBodySlides { get; set; } = new(); // storage variable - keeps data from model to pass back to model on dump

    public void CopyInViewModelFromModel(Settings_OBody model, VM_BodyShapeDescriptorCreator descriptorCreator, VM_OBodyMiscSettings.Factory miscSettingsFactory, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory, VM_NPCAttributeCreator attCreator, Logger logger)
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
            var presetVM = _bodySlidePlaceHolderFactory(preset, BodySlidesUI.BodySlidesMale);
            BodySlidesUI.BodySlidesMale.Add(presetVM);      
        }

        var existingPresets = new HashSet<string>();
        foreach (var presetVM in BodySlidesUI.BodySlidesMale)
        {
            if (existingPresets.Contains(presetVM.Label))
            {
                presetVM.RenameByIndex();
                presetVM.AssociatedModel.Label = presetVM.Label;
                if (presetVM.AssociatedViewModel != null)
                {
                    presetVM.AssociatedViewModel.Label = presetVM.Label;
                }
            }
            existingPresets.Add(presetVM.Label);
        }

        existingPresets.Clear();
        foreach (var preset in model.BodySlidesFemale)
        {
            var presetVM = _bodySlidePlaceHolderFactory(preset, BodySlidesUI.BodySlidesFemale);
            BodySlidesUI.BodySlidesFemale.Add(presetVM);
        }

        foreach (var presetVM in BodySlidesUI.BodySlidesFemale)
        {
            if (existingPresets.Contains(presetVM.Label))
            {
                presetVM.RenameByIndex();
                presetVM.AssociatedModel.Label = presetVM.Label;
                if (presetVM.AssociatedViewModel != null)
                {
                    presetVM.AssociatedViewModel.Label = presetVM.Label;
                }
            }
            existingPresets.Add(presetVM.Label);
        }

        MiscUI = MiscUI.GetViewModelFromModel(model);

        AnnotatorUI.CopyInFromModel();

        CurrentlyExistingBodySlides = model.CurrentlyExistingBodySlides;
        _logger.LogStartupEventEnd("Loading OBody Menu UI");
    }

    public Settings_OBody DumpViewModelToModel()
    {
        Settings_OBody model = new();
        model.TemplateDescriptors = DescriptorUI.DumpToViewModels();

        model.BodySlidesMale.Clear();
        model.BodySlidesFemale.Clear();

        if (BodySlidesUI.CurrentlyDisplayedBodySlide != null)
        {
            BodySlidesUI.CurrentlyDisplayedBodySlide.AssociatedPlaceHolder.AssociatedModel = BodySlidesUI.CurrentlyDisplayedBodySlide.DumpToModel();
        }

        foreach (var preset in BodySlidesUI.BodySlidesMale)
        {
            model.BodySlidesMale.Add(preset.AssociatedModel);
        }
        foreach (var preset in BodySlidesUI.BodySlidesFemale)
        {
            model.BodySlidesFemale.Add(preset.AssociatedModel);
        }
        VM_AttributeGroupMenu.DumpViewModelToModels(AttributeGroupMenu, model.AttributeGroups);

        MiscUI.DumpViewModelToModel(model);

        model.BodySlideClassificationRules = AnnotatorUI.DumpToModel();

        model.CurrentlyExistingBodySlides = CurrentlyExistingBodySlides;
        return model;
    }
}