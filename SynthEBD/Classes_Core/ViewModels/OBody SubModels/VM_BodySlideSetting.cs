using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.Windows.Media;
using ReactiveUI;
using static SynthEBD.VM_NPCAttribute;
using ControlzEx.Standard;
using System.Diagnostics;
using System.Linq;
using DynamicData;
using DynamicData.Binding;
using System.Reactive.Linq;

namespace SynthEBD;

[DebuggerDisplay("{Label}")]
public class VM_BodySlideSetting : VM
{
    private IEnvironmentStateProvider _environmentProvider;
    public VM_SettingsOBody ParentMenuVM;
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly BodySlideAnnotator _bodySlideAnnotator;
    private readonly VM_BodyShapeDescriptorCreationMenu _bodyShapeDescriptors;
    private readonly ObservableCollection<VM_RaceGrouping> _raceGroupingVMs;
    private readonly VM_BodySlidePlaceHolder.Factory _placeHolderFactory;
    private readonly VM_BodySlideSetting.Factory _selfFactory;
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
    private readonly PatcherState _patcherState;

    private readonly Logger _logger;

    public delegate VM_BodySlideSetting Factory(VM_BodySlidePlaceHolder associatedPlaceHolder, ObservableCollection<VM_RaceGrouping> raceGroupingVMs);
    public VM_BodySlideSetting(VM_BodySlidePlaceHolder associatedPlaceHolder, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_SettingsOBody oBodySettingsVM, VM_NPCAttributeCreator attributeCreator, BodySlideAnnotator bodySlideAnnotator, IEnvironmentStateProvider environmentProvider, Logger logger, Factory selfFactory, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory, VM_BodySlidePlaceHolder.Factory placeHolderFactory, PatcherState patcherState, Func<VM_CharacterViewer> characterViewerFactory)
    {
        ParentMenuVM = oBodySettingsVM;

        AssociatedPlaceHolder = associatedPlaceHolder;
        AssociatedPlaceHolder.AssociatedViewModel = this;

        _environmentProvider = environmentProvider;
        _attributeCreator = attributeCreator;
        _bodySlideAnnotator = bodySlideAnnotator;
        _bodyShapeDescriptors = oBodySettingsVM.DescriptorUI;
        _raceGroupingVMs = raceGroupingVMs;
        _selfFactory = selfFactory;
        _placeHolderFactory = placeHolderFactory;
        _descriptorSelectionFactory = descriptorSelectionFactory;
        _patcherState = patcherState;
        _logger = logger;

        CharacterViewer = characterViewerFactory();
        CharacterViewer.Mode = ViewerMode.ReadOnly;

        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        // Recompute aggregate rollup whenever slot membership changes.
        WeightSlots.ToObservableChangeSet()
            .Subscribe(_ => UpdateAggregateAnnotationState())
            .DisposeWith(this);

        ToggleLock = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                if (!ReferenceUnlocked)
                {
                    if (MessageWindow.DisplayNotificationYesNo("Confirm Unlock", "Bodyslide is exactly the name that will be fed to O/AutoBody and is expected to be read from the data in your CalienteTools\\BodySlide\\SliderPresets directory. Are you sure you want to unlock it for editing?"))
                    {
                        UnlockReference();
                    }
                }
                else
                {
                    LockReference();
                }
            });

        AddAllowedAttribute = new RelayCommand(
            canExecute: _ => true,
            execute: _ => AllowedAttributes.Add(_attributeCreator.CreateNewFromUI(AllowedAttributes, true, null, ParentMenuVM.AttributeGroupMenu.Groups))
        );

        AddDisallowedAttribute = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisallowedAttributes.Add(_attributeCreator.CreateNewFromUI(DisallowedAttributes, false, null, ParentMenuVM.AttributeGroupMenu.Groups))
        );

        DeleteMe = new RelayCommand(
            canExecute: _ => true,
            execute: _ => AssociatedPlaceHolder.ParentCollection.Remove(AssociatedPlaceHolder)
        );

        ToggleHide = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (AssociatedPlaceHolder.IsHidden)
                {
                    AssociatedPlaceHolder.IsHidden = false;
                    HideButtonText = "Hide";
                }
                else
                {
                    AssociatedPlaceHolder.IsHidden = true;
                    HideButtonText = "Unhide";
                }
            }
        );

        CloneCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Clone()
        );

        AcceptAutoAnnotations = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                foreach (var slot in WeightSlots)
                {
                    AcceptAutoAnnotationsForMenu(slot.DescriptorsSelectionMenu);
                }
                UpdateAggregateAnnotationState();
            }
        );

        AddWeightSlotCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => AddWeightSlot(NewSlotWeight)
        );

        this.WhenAnyValue(x => x.ReferencedBodySlide).Subscribe(_ => UpdateStatusDisplay()).DisposeWith(this);
        this.WhenAnyValue(x => x.AggregateAnnotationState).Subscribe(state =>
        {
            ShowAcceptAnnotationsButton = state != BodyShapeAnnotationState.None && state != BodyShapeAnnotationState.Manual;
            UpdateStatusDisplay();
        }).DisposeWith(this);

        // Section B5: when the user picks a different weight tab, load the configured preview NPC
        // for that (gender, weight) and apply this preset's morph at that weight. Skip(1) drops
        // the construction-time emission so we don't fire a load before CopyInViewModelFromModel
        // has populated the slots; named lambda param avoids the discard-shadowing trap from
        // Session 3 surprise #3.
        this.WhenAnyValue(x => x.SelectedWeightSlot)
            .Skip(1)
            .Where(slot => slot != null)
            .Throttle(TimeSpan.FromMilliseconds(50), RxApp.MainThreadScheduler)
            .Subscribe(slot => RefreshPreview(slot))
            .DisposeWith(this);

        // Live deformation refresh when slider values change (descriptor edits flowing through
        // SliderValues). Throttle to avoid hammering the GL thread mid-edit.
        SliderValues.ToObservableChangeSet()
            .Throttle(TimeSpan.FromMilliseconds(150), RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshPreview(SelectedWeightSlot))
            .DisposeWith(this);
    }

    /// <summary>
    /// Loads the configured preview NPC for the active weight slot and applies this preset's
    /// BodySlide deformation at the slot's weight. Safe to call with a null slot (no-op) and
    /// safe to call rapidly — VM_CharacterViewer.LoadNpcAsync owns its own cancellation token,
    /// and ApplyBodySlide is queued behind _sceneRebuildPending if the scene is mid-rebuild.
    /// </summary>
    private async void RefreshPreview(VM_BodySlideWeightSlot slot)
    {
        if (slot == null || CharacterViewer == null) return;
        if (lk == null) return;

        try
        {
            var gender = ResolveGender();
            var preview = _patcherState?.OBodySettings?.PreviewNpcs;
            FormKey npc = FormKey.Null;
            if (preview != null && preview.WeightPreviewNpcs.TryGetValue(slot.Weight, out var pair) && pair != null)
            {
                npc = gender == Gender.Female ? pair.FemaleNpc : pair.MaleNpc;
            }

            if (npc.IsNull)
            {
                _logger?.LogMessage("VM_BodySlideSetting: no preview NPC configured for weight " + slot.Weight + " (" + gender + ")");
                return;
            }

            await CharacterViewer.LoadNpcAsync(npc, lk);
            CharacterViewer.ApplyBodySlide(AssociatedPlaceHolder.AssociatedModel, slot.Weight);
        }
        catch (Exception ex)
        {
            // LogError (not LogMessage) so the failure is actually visible in the Status
            // Log instead of silently switching tabs with no entry. Full stack is logged
            // because most NRE / index-out-of-range failures here have empty Message.
            _logger?.LogError("VM_BodySlideSetting.RefreshPreview failed: " + ExceptionLogger.GetExceptionStack(ex));
        }
    }

    /// <summary>
    /// Determines whether this preset is hosted in the male or female list. ParentCollection
    /// is the same reference as either BodySlidesUI.BodySlidesMale or .BodySlidesFemale, so a
    /// reference comparison is authoritative — no need to inspect descriptors or destinations.
    /// </summary>
    private Gender ResolveGender()
    {
        var parentColl = AssociatedPlaceHolder?.ParentCollection;
        if (parentColl != null && ReferenceEquals(parentColl, ParentMenuVM?.BodySlidesUI?.BodySlidesMale))
        {
            return Gender.Male;
        }
        return Gender.Female;
    }

    public string Label { get; set; } = "";
    public string ReferencedBodySlide { get; set; } = "";
    public string SliderGroup { get; set; } = "";
    public string Notes { get; set; } = "";

    /// <summary>Per-weight descriptor selection menus, sorted by weight ascending.</summary>
    public ObservableCollection<VM_BodySlideWeightSlot> WeightSlots { get; } = new();
    public VM_BodySlideWeightSlot SelectedWeightSlot { get; set; }
    public int NewSlotWeight { get; set; } = 50;
    public BodyShapeAnnotationState AggregateAnnotationState { get; private set; } = BodyShapeAnnotationState.None;

    /// <summary>Default weight slots that the user removed for this preset; persisted via DumpToModel.</summary>
    public HashSet<int> RemovedDefaultWeightSlots { get; private set; } = new();

    public ObservableCollection<FormKey> AllowedRaces { get; set; } = new();
    public ObservableCollection<FormKey> DisallowedRaces { get; set; } = new();
    public VM_RaceGroupingCheckboxList AllowedRaceGroupings { get; set; }
    public VM_RaceGroupingCheckboxList DisallowedRaceGroupings { get; set; }
    public ObservableCollection<VM_NPCAttribute> AllowedAttributes { get; set; } = new(); // keeping as array to allow deserialization of original zEBD settings files
    public ObservableCollection<VM_NPCAttribute> DisallowedAttributes { get; set; } = new();
    public bool bAllowUnique { get; set; } = true;
    public bool bAllowNonUnique { get; set; } = true;
    public bool bAllowRandom { get; set; } = true;
    public double ProbabilityWeighting { get; set; } = 1;
    public NPCWeightRange WeightRange { get; set; } = new();
    public string Caption_BodyShapeDescriptors { get; set; } = "";

    public bool ReferenceUnlocked { get; set; } = false;
    private static string LockOnLabel = "Unlock";
    private static string LockOffLabel = "Lock";
    public string LockLabel { get; set; } = LockOnLabel;
    public ObservableCollection<string> SliderValues { get; set; } = new();

    public VM_BodySlidePlaceHolder AssociatedPlaceHolder { get; }
    public ILinkCache lk { get; private set; }

    /// <summary>
    /// Embedded read-only 3D viewer. Created per-preset from the Autofac factory so each
    /// BodySlide card owns its own GL state. Configured as ReadOnly in the ctor — the
    /// viewer UI has no NPC picker or mesh-override column in this mode (Section B4).
    /// </summary>
    public VM_CharacterViewer CharacterViewer { get; }
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();
    public RelayCommand ToggleLock { get; }
    public RelayCommand AddAllowedAttribute { get; }
    public RelayCommand AddDisallowedAttribute { get; }
    public RelayCommand DeleteMe { get; }
    public RelayCommand CloneCommand { get; }
    public RelayCommand ToggleHide { get; }
    public RelayCommand AcceptAutoAnnotations { get; }
    public RelayCommand AddWeightSlotCommand { get; }
    public bool ShowAcceptAnnotationsButton { get; set; }
    public SolidColorBrush BorderColor { get; set; }

    public string HideButtonText { get; set; } = "Hide";
    public string StatusHeader { get; set; }
    public string StatusText { get; set; }
    public bool ShowStatus { get; set; }

    public static SolidColorBrush BorderColorMissing = CommonColors.Red;
    public static SolidColorBrush BorderColorUnannotated = CommonColors.Yellow;
    public static SolidColorBrush BorderColorValid = CommonColors.LightGreen;
    public static SolidColorBrush BorderColorHidden = CommonColors.LightSlateGrey;
    public static SolidColorBrush BorderColorAnnotationRuleBased = CommonColors.MediumPurple;
    public static SolidColorBrush BorderColorAnnotationMixManual_RulesBased = new(Colors.Teal);

    public VM_BodySlideSetting Clone()
    {
        var cloneModel = DumpToModel();
        var clonePlaceHolder = _placeHolderFactory(cloneModel, AssociatedPlaceHolder.ParentCollection);
        var cloneViewModel = _selfFactory(clonePlaceHolder, _raceGroupingVMs);
        cloneViewModel.CopyInViewModelFromModel(cloneModel);
        int lastClonePosition = clonePlaceHolder.RenameByIndex();
        clonePlaceHolder.ParentCollection.Insert(lastClonePosition + 1, clonePlaceHolder);
        return cloneViewModel;
    }

    public void UnlockReference()
    {
        ReferenceUnlocked = true;
        LockLabel = LockOffLabel;
    }

    public void LockReference()
    {
        ReferenceUnlocked = false;
        LockLabel = LockOnLabel;
    }

    public VM_BodySlidePlaceHolder CopyToNewCollection(ObservableCollection<VM_BodySlidePlaceHolder> parentCollection)
    {
        var model = DumpToModel();
        return _placeHolderFactory(model, parentCollection);
    }

    private VM_BodySlideWeightSlot CreateSlot(int weight, HashSet<AnnotatedDescriptorSignature> descriptors)
    {
        var menu = _descriptorSelectionFactory(_bodyShapeDescriptors, _raceGroupingVMs, ParentMenuVM, false, DescriptorMatchMode.Any, false);
        if (descriptors != null)
        {
            menu.CopyInFromHashSet(descriptors);
        }
        var slot = new VM_BodySlideWeightSlot(weight, menu, this);
        // Bubble menu state changes up to the aggregate rollup.
        slot.DescriptorsSelectionMenu
            .WhenAnyValue(x => x.AnnotationState)
            .Subscribe(_ => UpdateAggregateAnnotationState())
            .DisposeWith(slot);
        return slot;
    }

    public void AddWeightSlot(int weight)
    {
        if (weight < 0 || weight > 100) return;
        if (WeightSlots.Any(s => s.Weight == weight)) return;

        var slot = CreateSlot(weight, new HashSet<AnnotatedDescriptorSignature>());

        // Insert in sorted order so the tab strip stays ordered.
        int insertAt = 0;
        while (insertAt < WeightSlots.Count && WeightSlots[insertAt].Weight < weight) insertAt++;
        WeightSlots.Insert(insertAt, slot);

        // If the user is re-adding a default slot they previously removed, take it back off the removed list.
        if (RemovedDefaultWeightSlots.Contains(weight))
        {
            RemovedDefaultWeightSlots.Remove(weight);
        }

        SelectedWeightSlot = slot;
        UpdateAggregateAnnotationState();
    }

    public void RemoveWeightSlot(VM_BodySlideWeightSlot slot)
    {
        if (slot == null || !WeightSlots.Contains(slot)) return;

        bool isDefault = _patcherState?.OBodySettings?.DefaultWeightSlots?.Contains(slot.Weight) == true;
        bool hasContent = slot.DescriptorsSelectionMenu?.IsAnnotated() == true;

        if (hasContent || isDefault)
        {
            string msg = isDefault
                ? $"Weight slot {slot.Weight} is one of the default weight slots. Removing it will skip annotation at this weight for this preset only. Continue?"
                : $"Weight slot {slot.Weight} contains descriptors. Remove it?";
            if (!MessageWindow.DisplayNotificationYesNo("Remove Weight Slot", msg))
            {
                return;
            }
        }

        WeightSlots.Remove(slot);
        if (isDefault)
        {
            RemovedDefaultWeightSlots.Add(slot.Weight);
        }
        slot.Dispose();
        UpdateAggregateAnnotationState();
    }

    public void UpdateAggregateAnnotationState()
    {
        var states = WeightSlots
            .Select(s => s.DescriptorsSelectionMenu?.AnnotationState ?? BodyShapeAnnotationState.None)
            .Where(s => s != BodyShapeAnnotationState.None)
            .Distinct()
            .ToList();

        if (states.Count == 0)
        {
            AggregateAnnotationState = BodyShapeAnnotationState.None;
        }
        else if (states.Count == 1)
        {
            AggregateAnnotationState = states[0];
        }
        else
        {
            // Treat any cross-slot or cross-source mix as the legacy Mix state so the existing
            // color brush table still applies until stage 5/6 introduces dedicated colors.
            AggregateAnnotationState = BodyShapeAnnotationState.Mix_Manual_RulesBased;
        }
    }

    private static void AcceptAutoAnnotationsForMenu(VM_BodyShapeDescriptorSelectionMenu menu)
    {
        if (menu == null) return;
        bool hasSelected = false;
        foreach (var category in menu.DescriptorShells)
        {
            foreach (var value in category.DescriptorSelectors)
            {
                value.AnnotationState = BodyShapeAnnotationState.Manual;
                value.TextColor = CommonColors.White;
            }
            category.AnnotationState = category.DescriptorSelectors.Any(x => x.IsSelected)
                ? BodyShapeAnnotationState.Manual
                : BodyShapeAnnotationState.None;
            if (category.AnnotationState == BodyShapeAnnotationState.Manual)
            {
                hasSelected = true;
            }
        }
        menu.AnnotationState = hasSelected ? BodyShapeAnnotationState.Manual : BodyShapeAnnotationState.None;
    }

    public void UpdateStatusDisplay() // this should follow the same logic as VM_BodySlidePlaceHolder.InitializeBorderColor()
    {
        if (!ParentMenuVM.BodySlidesUI.CurrentlyExistingBodySlides.Contains(this.ReferencedBodySlide))
        {
            BorderColor = BorderColorMissing;
            StatusHeader = "Warning:";
            StatusText = "Source BodySlide XML files are missing. Will not be assigned.";
            ShowStatus = true;
            return;
        }

        if (AssociatedPlaceHolder.IsHidden)
        {
            BorderColor = BorderColorHidden;
            StatusHeader = string.Empty;
            StatusText = string.Empty;
            ShowStatus = false;
            return;
        }

        switch (AggregateAnnotationState)
        {
            case BodyShapeAnnotationState.None:
                BorderColor = AnnotationToColor[BodyShapeAnnotationState.None];
                StatusHeader = "Warning:";
                StatusText = "Bodyslide has not been annotated with descriptors. May not pair correctly with textures.";
                ShowStatus = true;
                break;
            case BodyShapeAnnotationState.RulesBased:
                BorderColor = AnnotationToColor[BodyShapeAnnotationState.RulesBased];
                StatusHeader = "Note:";
                StatusText = "Bodyslide has been automatically annotated using slider rules";
                ShowStatus = true;
                break;
            case BodyShapeAnnotationState.Mix_Manual_RulesBased:
            case BodyShapeAnnotationState.Mixed:
                BorderColor = AnnotationToColor[BodyShapeAnnotationState.Mix_Manual_RulesBased];
                StatusHeader = "Note:";
                StatusText = "Bodyslide has annotations from multiple sources or weight slots";
                ShowStatus = true;
                break;
            case BodyShapeAnnotationState.Manual:
                BorderColor = AnnotationToColor[BodyShapeAnnotationState.Manual];
                StatusHeader = string.Empty;
                StatusText = string.Empty;
                ShowStatus = false;
                break;
            default:
                BorderColor = AnnotationToColor.TryGetValue(AggregateAnnotationState, out var c) ? c : BorderColorValid;
                StatusHeader = string.Empty;
                StatusText = string.Empty;
                ShowStatus = false;
                break;
        }
    }

    public static Dictionary<BodyShapeAnnotationState, SolidColorBrush> AnnotationToColor = new()
    {
        { BodyShapeAnnotationState.None, BorderColorUnannotated},
        { BodyShapeAnnotationState.RulesBased, BorderColorAnnotationRuleBased},
        { BodyShapeAnnotationState.Manual, BorderColorValid },
        { BodyShapeAnnotationState.Mix_Manual_RulesBased, BorderColorAnnotationMixManual_RulesBased }
    };

    public void CopyInViewModelFromModel(BodySlideSetting model)
    {
        Label = model.Label;
        ReferencedBodySlide = model.ReferencedBodySlide;
        if (ReferencedBodySlide.IsNullOrWhitespace()) // update for pre-0.9.3
        {
            ReferencedBodySlide = Label;
        }
        SliderGroup = model.SliderGroup;
        Notes = model.Notes;

        // Rebuild the weight slot UI from the model dictionary.
        foreach (var existing in WeightSlots) { existing.Dispose(); }
        WeightSlots.Clear();
        RemovedDefaultWeightSlots = new HashSet<int>(model.RemovedDefaultWeightSlots ?? new HashSet<int>());

        if (model.BodyShapeDescriptorsByWeight != null)
        {
            foreach (var pair in model.BodyShapeDescriptorsByWeight.OrderBy(p => p.Key))
            {
                var slot = CreateSlot(pair.Key, pair.Value ?? new HashSet<AnnotatedDescriptorSignature>());
                WeightSlots.Add(slot);
            }
        }
        SelectedWeightSlot = WeightSlots.FirstOrDefault();
        UpdateAggregateAnnotationState();

        foreach (var fk in model.AllowedRaces) { AllowedRaces.Add(fk); }
        AllowedRaceGroupings.CopyInRaceGroupingsByLabel(model.AllowedRaceGroupings, _raceGroupingVMs);
        foreach (var grouping in AllowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.AllowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        foreach (var fk in model.DisallowedRaces) { DisallowedRaces.Add(fk); }
        DisallowedRaceGroupings.CopyInRaceGroupingsByLabel(model.DisallowedRaceGroupings, _raceGroupingVMs);

        foreach (var grouping in DisallowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.DisallowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        _attributeCreator.CopyInFromModels(model.AllowedAttributes, AllowedAttributes, ParentMenuVM.AttributeGroupMenu.Groups, true, null);
        _attributeCreator.CopyInFromModels(model.DisallowedAttributes, DisallowedAttributes, ParentMenuVM.AttributeGroupMenu.Groups, false, null);
        foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }
        bAllowUnique = model.AllowUnique;
        bAllowNonUnique = model.AllowNonUnique;
        bAllowRandom = model.AllowRandom;
        ProbabilityWeighting = model.ProbabilityWeighting;
        WeightRange = model.WeightRange.Clone();

        if (model.HideInMenu)
        {
            HideButtonText = "Unhide";
        }
        else
        {
            HideButtonText = "Hide";
        }

        SliderValues.Clear();
        foreach (var slider in model.SliderValues.Values)
        {
            SliderValues.Add(slider.SliderName + " [Small: " + slider.Small + "] | [Big: " + slider.Big + "]");
        }
    }

    public BodySlideSetting DumpToModel()
    {
        BodySlideSetting model = new BodySlideSetting();
        model.Label = Label;
        model.ReferencedBodySlide = ReferencedBodySlide;
        model.Notes = Notes;

        // Stage 2: dump each slot's selection menu directly into the matching weight key.
        model.BodyShapeDescriptorsByWeight = new Dictionary<int, HashSet<AnnotatedDescriptorSignature>>();
        foreach (var slot in WeightSlots)
        {
            model.BodyShapeDescriptorsByWeight[slot.Weight] = slot.DescriptorsSelectionMenu.DumpToOBodySettingsHashSet();
        }
        model.RemovedDefaultWeightSlots = new HashSet<int>(RemovedDefaultWeightSlots);

        model.AllowedRaces = AllowedRaces.ToHashSet();
        model.AllowedRaceGroupings = AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.DisallowedRaces = DisallowedRaces.ToHashSet();
        model.DisallowedRaceGroupings = DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.AllowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(AllowedAttributes);
        model.DisallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(DisallowedAttributes);
        model.AllowUnique = bAllowUnique;
        model.AllowNonUnique = bAllowNonUnique;
        model.AllowRandom = bAllowRandom;
        model.ProbabilityWeighting = ProbabilityWeighting;
        model.WeightRange = WeightRange.Clone();
        model.HideInMenu = AssociatedPlaceHolder.IsHidden;

        // also copy JSonIgnored values because they're needed by the patcher or if returning to this VM
        model.SliderGroup = AssociatedPlaceHolder.AssociatedModel.SliderGroup;
        model.SliderValues = new(AssociatedPlaceHolder.AssociatedModel.SliderValues);
        model.AnnotationState = AggregateAnnotationState;
        return model;
    }
}

[DebuggerDisplay("Weight {Weight}")]
public class VM_BodySlideWeightSlot : VM
{
    public VM_BodySlideWeightSlot(int weight, VM_BodyShapeDescriptorSelectionMenu menu, VM_BodySlideSetting parent)
    {
        Weight = weight;
        DescriptorsSelectionMenu = menu;
        Parent = parent;
        Header = "Weight " + weight;
        RemoveCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Parent.RemoveWeightSlot(this)
        );
    }

    public int Weight { get; }
    public string Header { get; }
    public VM_BodyShapeDescriptorSelectionMenu DescriptorsSelectionMenu { get; }
    public VM_BodySlideSetting Parent { get; }
    public RelayCommand RemoveCommand { get; }
}
