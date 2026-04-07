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

        AcceptAll = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                foreach (var subgroup in RemappedSubgroups)
                {
                    foreach (var path in subgroup.Paths)
                    {
                        path.AcceptRenaming = true;
                    }
                }
            });

        RejectAll = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                foreach (var subgroup in RemappedSubgroups)
                {
                    foreach (var path in subgroup.Paths)
                    {
                        path.AcceptRenaming = false;
                    }
                }
            });
    }
    public string DisplayText { get; set; }
    public ObservableCollection<RemappedSubgroup> RemappedSubgroups { get; set; } = new();
    public RelayCommand AcceptAll { get; }
    public RelayCommand RejectAll { get; }

    public void Refresh(string subgroupSearchStr, bool subgroupCaseSensitive, string pathSearchStr, bool pathCaseSensitive)
    {
        foreach (var subgroup in RemappedSubgroups)
        {
            subgroup.IsVisible = subgroup.SearchMatches(subgroupSearchStr, subgroupCaseSensitive, pathSearchStr, pathCaseSensitive);
        }
    }
}
