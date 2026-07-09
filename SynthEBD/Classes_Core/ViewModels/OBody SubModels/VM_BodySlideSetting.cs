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
    private readonly VM_AttributeWeightModifier.Factory _weightModifierFactory;
    private readonly BodySlideAnnotator _bodySlideAnnotator;
    private readonly VM_BodyShapeDescriptorCreationMenu _bodyShapeDescriptors;
    private readonly ObservableCollection<VM_RaceGrouping> _raceGroupingVMs;
    private readonly VM_BodySlidePlaceHolder.Factory _placeHolderFactory;
    private readonly VM_BodySlideSetting.Factory _selfFactory;
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
    private readonly PatcherState _patcherState;
    private readonly BodySlideGroupClassifier _classifier;

    private readonly Logger _logger;

    public delegate VM_BodySlideSetting Factory(VM_BodySlidePlaceHolder associatedPlaceHolder, ObservableCollection<VM_RaceGrouping> raceGroupingVMs);
    public VM_BodySlideSetting(VM_BodySlidePlaceHolder associatedPlaceHolder, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_SettingsOBody oBodySettingsVM, VM_NPCAttributeCreator attributeCreator, VM_AttributeWeightModifier.Factory weightModifierFactory, BodySlideAnnotator bodySlideAnnotator, IEnvironmentStateProvider environmentProvider, Logger logger, Factory selfFactory, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory, VM_BodySlidePlaceHolder.Factory placeHolderFactory, PatcherState patcherState, Func<VM_CharacterViewer> characterViewerFactory, BodySlideGroupClassifier classifier)
    {
        ParentMenuVM = oBodySettingsVM;

        AssociatedPlaceHolder = associatedPlaceHolder;
        AssociatedPlaceHolder.AssociatedViewModel = this;

        _environmentProvider = environmentProvider;
        _attributeCreator = attributeCreator;
        _weightModifierFactory = weightModifierFactory;
        _bodySlideAnnotator = bodySlideAnnotator;
        _bodyShapeDescriptors = oBodySettingsVM.DescriptorUI;
        _raceGroupingVMs = raceGroupingVMs;
        _selfFactory = selfFactory;
        _placeHolderFactory = placeHolderFactory;
        _descriptorSelectionFactory = descriptorSelectionFactory;
        _patcherState = patcherState;
        _classifier = classifier;
        _logger = logger;

        CharacterViewer = characterViewerFactory();
        CharacterViewer.Mode = ViewerMode.ReadOnly;
        CharacterViewer.DisposeWith(this);

        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        // Recompute aggregate rollup whenever slot membership changes.
        WeightSlots.ToObservableChangeSet()
            .Subscribe(_ => UpdateAggregateAnnotationState())
            .DisposeWith(this);

        this.WhenAnyValue(x => x.SliderGroup)
            .Subscribe(_ => RefreshMatchedRegistryBodyType())
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

        AddProbabilityWeightModifier = new RelayCommand(
            canExecute: _ => true,
            execute: _ => ProbabilityWeightModifiers.Add(_weightModifierFactory(ProbabilityWeightModifiers, ParentMenuVM.AttributeGroupMenu.Groups))
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

        // Re-fire preview when the user picks a different NPC override.
        this.WhenAnyValue(x => x.PreviewNpcOverride)
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(50), RxApp.MainThreadScheduler)
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
            FormKey npc = FormKey.Null;
            if (!PreviewNpcOverride.IsNull)
            {
                npc = PreviewNpcOverride;
            }
            else
            {
                var preview = _patcherState?.OBodySettings?.PreviewNpcs;
                if (preview != null && preview.WeightPreviewNpcs.TryGetValue(slot.Weight, out var pair) && pair != null)
                {
                    npc = gender == Gender.Female ? pair.FemaleNpc : pair.MaleNpc;
                }
            }

            if (npc.IsNull)
            {
                _logger?.LogMessage("VM_BodySlideSetting: no preview NPC configured for weight " + slot.Weight + " (" + gender + ")");
                return;
            }

            await CharacterViewer.LoadNpcAsync(npc, lk);
            CharacterViewer.ApplyBodySlide(AssociatedPlaceHolder.AssociatedModel, slot.Weight);

            // Phase 5: run the BodySlide Classifier against the just-deformed mesh and merge any
            // matched descriptors into this weight slot. Skips silently when no profile is
            // installed for this body topology so non-classifier users see no change.
            TryRunClassifierForSlot(slot);
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
    /// Phase 5 helper. Looks up an applicable <see cref="BodyTypeProfile"/> for the current
    /// preset's mesh topology, evaluates it against the live viewer state, merges classifier-
    /// sourced descriptors into the model's per-weight slot, and refreshes the matching
    /// selection menu so the UI reflects the merge. Failures are logged but never thrown --
    /// classification is best-effort and must not break the preview pipeline.
    /// </summary>
    private void TryRunClassifierForSlot(VM_BodySlideWeightSlot slot)
    {
        try
        {
            if (slot == null) return;
            var profiles = _patcherState?.OBodySettings?.BodyTypeProfiles;
            if (profiles == null || profiles.Count == 0) return;

            string sliderGroupHint = AssociatedPlaceHolder?.AssociatedModel?.SliderGroup;
            var profile = BodySlideMeasurementEvaluator.FindMatchingProfile(profiles, CharacterViewer, sliderGroupHint);
            if (profile == null) return;

            var model = AssociatedPlaceHolder?.AssociatedModel;
            if (model?.BodyShapeDescriptorsByWeight == null) return;
            if (!model.BodyShapeDescriptorsByWeight.TryGetValue(slot.Weight, out var modelSlot) || modelSlot == null) return;

            // Pass the preset's gender so RuleGender-filtered rules fire correctly in the
            // live preview (e.g. a male-only Powerful rule on a male preset). ResolveGender
            // is a reference-comparison against the parent BodySlidesMale/Female list, so
            // it's authoritative even when descriptors are sparse.
            //
            // Seed the rule pass with the descriptors senior to the classifier, so DescriptorRef
            // conditions can test slider-assigned labels — e.g. a Belly:Chubby measurement rule
            // excluding [Belly:Muscular] assigned by a MuscleAbs slider rule. Slider labels are
            // derived live from the CURRENT Label-by-Sliders rules (the annotator VM when alive —
            // it holds unsaved edits — else the persisted model), not from stored RulesBased
            // annotations, so rule drafts count immediately without an apply pass. Manual/Library
            // entries seed from storage; prior Classifier output is excluded by
            // CollectExternalDescriptors, so the MergeIntoSlot below never feeds its own previous
            // results back into this evaluation.
            var sliderRules = ParentMenuVM?.AnnotatorUI?.DumpToModel()
                ?? _patcherState?.OBodySettings?.BodySlideClassificationRules;
            var universeShells = ParentMenuVM?.DescriptorUI?.DumpToViewModels()
                ?? _patcherState?.OBodySettings?.TemplateDescriptors;
            var descriptorUniverse = universeShells?.Flatten()
                .Where(d => d?.ID != null)
                .Select(d => d.ID)
                .ToHashSet();
            var externalDescriptors = BodySlideMeasurementEvaluator.CollectExternalDescriptors(
                model, slot.Weight, sliderRules, descriptorUniverse);
            var result = BodySlideMeasurementEvaluator.Evaluate(CharacterViewer, profile, evaluationGender: ResolveGender(),
                externalDescriptors: externalDescriptors);
            BodySlideMeasurementEvaluator.MergeIntoSlot(modelSlot, result.Descriptors);
            slot.DescriptorsSelectionMenu?.ApplyClassifierDescriptors(result.Descriptors);
            UpdateAggregateAnnotationState();

            // Parallel diagnostic to the Match Presets scan: when the viewer's
            // VerboseLog toggle is on, dump the same key-measurement summary the
            // scan emits, so live-preview vs cached-scan output can be compared
            // for the same (preset, weight). Same set of measurement keys.
            if (CharacterViewer != null && CharacterViewer.VerboseLog)
            {
                string FmtMeas(string key)
                {
                    if (result.Measurements.TryGetValue(key, out var v))
                        return v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                    if (result.FailedMeasurements.TryGetValue(key, out var why))
                        return "FAIL(" + why + ")";
                    return "—";
                }
                _logger?.LogMessage($"BodySlide Classifier live [{(model.Label ?? "?")}]@W{slot.Weight}: "
                    + $"npcW={CharacterViewer.NpcWeight} sceneReady={CharacterViewer.IsSceneReady} "
                    + $"cp={FmtMeas("chest_projection")} wh={FmtMeas("waist_to_hip")} "
                    + $"csr={FmtMeas("chest_sag_ratio")} att={FmtMeas("arm_thickness_to_torso")} "
                    + $"bp={FmtMeas("belly_projection")} hpt={FmtMeas("hip_to_torso")} "
                    + $"ww={FmtMeas("waist_width")} hipW={FmtMeas("hip_width")} "
                    + $"meas={result.Measurements.Count} failed={result.FailedMeasurements.Count} "
                    + $"topoMismatch={result.TopologyMismatch} ruleMatches={result.Descriptors.Count}");
            }

            if (result.TopologyMismatch)
            {
                _logger?.LogMessage("BodySlide Classifier: topology fingerprint mismatch on profile '"
                    + profile.Name + "' for preset '" + (model.Label ?? "?") + "' -- results may be invalid");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("BodySlide Classifier failed during RefreshPreview: " + ExceptionLogger.GetExceptionStack(ex));
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
    public string MatchedRegistryBodyType { get; private set; } = "";
    public Brush MatchedRegistryBrush { get; private set; } = Brushes.OrangeRed;
    public string MatchedRegistryTooltip { get; private set; } = "";
    public bool MatchedRegistryTooltipEnabled { get; private set; } = false;
    public string Notes { get; set; } = "";

    private void RefreshMatchedRegistryBodyType()
    {
        var registry = _patcherState?.OBodySettings?.BodyTypeRegistry;
        if (registry == null || string.IsNullOrWhiteSpace(SliderGroup))
        {
            MatchedRegistryBodyType = "(no match)";
            MatchedRegistryBrush = Brushes.OrangeRed;
            MatchedRegistryTooltip = "";
            MatchedRegistryTooltipEnabled = false;
            return;
        }

        var gender = ResolveGender();
        var exact = registry.FirstOrDefault(e =>
            e != null &&
            string.Equals(e.Name, SliderGroup, StringComparison.OrdinalIgnoreCase) &&
            e.Gender == gender);
        if (exact != null)
        {
            int drift = ComputeDrift(exact, out var presetOnly, out var bodyOnly);
            MatchedRegistryBodyType = drift > 0 ? $"{exact.Name} ({drift} slider drift)" : exact.Name;
            MatchedRegistryBrush = drift > 2 ? Brushes.Goldenrod : Brushes.LightGreen;
            MatchedRegistryTooltip = drift > 0 ? BuildDriftTooltip(exact.Name, presetOnly, bodyOnly) : "";
            MatchedRegistryTooltipEnabled = drift > 0;
            return;
        }

        var crossGender = registry.FirstOrDefault(e =>
            e != null &&
            string.Equals(e.Name, SliderGroup, StringComparison.OrdinalIgnoreCase));
        if (crossGender != null)
        {
            MatchedRegistryBodyType = $"{crossGender.Name} (gender mismatch: {crossGender.Gender})";
            MatchedRegistryBrush = Brushes.Goldenrod;
            MatchedRegistryTooltip = "";
            MatchedRegistryTooltipEnabled = false;
            return;
        }

        MatchedRegistryBodyType = "(no match)";
        MatchedRegistryBrush = Brushes.OrangeRed;
        MatchedRegistryTooltip = "";
        MatchedRegistryTooltipEnabled = false;
    }

    /// <summary>
    /// Count preset sliders absent from the matched entry's resolved catalog (the "drift" metric
    /// shown in the UI) and also collect body-side sliders the preset never sets, so the tooltip
    /// can show both directions of mismatch. Drift > 0 typically means a derived body's reference
    /// OSD dropped legacy sliders that presets still set (e.g. CBBE 3BA drops AreolaSize, yet
    /// Alera-style 3BA presets use it).
    /// </summary>
    private int ComputeDrift(BodyTypeRegistryEntry entry, out List<string> presetOnly, out List<string> bodyOnly)
    {
        presetOnly = new List<string>();
        bodyOnly = new List<string>();
        if (entry?.ResolvedSliders == null || entry.ResolvedSliders.Count == 0) return 0;
        var presetSliders = AssociatedPlaceHolder?.AssociatedModel?.SliderValues;
        if (presetSliders == null || presetSliders.Count == 0) return 0;

        foreach (var sliderName in presetSliders.Keys)
        {
            if (string.IsNullOrEmpty(sliderName)) continue;
            if (!entry.ResolvedSliders.Contains(sliderName)) presetOnly.Add(sliderName);
        }
        foreach (var registryName in entry.ResolvedSliders)
        {
            if (string.IsNullOrEmpty(registryName)) continue;
            if (!presetSliders.ContainsKey(registryName)) bodyOnly.Add(registryName);
        }

        presetOnly.Sort(StringComparer.OrdinalIgnoreCase);
        bodyOnly.Sort(StringComparer.OrdinalIgnoreCase);
        return presetOnly.Count;
    }

    /// <summary>
    /// Build the tooltip listing mismatched sliders, grouped by direction. Preset-only sliders
    /// (the ones contributing to the drift count) come first; body-only sliders (catalog entries
    /// the preset never sets) follow, truncated at 30 lines to keep the tooltip manageable for
    /// large catalogs where a preset only exercises a fraction of the body's sliders.
    /// </summary>
    private static string BuildDriftTooltip(string bodyName, List<string> presetOnly, List<string> bodyOnly)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("In preset, not in ").Append(bodyName).Append(" reference (")
          .Append(presetOnly.Count).AppendLine("):");
        foreach (var s in presetOnly) sb.Append("  → ").AppendLine(s);

        const int bodyOnlyCap = 30;
        if (bodyOnly.Count > 0)
        {
            sb.AppendLine();
            sb.Append("In ").Append(bodyName).Append(" reference, not set by preset (")
              .Append(bodyOnly.Count).AppendLine("):");
            int shown = Math.Min(bodyOnly.Count, bodyOnlyCap);
            for (int i = 0; i < shown; i++) sb.Append("  ← ").AppendLine(bodyOnly[i]);
            if (bodyOnly.Count > bodyOnlyCap)
            {
                sb.Append("  … and ").Append(bodyOnly.Count - bodyOnlyCap).AppendLine(" more");
            }
        }

        return sb.ToString().TrimEnd();
    }

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
    public ObservableCollection<VM_AttributeWeightModifier> ProbabilityWeightModifiers { get; set; } = new();
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
    public FormKey PreviewNpcOverride { get; set; } = FormKey.Null;
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();
    public IEnumerable<Type> NPCPickerFormKeys { get; } = typeof(INpcGetter).AsEnumerable();
    public RelayCommand ToggleLock { get; }
    public RelayCommand AddAllowedAttribute { get; }
    public RelayCommand AddDisallowedAttribute { get; }
    public RelayCommand AddProbabilityWeightModifier { get; }
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
    public static SolidColorBrush BorderColorUnannotated = AnnotationColors.Unannotated;
    public static SolidColorBrush BorderColorValid = AnnotationColors.Valid;
    public static SolidColorBrush BorderColorHidden = CommonColors.LightSlateGrey;
    public static SolidColorBrush BorderColorAnnotationRuleBased = CommonColors.MediumPurple;
    public static SolidColorBrush BorderColorAnnotationMixManual_RulesBased = new(Colors.Teal);
    public static SolidColorBrush BorderColorAnnotationLibrary = new(Colors.CornflowerBlue);
    public static SolidColorBrush BorderColorAnnotationClassifier = new(Colors.Orange);

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
                value.TextColor = AnnotationColors.DefaultText;
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

    // Must cover every BodyShapeAnnotationState member: UpdateTextColor (descriptor selectors/shells)
    // and VM_BodySlidePlaceHolder.InitializeBorderColor index this directly, so a missing key throws
    // as soon as a state first appears in the UI (e.g. Classifier during RefreshPreview).
    public static Dictionary<BodyShapeAnnotationState, SolidColorBrush> AnnotationToColor = new()
    {
        { BodyShapeAnnotationState.None, BorderColorUnannotated},
        { BodyShapeAnnotationState.RulesBased, BorderColorAnnotationRuleBased},
        { BodyShapeAnnotationState.Manual, BorderColorValid },
        { BodyShapeAnnotationState.Mix_Manual_RulesBased, BorderColorAnnotationMixManual_RulesBased },
        { BodyShapeAnnotationState.Library, BorderColorAnnotationLibrary },
        { BodyShapeAnnotationState.Classifier, BorderColorAnnotationClassifier },
        { BodyShapeAnnotationState.Mixed, BorderColorAnnotationMixManual_RulesBased }, // same brush as Mix_Manual_RulesBased (see UpdateStatusDisplay's shared case)
    };

    public void CopyInViewModelFromModel(BodySlideSetting model, int? preferredWeight = null)
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
        // Prefer the caller-supplied weight (e.g. preserved across preset switches) so the
        // user doesn't get bounced back to weight 0 and trigger an unnecessary preview-NPC
        // reload. Fall back to the first slot if the preferred weight isn't available here.
        SelectedWeightSlot = (preferredWeight.HasValue
            ? WeightSlots.FirstOrDefault(s => s.Weight == preferredWeight.Value)
            : null) ?? WeightSlots.FirstOrDefault();
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
        ProbabilityWeightModifiers.Clear();
        foreach (var m in model.ProbabilityWeightModifiers)
        {
            ProbabilityWeightModifiers.Add(VM_AttributeWeightModifier.GetViewModelFromModel(m, ProbabilityWeightModifiers, ParentMenuVM.AttributeGroupMenu.Groups, _weightModifierFactory, _attributeCreator));
        }
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

        // If the preset didn't classify at load time (SliderGroup empty or "Unknown"), retry now
        // with verbose trace logging so the user can see exactly why each registry entry was
        // dropped. Only writes back when the retry actually picks a body type -- a second miss
        // just produces the diagnostic log and leaves the preset unclassified.
        TryReclassifyIfUnknown(model);
    }

    /// <summary>
    /// Diagnostic re-classify triggered on preset selection. Fires only for presets whose
    /// <see cref="BodySlideSetting.SliderGroup"/> is empty or "Unknown" after load; emits
    /// per-entry trace lines to the Status Log so the user can see why classification failed
    /// (missing slider samples, coverage below threshold, no catalogs loaded, etc.). If the
    /// retry succeeds, both the model and this VM are updated so the UI reflects the match.
    /// </summary>
    private void TryReclassifyIfUnknown(BodySlideSetting model)
    {
        if (_classifier == null || _logger == null) return;
        if (model == null) return;
        bool isUnknown = string.IsNullOrWhiteSpace(model.SliderGroup)
            || string.Equals(model.SliderGroup, "Unknown", StringComparison.OrdinalIgnoreCase);
        if (!isUnknown) return;
        if (model.SliderValues == null || model.SliderValues.Count == 0) return;

        var sliderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in model.SliderValues.Keys)
        {
            if (!string.IsNullOrEmpty(name)) sliderNames.Add(name);
        }

        _logger.LogMessage($"Re-classifying unclassified preset '{model.Label}' on selection:");
        var result = _classifier.Classify(model.Label, sliderNames, msg => _logger.LogMessage(msg));
        if (result == null) return;

        if (!string.IsNullOrEmpty(result.BodyType) && !string.Equals(result.BodyType, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogMessage($"Re-classify: '{model.Label}' -> {result.BodyType} (gender={result.Gender}, reason={result.Reason}). Updating preset.");
            model.SliderGroup = result.BodyType;
            SliderGroup = result.BodyType;
        }
        else
        {
            _logger.LogMessage($"Re-classify: '{model.Label}' still Unknown (reason={result.Reason}).");
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
        model.ProbabilityWeightModifiers = ProbabilityWeightModifiers.Select(x => x.DumpViewModelToModel()).ToList();
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
