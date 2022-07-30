using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_HeadPartList
    {
        public VM_HeadPartList(VM_BodyShapeDescriptorCreationMenu bodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_Settings_Headparts parentConfig, VM_SettingsOBody oBody)
        {
            DisplayedRuleSet = new(raceGroupingVMs, parentConfig, oBody);
        }

        public ObservableCollection<VM_HeadPart> DisplayedList { get; set; } = new();
        public VM_HeadPart DisplayedHeadPart { get; set; }
        public VM_HeadPartCategoryRules DisplayedRuleSet { get; set; }

        public void CopyInFromModel(Settings_HeadPartType model, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_AttributeGroupMenu attributeGroupMenu, VM_Settings_Headparts parentConfig, VM_SettingsOBody oBody)
        {
            foreach (var hp in model.HeadParts)
            {
                DisplayedList.Add(VM_HeadPart.GetViewModelFromModel(hp, raceGroupingVMs, attributeGroupMenu, oBody.DescriptorUI, parentConfig, DisplayedList));
            }

            DisplayedRuleSet = VM_HeadPartCategoryRules.GetViewModelFromModel(model, raceGroupingVMs, attributeGroupMenu, parentConfig, oBody);
        }

        public void DumpToModel(Settings_HeadPartType model)
        {
            DisplayedRuleSet.DumpToModel(model);
            model.HeadParts = DisplayedList.Select(x => x.DumpToModel()).ToList();
        }
    }
}
