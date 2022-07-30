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
    }
}
