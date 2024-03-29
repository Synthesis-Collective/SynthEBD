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
using System.Windows.Media;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD
{
    public class VM_HeadPart : VM
    {
        private IEnvironmentStateProvider _environmentProvider;
        private readonly VM_NPCAttributeCreator _attributeCreator;
        private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
        public delegate VM_HeadPart Factory(FormKey headPartFormKey, VM_HeadPartPlaceHolder associatedPlaceHolder, VM_BodyShapeDescriptorCreationMenu bodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_Settings_Headparts parentConfig);
        public VM_HeadPart(FormKey headPartFormKey, VM_HeadPartPlaceHolder associatedPlaceHolder, VM_BodyShapeDescriptorCreationMenu bodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_Settings_Headparts parentConfig, IEnvironmentStateProvider environmentProvider, VM_NPCAttributeCreator attributeCreator, VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
        {
            _environmentProvider = environmentProvider;
            _attributeCreator = attributeCreator;
            _descriptorSelectionFactory = descriptorSelectionFactory;

            AssociatedPlaceHolder = associatedPlaceHolder;
            AssociatedPlaceHolder.AssociatedViewModel = this;

            ParentMenu = parentConfig;

            FormKey = headPartFormKey;
            AllowedBodySlideDescriptors = descriptorSelectionFactory(bodyShapeDescriptors, raceGroupingVMs, parentConfig, true, DescriptorMatchMode.All, false);
            DisallowedBodySlideDescriptors = descriptorSelectionFactory(bodyShapeDescriptors, raceGroupingVMs, parentConfig, true, DescriptorMatchMode.Any, false);
            this.WhenAnyValue(x => x.ParentMenu.SettingsMenu.TrackedBodyGenConfigMale).Subscribe(_ => RefreshBodyGenDescriptorsMale(raceGroupingVMs)).DisposeWith(this);
            this.WhenAnyValue(x => x.ParentMenu.SettingsMenu.TrackedBodyGenConfigFemale).Subscribe(_ => RefreshBodyGenDescriptorsFemale(raceGroupingVMs)).DisposeWith(this);

            AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
            DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

            _environmentProvider.WhenAnyValue(x => x.LinkCache)
                .Subscribe(x => lk = x)
                .DisposeWith(this);

            DeleteMe = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => AssociatedPlaceHolder.ParentCollection.Remove(AssociatedPlaceHolder)
            );

            AddAllowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => AllowedAttributes.Add(_attributeCreator.CreateNewFromUI(AllowedAttributes, true, null, ParentMenu.AttributeGroupMenu.Groups))
            );

            AddDisallowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => DisallowedAttributes.Add(_attributeCreator.CreateNewFromUI(DisallowedAttributes, false, null, ParentMenu.AttributeGroupMenu.Groups))
            );

            this.WhenAnyValue(x => x.FormKey).Subscribe(x =>
            {
                if (lk.TryResolve<IHeadPartGetter>(FormKey, out var getter))
                {
                    BorderColor = CommonColors.Green;
                    StatusString = String.Empty;
                }
                else
                {
                    BorderColor = CommonColors.Red;
                    StatusString = "This head part is no longer present in your load order";
                }
            }).DisposeWith(this);

            /*
            Clone = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => {
                    var cloneModel = VM_HeadPart.DumpViewModelToModel(this);
                    var cloneViewModel = new VM_BodySlideSetting(BodyShapeDescriptors, raceGroupingVMs, ParentCollection, ParentConfig);
                    VM_BodySlideSetting.GetViewModelFromModel(cloneModel, cloneViewModel, BodyShapeDescriptors, raceGroupingVMs, ParentConfig);
                    var index = parentCollection.IndexOf(this);
                    parentCollection.Insert(index, cloneViewModel);
                }
            );
            */
        }

        public FormKey FormKey { get; set; }
        public string Label { get; set; } = "";
        public bool bAllowMale { get; set; }
        public bool bAllowFemale { get; set; }
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
        public VM_BodyShapeDescriptorSelectionMenu AllowedBodySlideDescriptors { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu DisallowedBodySlideDescriptors { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu AllowedBodyGenDescriptorsMale { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu DisallowedBodyGenDescriptorsMale { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu AllowedBodyGenDescriptorsFemale { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu DisallowedBodyGenDescriptorsFemale { get; set; }

        public HeadPartSetting AssociatedModel { get; set; }
        public VM_HeadPartPlaceHolder AssociatedPlaceHolder { get; }

        public ILinkCache lk { get; private set; }
        public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();
        public RelayCommand DeleteMe { get; }
        public RelayCommand AddAllowedAttribute { get; }
        public RelayCommand AddDisallowedAttribute { get; }
        public RelayCommand Clone { get; }
        public RelayCommand ToggleHide { get; }
        public VM_Settings_Headparts ParentMenu { get; set; }
        public SolidColorBrush BorderColor { get; set; } = CommonColors.Green;
        public string StatusString { get; set; } = string.Empty;

        public void CopyInFromModel(HeadPartSetting model)
        {
            FormKey = model.HeadPartFormKey;
            Label = EditorIDHandler.GetEditorIDSafely<IHeadPartGetter>(FormKey, lk);
            bAllowFemale = model.bAllowFemale;
            bAllowMale = model.bAllowMale;
            AllowedRaces.AddRange(model.AllowedRaces);
            AllowedRaceGroupings.CopyInRaceGroupingsByLabel(model.AllowedRaceGroupings, ParentMenu.RaceGroupings);
            DisallowedRaces.AddRange(model.DisallowedRaces);
            DisallowedRaceGroupings.CopyInRaceGroupingsByLabel(model.DisallowedRaceGroupings, ParentMenu.RaceGroupings);
            _attributeCreator.CopyInFromModels(model.AllowedAttributes, AllowedAttributes, ParentMenu.AttributeGroupMenu.Groups, true, null);
            _attributeCreator.CopyInFromModels(model.DisallowedAttributes, DisallowedAttributes, ParentMenu.AttributeGroupMenu.Groups, false, null);
            foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }
            bAllowUnique = model.bAllowUnique;
            bAllowNonUnique = model.bAllowNonUnique;
            bAllowRandom = model.bAllowRandom;
            ProbabilityWeighting = model.ProbabilityWeighting;
            WeightRange = model.WeightRange;
            AllowedBodySlideDescriptors.CopyInFromHashSet(model.AllowedBodySlideDescriptors);
            DisallowedBodySlideDescriptors.CopyInFromHashSet(model.DisallowedBodySlideDescriptors);
            if (AllowedBodyGenDescriptorsMale != null)
            {
                AllowedBodyGenDescriptorsMale.CopyInFromHashSet(model.AllowedBodyGenDescriptorsMale);
                DisallowedBodyGenDescriptorsMale.CopyInFromHashSet(model.DisallowedBodyGenDescriptorsMale);
            }
            if (AllowedBodyGenDescriptorsFemale != null)
            {
                AllowedBodyGenDescriptorsFemale.CopyInFromHashSet(model.AllowedBodyGenDescriptorsFemale);
                DisallowedBodyGenDescriptorsFemale.CopyInFromHashSet(model.DisallowedBodyGenDescriptorsFemale);
            }
        }

        public HeadPartSetting DumpToModel()
        {
            return new HeadPartSetting()
            {
                HeadPartFormKey = FormKey,
                EditorID = Label,
                bAllowMale = bAllowMale,
                bAllowFemale = bAllowFemale,
                AllowedRaces = AllowedRaces.ToHashSet(),
                AllowedRaceGroupings = AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet(),
                DisallowedRaces = DisallowedRaces.ToHashSet(),
                DisallowedRaceGroupings = DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet(),
                AllowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(AllowedAttributes),
                DisallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(DisallowedAttributes),
                bAllowUnique = bAllowUnique,
                bAllowNonUnique = bAllowNonUnique,
                bAllowRandom = bAllowRandom,
                ProbabilityWeighting = ProbabilityWeighting,
                WeightRange = WeightRange,
                AllowedBodySlideDescriptors = AllowedBodySlideDescriptors?.DumpToHashSet() ?? new(),
                AllowedBodySlideMatchMode = AllowedBodySlideDescriptors?.MatchMode ?? DescriptorMatchMode.All,
                DisallowedBodySlideDescriptors = DisallowedBodySlideDescriptors?.DumpToHashSet() ?? new(),
                DisallowedBodySlideMatchMode = DisallowedBodySlideDescriptors?.MatchMode ?? DescriptorMatchMode.Any,
                AllowedBodyGenDescriptorsMale = AllowedBodyGenDescriptorsMale?.DumpToHashSet() ?? new(),
                AllowedBodyGenDescriptorMatchModeMale = AllowedBodyGenDescriptorsMale?.MatchMode ?? DescriptorMatchMode.All,
                DisallowedBodyGenDescriptorsMale = DisallowedBodyGenDescriptorsMale?.DumpToHashSet() ?? new(),
                DisallowedBodyGenDescriptorMatchModeMale = DisallowedBodyGenDescriptorsMale?.MatchMode ?? DescriptorMatchMode.Any,
                AllowedBodyGenDescriptorsFemale = AllowedBodyGenDescriptorsFemale?.DumpToHashSet() ?? new(),
                AllowedBodyGenDescriptorMatchModeFemale = AllowedBodyGenDescriptorsFemale?.MatchMode ?? DescriptorMatchMode.All,
                DisallowedBodyGenDescriptorsFemale = DisallowedBodyGenDescriptorsFemale?.DumpToHashSet() ?? new(),
                DisallowedBodyGenDescriptorMatchModeFemale = DisallowedBodyGenDescriptorsFemale?.MatchMode ?? DescriptorMatchMode.Any
            };
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
                AllowedBodyGenDescriptorsMale = _descriptorSelectionFactory(ParentMenu.SettingsMenu.TrackedBodyGenConfigMale.DescriptorUI, raceGroupingVMs, ParentMenu, true, allowedMode, false);
                DisallowedBodyGenDescriptorsMale = _descriptorSelectionFactory(ParentMenu.SettingsMenu.TrackedBodyGenConfigMale.DescriptorUI, raceGroupingVMs, ParentMenu, true, disallowedMode, false);
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
                AllowedBodyGenDescriptorsFemale = _descriptorSelectionFactory(ParentMenu.SettingsMenu.TrackedBodyGenConfigFemale.DescriptorUI, raceGroupingVMs, ParentMenu, true, allowedMode, false);
                DisallowedBodyGenDescriptorsFemale = _descriptorSelectionFactory(ParentMenu.SettingsMenu.TrackedBodyGenConfigFemale.DescriptorUI, raceGroupingVMs, ParentMenu, true, disallowedMode, false);
            }
        }
    }
}
