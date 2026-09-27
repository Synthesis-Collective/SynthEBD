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
    private readonly DescriptorDefaultSynchronizer _descriptorDefaultSynchronizer;
    private readonly Func<VM_SettingsTexMesh> _texMeshSettings;
    private readonly PatcherState _patcherState;

    /// <summary>Carried from the model to the model: descriptor imports the user declined
    /// (<see cref="Settings_OBody.DeclinedDescriptorImports"/>), extended by this session's prompts.</summary>
    private HashSet<string> _declinedDescriptorImports = new(StringComparer.Ordinal);

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
        VM_BodyTypeProfileEditor bodyTypeProfileEditor,
        DescriptorDefaultSynchronizer descriptorDefaultSynchronizer,
        PatcherState patcherState
        )
    {
        _patcherState = patcherState;
        _logger = logger;
        _attributeGroupFactory = attributeGroupFactory;
        _bodySlidesMenuFactory = bodySlidesMenuFactory;
        _bodySlidePlaceHolderFactory = bodySlidePlaceHolderFactory;
        _bodySlideAnnotatorFactory = bodySlideAnnotatorFactory;
        _descriptorDefaultSynchronizer = descriptorDefaultSynchronizer;
        _texMeshSettings = texMeshSettings;

        DescriptorUI = bodyShapeDescriptorCreationMenuFactory(this, UpdateState, OnDescriptorValueDeletion, OnDescriptorCategoryDeletion);
        BodySlidesUI = _bodySlidesMenuFactory(generalSettingsVM.RaceGroupingEditor.RaceGroupings);
        AttributeGroupMenu = _attributeGroupFactory(generalSettingsVM.AttributeGroupMenu, true);
        MiscUI = miscSettingsFactory();
        AnnotatorUI = _bodySlideAnnotatorFactory(DescriptorUI, BodySlidesUI, MiscUI);
        BodyTypeRegistryUI = bodyTypeRegistry;
        BodyTypeProfileEditorUI = bodyTypeProfileEditor;
        // The editor reads the active rule file from the Misc menu; hand it a direct accessor now that
        // both exist, rather than letting it resolve this VM (which would re-enter Autofac mid-build).
        BodyTypeProfileEditorUI._miscSettingsAccessor = () => MiscUI;

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
        // Suspend descriptor-default sync for the whole hydration: the sub-menus load their
        // defaults wholesale below, and mid-load pushes would smear half-loaded values between
        // the Label by Sliders and Label by Measurements menus. The matching
        // EndHydrationAndReconcile at the end of this method deconflicts both sides once
        // everything is loaded, then enables live sync.
        _descriptorDefaultSynchronizer.BeginHydration();
        AttributeGroupMenu.CopyInViewModelFromModels(model.AttributeGroups); // get this first so other properties can reference it

        // Active rule files may reference descriptors this user doesn't have (a downloaded file carries
        // their definitions). Offer them before the descriptor menu loads, so accepted ones show up in
        // both labeling menus straight away.
        _declinedDescriptorImports = new HashSet<string>(model.DeclinedDescriptorImports ?? new HashSet<string>(), StringComparer.Ordinal);
        foreach (var bodyType in SettingsIO_BodyTypeRules.BodyTypesWithFiles(_patcherState.BodyTypeRuleSets))
        {
            var active = SettingsIO_BodyTypeRules.ResolveActive(model, _patcherState.BodyTypeRuleSets, bodyType);
            if (active == null) continue;
            var accepted = OfferDescriptorImport(active, model.TemplateDescriptors);
            if (accepted.Count > 0) BodyTypeRuleSet.MergeMissingDescriptorDefinitions(model.TemplateDescriptors, accepted);
        }

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
        MiscUI.RebuildRuleFileSelections(model);

        BodyTypeRegistryUI.CopyInViewModelFromModel(model);

        BodyTypeProfileEditorUI.CopyInViewModelFromModel(model);

        // Profiles + the initial selection (last session's profile when saved, else FirstOrDefault)
        // are now populated. Kick off the non-blocking detection of the installed default body so
        // the editor opens on the matching profile — it no-ops when a saved selection was restored.
        BodyTypeProfileEditorUI.BeginAutoSelectProfileFromInstalledBody();

        AnnotatorUI.CopyInFromModel();

        CurrentlyExistingBodySlides = model.CurrentlyExistingBodySlides;

        // Both labeling menus are fully loaded: deconflict their shared per-category default
        // descriptors (empty side adopts; true conflicts follow the Misc toggle) and enable
        // live two-way sync of future edits.
        _descriptorDefaultSynchronizer.EndHydrationAndReconcile();

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

        model.LastSelectedSliderAnnotationBodyType = AnnotatorUI.LastSelectedBodyTypeGroup;

        model.CurrentlyExistingBodySlides = CurrentlyExistingBodySlides;
        model.DeclinedDescriptorImports = new HashSet<string>(_declinedDescriptorImports, StringComparer.Ordinal);
        return model;
    }

    /// <summary>
    /// Writes the labeling menus' current state for <paramref name="bodyType"/> (Label by Sliders rules and
    /// the measurement profile, including unsaved edits) into <paramref name="ruleFile"/>. Used before a
    /// file switch -- so the file being left keeps its edits and is written on the next save -- and
    /// before Duplicate, so the copy includes them.
    /// </summary>
    public void FlushLabelingStateIntoRuleFile(string bodyType, BodyTypeRuleSet ruleFile)
    {
        if (ruleFile == null) return;
        ruleFile.SliderRules = AnnotatorUI.DumpRulesForBodyType(bodyType);
        ruleFile.MeasurementProfile = BodyTypeProfileEditorUI.DumpProfileForRuleFile(bodyType);
        ruleFile.NormalizeBodyType();
    }

    /// <summary>
    /// Makes <paramref name="next"/> the active rule file for <paramref name="bodyType"/>: flushes the menus
    /// into <paramref name="previous"/>, offers any descriptor definitions <paramref name="next"/> needs, then
    /// reloads the body type in both labeling menus from <paramref name="next"/> (deep copies, so the menus
    /// never alias a file object). Wrapped in a synchronizer hydration pass so the default-descriptor sync
    /// doesn't push half-swapped values between the menus.
    /// </summary>
    public void SwitchActiveRuleFile(string bodyType, BodyTypeRuleSet? previous, BodyTypeRuleSet next)
    {
        if (next == null) return;
        if (previous != null) FlushLabelingStateIntoRuleFile(bodyType, previous);

        var accepted = OfferDescriptorImport(next, DescriptorUI.DumpToViewModels());
        if (accepted.Count > 0)
        {
            DescriptorUI.MergeInMissingModels(accepted, DescriptorRulesMergeMode.Skip, new List<string>());
        }

        _descriptorDefaultSynchronizer.BeginHydration();
        try
        {
            AnnotatorUI.ReplaceBodyTypeRules(bodyType,
                JSONhandler<SliderClassificationRulesByBodyType>.CloneViaJSON(next.SliderRules ?? new SliderClassificationRulesByBodyType()));
            BodyTypeProfileEditorUI.ReplaceProfileForBodyType(bodyType,
                next.MeasurementProfile == null ? null : JSONhandler<BodyTypeProfile>.CloneViaJSON(next.MeasurementProfile));
        }
        finally
        {
            _descriptorDefaultSynchronizer.EndHydrationAndReconcile();
        }
        BodyTypeProfileEditorUI.RefreshActiveRuleFileLabel();
        _logger.LogMessage("Body Type Rules: '" + bodyType + "' now uses '" + next.Name + "' (" + next.FileName + ").");
    }

    /// <summary>
    /// Asks whether to import the descriptor definitions <paramref name="ruleFile"/> carries that
    /// <paramref name="templateDescriptors"/> lacks. Returns the definitions to import (empty when there
    /// are none or the user skips). A skip is remembered per file and descriptor so it isn't asked again.
    /// Existing definitions are never overwritten -- only missing ones are ever offered. When dialogs are
    /// suppressed (headless harness) nothing is imported and nothing is recorded as declined.
    /// </summary>
    private List<BodyShapeDescriptorShell> OfferDescriptorImport(BodyTypeRuleSet ruleFile, IEnumerable<BodyShapeDescriptorShell> templateDescriptors)
    {
        var missing = ruleFile.FindMissingDescriptorDefinitions(templateDescriptors, _declinedDescriptorImports);
        if (missing.Count == 0) return missing;

        var lines = missing
            .SelectMany(s => s.Descriptors.Select(d => "  " + BodyShapeDescriptor.LabelSignature.ToSignatureString(s.Category, d.ID.Value)))
            .ToList();
        if (MessageWindow.SuppressAllDialogs)
        {
            _logger.LogMessage("Body Type Rules: '" + ruleFile.FileName + "' uses " + lines.Count + " descriptor(s) not in your settings; import prompt suppressed, not imported.");
            return new List<BodyShapeDescriptorShell>();
        }

        var shown = lines.Take(30).ToList();
        if (lines.Count > 30) shown.Add("  ... and " + (lines.Count - 30) + " more");
        bool import = MessageWindow.DisplayNotificationYesNo("Import Body Shape Descriptors?",
            "The body type rule file '" + ruleFile.Name + "' (" + ruleFile.FileName + ") uses " + lines.Count
            + " body shape descriptor(s) that aren't in your settings:\n\n" + string.Join("\n", shown)
            + "\n\nImport them? Your existing descriptors are never changed.\n"
            + "If you skip, rules that assign these descriptors will have no effect, and you won't be asked again for this file.");

        if (import)
        {
            _logger.LogMessage("Body Type Rules: imported " + lines.Count + " descriptor definition(s) from '" + ruleFile.FileName + "'.");
            return missing;
        }

        foreach (var shell in missing)
        {
            foreach (var d in shell.Descriptors)
            {
                _declinedDescriptorImports.Add(BodyTypeRuleSet.DeclineKey(ruleFile.FileName, shell.Category, d.ID.Value));
            }
        }
        _logger.LogMessage("Body Type Rules: skipped importing " + lines.Count + " descriptor definition(s) from '" + ruleFile.FileName + "'.");
        return new List<BodyShapeDescriptorShell>();
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