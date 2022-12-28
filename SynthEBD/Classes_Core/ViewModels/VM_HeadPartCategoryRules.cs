using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD
{
    public class VM_HeadPartCategoryRules : VM
    {
        private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
        public delegate VM_HeadPartCategoryRules Factory(ObservableCollection<VM_RaceGrouping> raceGroupingVMs);
        public VM_HeadPartCategoryRules(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_Settings_Headparts parentConfig, VM_SettingsOBody oBody, VM_NPCAttributeCreator creator, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
        {
            _descriptorSelectionFactory = descriptorSelectionFactory;

            AllowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, raceGroupingVMs, parentConfig);
            DisallowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, raceGroupingVMs, parentConfig);
            AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
            DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

            PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
                .Subscribe(x => lk = x)
                .DisposeWith(this);

            AddAllowedAttribute = new RelayCommand(
                canExecute: _ => true,
                execute: _ => AllowedAttributes.Add(creator.CreateNewFromUI(AllowedAttributes, true, null, parentConfig.AttributeGroupMenu.Groups))
            );

            AddDisallowedAttribute = new RelayCommand(
                canExecute: _ => true,
                execute: _ => DisallowedAttributes.Add(creator.CreateNewFromUI(DisallowedAttributes, false, null, parentConfig.AttributeGroupMenu.Groups))
            );
        }
        public bool bAllowFemale { get; set; } = true;
        public bool bAllowMale { get; set; } = true;
        public bool bRestrictToNPCsWithThisType { get; set; } = true;
        public ObservableCollection<FormKey> AllowedRaces { get; set; } = new();
        public ObservableCollection<FormKey> DisallowedRaces { get; set; } = new();
        public VM_RaceGroupingCheckboxList AllowedRaceGroupings { get; set; }
        public VM_RaceGroupingCheckboxList DisallowedRaceGroupings { get; set; }
        public ObservableCollection<VM_NPCAttribute> AllowedAttributes { get; set; } = new(); // keeping as array to allow deserialization of original zEBD settings files
        public ObservableCollection<VM_NPCAttribute> DisallowedAttributes { get; set; } = new();
        public bool bAllowUnique { get; set; } = true;
        public bool bAllowNonUnique { get; set; } = true;
        public bool bAllowRandom { get; set; } = true;
        public NPCWeightRange WeightRange { get; set; } = new();
        public double DistributionProbability { get; set; } = 0.5;
        public RelayCommand AddDistributionWeighting { get; }
        public string Caption_BodyShapeDescriptors { get; set; } = "";
        public ILinkCache lk { get; private set; }
        public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();
        public RelayCommand AddAllowedAttribute { get; }
        public RelayCommand AddDisallowedAttribute { get; }
        public VM_BodyShapeDescriptorSelectionMenu AllowedBodySlideDescriptors { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu DisallowedBodySlideDescriptors { get; set; }

        public static VM_HeadPartCategoryRules GetViewModelFromModel(Settings_HeadPartType model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_AttributeGroupMenu attributeGroupMenu, VM_Settings_Headparts parentConfig, VM_SettingsOBody oBody, VM_NPCAttributeCreator creator, Logger logger, VM_HeadPartCategoryRules.Factory rulesFactory, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
        {
            VM_HeadPartCategoryRules viewModel = rulesFactory(raceGroupingVMs);
            viewModel.bAllowFemale = model.bAllowFemale;
            viewModel.bAllowMale = model.bAllowMale;
            viewModel.bRestrictToNPCsWithThisType = model.bRestrictToNPCsWithThisType;
            viewModel.AllowedRaces = new ObservableCollection<FormKey>(model.AllowedRaces);
            viewModel.AllowedRaceGroupings = VM_RaceGroupingCheckboxList.GetRaceGroupingsByLabel(model.AllowedRaceGroupings, raceGroupingVMs);
            viewModel.DisallowedRaces = new ObservableCollection<FormKey>(model.DisallowedRaces);
            viewModel.DisallowedRaceGroupings = VM_RaceGroupingCheckboxList.GetRaceGroupingsByLabel(model.DisallowedRaceGroupings, raceGroupingVMs);
            viewModel.AllowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.AllowedAttributes, attributeGroupMenu.Groups, true, null, creator, logger);
            viewModel.DisallowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.DisallowedAttributes, attributeGroupMenu.Groups, false, null, creator, logger);
            foreach (var x in viewModel.DisallowedAttributes) { x.DisplayForceIfOption = false; }
            viewModel.bAllowUnique = model.bAllowUnique;
            viewModel.bAllowNonUnique = model.bAllowNonUnique;
            viewModel.bAllowRandom = model.bAllowRandom;
            viewModel.DistributionProbability = model.RandomizationPercentage;
            viewModel.WeightRange = model.WeightRange;
            viewModel.AllowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.AllowedBodySlideDescriptors, oBody.DescriptorUI, raceGroupingVMs, parentConfig, descriptorSelectionFactory);
            viewModel.DisallowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.DisallowedBodySlideDescriptors, oBody.DescriptorUI, raceGroupingVMs, parentConfig, descriptorSelectionFactory);
            return viewModel;
        }

        public void DumpToModel(Settings_HeadPartType model)
        {
            model.bAllowFemale = bAllowFemale;
            model.bAllowMale = bAllowMale;
            model.bRestrictToNPCsWithThisType = bRestrictToNPCsWithThisType;
            model.AllowedRaces = AllowedRaces.ToHashSet();
            model.AllowedRaceGroupings = AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
            model.DisallowedRaces = DisallowedRaces.ToHashSet();
            model.DisallowedRaceGroupings = DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
            model.AllowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(AllowedAttributes);
            model.DisallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(DisallowedAttributes);
            model.bAllowUnique = bAllowUnique;
            model.bAllowNonUnique = bAllowNonUnique;
            model.bAllowRandom = bAllowRandom;
            model.RandomizationPercentage = DistributionProbability;
            model.WeightRange = WeightRange;
            model.AllowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(AllowedBodySlideDescriptors);
            model.DisallowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(DisallowedBodySlideDescriptors);
        }
    }
}
