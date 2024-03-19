using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.VM_ConfigPathRemapper;

namespace SynthEBD;

class VM_ConfigRemapperMissingPaths : VM
{
    public VM_ConfigRemapperMissingPaths(ObservableCollection<RemappedSubgroup> missingPathSubgroups, string displayStr)
    {
        MissingPathSubgroups = missingPathSubgroups;
        DisplayStr = displayStr;
    }

    public ObservableCollection<RemappedSubgroup> MissingPathSubgroups { get; set; } = new();
    public string DisplayStr { get; set; }
}
