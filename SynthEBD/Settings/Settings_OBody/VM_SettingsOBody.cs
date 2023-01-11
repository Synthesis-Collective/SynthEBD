using System.Collections.ObjectModel;
using static SynthEBD.VM_BodyShapeDescriptor;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

public class VM_SettingsOBody : VM, IHasAttributeGroupMenu
{
    private readonly SettingsIO_OBody _oBodyIO;
    private readonly VM_OBodyMiscSettings.Factory _miscSettingsFactory;
    private readonly VM_AttributeGroupMenu.Factory _attributeGroupFactory;
    private readonly VM_BodyShapeDescriptor.Factory _bodyShapeDescriptorFactory;
    private readonly VM_BodySlideSetting.Factory _bodySlideFactory;
    private readonly AttributeMatcher _attributeMatcher;
    private readonly Logger _logger;
    private readonly IStateProvider _stateProvider;
    public VM_SettingsOBody(
        VM_Settings_General generalSettingsVM,
        VM_BodyShapeDescriptorCreationMenu.Factory bodyShapeDescriptorCreationMenuFactory,
        VM_OBodyMiscSettings.Factory miscSettingsFactory,
        VM_AttributeGroupMenu.Factory attributeGroupFactory,
        VM_BodyShapeDescriptor.Factory bodyShapeDescriptorFactory,
        VM_BodySlideSetting.Factory bodySlideFactory,
        SettingsIO_OBody oBodyIO,
        AttributeMatcher attributeMatcher,
        Logger logger,
        IStateProvider stateProvider)
    {
        _oBodyIO = oBodyIO;
        _miscSettingsFactory = miscSettingsFactory;
        _attributeGroupFactory = attributeGroupFactory;
        _bodyShapeDescriptorFactory = bodyShapeDescriptorFactory;
        _bodySlideFactory = bodySlideFactory;
        _attributeMatcher = attributeMatcher;
        _logger = logger;
        _stateProvider = stateProvider;

        DescriptorUI = bodyShapeDescriptorCreationMenuFactory(this);
        BodySlidesUI = new VM_BodySlidesMenu(this, generalSettingsVM.RaceGroupingEditor.RaceGroupings, _bodySlideFactory);
        AttributeGroupMenu = _attributeGroupFactory(generalSettingsVM.AttributeGroupMenu, true);
        MiscUI = miscSettingsFactory();

        DisplayedUI = BodySlidesUI;

        ClickBodySlidesMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => this.DisplayedUI = BodySlidesUI
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

    public static void GetViewModelFromModel(Settings_OBody model, VM_SettingsOBody viewModel, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_BodyShapeDescriptorCreator descriptorCreator, VM_OBodyMiscSettings.Factory miscSettingsFactory, VM_BodySlideSetting.Factory bodySlideFactory, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory, VM_NPCAttributeCreator attCreator, Logger logger)
    {
        viewModel.AttributeGroupMenu.CopyInViewModelFromModels(model.AttributeGroups); // get this first so other properties can reference it

        viewModel.DescriptorUI.TemplateDescriptors = VM_BodyShapeDescriptorShell.GetViewModelsFromModels(model.TemplateDescriptors, raceGroupingVMs, viewModel, descriptorCreator);

        viewModel.DescriptorUI.TemplateDescriptorList.Clear();
        foreach (var descriptor in model.TemplateDescriptors)
        {
            var subVm = descriptorCreator.CreateNew(descriptorCreator.CreateNewShell(
                new ObservableCollection<VM_BodyShapeDescriptorShell>(), 
                raceGroupingVMs, viewModel),
                raceGroupingVMs, 
                viewModel);
            subVm.CopyInViewModelFromModel(descriptor, raceGroupingVMs, viewModel);
            viewModel.DescriptorUI.TemplateDescriptorList.Add(subVm);
        }

        viewModel.BodySlidesUI.CurrentlyExistingBodySlides = model.CurrentlyExistingBodySlides; // must load before presets

        viewModel.BodySlidesUI.BodySlidesMale.Clear();
        viewModel.BodySlidesUI.BodySlidesFemale.Clear();

        foreach (var preset in model.BodySlidesMale)
        {
            var presetVM = bodySlideFactory(viewModel.DescriptorUI, raceGroupingVMs, viewModel.BodySlidesUI.BodySlidesMale);
            VM_BodySlideSetting.GetViewModelFromModel(preset, presetVM, viewModel.DescriptorUI, raceGroupingVMs, viewModel, attCreator, logger, descriptorSelectionFactory);
            viewModel.BodySlidesUI.BodySlidesMale.Add(presetVM);
        }

        foreach (var preset in model.BodySlidesFemale)
        {
            var presetVM = bodySlideFactory(viewModel.DescriptorUI, raceGroupingVMs, viewModel.BodySlidesUI.BodySlidesFemale);
            VM_BodySlideSetting.GetViewModelFromModel(preset, presetVM, viewModel.DescriptorUI, raceGroupingVMs, viewModel, attCreator, logger, descriptorSelectionFactory);
            viewModel.BodySlidesUI.BodySlidesFemale.Add(presetVM);
        }

        viewModel.MiscUI = viewModel.MiscUI.GetViewModelFromModel(model);

        viewModel.CurrentlyExistingBodySlides = model.CurrentlyExistingBodySlides;
    }

    public Settings_OBody DumpViewModelToModel()
    {
        Settings_OBody model = new();
        model.TemplateDescriptors = VM_BodyShapeDescriptorShell.DumpViewModelsToModels(DescriptorUI.TemplateDescriptors);

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

        VM_OBodyMiscSettings.DumpViewModelToModel(model, MiscUI);
        model.CurrentlyExistingBodySlides = CurrentlyExistingBodySlides;
        return model;
    }
}