using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.Windows.Media;
using ReactiveUI;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

public class VM_BodySlideSetting : VM
{
    private IEnvironmentStateProvider _environmentProvider;
    public VM_SettingsOBody ParentMenuVM;
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly Logger _logger;
    private readonly VM_BodySlideSetting.Factory _selfFactory;
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;

    public delegate VM_BodySlideSetting Factory(VM_BodyShapeDescriptorCreationMenu BodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, ObservableCollection<VM_BodySlideSetting> parentCollection);
    public VM_BodySlideSetting(VM_BodyShapeDescriptorCreationMenu BodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, ObservableCollection<VM_BodySlideSetting> parentCollection, VM_SettingsOBody oBodySettingsVM, VM_NPCAttributeCreator attributeCreator, IEnvironmentStateProvider environmentProvider, Logger logger, Factory selfFactory, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
    {
        ParentMenuVM = oBodySettingsVM;
        _environmentProvider = environmentProvider;
        _attributeCreator = attributeCreator;
        _logger = logger;
        _selfFactory = selfFactory;
        _descriptorSelectionFactory = descriptorSelectionFactory;

        DescriptorsSelectionMenu = _descriptorSelectionFactory(BodyShapeDescriptors, raceGroupingVMs, oBodySettingsVM);
        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

        ParentCollection = parentCollection;

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

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
        });

        Clone = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { 
                var cloneModel = DumpViewModelToModel(this);
                var cloneViewModel = _selfFactory(BodyShapeDescriptors, raceGroupingVMs, ParentCollection);
                VM_BodySlideSetting.GetViewModelFromModel(cloneModel, cloneViewModel, BodyShapeDescriptors, raceGroupingVMs, ParentMenuVM, _attributeCreator, _logger, _descriptorSelectionFactory);
                var index = parentCollection.IndexOf(this);
                parentCollection.Insert(index, cloneViewModel);
            }
        );

        DescriptorsSelectionMenu.WhenAnyValue(x => x.Header).Subscribe(x => UpdateStatusDisplay());
    }

    public string Label { get; set; } = "";
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

    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();

    public RelayCommand AddAllowedAttribute { get; }
    public RelayCommand AddDisallowedAttribute { get; }
    public RelayCommand DeleteMe { get; }
    public RelayCommand Clone { get; }
    public RelayCommand ToggleHide { get; }
    public ObservableCollection<VM_BodySlideSetting> ParentCollection { get; set; }

    public SolidColorBrush BorderColor { get; set; }
    public bool IsVisible {  get; set; } = true;
    public bool IsHidden { get; set; } = false; // not the same as IsVisible. IsVisible can be set true if the "show hidden" button is checked.
    public string HideButtonText { get; set; } = "Hide";
    public string StatusHeader { get; set; }
    public string StatusText { get; set; }
    public bool ShowStatus { get; set; }

    public void UpdateStatusDisplay()
    {
        if (!ParentMenuVM.BodySlidesUI.CurrentlyExistingBodySlides.Contains(this.Label))
        {
            BorderColor = new SolidColorBrush(Colors.Red);
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
            BorderColor = new SolidColorBrush(Colors.Yellow);
            StatusHeader = "Warning:";
            StatusText = "Bodyslide has not been annotated with descriptors. May not pair correctly with textures.";
            ShowStatus = true;
        }
        else
        {
            BorderColor = new SolidColorBrush(Colors.LightGreen);
            StatusHeader = string.Empty;
            StatusText = string.Empty;
            ShowStatus = false;
        }
    }

    public static void GetViewModelFromModel(BodySlideSetting model, VM_BodySlideSetting viewModel, VM_BodyShapeDescriptorCreationMenu descriptorMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, VM_NPCAttributeCreator attCreator, Logger logger, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
    {
        viewModel.Label = model.Label;
        viewModel.Notes = model.Notes;
        viewModel.DescriptorsSelectionMenu = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.BodyShapeDescriptors, descriptorMenu, raceGroupingVMs, parentConfig, descriptorSelectionFactory);
        viewModel.AllowedRaces = new ObservableCollection<FormKey>(model.AllowedRaces);
        viewModel.AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        foreach (var grouping in viewModel.AllowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.AllowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        viewModel.DisallowedRaces = new ObservableCollection<FormKey>(model.DisallowedRaces);
        viewModel.DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

        foreach (var grouping in viewModel.DisallowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.DisallowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        viewModel.AllowedAttributes = attCreator.GetViewModelsFromModels(model.AllowedAttributes, viewModel.ParentMenuVM.AttributeGroupMenu.Groups, true, null);
        viewModel.DisallowedAttributes = attCreator.GetViewModelsFromModels(model.DisallowedAttributes, viewModel.ParentMenuVM.AttributeGroupMenu.Groups, false, null);
        foreach (var x in viewModel.DisallowedAttributes) { x.DisplayForceIfOption = false; }
        viewModel.bAllowUnique = model.AllowUnique;
        viewModel.bAllowNonUnique = model.AllowNonUnique;
        viewModel.bAllowRandom = model.AllowRandom;
        viewModel.ProbabilityWeighting = model.ProbabilityWeighting;
        viewModel.WeightRange = model.WeightRange.Clone();

        viewModel.UpdateStatusDisplay();

        viewModel.DescriptorsSelectionMenu.WhenAnyValue(x => x.Header).Subscribe(x => viewModel.UpdateStatusDisplay());

        viewModel.IsHidden = model.HideInMenu;
    }

    public static BodySlideSetting DumpViewModelToModel(VM_BodySlideSetting viewModel)
    {
        BodySlideSetting model = new BodySlideSetting();
        model.Label = viewModel.Label;
        model.Notes = viewModel.Notes;
        model.BodyShapeDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.DescriptorsSelectionMenu);
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
}