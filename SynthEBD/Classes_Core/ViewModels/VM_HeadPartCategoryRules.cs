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
        private IEnvironmentStateProvider _environmentProvider;
        private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
        public VM_Settings_Headparts ParentMenu { get; set; } // needed for xaml binding
        public delegate VM_HeadPartCategoryRules Factory(ObservableCollection<VM_RaceGrouping> raceGroupingVMs);
        public VM_HeadPartCategoryRules(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_Settings_Headparts parentMenu, VM_SettingsOBody oBody, VM_NPCAttributeCreator creator, IEnvironmentStateProvider environmentProvider, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
        {
            _environmentProvider = environmentProvider;
            _descriptorSelectionFactory = descriptorSelectionFactory;
            ParentMenu = parentMenu;

            AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
            DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

            AllowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, raceGroupingVMs, parentMenu, true, DescriptorMatchMode.All);
            DisallowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, raceGroupingVMs, parentMenu, true, DescriptorMatchMode.Any);
            this.WhenAnyValue(x => x.ParentMenu.SettingsMenu.TrackedBodyGenConfigMale).Subscribe(_ => RefreshBodyGenDescriptorsMale(raceGroupingVMs)).DisposeWith(this);
            this.WhenAnyValue(x => x.ParentMenu.SettingsMenu.TrackedBodyGenConfigFemale).Subscribe(_ => RefreshBodyGenDescriptorsFemale(raceGroupingVMs)).DisposeWith(this);

            _environmentProvider.WhenAnyValue(x => x.LinkCache)
                .Subscribe(x => lk = x)
                .DisposeWith(this);

            AddAllowedAttribute = new RelayCommand(
                canExecute: _ => true,
                execute: _ => AllowedAttributes.Add(creator.CreateNewFromUI(AllowedAttributes, true, null, parentMenu.AttributeGroupMenu.Groups))
            );

            AddDisallowedAttribute = new RelayCommand(
                canExecute: _ => true,
                execute: _ => DisallowedAttributes.Add(creator.CreateNewFromUI(DisallowedAttributes, false, null, parentMenu.AttributeGroupMenu.Groups))
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
        public VM_BodyShapeDescriptorSelectionMenu AllowedBodyGenDescriptorsMale { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu DisallowedBodyGenDescriptorsMale { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu AllowedBodyGenDescriptorsFemale { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu DisallowedBodyGenDescriptorsFemale { get; set; }

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
            viewModel.AllowedAttributes = creator.GetViewModelsFromModels(model.AllowedAttributes, attributeGroupMenu.Groups, true, null);
            viewModel.DisallowedAttributes = creator.GetViewModelsFromModels(model.DisallowedAttributes, attributeGroupMenu.Groups, false, null);
            foreach (var x in viewModel.DisallowedAttributes) { x.DisplayForceIfOption = false; }
            viewModel.bAllowUnique = model.bAllowUnique;
            viewModel.bAllowNonUnique = model.bAllowNonUnique;
            viewModel.bAllowRandom = model.bAllowRandom;
            viewModel.DistributionProbability = model.RandomizationPercentage;
            viewModel.WeightRange = model.WeightRange;
            viewModel.AllowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.AllowedBodySlideDescriptors, oBody.DescriptorUI, raceGroupingVMs, parentConfig, true, model.AllowedBodySlideMatchMode, descriptorSelectionFactory);
            viewModel.DisallowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.DisallowedBodySlideDescriptors, oBody.DescriptorUI, raceGroupingVMs, parentConfig, true, model.DisallowedBodySlideMatchMode, descriptorSelectionFactory);
            viewModel.AllowedBodyGenDescriptorsMale = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.AllowedBodyGenDescriptorsMale, parentConfig.SettingsMenu.TrackedBodyGenConfigMale?.DescriptorUI ?? null, raceGroupingVMs, parentConfig, true, model.AllowedBodyGenDescriptorMatchModeMale, descriptorSelectionFactory);
            viewModel.DisallowedBodyGenDescriptorsMale = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.DisallowedBodyGenDescriptorsMale, parentConfig.SettingsMenu.TrackedBodyGenConfigMale?.DescriptorUI ?? null, raceGroupingVMs, parentConfig, true, model.DisallowedBodyGenDescriptorMatchModeMale, descriptorSelectionFactory);
            viewModel.AllowedBodyGenDescriptorsFemale = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.AllowedBodyGenDescriptorsFemale, parentConfig.SettingsMenu.TrackedBodyGenConfigFemale?.DescriptorUI ?? null, raceGroupingVMs, parentConfig, true, model.AllowedBodyGenDescriptorMatchModeFemale, descriptorSelectionFactory);
            viewModel.DisallowedBodyGenDescriptorsFemale = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.DisallowedBodyGenDescriptorsFemale, parentConfig.SettingsMenu.TrackedBodyGenConfigFemale?.DescriptorUI ?? null, raceGroupingVMs, parentConfig, true, model.DisallowedBodyGenDescriptorMatchModeFemale, descriptorSelectionFactory);
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
            model.AllowedBodySlideDescriptors = AllowedBodySlideDescriptors.DumpToHashSet();
            model.AllowedBodySlideMatchMode = AllowedBodySlideDescriptors.MatchMode;
            model.DisallowedBodySlideDescriptors = DisallowedBodySlideDescriptors.DumpToHashSet();
            model.DisallowedBodySlideMatchMode = DisallowedBodySlideDescriptors.MatchMode;
            model.AllowedBodyGenDescriptorsMale = AllowedBodyGenDescriptorsMale.DumpToHashSet();
            model.AllowedBodyGenDescriptorMatchModeMale = AllowedBodyGenDescriptorsMale.MatchMode;
            model.DisallowedBodyGenDescriptorsMale = DisallowedBodyGenDescriptorsMale.DumpToHashSet();
            model.DisallowedBodyGenDescriptorMatchModeMale = DisallowedBodyGenDescriptorsMale.MatchMode;
            model.AllowedBodyGenDescriptorsFemale = AllowedBodyGenDescriptorsFemale.DumpToHashSet();
            model.AllowedBodyGenDescriptorMatchModeFemale = AllowedBodyGenDescriptorsFemale.MatchMode;
            model.DisallowedBodyGenDescriptorsFemale = DisallowedBodyGenDescriptorsFemale.DumpToHashSet();
            model.DisallowedBodyGenDescriptorMatchModeFemale = DisallowedBodyGenDescriptorsFemale.MatchMode;
        }

        public void RefreshBodyGenDescriptorsMale(ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
        {
            DescriptorMatchMode allowedMode = DescriptorMatchMode.All;
            DescriptorMatchMode disallowedMode = DescriptorMatchMode.Any;

            // keep existing settings if any
            if (AllowedBodyGenDescriptorsMale != null)
            {
                allowedMode = AllowedBodyGenDescriptorsMale.MatchMode;
            }
            if (DisallowedBodyGenDescriptorsMale != null)
            {
                disallowedMode = DisallowedBodyGenDescriptorsMale.MatchMode;
            }

            if (ParentMenu.SettingsMenu.TrackedBodyGenConfigMale != null)
            {
                AllowedBodyGenDescriptorsMale = _descriptorSelectionFactory(ParentMenu.SettingsMenu.TrackedBodyGenConfigMale.DescriptorUI, raceGroupingVMs, ParentMenu, true, allowedMode);
                DisallowedBodyGenDescriptorsMale = _descriptorSelectionFactory(ParentMenu.SettingsMenu.TrackedBodyGenConfigMale.DescriptorUI, raceGroupingVMs, ParentMenu, true, disallowedMode);
            }
        }

        public void RefreshBodyGenDescriptorsFemale(ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
        {
            DescriptorMatchMode allowedMode = DescriptorMatchMode.All;
            DescriptorMatchMode disallowedMode = DescriptorMatchMode.Any;

            // keep existing settings if any
            if (AllowedBodyGenDescriptorsFemale != null)
            {
                allowedMode = AllowedBodyGenDescriptorsFemale.MatchMode;
            }
            if (DisallowedBodyGenDescriptorsFemale != null)
            {
                disallowedMode = DisallowedBodyGenDescriptorsFemale.MatchMode;
            }

            if (ParentMenu.SettingsMenu.TrackedBodyGenConfigFemale != null)
            {
                AllowedBodyGenDescriptorsFemale = _descriptorSelectionFactory(ParentMenu.SettingsMenu.TrackedBodyGenConfigFemale.DescriptorUI, raceGroupingVMs, ParentMenu, true, allowedMode);
                DisallowedBodyGenDescriptorsFemale = _descriptorSelectionFactory(ParentMenu.SettingsMenu.TrackedBodyGenConfigFemale.DescriptorUI, raceGroupingVMs, ParentMenu, true, disallowedMode);
            }
        }
    }
}
