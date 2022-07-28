using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_HeadPartCategoryRules
    {
        public VM_HeadPartCategoryRules(VM_BodyShapeDescriptorCreationMenu BodyShapeDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, ObservableCollection<VM_HeadPart> parentCollection, VM_Settings_Headparts parentConfig)
        {
            StandardDistributionRules = new VM_HeadPart(BodyShapeDescriptors, raceGroupingVMs, new ObservableCollection<VM_HeadPart>(), parentConfig);
        }

        public double DistributionProbability { get; set; } = 1;
        public bool bAllowFemale { get; set; } = true;
        public bool bAllowMale { get; set; } = true;

        public VM_HeadPart StandardDistributionRules { get; set;}
    }
}
