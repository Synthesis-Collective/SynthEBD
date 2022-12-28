using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

public class VM_BodyShapeDescriptorRules : VM
{
    private Logger _logger;
    private VM_NPCAttributeCreator _attributeCreator;
    private AttributeMatcher _attributeMatcher;
    public delegate VM_BodyShapeDescriptorRules Factory(VM_BodyShapeDescriptor descriptor, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig);
    public VM_BodyShapeDescriptorRules(VM_BodyShapeDescriptor descriptor, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, Logger logger, VM_NPCAttributeCreator creator, AttributeMatcher attributeMatcher)
    {
        _logger = logger;
        _attributeCreator = creator;
        _attributeMatcher = attributeMatcher;

        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

        ParentConfig = parentConfig;
        
        PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        AddAllowedAttribute = new RelayCommand(
            canExecute: _ => true,
            execute: _ => AllowedAttributes.Add(_attributeCreator.CreateNewFromUI(this.AllowedAttributes, true, null, ParentConfig.AttributeGroupMenu.Groups))
        );

        AddDisallowedAttribute = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisallowedAttributes.Add(_attributeCreator.CreateNewFromUI(this.DisallowedAttributes, false, null, ParentConfig.AttributeGroupMenu.Groups))
        );
    }

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
    IHasAttributeGroupMenu ParentConfig { get; set; }
    public ILinkCache lk { get; private set; }
    
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();

    public RelayCommand AddAllowedAttribute { get; }
    public RelayCommand AddDisallowedAttribute { get; }

    public void CopyInViewModelFromModel(BodyShapeDescriptorRules model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
        AllowedRaces = new ObservableCollection<FormKey>(model.AllowedRaces);
        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        foreach (var grouping in AllowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.AllowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        DisallowedRaces = new ObservableCollection<FormKey>(model.DisallowedRaces);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

        foreach (var grouping in DisallowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.DisallowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        AllowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.AllowedAttributes, ParentConfig.AttributeGroupMenu.Groups, true, null, _attributeCreator, _logger);
        DisallowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.DisallowedAttributes, ParentConfig.AttributeGroupMenu.Groups, false, null, _attributeCreator, _logger);
        foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }
        bAllowUnique = model.AllowUnique;
        bAllowNonUnique = model.AllowNonUnique;
        bAllowRandom = model.AllowRandom;
        ProbabilityWeighting = model.ProbabilityWeighting;
        WeightRange = model.WeightRange;
    }

    public BodyShapeDescriptorRules DumpViewModelToModel()
    {
        BodyShapeDescriptorRules model = new();
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
        model.WeightRange = WeightRange;
        return model;
    }
}