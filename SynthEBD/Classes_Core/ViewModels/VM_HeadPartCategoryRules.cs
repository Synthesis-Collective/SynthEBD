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
    /// <summary>
    /// View model for the per-category distribution rules of a head part type (a
    /// <see cref="Settings_HeadPartType"/>): gender/uniqueness gating, allowed/disallowed races,
    /// race groupings, NPC attributes, weight range, distribution probability, and the
    /// BodySlide/BodyGen descriptor filters that constrain which NPCs receive this head part type.
    /// </summary>
    public class VM_HeadPartCategoryRules : VM
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
        private readonly ObservableCollection<VM_RaceGrouping> _raceGroupingVMs;
        private readonly VM_NPCAttributeCreator _npcAttributeCreator;
        public VM_Settings_Headparts ParentMenu { get; set; } // needed for xaml binding
        /// <summary>Autofac factory delegate for constructing a <see cref="VM_HeadPartCategoryRules"/>.</summary>
        public delegate VM_HeadPartCategoryRules Factory(ObservableCollection<VM_RaceGrouping> raceGroupingVMs);
        /// <summary>
        /// Builds the race-grouping checkbox lists and BodySlide descriptor selection menus, tracks
        /// the male/female BodyGen configs to rebuild their descriptor menus, follows the environment
        /// link cache, and wires the add-allowed/disallowed-attribute commands.
        /// </summary>
        public VM_HeadPartCategoryRules(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_Settings_Headparts parentMenu, VM_SettingsOBody oBody, VM_NPCAttributeCreator creator, IEnvironmentStateProvider environmentProvider, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
        {
            _environmentProvider = environmentProvider;
            _descriptorSelectionFactory = descriptorSelectionFactory;
            _raceGroupingVMs = raceGroupingVMs;
            _npcAttributeCreator = creator;
            ParentMenu = parentMenu;

            AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
            DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

            AllowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, raceGroupingVMs, parentMenu, true, DescriptorMatchMode.All, false);
            DisallowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, raceGroupingVMs, parentMenu, true, DescriptorMatchMode.Any, false);
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

        /// <summary>Model → view model: loads this category's rules from a <see cref="Settings_HeadPartType"/>.</summary>
        public void CopyInFromModel(Settings_HeadPartType model)
        {
            bAllowFemale = model.bAllowFemale;
            bAllowMale = model.bAllowMale;
            bRestrictToNPCsWithThisType = model.bRestrictToNPCsWithThisType;
            AllowedRaces.AddRange(model.AllowedRaces);
            AllowedRaceGroupings.CopyInRaceGroupingsByLabel(model.AllowedRaceGroupings, _raceGroupingVMs);
            DisallowedRaces.AddRange(model.DisallowedRaces);
            DisallowedRaceGroupings.CopyInRaceGroupingsByLabel(model.DisallowedRaceGroupings, _raceGroupingVMs);
            _npcAttributeCreator.CopyInFromModels(model.AllowedAttributes, AllowedAttributes, ParentMenu.AttributeGroupMenu.Groups, true, null);
            _npcAttributeCreator.CopyInFromModels(model.DisallowedAttributes, DisallowedAttributes, ParentMenu.AttributeGroupMenu.Groups, false, null);
            bAllowUnique = model.bAllowUnique;
            bAllowNonUnique = model.bAllowNonUnique;
            bAllowRandom = model.bAllowRandom;
            DistributionProbability = model.RandomizationPercentage;
            WeightRange = model.WeightRange;
            AllowedBodySlideDescriptors.CopyInFromHashSet(model.AllowedBodySlideDescriptors);
            DisallowedBodySlideDescriptors.CopyInFromHashSet(model.DisallowedBodySlideDescriptors);
            if (ParentMenu.SettingsMenu.TrackedBodyGenConfigMale != null)
            {
                AllowedBodyGenDescriptorsMale.CopyInFromHashSet(model.AllowedBodyGenDescriptorsMale);
                DisallowedBodyGenDescriptorsMale.CopyInFromHashSet(model.DisallowedBodyGenDescriptorsMale);
            }
            if (ParentMenu.SettingsMenu.TrackedBodyGenConfigFemale != null)
            {
                AllowedBodyGenDescriptorsFemale.CopyInFromHashSet(model.AllowedBodyGenDescriptorsFemale);
                DisallowedBodyGenDescriptorsFemale.CopyInFromHashSet(model.DisallowedBodyGenDescriptorsFemale);
            }
        }

        /// <summary>View model → model: writes this category's rules into the supplied <see cref="Settings_HeadPartType"/>.</summary>
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
            model.AllowedBodyGenDescriptorsMale = AllowedBodyGenDescriptorsMale?.DumpToHashSet() ?? new();
            model.AllowedBodyGenDescriptorMatchModeMale = AllowedBodyGenDescriptorsMale?.MatchMode ?? DescriptorMatchMode.All;
            model.DisallowedBodyGenDescriptorsMale = DisallowedBodyGenDescriptorsMale?.DumpToHashSet() ?? new();
            model.DisallowedBodyGenDescriptorMatchModeMale = DisallowedBodyGenDescriptorsMale?.MatchMode ?? DescriptorMatchMode.Any;
            model.AllowedBodyGenDescriptorsFemale = AllowedBodyGenDescriptorsFemale?.DumpToHashSet() ?? new();
            model.AllowedBodyGenDescriptorMatchModeFemale = AllowedBodyGenDescriptorsFemale?.MatchMode ?? DescriptorMatchMode.All;
            model.DisallowedBodyGenDescriptorsFemale = DisallowedBodyGenDescriptorsFemale?.DumpToHashSet() ?? new();
            model.DisallowedBodyGenDescriptorMatchModeFemale = DisallowedBodyGenDescriptorsFemale?.MatchMode ?? DescriptorMatchMode.Any;
        }

        /// <summary>Rebuilds the male allowed/disallowed BodyGen descriptor menus from the tracked male BodyGen config, preserving the existing match modes.</summary>
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
                AllowedBodyGenDescriptorsMale = _descriptorSelectionFactory(ParentMenu.SettingsMenu.TrackedBodyGenConfigMale.DescriptorUI, raceGroupingVMs, ParentMenu, true, allowedMode, false);
                DisallowedBodyGenDescriptorsMale = _descriptorSelectionFactory(ParentMenu.SettingsMenu.TrackedBodyGenConfigMale.DescriptorUI, raceGroupingVMs, ParentMenu, true, disallowedMode, false);
            }
        }

        /// <summary>Rebuilds the female allowed/disallowed BodyGen descriptor menus from the tracked female BodyGen config, preserving the existing match modes.</summary>
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
                AllowedBodyGenDescriptorsFemale = _descriptorSelectionFactory(ParentMenu.SettingsMenu.TrackedBodyGenConfigFemale.DescriptorUI, raceGroupingVMs, ParentMenu, true, allowedMode, false);
                DisallowedBodyGenDescriptorsFemale = _descriptorSelectionFactory(ParentMenu.SettingsMenu.TrackedBodyGenConfigFemale.DescriptorUI, raceGroupingVMs, ParentMenu, true, disallowedMode, false);
            }
        }
    }
}
