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
        public ObservableCollection<VM_HeadPart> DisplayedList { get; set; } = new();
    }
}
