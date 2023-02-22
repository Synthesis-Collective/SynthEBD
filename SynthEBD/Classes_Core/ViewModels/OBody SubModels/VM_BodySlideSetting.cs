using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.Windows.Media;
using ReactiveUI;
using static SynthEBD.VM_NPCAttribute;
using ControlzEx.Standard;

namespace SynthEBD;

public class VM_BodySlideSetting : VM
{
    private IEnvironmentStateProvider _environmentProvider;
    public VM_SettingsOBody ParentMenuVM;
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly VM_BodyShapeDescriptorCreationMenu _bodyShapeDescriptors;
    private readonly ObservableCollection<VM_RaceGrouping> _raceGroupingVMs;
    private readonly VM_BodySlideSetting.Factory _selfFactory;
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;

    public delegate VM_BodySlideSetting Factory(VM_BodyShapeDescriptorCreationMenu BodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, ObservableCollection<VM_BodySlideSetting> parentCollection);
    public VM_BodySlideSetting(VM_BodyShapeDescriptorCreationMenu bodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, ObservableCollection<VM_BodySlideSetting> parentCollection, VM_SettingsOBody oBodySettingsVM, VM_NPCAttributeCreator attributeCreator, IEnvironmentStateProvider environmentProvider, Logger logger, Factory selfFactory, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
    {
        ParentMenuVM = oBodySettingsVM;
        _environmentProvider = environmentProvider;
        _attributeCreator = attributeCreator;
        _bodyShapeDescriptors = bodyShapeDescriptors;
        _raceGroupingVMs = raceGroupingVMs;
        _selfFactory = selfFactory;
        _descriptorSelectionFactory = descriptorSelectionFactory;

        DescriptorsSelectionMenu = _descriptorSelectionFactory(bodyShapeDescriptors, raceGroupingVMs, oBodySettingsVM, false, DescriptorMatchMode.Any);
        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

        ParentCollection = parentCollection;

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
            execute: _ => ParentCollection.Remove(this)
        );

        ToggleHide = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IsHidden)
                {
                    IsHidden = false;
                    HideButtonText = "Hide";
                }
                else
                {
                    IsHidden = true;
                    HideButtonText = "Unhide";
                }
            }
        );

        this.WhenAnyValue(x => x.IsHidden).Subscribe(x =>
        {
            if (!oBodySettingsVM.BodySlidesUI.ShowHidden && IsHidden)
            {
                IsVisible = false;
            }
            else
            {
                IsVisible = true;
            }
            UpdateStatusDisplay();
        }).DisposeWith(this);

        CloneCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Clone()
        );

        this.WhenAnyValue(x => x.DescriptorsSelectionMenu.Header).Subscribe(x => UpdateStatusDisplay()).DisposeWith(this);
        this.WhenAnyValue(x => x.ReferencedBodySlide).Subscribe(_ => UpdateStatusDisplay()).DisposeWith(this);
    }

    public string Label { get; set; } = "";
    public string ReferencedBodySlide { get; set; } = "";
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

    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();
    public RelayCommand ToggleLock { get; }
    public RelayCommand AddAllowedAttribute { get; }
    public RelayCommand AddDisallowedAttribute { get; }
    public RelayCommand DeleteMe { get; }
    public RelayCommand CloneCommand { get; }
    public RelayCommand ToggleHide { get; }
    public ObservableCollection<VM_BodySlideSetting> ParentCollection { get; set; }

    public SolidColorBrush BorderColor { get; set; }
    public bool IsVisible {  get; set; } = true;
    public bool IsHidden { get; set; } = false; // not the same as IsVisible. IsVisible can be set true if the "show hidden" button is checked.
    public string HideButtonText { get; set; } = "Hide";
    public string StatusHeader { get; set; }
    public string StatusText { get; set; }
    public bool ShowStatus { get; set; }

    public static SolidColorBrush BorderColorMissing = new SolidColorBrush(Colors.Red);
    public static SolidColorBrush BorderColorUnannotated = new SolidColorBrush(Colors.Yellow);
    public static SolidColorBrush BorderColorValid = new SolidColorBrush(Colors.LightGreen);

    public VM_BodySlideSetting Clone()
    {
        var cloneModel = DumpViewModelToModel(this);
        var cloneViewModel = _selfFactory(_bodyShapeDescriptors, _raceGroupingVMs, ParentCollection);
        cloneViewModel.CopyInViewModelFromModel(cloneModel);
        int lastClonePosition = cloneViewModel.RenameByIndex();
        ParentCollection.Insert(lastClonePosition + 1, cloneViewModel);
        return cloneViewModel;
    }

    public int RenameByIndex()
    {
        int cloneIndex = 0;
        int lastClonePosition = 0;

        for (int i = 0; i < ParentCollection.Count; i++)
        {
            var clone = ParentCollection[i];
            if (clone.ReferencedBodySlide != ReferencedBodySlide) { continue; }
            lastClonePosition = i;
            if (GetTrailingInt(clone.Label, out int currentIndex) && currentIndex > cloneIndex)
            {
                cloneIndex = currentIndex;
            }
        }

        if (cloneIndex == 0)
        {
            cloneIndex = 2;
        }
        else
        {
            cloneIndex++;
        }

        if (GetTrailingInt(Label, out int selectedCloneIndex))
        {
            Label = Label.TrimEnd(selectedCloneIndex.ToString()) + cloneIndex.ToString();
        }
        else
        {
            Label += cloneIndex;
        }
        return lastClonePosition;
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

    public VM_BodySlideSetting CopyToNewCollection(ObservableCollection<VM_BodySlideSetting> parentCollection)
    {
        var model = DumpViewModelToModel(this);
        return _selfFactory(_bodyShapeDescriptors, _raceGroupingVMs, parentCollection);
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
        else if (IsHidden)
        {
            BorderColor = new SolidColorBrush(Colors.LightSlateGray);
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
        Notes = model.Notes;
        DescriptorsSelectionMenu = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.BodyShapeDescriptors, ParentMenuVM.DescriptorUI, _raceGroupingVMs, ParentMenuVM, false, DescriptorMatchMode.Any, _descriptorSelectionFactory);
        AllowedRaces = new ObservableCollection<FormKey>(model.AllowedRaces);
        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(_raceGroupingVMs);
        foreach (var grouping in AllowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.AllowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        DisallowedRaces = new ObservableCollection<FormKey>(model.DisallowedRaces);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(_raceGroupingVMs);

        foreach (var grouping in DisallowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.DisallowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        AllowedAttributes = _attributeCreator.GetViewModelsFromModels(model.AllowedAttributes, ParentMenuVM.AttributeGroupMenu.Groups, true, null);
        DisallowedAttributes = _attributeCreator.GetViewModelsFromModels(model.DisallowedAttributes, ParentMenuVM.AttributeGroupMenu.Groups, false, null);
        foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }
        bAllowUnique = model.AllowUnique;
        bAllowNonUnique = model.AllowNonUnique;
        bAllowRandom = model.AllowRandom;
        ProbabilityWeighting = model.ProbabilityWeighting;
        WeightRange = model.WeightRange.Clone();

        IsHidden = model.HideInMenu;
    }

    public static BodySlideSetting DumpViewModelToModel(VM_BodySlideSetting viewModel)
    {
        BodySlideSetting model = new BodySlideSetting();
        model.Label = viewModel.Label;
        model.ReferencedBodySlide = viewModel.ReferencedBodySlide;
        model.Notes = viewModel.Notes;
        model.BodyShapeDescriptors = viewModel.DescriptorsSelectionMenu.DumpToHashSet();
        model.AllowedRaces = viewModel.AllowedRaces.ToHashSet();
        model.AllowedRaceGroupings = viewModel.AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.DisallowedRaces = viewModel.DisallowedRaces.ToHashSet();
        model.DisallowedRaceGroupings = viewModel.DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.AllowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.AllowedAttributes);
        model.DisallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.DisallowedAttributes);
        model.AllowUnique = viewModel.bAllowUnique;
        model.AllowNonUnique = viewModel.bAllowNonUnique;
        model.AllowRandom = viewModel.bAllowRandom;
        model.ProbabilityWeighting = viewModel.ProbabilityWeighting;
        model.WeightRange = viewModel.WeightRange.Clone();
        model.HideInMenu = viewModel.IsHidden;
        return model;
    }

    private static bool GetTrailingInt(string input, out int number)
    {
        number = 0;
        var stack = new Stack<char>();

        for (var i = input.Length - 1; i >= 0; i--)
        {
            if (!char.IsNumber(input[i]))
            {
                break;
            }

            stack.Push(input[i]);
        }

        var result = new string(stack.ToArray());
        if (result == null || !int.TryParse(result, out number))
        {
            return false;
        }
        else
        {
            return true;
        }
    }
}