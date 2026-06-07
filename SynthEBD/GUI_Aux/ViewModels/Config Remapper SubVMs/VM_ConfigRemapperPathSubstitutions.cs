using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.VM_ConfigPathRemapper;

namespace SynthEBD;

/// <summary>
/// Config Path Remapper sub-panel listing proposed path substitutions per subgroup, with commands to accept or reject all renamings at once.
/// </summary>
public class VM_ConfigRemapperPathSubstitutions : VM, IConfigRemapperSubVM
{
    /// <summary>Stores the display text and remapped subgroups, and wires the AcceptAll/RejectAll commands that toggle every path's <c>AcceptRenaming</c> flag.</summary>
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

    /// <summary>Updates each subgroup's visibility based on whether it matches the subgroup and path search filters.</summary>
    public void Refresh(string subgroupSearchStr, bool subgroupCaseSensitive, string pathSearchStr, bool pathCaseSensitive)
    {
        foreach (var subgroup in RemappedSubgroups)
        {
            subgroup.IsVisible = subgroup.SearchMatches(subgroupSearchStr, subgroupCaseSensitive, pathSearchStr, pathCaseSensitive);
        }
    }
}
