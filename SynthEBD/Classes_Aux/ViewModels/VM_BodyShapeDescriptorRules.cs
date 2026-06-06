using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

/// <summary>
/// View model for a body-shape descriptor's allow/disallow rules: race / race-grouping and attribute
/// filters, unique/non-unique/random flags, probability weight, and weight range. Round-trips to/from
/// <see cref="BodyShapeDescriptorRules"/> and can also merge rules.
/// </summary>
public class VM_BodyShapeDescriptorRules : VM
{
    private IEnvironmentStateProvider _environmentProvider;
    private Logger _logger;
    private VM_NPCAttributeCreator _attributeCreator;
    private AttributeMatcher _attributeMatcher;
    /// <summary>Autofac factory delegate for constructing the rules under a descriptor.</summary>
    public delegate VM_BodyShapeDescriptorRules Factory(VM_BodyShapeDescriptor descriptor, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig);
    /// <summary>Creates the rules VM, building the allowed/disallowed race-grouping checkbox lists and wiring the add-attribute commands.</summary>
    /// <param name="descriptor">The descriptor these rules belong to.</param>
    /// <param name="raceGroupingVMs">Race groupings available to the rules.</param>
    /// <param name="parentConfig">The owning config (attribute-group menu source).</param>
    /// <param name="environmentProvider">Supplies the link cache for race pickers.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="creator">Factory for attribute VMs.</param>
    /// <param name="attributeMatcher">Matcher (held for validation use).</param>
    public VM_BodyShapeDescriptorRules(VM_BodyShapeDescriptor descriptor, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig, IEnvironmentStateProvider environmentProvider, Logger logger, VM_NPCAttributeCreator creator, AttributeMatcher attributeMatcher)
    {
        _environmentProvider = environmentProvider;
        _logger = logger;
        _attributeCreator = creator;
        _attributeMatcher = attributeMatcher;

        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

        ParentConfig = parentConfig;

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        AddAllowedAttribute = new RelayCommand(
            canExecute: _ => true,
            execute: _ => AllowedAttributes.Add(_attributeCreator.CreateNewFromUI(AllowedAttributes, true, null, ParentConfig.AttributeGroupMenu.Groups))
        );

        AddDisallowedAttribute = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DisallowedAttributes.Add(_attributeCreator.CreateNewFromUI(DisallowedAttributes, false, null, ParentConfig.AttributeGroupMenu.Groups))
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

    /// <summary>Replaces this VM's rules with those from a model.</summary>
    /// <param name="model">The rules model to load.</param>
    /// <param name="raceGroupingVMs">Race groupings used to resolve grouping selections.</param>
    public void CopyInViewModelFromModel(BodyShapeDescriptorRules model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
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
        bAllowUnique = model.AllowUnique;
        bAllowNonUnique = model.AllowNonUnique;
        bAllowRandom = model.AllowRandom;
        ProbabilityWeighting = model.ProbabilityWeighting;
        WeightRange = model.WeightRange;
    }

    /// <summary>Projects this VM back into a <see cref="BodyShapeDescriptorRules"/> model.</summary>
    /// <returns>The populated model.</returns>
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

    /// <summary>Merges another model's rules into this VM, unioning races/attributes and taking the more restrictive option for each flag/range.</summary>
    /// <param name="model">The rules model to merge in.</param>
    /// <param name="raceGroupingVMs">Race groupings used to resolve grouping selections.</param>
    public void MergeInViewModelFromModel(BodyShapeDescriptorRules model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
        AllowedRaces.AddRange(model.AllowedRaces.Where(x => !AllowedRaces.Contains(x)).ToArray());
        foreach (var grouping in AllowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.AllowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
        }

        DisallowedRaces.AddRange(model.DisallowedRaces.Where(x => !DisallowedRaces.Contains(x)).ToArray());
        foreach (var grouping in DisallowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.DisallowedRaceGroupings.Contains(grouping.SubscribedMasterRaceGrouping.Label))
            {
                grouping.IsSelected = true;
            }
        }

        _attributeCreator.CopyInFromModels(model.AllowedAttributes, AllowedAttributes, ParentConfig.AttributeGroupMenu.Groups, true, null);
        _attributeCreator.CopyInFromModels(model.DisallowedAttributes, DisallowedAttributes, ParentConfig.AttributeGroupMenu.Groups, false, null);
        foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }

        if (bAllowUnique == false || model.AllowUnique == false) { bAllowUnique = false; }
        else { bAllowUnique = true; }

        if (bAllowNonUnique == false || model.AllowNonUnique == false) { bAllowNonUnique = false; }
        else { bAllowNonUnique = true; }

        if (bAllowRandom == false || model.AllowRandom == false) { bAllowRandom = false; }
        else { bAllowRandom = true; }

        if (ProbabilityWeighting == 1 && model.ProbabilityWeighting != 1)
        {
            ProbabilityWeighting = model.ProbabilityWeighting;
        }

        if (model.WeightRange.Lower > WeightRange.Lower)
        {
            WeightRange.Lower = model.WeightRange.Lower;
        }

        if (model.WeightRange.Upper < WeightRange.Upper)
        {
            WeightRange.Upper = model.WeightRange.Upper;
        }
    }
}