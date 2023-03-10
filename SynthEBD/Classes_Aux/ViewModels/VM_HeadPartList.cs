using DynamicData.Binding;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.VM_NPCAttribute;
using Noggog;
using DynamicData;

namespace SynthEBD
{
    public class VM_HeadPartList : VM
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly Logger _logger;
        private readonly VM_Settings_Headparts _headPartMenuVM;
        private readonly VM_NPCAttributeCreator _attributeCreator;
        private readonly VM_SettingsOBody _oBodySettings;
        private readonly VM_HeadPartPlaceHolder.Factory _placeHolderFactory;
        private readonly VM_HeadPart.Factory _headPartFactory;
        private readonly VM_HeadPartCategoryRules.Factory _headPartCategoryRulesFactory;
        private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
        public delegate VM_HeadPartList Factory(ObservableCollection<VM_RaceGrouping> raceGroupingVMs);
        public VM_HeadPartList(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, 
            VM_Settings_Headparts headPartMenuVM, 
            VM_SettingsOBody oBodyMenuVM, 
            VM_NPCAttributeCreator attributeCreator,
            IEnvironmentStateProvider environmentProvider,
            Logger logger, 
            VM_HeadPartPlaceHolder.Factory placeHolderFactory,
            VM_HeadPart.Factory headPartFactory, 
            VM_HeadPartCategoryRules.Factory headPartCategoryRulesFactory, 
            VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
        {
            _environmentProvider = environmentProvider;
            _logger = logger;
            _headPartMenuVM = headPartMenuVM;
            _attributeCreator = attributeCreator;
            _oBodySettings = oBodyMenuVM;
            _placeHolderFactory = placeHolderFactory;
            _headPartFactory = headPartFactory;
            _headPartCategoryRulesFactory = headPartCategoryRulesFactory;
            _descriptorSelectionFactory = descriptorSelectionFactory;

            TypeRuleSet = _headPartCategoryRulesFactory(raceGroupingVMs);

            Alphabetizer = new(HeadPartList, x => x.Label, new(System.Windows.Media.Colors.MediumPurple));

            this.WhenAnyValue(vm => vm.SelectedPlaceHolder)
             .Buffer(2, 1)
             .Select(b => (Previous: b[0], Current: b[1]))
             .Subscribe(t => {
                 if (t.Previous != null && t.Previous.AssociatedViewModel != null)
                 {
                     t.Previous.AssociatedModel = t.Previous.AssociatedViewModel.DumpToModel();
                 }

                 if (t.Current != null)
                 {
                     DisplayedHeadPart = _headPartFactory(t.Current.AssociatedModel.HeadPartFormKey, t.Current, _oBodySettings.DescriptorUI, raceGroupingVMs, _headPartMenuVM);
                     DisplayedHeadPart.CopyInFromModel(t.Current.AssociatedModel);
                 }
             }).DisposeWith(this);

            this.WhenAnyValue(x => x.GenderToggle).Subscribe(x => UpdateList()).DisposeWith(this);
            HeadPartList.ToObservableChangeSet().Subscribe(_ => UpdateList()).DisposeWith(this);
        }

        public ObservableCollection<VM_HeadPartPlaceHolder> HeadPartList { get; set; } = new();
        public ObservableCollection<VM_HeadPartPlaceHolder> DisplayedList { get; set; } = new();
        public VM_HeadPartPlaceHolder SelectedPlaceHolder { get; set; }
        public VM_HeadPart DisplayedHeadPart { get; set; }
        public VM_HeadPartCategoryRules TypeRuleSet { get; set; }
        public DisplayGender GenderToggle { get; set; } = DisplayGender.Both; 
        public VM_Alphabetizer<VM_HeadPartPlaceHolder, string> Alphabetizer { get; set; }

        public void UpdateList()
        {
            DisplayedList.Clear();
            foreach (var headPart in HeadPartList)
            {
                if (GenderToggle == DisplayGender.Both || (GenderToggle == DisplayGender.Male && headPart.AssociatedModel.bAllowMale) || (GenderToggle == DisplayGender.Female && headPart.AssociatedModel.bAllowFemale))
                {
                    DisplayedList.Add(headPart);
                }
            }
        }

        public void CopyInFromModel(Settings_HeadPartType model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_AttributeGroupMenu attributeGroupMenu)
        {
            foreach (var hp in model.HeadParts)
            {
                HeadPartList.Add(_placeHolderFactory(hp, HeadPartList));
                //var viewModel = _headPartFactory(hp.HeadPartFormKey, _oBodySettings.DescriptorUI, raceGroupingVMs, HeadPartList, _headPartMenuVM);
                //HeadPartList.Add(viewModel);
                //Task.Run(() => viewModel.CopyInFromModel(hp, raceGroupingVMs, attributeGroupMenu, _oBodySettings.DescriptorUI, _headPartMenuVM, HeadPartList, _attributeCreator, _logger, _descriptorSelectionFactory, _environmentProvider.LinkCache));
            }

            TypeRuleSet.CopyInFromModel(model);
        }

        public void DumpToModel(Settings_HeadPartType model)
        {
            TypeRuleSet.DumpToModel(model);

            if (DisplayedHeadPart != null)
            {
                DisplayedHeadPart.AssociatedModel = DisplayedHeadPart.DumpToModel();
            }

            model.HeadParts = HeadPartList.Select(x => x.AssociatedModel).ToList();
        }
    }

    public enum DisplayGender
    {
        Both,
        Male,
        Female
    }
}
