using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;
using static SynthEBD.VM_NPCAttribute;
using DynamicData;

namespace SynthEBD;

public class VM_ConfigDistributionRules : VM, IProbabilityWeighted
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly VM_SettingsOBody _oBody;
    private readonly Logger _logger;
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
    public delegate VM_ConfigDistributionRules Factory(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_AssetPack parentAssetPack);

    public VM_ConfigDistributionRules(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_AssetPack parentAssetPack, IEnvironmentStateProvider environmentProvider, VM_SettingsOBody oBody, Logger logger, VM_NPCAttributeCreator attributeCreator, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
    {
        _environmentProvider = environmentProvider;
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
            AllowedBodyGenDescriptors = _descriptorSelectionFactory(parentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, parentAssetPack, true, DescriptorMatchMode.All);
            DisallowedBodyGenDescriptors = _descriptorSelectionFactory(parentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, parentAssetPack, true, DescriptorMatchMode.Any);
        }
        AllowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, SubscribedRaceGroupings, parentAssetPack, true, DescriptorMatchMode.All);
        DisallowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, SubscribedRaceGroupings, parentAssetPack, true, DescriptorMatchMode.Any);

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
        
        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => LinkCache = x)
            .DisposeWith(this);

        ParentAssetPack.WhenAnyValue(x => x.ConfigType).Subscribe(x =>
        {
            if (x == AssetPackType.Primary)
            {
                ProbabilityLabelStr = "Distribution Probability Weighting";
                bIsMixIn = false;
                if (ProbabilityWeighting / 100 >= 1)
                {
                    ProbabilityWeighting /= 100;
                }
            }
            else if (x == AssetPackType.MixIn)
            {
                ProbabilityLabelStr = "Distribution Probability";
                if (ProbabilityWeighting * 100 <= 100)
                {
                    ProbabilityWeighting *= 100;
                }
                bIsMixIn = true;
            }
        }).DisposeWith(this);
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
    public string ProbabilityLabelStr { get; set; } = "Distribution Probability Weighting";
    public bool bIsMixIn { get; set; } = false;

    public void CopyInViewModelFromModel(AssetPack.ConfigDistributionRules model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_AssetPack parentAssetPack)
    {
        if (model != null)
        {
            Noggog.ListExt.AddRange(AllowedRaces, model.AllowedRaces);
            AllowedRaceGroupings.CopyInRaceGroupingsByLabel(model.AllowedRaceGroupings, raceGroupingVMs);
            Noggog.ListExt.AddRange(DisallowedRaces, model.DisallowedRaces);
            DisallowedRaceGroupings.CopyInRaceGroupingsByLabel(model.DisallowedRaceGroupings, raceGroupingVMs);
            _attributeCreator.CopyInFromModels(model.AllowedAttributes, AllowedAttributes, parentAssetPack.AttributeGroupMenu.Groups, true, null);
            _attributeCreator.CopyInFromModels(model.DisallowedAttributes, DisallowedAttributes, parentAssetPack.AttributeGroupMenu.Groups, false, null);
            foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }
            AllowUnique = model.AllowUnique;
            AllowNonUnique = model.AllowNonUnique;
            VM_CollectionMemberString.CopyInObservableCollectionFromICollection(model.AddKeywords, AddKeywords);
            ProbabilityWeighting = model.ProbabilityWeighting;
            WeightRange = model.WeightRange;

            if (parentAssetPack.TrackedBodyGenConfig != null)
            {
                AllowedBodyGenDescriptors.CopyInFromHashSet(model.AllowedBodyGenDescriptors);
                DisallowedBodyGenDescriptors.CopyInFromHashSet(model.DisallowedBodyGenDescriptors);
            }

            AllowedBodySlideDescriptors.CopyInFromHashSet(model.AllowedBodySlideDescriptors);
            DisallowedBodySlideDescriptors.CopyInFromHashSet(model.DisallowedBodySlideDescriptors);
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

        model.AllowedBodyGenDescriptors = viewModel.AllowedBodyGenDescriptors?.DumpToHashSet() ?? null;
        model.AllowedBodyGenMatchMode = viewModel.AllowedBodyGenDescriptors?.MatchMode ?? DescriptorMatchMode.All;
        model.DisallowedBodyGenDescriptors = viewModel.DisallowedBodyGenDescriptors?.DumpToHashSet() ?? null;
        model.DisallowedBodyGenMatchMode = viewModel.DisallowedBodyGenDescriptors?.MatchMode ?? DescriptorMatchMode.Any;
        model.AllowedBodySlideDescriptors = viewModel.AllowedBodySlideDescriptors.DumpToHashSet();
        model.AllowedBodySlideMatchMode = viewModel.AllowedBodySlideDescriptors.MatchMode;
        model.DisallowedBodySlideDescriptors = viewModel.DisallowedBodySlideDescriptors.DumpToHashSet();
        model.DisallowedBodySlideMatchMode = viewModel.DisallowedBodySlideDescriptors.MatchMode;

        return model;
    }

    public List<string> GetRulesSummary()
    {
        List<string> rulesSummary = new();
        string tmpReport = "";
        if (_logger.GetRaceLogString("Allowed", AllowedRaces, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (_logger.GetRaceGroupingLogString("Allowed", AllowedRaceGroupings, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (_logger.GetRaceLogString("Disallowed", DisallowedRaces, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (_logger.GetRaceGroupingLogString("Disallowed", DisallowedRaceGroupings, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (_logger.GetAttributeLogString("Allowed", AllowedAttributes, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (_logger.GetAttributeLogString("Disallowed", DisallowedAttributes, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (!AllowUnique) { rulesSummary.Add("Unique NPCs: Disallowed"); }
        if (!AllowNonUnique) { rulesSummary.Add("Generic NPCs: Disallowed"); }
        if (ProbabilityWeighting != 1) { rulesSummary.Add("Probability Weighting: " + ProbabilityWeighting.ToString()); }
        if (WeightRange.Lower != 0 || WeightRange.Upper != 100) { rulesSummary.Add("Weight Range: " + WeightRange.Lower.ToString() + " to " + WeightRange.Upper.ToString()); }

        if (rulesSummary.Any())
        {
            rulesSummary.Insert(0, "");
            rulesSummary.Insert(0, "Whole-Config Distribution Rules:");
        }
        
        return rulesSummary;
    }
}