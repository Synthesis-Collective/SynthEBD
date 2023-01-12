using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

public class VM_ConfigDistributionRules : VM, IProbabilityWeighted
{
    private readonly IEnvironmentStateProvider _stateProvider;
    private readonly VM_SettingsOBody _oBody;
    private readonly Logger _logger;
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
    public delegate VM_ConfigDistributionRules Factory(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_AssetPack parentAssetPack);

    public VM_ConfigDistributionRules(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_AssetPack parentAssetPack, IEnvironmentStateProvider stateProvider, VM_SettingsOBody oBody, Logger logger, VM_NPCAttributeCreator attributeCreator, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
    {
        _stateProvider = stateProvider;
        _oBody = oBody;
        _logger = logger;
        _attributeCreator = attributeCreator;
        _descriptorSelectionFactory = descriptorSelectionFactory;

        SubscribedRaceGroupings = raceGroupingVMs;
        ParentAssetPack = parentAssetPack;

        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(SubscribedRaceGroupings);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(SubscribedRaceGroupings);
        if (parentAssetPack.TrackedBodyGenConfig != null)
        {
            AllowedBodyGenDescriptors = _descriptorSelectionFactory(parentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, parentAssetPack);
            DisallowedBodyGenDescriptors = _descriptorSelectionFactory(parentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, parentAssetPack);
        }
        AllowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, SubscribedRaceGroupings, parentAssetPack);
        DisallowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, SubscribedRaceGroupings, parentAssetPack);

        //UI-related

        AddAllowedAttribute = new RelayCommand(
            canExecute: _ => true,
            execute: _ => AllowedAttributes.Add(_attributeCreator.CreateNewFromUI(AllowedAttributes, true, null, parentAssetPack.AttributeGroupMenu.Groups))
        );

        AddDisallowedAttribute = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisallowedAttributes.Add(_attributeCreator.CreateNewFromUI(DisallowedAttributes, false, null, parentAssetPack.AttributeGroupMenu.Groups))
        );

        AddNPCKeyword = new RelayCommand(
            canExecute: _ => true,
            execute: _ => AddKeywords.Add(new VM_CollectionMemberString("", this.AddKeywords))
        );
        
        _stateProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => LinkCache = x)
            .DisposeWith(this);
    }

    public ObservableCollection<FormKey> AllowedRaces { get; set; } = new();
    public VM_RaceGroupingCheckboxList AllowedRaceGroupings { get; set; }
    public ObservableCollection<FormKey> DisallowedRaces { get; set; } = new();
    public VM_RaceGroupingCheckboxList DisallowedRaceGroupings { get; set; }
    public ObservableCollection<VM_NPCAttribute> AllowedAttributes { get; set; } = new();
    public ObservableCollection<VM_NPCAttribute> DisallowedAttributes { get; set; } = new();
    public bool AllowUnique { get; set; } = true;
    public bool AllowNonUnique { get; set; } = true;
    public ObservableCollection<VM_CollectionMemberString> AddKeywords { get; set; } = new();
    public double ProbabilityWeighting { get; set; } = 1;
    public VM_BodyShapeDescriptorSelectionMenu AllowedBodyGenDescriptors { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu DisallowedBodyGenDescriptors { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu AllowedBodySlideDescriptors { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu DisallowedBodySlideDescriptors { get; set; }
    public NPCWeightRange WeightRange { get; set; } = new();

    //UI-related
    public ILinkCache LinkCache { get; private set; }
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();

    public RelayCommand AddAllowedAttribute { get; }
    public RelayCommand AddDisallowedAttribute { get; }
    public RelayCommand AddNPCKeyword { get; }
    public VM_AssetPack ParentAssetPack { get; set; }
    public ObservableCollection<VM_RaceGrouping> SubscribedRaceGroupings { get; set; }

    public void CopyInViewModelFromModel(AssetPack.ConfigDistributionRules model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_AssetPack parentAssetPack)
    {
        if (model != null)
        {
            AllowedRaces = new ObservableCollection<FormKey>(model.AllowedRaces);
            AllowedRaceGroupings = VM_RaceGroupingCheckboxList.GetRaceGroupingsByLabel(model.AllowedRaceGroupings, raceGroupingVMs);
            DisallowedRaces = new ObservableCollection<FormKey>(model.DisallowedRaces);
            DisallowedRaceGroupings = VM_RaceGroupingCheckboxList.GetRaceGroupingsByLabel(model.DisallowedRaceGroupings, raceGroupingVMs);
            AllowedAttributes = _attributeCreator.GetViewModelsFromModels(model.AllowedAttributes, parentAssetPack.AttributeGroupMenu.Groups, true, null);
            DisallowedAttributes = _attributeCreator.GetViewModelsFromModels(model.DisallowedAttributes, parentAssetPack.AttributeGroupMenu.Groups, false, null);
            foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }
            AllowUnique = model.AllowUnique;
            AllowNonUnique = model.AllowNonUnique;
            AddKeywords = VM_CollectionMemberString.InitializeObservableCollectionFromICollection(model.AddKeywords);
            ProbabilityWeighting = model.ProbabilityWeighting;
            WeightRange = model.WeightRange;

            if (parentAssetPack.TrackedBodyGenConfig != null)
            {
                AllowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.AllowedBodyGenDescriptors, parentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, parentAssetPack, _descriptorSelectionFactory);
                DisallowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.DisallowedBodyGenDescriptors, parentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, parentAssetPack, _descriptorSelectionFactory);
            }

            AllowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.AllowedBodySlideDescriptors, _oBody.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack, _descriptorSelectionFactory);
            DisallowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.DisallowedBodySlideDescriptors, _oBody.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack, _descriptorSelectionFactory);
        }
    }

    public static AssetPack.ConfigDistributionRules DumpViewModelToModel(VM_ConfigDistributionRules viewModel)
    {
        var model = new AssetPack.ConfigDistributionRules();

        model.AllowedRaces = viewModel.AllowedRaces.ToHashSet();
        model.AllowedRaceGroupings = viewModel.AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.DisallowedRaces = viewModel.DisallowedRaces.ToHashSet();
        model.DisallowedRaceGroupings = viewModel.DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.AllowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.AllowedAttributes);
        model.DisallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.DisallowedAttributes);
        model.AllowUnique = viewModel.AllowUnique;
        model.AllowNonUnique = viewModel.AllowNonUnique;
        model.AddKeywords = viewModel.AddKeywords.Select(x => x.Content).ToHashSet();
        model.ProbabilityWeighting = viewModel.ProbabilityWeighting;
        model.WeightRange = viewModel.WeightRange;

        model.AllowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.AllowedBodyGenDescriptors);
        model.DisallowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.DisallowedBodyGenDescriptors);
        model.AllowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.AllowedBodySlideDescriptors);
        model.DisallowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.DisallowedBodySlideDescriptors);

        return model;
    }
}