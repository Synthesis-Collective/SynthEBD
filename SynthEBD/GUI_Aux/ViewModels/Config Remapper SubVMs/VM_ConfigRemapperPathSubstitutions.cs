using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.VM_ConfigPathRemapper;

namespace SynthEBD;

public class VM_ConfigRemapperPathSubstitutions : VM, IConfigRemapperSubVM
{
    public VM_ConfigRemapperPathSubstitutions(string displayText, ObservableCollection<RemappedSubgroup> remappedSubgroups)
    {
        DisplayText = displayText;
        RemappedSubgroups = remappedSubgroups;
    }
    public string DisplayText { get; set; }
    public ObservableCollection<RemappedSubgroup> RemappedSubgroups { get; set; } = new();

    public void Refresh(string searchStr, bool caseSensitive)
    {
        foreach (var subgroup in RemappedSubgroups)
        {
            subgroup.IsVisible = subgroup.NameMatches(searchStr, caseSensitive);
        }
    }
}
