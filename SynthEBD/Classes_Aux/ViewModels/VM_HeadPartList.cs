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

namespace SynthEBD
{
    public class VM_HeadPartList : VM
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly Logger _logger;
        private readonly VM_Settings_Headparts _headPartMenuVM;
        private readonly VM_NPCAttributeCreator _attributeCreator;
        private readonly VM_SettingsOBody _oBodySettings;
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
            VM_HeadPart.Factory headPartFactory, 
            VM_HeadPartCategoryRules.Factory headPartCategoryRulesFactory, 
            VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
        {
            _environmentProvider = environmentProvider;
            _logger = logger;
            _headPartMenuVM = headPartMenuVM;
            _attributeCreator = attributeCreator;
            _oBodySettings = oBodyMenuVM;
            _headPartFactory = headPartFactory;
            _headPartCategoryRulesFactory = headPartCategoryRulesFactory;
            _descriptorSelectionFactory = descriptorSelectionFactory;

            //TypeRuleSet = _headPartCategoryRulesFactory(raceGroupingVMs);

            Alphabetizer = new(HeadPartList, x => x.Label, new(System.Windows.Media.Colors.MediumPurple));

            this.WhenAnyValue(x => x.GenderToggle).Subscribe(x => UpdateList()).DisposeWith(this);
            HeadPartList.ToObservableChangeSet().Subscribe(_ => UpdateList()).DisposeWith(this);
        }

        public ObservableCollection<VM_HeadPart> HeadPartList { get; set; } = new();
        public ObservableCollection<VM_HeadPart> DisplayedList { get; set; } = new();
        public VM_HeadPart DisplayedHeadPart { get; set; } // for reference only - currently not used for anything but may be useful at some point to be able to tell which headpart category this instance of VM_HeadPartList is for.
        public VM_HeadPartCategoryRules TypeRuleSet { get; set; }
        public DisplayGender GenderToggle { get; set; } = DisplayGender.Both; 
        public VM_Alphabetizer<VM_HeadPart, string> Alphabetizer { get; set; }

        public void UpdateList()
        {
            DisplayedList.Clear();
            foreach (var headPart in HeadPartList)
            {
                if (GenderToggle == DisplayGender.Both || (GenderToggle == DisplayGender.Male && headPart.bAllowMale) || (GenderToggle == DisplayGender.Female && headPart.bAllowFemale))
                {
                    DisplayedList.Add(headPart);
                }
            }
        }

        public void CopyInFromModel(Settings_HeadPartType model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_AttributeGroupMenu attributeGroupMenu)
        {
            foreach (var hp in model.HeadParts)
            {
                HeadPartList.Add(VM_HeadPart.GetViewModelFromModel(hp, _headPartFactory, raceGroupingVMs, attributeGroupMenu, _oBodySettings.DescriptorUI, _headPartMenuVM, HeadPartList, _attributeCreator, _logger, _descriptorSelectionFactory, _environmentProvider.LinkCache));
            }

            TypeRuleSet = VM_HeadPartCategoryRules.GetViewModelFromModel(model, raceGroupingVMs, attributeGroupMenu, _headPartMenuVM, _oBodySettings, _attributeCreator, _logger, _headPartCategoryRulesFactory, _descriptorSelectionFactory);
        }

        public void DumpToModel(Settings_HeadPartType model)
        {
            TypeRuleSet.DumpToModel(model);
            model.HeadParts = HeadPartList.Select(x => x.DumpToModel()).ToList();
        }
    }

    public enum DisplayGender
    {
        Both,
        Male,
        Female
    }
}
