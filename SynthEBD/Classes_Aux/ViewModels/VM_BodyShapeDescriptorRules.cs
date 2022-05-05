using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using Noggog.WPF;

namespace SynthEBD;

public class VM_BodyShapeDescriptorRules : ViewModel
{
    public VM_BodyShapeDescriptorRules(VM_BodyShapeDescriptor descriptor, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, IHasAttributeGroupMenu parentConfig)
    {
        DescriptorSignature = descriptor.Signature;
        this.AllowedRaces = new ObservableCollection<FormKey>();
        this.AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        this.DisallowedRaces = new ObservableCollection<FormKey>();
        this.DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        this.AllowedAttributes = new ObservableCollection<VM_NPCAttribute>();
        this.DisallowedAttributes = new ObservableCollection<VM_NPCAttribute>();
        this.bAllowUnique = true;
        this.bAllowNonUnique = true;
        this.bAllowRandom = true;
        this.ProbabilityWeighting = 1;
        this.WeightRange = new NPCWeightRange();

        this.lk = PatcherEnvironmentProvider.Environment.LinkCache;
        this.RacePickerFormKeys = typeof(IRaceGetter).AsEnumerable();

        ParentConfig = parentConfig;

        AddAllowedAttribute = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.AllowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.AllowedAttributes, true, null, ParentConfig.AttributeGroupMenu.Groups))
        );

        AddDisallowedAttribute = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.DisallowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.DisallowedAttributes, false, null, ParentConfig.AttributeGroupMenu.Groups))
        );
    }

    public string DescriptorSignature { get; set; }
    public ObservableCollection<FormKey> AllowedRaces { get; set; }
    public ObservableCollection<FormKey> DisallowedRaces { get; set; }
    public VM_RaceGroupingCheckboxList AllowedRaceGroupings { get; set; }
    public VM_RaceGroupingCheckboxList DisallowedRaceGroupings { get; set; }
    public ObservableCollection<VM_NPCAttribute> AllowedAttributes { get; set; } // keeping as array to allow deserialization of original zEBD settings files
    public ObservableCollection<VM_NPCAttribute> DisallowedAttributes { get; set; }
    public bool bAllowUnique { get; set; }
    public bool bAllowNonUnique { get; set; }
    public bool bAllowRandom { get; set; }
    public double ProbabilityWeighting { get; set; }
    public NPCWeightRange WeightRange { get; set; }

    IHasAttributeGroupMenu ParentConfig { get; set; }
    public ILinkCache lk { get; set; }
    public IEnumerable<Type> RacePickerFormKeys { get; set; }

    public RelayCommand AddAllowedAttribute { get; }
    public RelayCommand AddDisallowedAttribute { get; }

    public static VM_BodyShapeDescriptorRules GetViewModelFromModel(BodyShapeDescriptorRules model, VM_BodyShapeDescriptor parentVM, IHasAttributeGroupMenu parentConfig, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
        VM_BodyShapeDescriptorRules viewModel = new VM_BodyShapeDescriptorRules(parentVM, raceGroupingVMs, parentConfig);
        viewModel.DescriptorSignature = model.DescriptorSignature;
        viewModel.AllowedRaces = new ObservableCollection<FormKey>(model.AllowedRaces);
        viewModel.AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
        foreach (var grouping in viewModel.AllowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.AllowedRaceGroupings.Contains(grouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        viewModel.DisallowedRaces = new ObservableCollection<FormKey>(model.DisallowedRaces);
        viewModel.DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

        foreach (var grouping in viewModel.DisallowedRaceGroupings.RaceGroupingSelections)
        {
            if (model.DisallowedRaceGroupings.Contains(grouping.Label))
            {
                grouping.IsSelected = true;
            }
            else { grouping.IsSelected = false; }
        }

        viewModel.AllowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.AllowedAttributes, viewModel.ParentConfig.AttributeGroupMenu.Groups, true, null);
        viewModel.DisallowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.DisallowedAttributes, viewModel.ParentConfig.AttributeGroupMenu.Groups, false, null);
        foreach (var x in viewModel.DisallowedAttributes) { x.DisplayForceIfOption = false; }
        viewModel.bAllowUnique = model.AllowUnique;
        viewModel.bAllowNonUnique = model.AllowNonUnique;
        viewModel.bAllowRandom = model.AllowRandom;
        viewModel.ProbabilityWeighting = model.ProbabilityWeighting;
        viewModel.WeightRange = model.WeightRange;

        return viewModel;
    }

    public static BodyShapeDescriptorRules DumpViewModelToModel(VM_BodyShapeDescriptorRules viewModel)
    {
        BodyShapeDescriptorRules model = new BodyShapeDescriptorRules();
        model.DescriptorSignature = viewModel.DescriptorSignature;
        model.AllowedRaces = viewModel.AllowedRaces.ToHashSet();
        model.AllowedRaceGroupings = viewModel.AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet();
        model.DisallowedRaces = viewModel.DisallowedRaces.ToHashSet();
        model.DisallowedRaceGroupings = viewModel.DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet();
        model.AllowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.AllowedAttributes);
        model.DisallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.DisallowedAttributes);
        model.AllowUnique = viewModel.bAllowUnique;
        model.AllowNonUnique = viewModel.bAllowNonUnique;
        model.AllowRandom = viewModel.bAllowRandom;
        model.ProbabilityWeighting = viewModel.ProbabilityWeighting;
        model.WeightRange = viewModel.WeightRange;
        return model;
    }
}