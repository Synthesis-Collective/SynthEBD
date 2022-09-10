using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_SettingsOBody : VM, IHasAttributeGroupMenu
{
    public VM_SettingsOBody(
        VM_Settings_General generalSettingsVM,
        VM_BodyShapeDescriptorCreationMenu.Factory bodyShapeDescriptorCreationMenuFactory)
    {
        DescriptorUI = bodyShapeDescriptorCreationMenuFactory(this);
        BodySlidesUI = new VM_BodySlidesMenu(this, generalSettingsVM.RaceGroupings);
        AttributeGroupMenu = new(generalSettingsVM.AttributeGroupMenu, true);

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
    public VM_OBodyMiscSettings MiscUI { get; set; } = new();
    public RelayCommand ClickBodySlidesMenu { get; }
    public RelayCommand ClickDescriptorsMenu { get; }
    public RelayCommand ClickAttributeGroupsMenu { get; }
    public RelayCommand ClickMiscMenu { get; }

    public static void GetViewModelFromModel(Settings_OBody model, VM_SettingsOBody viewModel, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
        viewModel.AttributeGroupMenu.CopyInViewModelFromModels(model.AttributeGroups); // get this first so other properties can reference it

        viewModel.DescriptorUI.TemplateDescriptors = VM_BodyShapeDescriptorShell.GetViewModelsFromModels(model.TemplateDescriptors, raceGroupingVMs, viewModel);

        viewModel.DescriptorUI.TemplateDescriptorList.Clear();
        foreach (var descriptor in model.TemplateDescriptors)
        {
            var subVm = new VM_BodyShapeDescriptor(new VM_BodyShapeDescriptorShell(
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
            var presetVM = new VM_BodySlideSetting(viewModel.DescriptorUI, raceGroupingVMs, viewModel.BodySlidesUI.BodySlidesMale, viewModel);
            VM_BodySlideSetting.GetViewModelFromModel(preset, presetVM, viewModel.DescriptorUI, raceGroupingVMs, viewModel);
            viewModel.BodySlidesUI.BodySlidesMale.Add(presetVM);
        }

        foreach (var preset in model.BodySlidesFemale)
        {
            var presetVM = new VM_BodySlideSetting(viewModel.DescriptorUI, raceGroupingVMs, viewModel.BodySlidesUI.BodySlidesFemale, viewModel);
            VM_BodySlideSetting.GetViewModelFromModel(preset, presetVM, viewModel.DescriptorUI, raceGroupingVMs, viewModel);
            viewModel.BodySlidesUI.BodySlidesFemale.Add(presetVM);
        }

        viewModel.MiscUI = VM_OBodyMiscSettings.GetViewModelFromModel(model);
    }

    public static void DumpViewModelToModel(Settings_OBody model, VM_SettingsOBody viewModel)
    {
        model.TemplateDescriptors = VM_BodyShapeDescriptorShell.DumpViewModelsToModels(viewModel.DescriptorUI.TemplateDescriptors);

        model.BodySlidesMale.Clear();
        model.BodySlidesFemale.Clear();

        foreach (var preset in viewModel.BodySlidesUI.BodySlidesMale)
        {
            model.BodySlidesMale.Add(VM_BodySlideSetting.DumpViewModelToModel(preset));
        }
        foreach (var preset in viewModel.BodySlidesUI.BodySlidesFemale)
        {
            model.BodySlidesFemale.Add(VM_BodySlideSetting.DumpViewModelToModel(preset));
        }
        VM_AttributeGroupMenu.DumpViewModelToModels(viewModel.AttributeGroupMenu, model.AttributeGroups);

        VM_OBodyMiscSettings.DumpViewModelToModel(model, viewModel.MiscUI);
    }
}