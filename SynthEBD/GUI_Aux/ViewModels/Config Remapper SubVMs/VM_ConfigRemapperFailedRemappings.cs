using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.VM_ConfigPathRemapper;

namespace SynthEBD;

class VM_ConfigRemapperFailedRemappings : VM, IConfigRemapperSubVM
{
    public VM_ConfigRemapperFailedRemappings(ObservableCollection<SelectableFilePath> newFilesUnmatched)
    {
        NewFilesUnmatched = newFilesUnmatched;
    }

    public ObservableCollection<SelectableFilePath> NewFilesUnmatched { get; set; } = new();

    public void Refresh(string searchStr, bool caseSensitive)
    {
        return;
    }
}
