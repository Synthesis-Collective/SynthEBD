using DynamicData.Binding;
using Noggog;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reflection.Emit;
using static SynthEBD.VM_BodyShapeDescriptor;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

/// <summary>
/// View model for the OBody / BodySlide settings tab, backing the <see cref="Settings_OBody"/>
/// model. Hosts the sub-menus shown in this tab — BodySlides, body-shape descriptors, attribute
/// groups, misc settings, the auto-annotator/trainer, and the body-type registry/profile editor —
/// swapping the active one via the Click* commands. Also propagates descriptor renames/deletions
/// across all BodySlides and referencing asset-pack subgroups.
/// </summary>
public class VM_SettingsOBody : VM, IHasAttributeGroupMenu
{
    private readonly Logger _logger;
    private readonly VM_AttributeGroupMenu.Factory _attributeGroupFactory;
    private readonly VM_BodySlidesMenu.Factory _bodySlidesMenuFactory;
    private readonly VM_BodySlidePlaceHolder.Factory _bodySlidePlaceHolderFactory;
    private readonly VM_BodySlideAnnotator.Factory _bodySlideAnnotatorFactory;
    private readonly Func<VM_SettingsTexMesh> _texMeshSettings;

    /// <summary>
    /// Builds the descriptor / BodySlides / attribute-group / misc / annotator sub-menus, wires
    /// the Click* navigation <see cref="RelayCommand"/>s, and subscribes to the body-selection
    /// mode so switching to BodySlide mid-session re-runs installed-body detection.
    /// </summary>
    public VM_SettingsOBody(
        VM_Settings_General generalSettingsVM,
        Func<VM_SettingsTexMesh> texMeshSettings,
        Logger logger,
        VM_BodyShapeDescriptorCreationMenu.Factory bodyShapeDescriptorCreationMenuFactory,
        VM_BodySlidesMenu.Factory bodySlidesMenuFactory,
        VM_OBodyMiscSettings.Factory miscSettingsFactory,
        VM_AttributeGroupMenu.Factory attributeGroupFactory,
        VM_BodySlidePlaceHolder.Factory bodySlidePlaceHolderFactory,
        VM_BodySlideAnnotator.Factory bodySlideAnnotatorFactory,
        VM_OBodyTrainer obodyTrainer,
        VM_BodyTypeRegistry bodyTypeRegistry,
        VM_BodyTypeProfileEditor bodyTypeProfileEditor
        )
    {
        _logger = logger;
        _attributeGroupFactory = attributeGroupFactory;
        _bodySlidesMenuFactory = bodySlidesMenuFactory;
        _bodySlidePlaceHolderFactory = bodySlidePlaceHolderFactory;
        _bodySlideAnnotatorFactory = bodySlideAnnotatorFactory;
        _texMeshSettings = texMeshSettings;

        DescriptorUI = bodyShapeDescriptorCreationMenuFactory(this, UpdateState, OnDescriptorValueDeletion, OnDescriptorCategoryDeletion);
        BodySlidesUI = _bodySlidesMenuFactory(generalSettingsVM.RaceGroupingEditor.RaceGroupings);
        AttributeGroupMenu = _attributeGroupFactory(generalSettingsVM.AttributeGroupMenu, true);
        MiscUI = miscSettingsFactory();
        AnnotatorUI = _bodySlideAnnotatorFactory(DescriptorUI, BodySlidesUI, MiscUI);
        BodyTypeRegistryUI = bodyTypeRegistry;
        BodyTypeProfileEditorUI = bodyTypeProfileEditor;

        BodySlidesUI.InitializeDescriptorFilter(this, generalSettingsVM.RaceGroupingEditor.RaceGroupings);
        BodyTypeProfileEditorUI.InitializeDescriptorFilter(this, generalSettingsVM.RaceGroupingEditor.RaceGroupings);

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

        // DEPRECATED / LEGACY: activates the hidden "Annotator Training" tab (never-completed
        // ML-on-slider-values trainer, superseded by the geometry/measurement-based Body Type Registry +
        // Profiles). The button in UC_SettingsOBody.xaml is Collapsed, so this command is unreachable
        // from the UI; retained for reference only. See VM_OBodyTrainer.
        ClickAnnotationTrainerMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = obodyTrainer
        );

        ClickBodyTypeRegistryMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = BodyTypeRegistryUI
        );

        ClickBodyTypeProfilesMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisplayedUI = BodyTypeProfileEditorUI
        );

        // Re-run installed-body detection when the user switches "Apply Body Shapes via" to
        // BodySlide mid-session. The startup pass in CopyInViewModelFromModel only fires if the
        // mode is already BodySlide at load; this covers the case where the user enables it later.
        // Skip(1) drops the synchronous initial emission (profiles aren't loaded yet at ctor time
        // and the startup pass handles the already-BodySlide case).
        generalSettingsVM.WhenAnyValue(x => x.BodySelectionMode)
            .Skip(1)
            .Subscribe(mode =>
            {
                if (mode == BodyShapeSelectionMode.BodySlide)
                {
                    BodyTypeProfileEditorUI.BeginAutoSelectProfileFromInstalledBody();
                }
            })
            .DisposeWith(this);
    }

    public object DisplayedUI { get; set; }
    public VM_BodyShapeDescriptorCreationMenu DescriptorUI { get; set; }
    public VM_BodySlidesMenu BodySlidesUI { get; set; }
    public VM_AttributeGroupMenu AttributeGroupMenu { get; set; }
    public VM_OBodyMiscSettings MiscUI { get; set; }
    public VM_BodySlideAnnotator AnnotatorUI { get; set; }
    public VM_BodyTypeRegistry BodyTypeRegistryUI { get; set; }
    public VM_BodyTypeProfileEditor BodyTypeProfileEditorUI { get; set; }
    public RelayCommand ClickBodySlidesMenu { get; }
    public RelayCommand ClickDescriptorsMenu { get; }
    public RelayCommand ClickAttributeGroupsMenu { get; }
    public RelayCommand ClickMiscMenu { get; }
    public RelayCommand ClickAnnotationMenu { get; }
    /// <summary>DEPRECATED / LEGACY - selects the hidden "Annotator Training" tab; unreachable from the UI. See VM_OBodyTrainer.</summary>
    public RelayCommand ClickAnnotationTrainerMenu { get; }
    public RelayCommand ClickBodyTypeRegistryMenu { get; }
    public RelayCommand ClickBodyTypeProfilesMenu { get; }
    public HashSet<string> CurrentlyExistingBodySlides { get; set; } = new(); // storage variable - keeps data from model to pass back to model on dump

    /// <summary>
    /// Model → VM: loads attribute groups (first, so others can reference them), descriptors,
    /// the existing-BodySlides set, then disposes/rebuilds the male/female BodySlide placeholder
    /// lists (de-duplicating labels), and loads misc / body-type registry / profile-editor /
    /// annotator state. Kicks off non-blocking installed-body detection.
    /// </summary>
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

        // Dispose any viewer VMs attached to the existing placeholders before
        // dropping them — otherwise the GL contexts/texture caches leak on reload.
        // CurrentlyDisplayedBodySlide is always the same instance as some placeholder's
        // AssociatedViewModel, so disposal happens transitively via the foreach.
        foreach (var ph in BodySlidesUI.BodySlidesMale)
        {
            if (ph.AssociatedViewModel != null)
            {
                ph.AssociatedViewModel.Dispose();
                ph.AssociatedViewModel = null;
            }
        }
        foreach (var ph in BodySlidesUI.BodySlidesFemale)
        {
            if (ph.AssociatedViewModel != null)
            {
                ph.AssociatedViewModel.Dispose();
                ph.AssociatedViewModel = null;
            }
        }
        BodySlidesUI.CurrentlyDisplayedBodySlide = null;
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

        MiscUI.CopyInViewModelFromModel(model);

        BodyTypeRegistryUI.CopyInViewModelFromModel(model);

        BodyTypeProfileEditorUI.CopyInViewModelFromModel(model);

        // Profiles + the FirstOrDefault selection are now populated. Kick off the non-blocking
        // detection of the installed default body so the editor opens on the matching profile.
        BodyTypeProfileEditorUI.BeginAutoSelectProfileFromInstalledBody();

        AnnotatorUI.CopyInFromModel();

        CurrentlyExistingBodySlides = model.CurrentlyExistingBodySlides;
        _logger.LogStartupEventEnd("Loading OBody Menu UI");
    }

    /// <summary>
    /// VM → Model: dumps descriptors, the male/female BodySlide presets (stripping non-manual
    /// auto-annotated descriptors first), attribute groups, misc / body-type / profile / classification-rule
    /// state, and the existing-BodySlides set into a new <see cref="Settings_OBody"/>.
    /// </summary>
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

        // clear auto-annotated descriptors
        foreach (var preset in BodySlidesUI.BodySlidesMale.And(BodySlidesUI.BodySlidesFemale).ToList())
        {
            preset.AssociatedModel.RemoveDescriptorsFromAllSlots(x => x.Source != BodyShapeAnnotationSource.Manual);
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

        BodyTypeRegistryUI.DumpViewModelToModel(model);

        BodyTypeProfileEditorUI.DumpViewModelToModel(model);

        model.BodySlideClassificationRules = AnnotatorUI.DumpToModel();

        model.CurrentlyExistingBodySlides = CurrentlyExistingBodySlides;
        return model;
    }

    /// <summary>Propagates a descriptor (category, value) rename across all BodySlides and asset-pack subgroup descriptor lists.</summary>
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

        foreach (var bodySlide in BodySlidesUI.BodySlidesMale.And(BodySlidesUI.BodySlidesFemale).ToList())
        {
            foreach (var slot in bodySlide.AssociatedModel.BodyShapeDescriptorsByWeight.Values)
            {
                UpdateDescriptors(slot, oldCategory, oldValue, newCategory, newValue);
            }
        }

        foreach (var subgroup in _texMeshSettings().AssetPacks.SelectMany(x => x.GetAllSubgroups()).ToArray())
        {
            UpdateDescriptors(subgroup.AssociatedModel.AllowedBodySlideDescriptors, oldCategory, oldValue, newCategory, newValue);
            UpdateDescriptors(subgroup.AssociatedModel.DisallowedBodySlideDescriptors, oldCategory, oldValue, newCategory, newValue);
            UpdateDescriptors(subgroup.AssociatedModel.PrioritizedBodySlideDescriptors, oldCategory, oldValue, newCategory, newValue);
        }
    }

    /// <summary>After a descriptor category is deleted, prompts to also remove all descriptors in that category from every BodySlide and asset-pack subgroup.</summary>
    public void OnDescriptorCategoryDeletion(string category)
    {
        if (MessageWindow.DisplayNotificationYesNo("", "Would you like to delete all " + category + " Descriptors from all BodySlides and Config Files that reference it?"))
        {
            BodySlidesUI.StashAndNullDisplayedBodySlide();

            foreach(var bs in BodySlidesUI.BodySlidesMale.And(BodySlidesUI.BodySlidesFemale).ToArray())
            {
                bs.AssociatedModel.RemoveDescriptorsFromAllSlots(x => x.Category == category);
            }

            foreach (var ap in _texMeshSettings().AssetPacks)
            {
                var subgroups = ap.GetAllSubgroups();
                foreach (var sg in subgroups)
                {
                    sg.AssociatedModel.AllowedBodySlideDescriptors.RemoveWhere(x => x.Category == category);
                    sg.AssociatedModel.DisallowedBodySlideDescriptors.RemoveWhere(x => x.Category == category);
                    sg.AssociatedModel.PrioritizedBodySlideDescriptors.RemoveWhere(x => x.Category == category);
                }
            }

            BodySlidesUI.RestoreStashedBodySlide();
        }
    }

    /// <summary>After a single descriptor value is deleted, prompts to also remove that descriptor (by signature) from every BodySlide and asset-pack subgroup.</summary>
    public void OnDescriptorValueDeletion(string decriptorSignature)
    {
        if (MessageWindow.DisplayNotificationYesNo("", "Would you like to delete all " + decriptorSignature + " Descriptors from all BodySlides and Config Files that reference it?"))
        {
            BodySlidesUI.StashAndNullDisplayedBodySlide();

            foreach (var bs in BodySlidesUI.BodySlidesMale.And(BodySlidesUI.BodySlidesFemale).ToArray())
            {
                bs.AssociatedModel.RemoveDescriptorsFromAllSlots(x => x.ToLabelSignature().ToString() == decriptorSignature);
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

            BodySlidesUI.RestoreStashedBodySlide();
        }
    }

    /// <summary>In-place renames matching descriptors in one collection: remaps category, and value only where the old value also matched.</summary>
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
}