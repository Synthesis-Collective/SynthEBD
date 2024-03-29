using Mutagen.Bethesda.Fallout4;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.VM_ConfigPathRemapper;

namespace SynthEBD;

class VM_ConfigRemapperMissingPaths : VM, IConfigRemapperSubVM
{
    public VM_ConfigRemapperMissingPaths(ObservableCollection<RemappedSubgroup> missingPathSubgroups, string displayStr)
    {
        MissingPathSubgroups = missingPathSubgroups;
        DisplayStr = displayStr;
    }

    public ObservableCollection<RemappedSubgroup> MissingPathSubgroups { get; set; } = new();
    public ObservableCollection<RemappedSubgroup> DisplayedSubgroups { get; set; } = new();
    public string DisplayStr { get; set; }

    public void Refresh(string subgroupSearchStr, bool subgroupCaseSensitive, string pathSearchStr, bool pathCaseSensitive)
    {
        foreach (var subgroup in MissingPathSubgroups)
        {
            subgroup.IsVisible = subgroup.SearchMatches(subgroupSearchStr, subgroupCaseSensitive, pathSearchStr, pathCaseSensitive);
        }
    }
}
