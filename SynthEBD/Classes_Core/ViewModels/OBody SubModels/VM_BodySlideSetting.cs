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

    public delegate VM_BodySlideSetting Factory(VM_BodySlidePlaceHolder associatedPlaceHolder, ObservableCollection<VM_RaceGrouping> raceGroupingVMs);
    public VM_BodySlideSetting(VM_BodySlidePlaceHolder associatedPlaceHolder, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_SettingsOBody oBodySettingsVM, VM_NPCAttributeCreator attributeCreator, BodySlideAnnotator bodySlideAnnotator, IEnvironmentStateProvider environmentProvider, Logger logger, Factory selfFactory, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory, VM_BodySlidePlaceHolder.Factory placeHolderFactory)
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

        DescriptorsSelectionMenu = _descriptorSelectionFactory(_bodyShapeDescriptors, raceGroupingVMs, oBodySettingsVM, false, DescriptorMatchMode.Any, false);
        DescriptorsSelectionMenu.AutoSelected = associatedPlaceHolder.AssociatedModel.AutoAnnotated; // if true will be set to false as soon as user makes a selection

        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        ToggleLock = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                if (!ReferenceUnlocked)
                {
                    if (CustomMessageBox.DisplayNotificationYesNo("Confirm Unlock", "Bodyslide is exactly the name that will be fed to O/AutoBody and is expected to be read from the data in your CalienteTools\\BodySlide\\SliderPresets directory. Are you sure you want to unlock it for editing?"))
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

        this.WhenAnyValue(x => x.DescriptorsSelectionMenu.Header).Subscribe(x => UpdateStatusDisplay()).DisposeWith(this);
        this.WhenAnyValue(x => x.ReferencedBodySlide).Subscribe(_ => UpdateStatusDisplay()).DisposeWith(this);
    }

    public string Label { get; set; } = "";
    public string ReferencedBodySlide { get; set; } = "";
    public string SliderGroup { get; set; } = "";
    public string Notes { get; set; } = "";
    public VM_BodyShapeDescriptorSelectionMenu DescriptorsSelectionMenu { get; set; }
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
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();
    public RelayCommand ToggleLock { get; }
    public RelayCommand AddAllowedAttribute { get; }
    public RelayCommand AddDisallowedAttribute { get; }
    public RelayCommand DeleteMe { get; }
    public RelayCommand CloneCommand { get; }
    public RelayCommand ToggleHide { get; }

    public SolidColorBrush BorderColor { get; set; }

    public string HideButtonText { get; set; } = "Hide";
    public string StatusHeader { get; set; }
    public string StatusText { get; set; }
    public bool ShowStatus { get; set; }

    public static SolidColorBrush BorderColorMissing = CommonColors.Red;
    public static SolidColorBrush BorderColorUnannotated = CommonColors.Yellow;
    public static SolidColorBrush BorderColorValid = CommonColors.LightGreen;
    public static SolidColorBrush BorderColorHidden = CommonColors.LightSlateGrey;
    public static SolidColorBrush BorderColorAutoAnnotated = CommonColors.MediumPurple;

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
    public void UpdateStatusDisplay()
    {
        if (!ParentMenuVM.BodySlidesUI.CurrentlyExistingBodySlides.Contains(this.ReferencedBodySlide))
        {
            BorderColor = BorderColorMissing;
            StatusHeader = "Warning:";
            StatusText = "Source BodySlide XML files are missing. Will not be assigned.";
            ShowStatus = true;
        }
        else if (AssociatedPlaceHolder.IsHidden)
        {
            BorderColor = BorderColorHidden;
        }
        else if (DescriptorsSelectionMenu.AutoSelected && DescriptorsSelectionMenu.IsAnnotated())
        {
            BorderColor = BorderColorAutoAnnotated;
            StatusHeader = "Note:";
            StatusText = "Bodyslide has been automatically annotated";
            ShowStatus = true;
        }
        else if(!DescriptorsSelectionMenu.IsAnnotated())
        {
            BorderColor = BorderColorUnannotated;
            StatusHeader = "Warning:";
            StatusText = "Bodyslide has not been annotated with descriptors. May not pair correctly with textures.";
            ShowStatus = true;
        }
        else
        {
            BorderColor = BorderColorValid;
            StatusHeader = string.Empty;
            StatusText = string.Empty;
            ShowStatus = false;   
        }
    }

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

        bool autoAnnotated = model.AutoAnnotated;
        if (ParentMenuVM.MiscUI.AutoApplyMissingAnnotations && !model.BodyShapeDescriptors.Any())
        {
            _bodySlideAnnotator.AnnotateBodySlide(model, ParentMenuVM.AnnotatorUI.DumpToModel());
            autoAnnotated = true;
        }

        DescriptorsSelectionMenu.CopyInFromHashSet(model.BodyShapeDescriptors);

        if (autoAnnotated)
        {
            DescriptorsSelectionMenu.AutoSelected = true;
        }

        AllowedRaces.AddRange(model.AllowedRaces);
        AllowedRaceGroupings.CopyInRaceGroupingsByLabel(model.AllowedRaceGroupings, _raceGroupingVMs);
        foreach (var grouping in AllowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.AllowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        DisallowedRaces.AddRange(model.DisallowedRaces);
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
        if (!DescriptorsSelectionMenu.AutoSelected) // don't save auto-generated descriptors. They will be recalculated upon next load (potentially with updated rules), unless the users makes a manual selection
        {
            model.BodyShapeDescriptors = DescriptorsSelectionMenu.DumpToHashSet();
        }
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
        model.AutoAnnotated = DescriptorsSelectionMenu.AutoSelected;
        return model;
    }
}