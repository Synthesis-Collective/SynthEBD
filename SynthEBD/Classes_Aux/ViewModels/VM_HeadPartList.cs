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

namespace SynthEBD
{
    public class VM_HeadPartList : VM
    {
        public VM_HeadPartList(HeadPart.TypeEnum type, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_Settings_Headparts parentConfig, VM_SettingsOBody oBody)
        {
            HeadPartType = type;
            DisplayedRuleSet = new(raceGroupingVMs, parentConfig, oBody);

            Alphabetizer = new(HeadPartList, x => x.Label, new(System.Windows.Media.Colors.MediumPurple));

            this.WhenAnyValue(x => x.GenderToggle).Subscribe(x => UpdateList());
            HeadPartList.ToObservableChangeSet().Subscribe(_ => UpdateList());

        }

        public ObservableCollection<VM_HeadPart> HeadPartList { get; set; } = new();
        public ObservableCollection<VM_HeadPart> DisplayedList { get; set; } = new();
        public VM_HeadPart DisplayedHeadPart { get; set; } // for reference only - currently not used for anything but may be useful at some point to be able to tell which headpart category this instance of VM_HeadPartList is for.
        public VM_HeadPartCategoryRules DisplayedRuleSet { get; set; }
        public DisplayGender GenderToggle { get; set; } = DisplayGender.Both; 
        private HeadPart.TypeEnum HeadPartType { get; set; }
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

        public void CopyInFromModel(Settings_HeadPartType model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_AttributeGroupMenu attributeGroupMenu, VM_Settings_Headparts parentConfig, VM_SettingsOBody oBody)
        {
            foreach (var hp in model.HeadParts)
            {
                HeadPartList.Add(VM_HeadPart.GetViewModelFromModel(hp, raceGroupingVMs, attributeGroupMenu, oBody.DescriptorUI, parentConfig, HeadPartList));
            }

            DisplayedRuleSet = VM_HeadPartCategoryRules.GetViewModelFromModel(model, raceGroupingVMs, attributeGroupMenu, parentConfig, oBody);
        }

        public void DumpToModel(Settings_HeadPartType model)
        {
            DisplayedRuleSet.DumpToModel(model);
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
