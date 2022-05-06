using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using Noggog.WPF;
using ReactiveUI;

namespace SynthEBD;

public class VM_ConfigDistributionRules : ViewModel, IProbabilityWeighted
{
    public VM_ConfigDistributionRules(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_AssetPack parentAssetPack, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu)
    {
        SubscribedRaceGroupings = raceGroupingVMs;
        ParentAssetPack = parentAssetPack;

        this.AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(SubscribedRaceGroupings);
        this.DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(SubscribedRaceGroupings);
        if (parentAssetPack.TrackedBodyGenConfig != null)
        {
            this.AllowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(parentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, parentAssetPack);
            this.DisallowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(parentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, parentAssetPack);
        }
        AllowedBodySlideDescriptors = new VM_BodyShapeDescriptorSelectionMenu(OBodyDescriptorMenu, SubscribedRaceGroupings, parentAssetPack);
        DisallowedBodySlideDescriptors = new VM_BodyShapeDescriptorSelectionMenu(OBodyDescriptorMenu, SubscribedRaceGroupings, parentAssetPack);

        //UI-related

        AddAllowedAttribute = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.AllowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.AllowedAttributes, true, null, parentAssetPack.AttributeGroupMenu.Groups))
        );

        AddDisallowedAttribute = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.DisallowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.DisallowedAttributes, false, null, parentAssetPack.AttributeGroupMenu.Groups))
        );

        AddNPCKeyword = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.AddKeywords.Add(new VM_CollectionMemberString("", this.AddKeywords))
        );
        
        _linkCache = PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
            .ToGuiProperty(this, nameof(LinkCache), default(ILinkCache))
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
    private readonly ObservableAsPropertyHelper<ILinkCache> _linkCache;
    public ILinkCache LinkCache => _linkCache.Value;
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();

    public RelayCommand AddAllowedAttribute { get; }
    public RelayCommand AddDisallowedAttribute { get; }
    public RelayCommand AddNPCKeyword { get; }
    public VM_AssetPack ParentAssetPack { get; set; }
    public ObservableCollection<VM_RaceGrouping> SubscribedRaceGroupings { get; set; }

    public static VM_ConfigDistributionRules GetViewModelFromModel(AssetPack.ConfigDistributionRules model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_AssetPack parentAssetPack, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu)
    {
        VM_ConfigDistributionRules viewModel = new VM_ConfigDistributionRules(raceGroupingVMs, parentAssetPack, OBodyDescriptorMenu);

        if (model != null)
        {

            viewModel.AllowedRaces = new ObservableCollection<FormKey>(model.AllowedRaces);
            viewModel.AllowedRaceGroupings = VM_RaceGroupingCheckboxList.GetRaceGroupingsByLabel(model.AllowedRaceGroupings, raceGroupingVMs);
            viewModel.DisallowedRaces = new ObservableCollection<FormKey>(model.DisallowedRaces);
            viewModel.DisallowedRaceGroupings = VM_RaceGroupingCheckboxList.GetRaceGroupingsByLabel(model.DisallowedRaceGroupings, raceGroupingVMs);
            viewModel.AllowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.AllowedAttributes, parentAssetPack.AttributeGroupMenu.Groups, true, null);
            viewModel.DisallowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.DisallowedAttributes, parentAssetPack.AttributeGroupMenu.Groups, false, null);
            foreach (var x in viewModel.DisallowedAttributes) { x.DisplayForceIfOption = false; }
            viewModel.AllowUnique = model.AllowUnique;
            viewModel.AllowNonUnique = model.AllowNonUnique;
            viewModel.AddKeywords = VM_CollectionMemberString.InitializeCollectionFromHashSet(model.AddKeywords);
            viewModel.ProbabilityWeighting = model.ProbabilityWeighting;
            viewModel.WeightRange = model.WeightRange;

            if (parentAssetPack.TrackedBodyGenConfig != null)
            {
                viewModel.AllowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.AllowedBodyGenDescriptors, parentAssetPack.TrackedBodyGenConfig.DescriptorUI, viewModel.SubscribedRaceGroupings, parentAssetPack);
                viewModel.DisallowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.DisallowedBodyGenDescriptors, parentAssetPack.TrackedBodyGenConfig.DescriptorUI, viewModel.SubscribedRaceGroupings, parentAssetPack);
            }
        }
        return viewModel;
    }

    public static AssetPack.ConfigDistributionRules DumpViewModelToModel(VM_ConfigDistributionRules viewModel)
    {
        var model = new AssetPack.ConfigDistributionRules();

        model.AllowedRaces = viewModel.AllowedRaces.ToHashSet();
        model.AllowedRaceGroupings = viewModel.AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet();
        model.DisallowedRaces = viewModel.DisallowedRaces.ToHashSet();
        model.DisallowedRaceGroupings = viewModel.DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet();
        model.AllowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.AllowedAttributes);
        model.DisallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.DisallowedAttributes);
        model.AllowUnique = viewModel.AllowUnique;
        model.AllowNonUnique = viewModel.AllowNonUnique;
        model.AddKeywords = viewModel.AddKeywords.Select(x => x.Content).ToHashSet();
        model.ProbabilityWeighting = viewModel.ProbabilityWeighting;
        model.WeightRange = viewModel.WeightRange;

        model.AllowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.AllowedBodyGenDescriptors);
        model.DisallowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.DisallowedBodyGenDescriptors);

        return model;
    }
}