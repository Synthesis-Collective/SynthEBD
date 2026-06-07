using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.VM_ConfigPathRemapper;

namespace SynthEBD;

/// <summary>
/// Config Path Remapper sub-panel listing new files that could not be matched to any existing path during remapping.
/// </summary>
class VM_ConfigRemapperFailedRemappings : VM, IConfigRemapperSubVM
{
    /// <summary>Stores the collection of unmatched new files to display.</summary>
    public VM_ConfigRemapperFailedRemappings(ObservableCollection<SelectableFilePath> newFilesUnmatched)
    {
        NewFilesUnmatched = newFilesUnmatched;
    }

    public ObservableCollection<SelectableFilePath> NewFilesUnmatched { get; set; } = new();

    /// <summary>Re-applies the path search filter to each unmatched file. Subgroup search parameters are unused here.</summary>
    public void Refresh(string subgroupSearchStr, bool subgroupCaseSensitive, string pathSearchStr, bool pathCaseSensitive)
    {
        foreach (var pathVM in NewFilesUnmatched)
        {
            pathVM.Refresh(pathSearchStr, pathCaseSensitive);
        }
    }
}
