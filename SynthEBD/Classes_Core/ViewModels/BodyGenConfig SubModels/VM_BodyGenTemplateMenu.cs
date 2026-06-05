using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ReactiveUI;
using System.Windows.Media;
using DynamicData.Binding;
using static SynthEBD.VM_NPCAttribute;
using System.Reactive.Linq;
using System.Diagnostics;

namespace SynthEBD;

public class VM_BodyGenTemplateMenu : VM
{
    private readonly SettingsIO_BodyGen _bodyGenIO;
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly VM_BodyGenTemplate.Factory _bodyGenTemplateFactory;
    public delegate VM_BodyGenTemplateMenu Factory(VM_BodyGenConfig parentConfig, ObservableCollection<VM_RaceGrouping> raceGroupingVMs);

    public VM_BodyGenTemplateMenu(VM_BodyGenConfig parentConfig, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, SettingsIO_BodyGen bodyGenIO, VM_NPCAttributeCreator attributeCreator, VM_BodyGenTemplate.Factory bodyGenTemplateFactory, Func<VM_CharacterViewer> characterViewerFactory)
    {
        _bodyGenIO = bodyGenIO;
        _attributeCreator = attributeCreator;
        _bodyGenTemplateFactory = bodyGenTemplateFactory;

        // Shared single viewer instance owned by the menu VM (one per BodyGen config).
        // Putting the viewer on VM_BodyGenTemplate would leak GL resources per template
        // click because VM_BodyGenTemplate is rebuilt on every SelectedPlaceHolder change.
        // DisposeWith(this) cascades: when the parent VM_BodyGenConfig is disposed (e.g.
        // on settings reload), it disposes this menu, which disposes the viewer, which
        // releases GL buffers/textures and cancels any in-flight NPC load.
        CharacterViewer = characterViewerFactory();
        CharacterViewer.Mode = ViewerMode.ReadOnly;
        CharacterViewer.DisposeWith(this);

        AddTemplate = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var placeHolder = new VM_BodyGenTemplatePlaceHolder(new BodyGenConfig.BodyGenTemplate(), Templates);
                Templates.Add(placeHolder);
                SelectedPlaceHolder = placeHolder;
            });

        RemoveTemplate = new RelayCommand(
            canExecute: _ => true,
            execute: x => Templates.Remove((VM_BodyGenTemplatePlaceHolder)x)
        );

        ImportBodyGen = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                if (IO_Aux.SelectFile("", "INI files (*.ini)|*.ini", "Select the Templates.ini file", out string templatePath))
                {
                    if (System.IO.Path.GetFileName(templatePath).Equals("morphs.ini", StringComparison.OrdinalIgnoreCase) && !MessageWindow.DisplayNotificationYesNo("Confirm File Name", "Expecting templates.ini but this file is morphs.ini, which should be imported in the Specific NPC Assignments Menu. Are you sure you want to continue?"))
                    {
                        return;
                    }

                    var newTemplates = _bodyGenIO.LoadTemplatesINI(templatePath);
                    foreach (var template in newTemplates.Where(x => !Templates.Select(x => x.Label).Contains(x.Label)).ToArray())
                    {
                        /*
                        var templateVM = _bodyGenTemplateFactory(parentConfig.GroupUI.TemplateGroups, parentConfig.DescriptorUI, raceGroupingVMs, Templates, parentConfig);
                        templateVM.CopyInViewModelFromModel(template, parentConfig.DescriptorUI, raceGroupingVMs);
                        Templates.Add(templateVM);*/
                        var placeHolder = new VM_BodyGenTemplatePlaceHolder(template, Templates);
                        Templates.Add(placeHolder);
                    }
                }
            }
        );

        Alphabetizer = new(Templates, x => x.Label, new(Colors.MediumPurple));

        this.WhenAnyValue(vm => vm.SelectedPlaceHolder)
         .Buffer(2, 1)
         .Select(b => (Previous: b[0], Current: b[1]))
         .Subscribe(t => {
             if (t.Previous != null && t.Previous.AssociatedViewModel != null)
             {
                 t.Previous.AssociatedViewModel.DumpViewModelToModel();
                 // VM_BodyGenTemplate only forwards CharacterViewer from the menu VM,
                 // but it still owns reactive subscriptions (Specs throttle, PreviewWeight)
                 // that accumulate across selection churn if not released.
                 t.Previous.AssociatedViewModel.Dispose();
                 t.Previous.AssociatedViewModel = null;
             }

             if (t.Current != null)
             {
                 CurrentlyDisplayedTemplate = _bodyGenTemplateFactory(t.Current, parentConfig.GroupUI.TemplateGroups, parentConfig.DescriptorUI, raceGroupingVMs, parentConfig);
                 CurrentlyDisplayedTemplate.CopyInViewModelFromModel(parentConfig.DescriptorUI, raceGroupingVMs);
             }
         }).DisposeWith(this);
    }
    public ObservableCollection<VM_BodyGenTemplatePlaceHolder> Templates { get; set; } = new();
    public VM_BodyGenTemplatePlaceHolder SelectedPlaceHolder { get; set; }
    public VM_BodyGenTemplate CurrentlyDisplayedTemplate { get; set; }
    public VM_CharacterViewer CharacterViewer { get; }

    public VM_Alphabetizer<VM_BodyGenTemplatePlaceHolder, string> Alphabetizer { get; set; }

    public RelayCommand AddTemplate { get; }
    public RelayCommand RemoveTemplate { get; }
    public RelayCommand ImportBodyGen { get; }

    private VM_BodyGenTemplatePlaceHolder _stashedPlaceHolder;

    public void StashAndNullDisplayedMorph()
    {
        if (SelectedPlaceHolder != null)
        {
            _stashedPlaceHolder = SelectedPlaceHolder;
            SelectedPlaceHolder = null;
        }
    }

    public void RestoreStashedMorph()
    {
        if (_stashedPlaceHolder != null)
        {
            SelectedPlaceHolder = _stashedPlaceHolder;
        }
    }
}

[DebuggerDisplay("{Label}")]
public class VM_BodyGenTemplatePlaceHolder : VM
{
    public VM_BodyGenTemplatePlaceHolder(BodyGenConfig.BodyGenTemplate model, ObservableCollection<VM_BodyGenTemplatePlaceHolder> parentCollection)
    {
        AssociatedModel = model;
        Label = model.Label;
        ParentCollection = parentCollection;
        if (!AssociatedModel.MemberOfTemplateGroups.Any())
        {
            BorderColor = CommonColors.Red;
        }
        else if (!AssociatedModel.BodyShapeDescriptors.Any())
        {
            BorderColor = CommonColors.Yellow;
        }
        else
        {
            BorderColor = CommonColors.Green;
        }

        this.WhenAnyValue(x => x.AssociatedViewModel.Label).Subscribe(y => Label = y).DisposeWith(this);
        this.WhenAnyValue(x => x.AssociatedViewModel.BorderColor).Subscribe(y => BorderColor = y).DisposeWith(this);
    }
    
    public string Label { get; set; }
    public SolidColorBrush BorderColor { get; set; }
    public BodyGenConfig.BodyGenTemplate AssociatedModel { get; set; }
    public VM_BodyGenTemplate? AssociatedViewModel { get; set; }
    public ObservableCollection<VM_BodyGenTemplatePlaceHolder> ParentCollection { get; set; }

}

[DebuggerDisplay("{Label}")]
public class VM_BodyGenTemplate : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly VM_AttributeWeightModifier.Factory _weightModifierFactory;
    private readonly Logger _logger;
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
    private readonly VM_SettingsBodyGen _bodyGenSettingsVM;
    private readonly PreviewNpcResolver _previewNpcResolver;
    public delegate VM_BodyGenTemplate Factory(VM_BodyGenTemplatePlaceHolder associatedPlaceHolder, ObservableCollection<VM_CollectionMemberString> templateGroups, VM_BodyShapeDescriptorCreationMenu BodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_BodyGenConfig parentConfig);
    public VM_BodyGenTemplate(VM_BodyGenTemplatePlaceHolder associatedPlaceHolder, ObservableCollection<VM_CollectionMemberString> templateGroups, VM_BodyShapeDescriptorCreationMenu BodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_BodyGenConfig parentConfig, IEnvironmentStateProvider environmentProvider, VM_NPCAttributeCreator attributeCreator, VM_AttributeWeightModifier.Factory weightModifierFactory, Logger logger, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory, VM_SettingsBodyGen bodyGenSettingsVM, PreviewNpcResolver previewNpcResolver)
    {
        _environmentProvider = environmentProvider;
        _attributeCreator = attributeCreator;
        _weightModifierFactory = weightModifierFactory;
        _logger = logger;
        _descriptorSelectionFactory = descriptorSelectionFactory;
        _bodyGenSettingsVM = bodyGenSettingsVM;
        _previewNpcResolver = previewNpcResolver;

        AssociatedPlaceHolder = associatedPlaceHolder;
        AssociatedPlaceHolder.AssociatedViewModel = this;

        SubscribedTemplateGroups = templateGroups;
        GroupSelectionCheckList = new VM_CollectionMemberStringCheckboxList(SubscribedTemplateGroups);
        DescriptorsSelectionMenu = descriptorSelectionFactory(BodyShapeDescriptors, raceGroupingVMs, parentConfig, false, DescriptorMatchMode.Any, false);
        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

        ParentConfig = parentConfig;
        SubscribedTemplateGroups.ToObservableChangeSet().Subscribe(x => UpdateThisOtherGroupsTemplateCollection()).DisposeWith(this);
        GroupSelectionCheckList.CollectionMemberStrings.ToObservableChangeSet().Subscribe(x => UpdateThisOtherGroupsTemplateCollection()).DisposeWith(this);

        this.WhenAnyValue(x => x.DescriptorsSelectionMenu.Header).Subscribe(x => UpdateStatusDisplay()).DisposeWith(this);
        this.WhenAnyValue(x => x.GroupSelectionCheckList.Header).Subscribe(x => UpdateStatusDisplay()).DisposeWith(this);

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        // Throttled live preview: re-parse Specs and re-apply the virtual BodySlide
        // whenever the user edits the spec string. Skip(1) drops the construction-time
        // emission so a freshly-built VM doesn't fire a preview before
        // CopyInViewModelFromModel has populated fields.
        this.WhenAnyValue(x => x.Specs)
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshPreview())
            .DisposeWith(this);

        // Preview-weight slider: scrubs between Low and High for spec ranges. The slider
        // self-rate-limits, so no throttle is needed here.
        this.WhenAnyValue(x => x.PreviewWeight)
            .Skip(1)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshPreview())
            .DisposeWith(this);

        UpdateStatusDisplay();

        AddAllowedAttribute = new RelayCommand(
            canExecute: _ => true,
            execute: _ => AllowedAttributes.Add(_attributeCreator.CreateNewFromUI(AllowedAttributes, true, null, ParentConfig.AttributeGroupMenu.Groups))
        );

        AddDisallowedAttribute = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisallowedAttributes.Add(_attributeCreator.CreateNewFromUI(DisallowedAttributes, false, null, ParentConfig.AttributeGroupMenu.Groups))
        );

        AddProbabilityWeightModifier = new RelayCommand(
            canExecute: _ => true,
            execute: _ => ProbabilityWeightModifiers.Add(_weightModifierFactory(ProbabilityWeightModifiers, ParentConfig.AttributeGroupMenu.Groups))
        );

        AddRequiredTemplate = new RelayCommand(
            canExecute: _ => true,
            execute: _ => RequiredTemplates.Add(new VM_CollectionMemberString("", this.RequiredTemplates))
        );

        DeleteMe = new RelayCommand(
            canExecute: _ => true,
            execute: _ => AssociatedPlaceHolder.ParentCollection.Remove(AssociatedPlaceHolder)
        );
    }

    public VM_BodyGenTemplatePlaceHolder AssociatedPlaceHolder { get; }
    public string Label { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Specs { get; set; } = ""; // will need special logic during I/O because in zEBD settings this is called "params" which is reserved in C#
    public VM_CollectionMemberStringCheckboxList GroupSelectionCheckList { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu DescriptorsSelectionMenu { get; set; }
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
    public ObservableCollection<VM_CollectionMemberString> RequiredTemplates { get; set; } = new();
    public NPCWeightRange WeightRange { get; set; } = new();
    public string Caption_MemberOfTemplateGroups { get; set; } = "";
    public string Caption_BodyShapeDescriptors { get; set; } = "";

    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();

    public RelayCommand AddAllowedAttribute { get; }
    public RelayCommand AddDisallowedAttribute { get; }
    public RelayCommand AddProbabilityWeightModifier { get; }
    public RelayCommand AddRequiredTemplate { get; }
    public RelayCommand DeleteMe { get; }

    public VM_BodyGenConfig ParentConfig { get; set; }
    public ObservableCollection<VM_CollectionMemberString> SubscribedTemplateGroups { get; set;}
    public ObservableCollection<VM_BodyGenTemplatePlaceHolder> OtherGroupsTemplateCollection { get; set; } = new();
    public SolidColorBrush BorderColor { get; set; }
    public string StatusHeader { get; set; }
    public string StatusText { get; set; }
    public bool ShowStatus { get; set; }

    // Weight used by BodySlideDeformer to interpolate between a spec's Low (weight=0)
    // and High (weight=100) values. BodyGen itself is weight-independent, so this slider
    // is purely a preview affordance that scrubs through the random range the patcher
    // will pick from. Default midpoint = 50.
    public int PreviewWeight { get; set; } = 50;
    public string ParseErrorText { get; set; } = "";
    // Forwarding accessor: the viewer lives on the menu VM so it survives template-
    // selection churn (VM_BodyGenTemplate is rebuilt on every selection).
    public VM_CharacterViewer CharacterViewer => ParentConfig?.TemplateMorphUI?.CharacterViewer;

    public void CopyInViewModelFromModel(VM_BodyShapeDescriptorCreationMenu descriptorMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
        var model = AssociatedPlaceHolder.AssociatedModel;
        Label = model.Label;
        Notes = model.Notes;
        Specs = model.Specs;
        GroupSelectionCheckList.InitializeFromHashSet(model.MemberOfTemplateGroups);
        DescriptorsSelectionMenu.CopyInFromHashSet(model.BodyShapeDescriptors);
        AllowedRaces.AddRange(model.AllowedRaces);
        AllowedRaceGroupings.CopyInRaceGroupingsByLabel(model.AllowedRaceGroupings, raceGroupingVMs);
        foreach (var grouping in AllowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.AllowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        DisallowedRaces.AddRange(model.DisallowedRaces);
        DisallowedRaceGroupings.CopyInRaceGroupingsByLabel(model.DisallowedRaceGroupings, raceGroupingVMs);
            
        foreach (var grouping in DisallowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.DisallowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        _attributeCreator.CopyInFromModels(model.AllowedAttributes, AllowedAttributes, ParentConfig.AttributeGroupMenu.Groups, true, null);
        _attributeCreator.CopyInFromModels(model.DisallowedAttributes, DisallowedAttributes, ParentConfig.AttributeGroupMenu.Groups, false, null);
        foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }
        ProbabilityWeightModifiers.Clear();
        foreach (var m in model.ProbabilityWeightModifiers)
        {
            ProbabilityWeightModifiers.Add(VM_AttributeWeightModifier.GetViewModelFromModel(m, ProbabilityWeightModifiers, ParentConfig.AttributeGroupMenu.Groups, _weightModifierFactory, _attributeCreator));
        }
        bAllowUnique = model.AllowUnique;
        bAllowNonUnique = model.AllowNonUnique;
        bAllowRandom = model.AllowRandom;
        ProbabilityWeighting = model.ProbabilityWeighting;
        VM_CollectionMemberString.CopyInObservableCollectionFromICollection(model.RequiredTemplates, RequiredTemplates);
        WeightRange = model.WeightRange.Clone();

        UpdateStatusDisplay();

        // Fire an immediate preview instead of waiting for the Specs-throttle to trip.
        // Safe even if lk hasn't resolved yet — RefreshPreview guards against that.
        RefreshPreview();
    }

    /// <summary>
    /// Parses the current <see cref="Specs"/> string into a virtual BodySlide preset,
    /// loads the configured preview NPC (per gender, from <see cref="VM_SettingsBodyGen"/>),
    /// and applies the deformation via the shared <see cref="CharacterViewer"/>.
    /// No-ops cleanly when the viewer isn't available, no preview NPC is configured,
    /// or link-cache is still warming up.
    /// </summary>
    private async void RefreshPreview()
    {
        try
        {
            var viewer = CharacterViewer;
            if (viewer == null) return;
            if (lk == null) return;

            var gender = ParentConfig?.Gender ?? Gender.Female;
            FormKey npc = gender == Gender.Female
                ? _bodyGenSettingsVM.PreviewNpcFemale
                : _bodyGenSettingsVM.PreviewNpcMale;
            string sliderGroup = gender == Gender.Female
                ? (_bodyGenSettingsVM.PreviewSliderGroupFemale ?? "")
                : (_bodyGenSettingsVM.PreviewSliderGroupMale ?? "");

            // Fallback: if the user hasn't picked a preview NPC, auto-pick the first Nord NPC
            // of matching gender. Nord is a humanoid vanilla race that's always present in
            // any Skyrim load order, so this is safe and deterministic per load order.
            if (npc.IsNull)
            {
                npc = _previewNpcResolver?.FindFirstNordRaceNpc(gender) ?? FormKey.Null;
            }

            if (npc.IsNull)
            {
                ParseErrorText = "No preview NPC configured and none auto-resolvable (BodyGen Settings → " + gender + " Preview NPC)";
                return;
            }

            var preset = BodyGenSpecsParser.Parse(Specs ?? "", sliderGroup, out var errors);
            ParseErrorText = errors.Count == 0 ? "" : string.Join("; ", errors);
            if (preset.SliderValues.Count == 0)
            {
                // No usable sliders — leave the viewer at its current pose rather than
                // flashing a bind-pose body mid-edit. Errors (if any) already surfaced.
                return;
            }

            await viewer.LoadNpcAsync(npc, lk);
            viewer.ApplyBodySlide(preset, PreviewWeight);
        }
        catch (Exception ex)
        {
            _logger?.LogError("VM_BodyGenTemplate.RefreshPreview failed: " + ExceptionLogger.GetExceptionStack(ex));
        }
    }

    public void DumpViewModelToModel()
    {
        var model = AssociatedPlaceHolder.AssociatedModel;
        model.Label = Label;
        model.Notes = Notes;
        model.Specs = Specs;
        model.MemberOfTemplateGroups = GroupSelectionCheckList.CollectionMemberStrings.Where(x => x.IsSelected).Select(x => x.SubscribedString.Content).ToHashSet();
        model.BodyShapeDescriptors = DescriptorsSelectionMenu.DumpToHashSet();
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
        model.RequiredTemplates = RequiredTemplates.Select(x => x.Content).ToHashSet();
        model.WeightRange = WeightRange.Clone();
    }

    public ObservableCollection<VM_BodyGenTemplatePlaceHolder> UpdateThisOtherGroupsTemplateCollection()
    {
        if (ParentConfig.IsLoadingFromViewModel)
        {
            return new(); // skip this when the parent BodyGen Config view model is being loaded in because every added Template will trigger this evaluation. 
        }

        var sameGroups = ParentConfig.TemplateMorphUI.Templates.Where(x => x.AssociatedModel.MemberOfTemplateGroups.Intersect(AssociatedPlaceHolder.AssociatedModel.MemberOfTemplateGroups).Any()).ToArray();
        var otherGroups = ParentConfig.TemplateMorphUI.Templates.Where(x => !x.AssociatedModel.MemberOfTemplateGroups.Intersect(AssociatedPlaceHolder.AssociatedModel.MemberOfTemplateGroups).Any()).ToArray();

        OtherGroupsTemplateCollection = new(otherGroups);

        return new(sameGroups);
    }

    public void UpdateStatusDisplay()
    {
        var belongsToGroup = false;
        foreach (var group in GroupSelectionCheckList.CollectionMemberStrings)
        {
            if (group.IsSelected)
            {
                belongsToGroup = true;
                break;
            }
        }

        if (!belongsToGroup)
        {
            BorderColor = CommonColors.Red;
            StatusHeader = "Warning:";
            StatusText = "Morph does not belong to any Morph Groups. Will not be assigned.";
            ShowStatus = true;
        }
        else if (!DescriptorsSelectionMenu.IsAnnotated())
        {
            BorderColor = CommonColors.Yellow;
            StatusHeader = "Warning:";
            StatusText = "Bodyslide has not been annotated with descriptors. May not pair correctly with textures.";
            ShowStatus = true;
        }
        else
        {
            BorderColor = CommonColors.Green;
            StatusHeader = string.Empty;
            StatusText = string.Empty;
            ShowStatus = false;
        }
    }
}