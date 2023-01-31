using System.Collections.ObjectModel;
using static SynthEBD.VM_BodyShapeDescriptor;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

public class VM_SettingsOBody : VM, IHasAttributeGroupMenu
{
    private readonly VM_AttributeGroupMenu.Factory _attributeGroupFactory;
    private readonly VM_BodySlidesMenu.Factory _bodySlidesMenuFactory;

    public VM_SettingsOBody(
        VM_Settings_General generalSettingsVM,
        VM_BodyShapeDescriptorCreationMenu.Factory bodyShapeDescriptorCreationMenuFactory,
        VM_BodySlidesMenu.Factory bodySlidesMenuFactory,
        VM_OBodyMiscSettings.Factory miscSettingsFactory,
        VM_AttributeGroupMenu.Factory attributeGroupFactory
        )
    {
        _attributeGroupFactory = attributeGroupFactory;
        _bodySlidesMenuFactory = bodySlidesMenuFactory;

        DescriptorUI = bodyShapeDescriptorCreationMenuFactory(this);
        BodySlidesUI = _bodySlidesMenuFactory(this, generalSettingsVM.RaceGroupingEditor.RaceGroupings);
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

    public void CopyInViewModelFromModel(Settings_OBody model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_BodyShapeDescriptorCreator descriptorCreator, VM_OBodyMiscSettings.Factory miscSettingsFactory, VM_BodySlideSetting.Factory bodySlideFactory, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory, VM_NPCAttributeCreator attCreator, Logger logger)
    {
        AttributeGroupMenu.CopyInViewModelFromModels(model.AttributeGroups); // get this first so other properties can reference it

        DescriptorUI.CopyInViewModelsFromModels(model.TemplateDescriptors);

        DescriptorUI.TemplateDescriptorList.Clear();
        foreach (var descriptor in model.TemplateDescriptors)
        {
            var subVm = descriptorCreator.CreateNew(descriptorCreator.CreateNewShell(
                new ObservableCollection<VM_BodyShapeDescriptorShell>(), 
                raceGroupingVMs, this),
                raceGroupingVMs, 
                this);
            subVm.CopyInViewModelFromModel(descriptor, raceGroupingVMs, this);
            DescriptorUI.TemplateDescriptorList.Add(subVm);
        }

        BodySlidesUI.CurrentlyExistingBodySlides = model.CurrentlyExistingBodySlides; // must load before presets

        BodySlidesUI.BodySlidesMale.Clear();
        BodySlidesUI.BodySlidesFemale.Clear();

        var existingPresets = new HashSet<string>();
        foreach (var preset in model.BodySlidesMale)
        {
            var presetVM = bodySlideFactory(DescriptorUI, raceGroupingVMs, BodySlidesUI.BodySlidesMale);
            presetVM.CopyInViewModelFromModel(preset);
            if (existingPresets.Contains(presetVM.Label))
            {
                presetVM.RenameByIndex();
            }
            BodySlidesUI.BodySlidesMale.Add(presetVM);
            existingPresets.Add(presetVM.Label);
        }

        existingPresets.Clear();
        foreach (var preset in model.BodySlidesFemale)
        {
            var presetVM = bodySlideFactory(DescriptorUI, raceGroupingVMs, BodySlidesUI.BodySlidesFemale);
            presetVM.CopyInViewModelFromModel(preset);
            if (existingPresets.Contains(presetVM.Label))
            {
                presetVM.RenameByIndex();
            }
            BodySlidesUI.BodySlidesFemale.Add(presetVM);
            existingPresets.Add(presetVM.Label);
        }

        MiscUI = MiscUI.GetViewModelFromModel(model);

        CurrentlyExistingBodySlides = model.CurrentlyExistingBodySlides;
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

        VM_OBodyMiscSettings.DumpViewModelToModel(model, MiscUI);
        model.CurrentlyExistingBodySlides = CurrentlyExistingBodySlides;
        return model;
    }
}