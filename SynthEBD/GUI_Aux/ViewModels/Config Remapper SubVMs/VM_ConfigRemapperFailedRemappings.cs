using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

class VM_ConfigRemapperFailedRemappings : VM, IConfigRemapperSubVM
{
    public VM_ConfigRemapperFailedRemappings(ObservableCollection<string> newFilesUnmatched)
    {
        NewFilesUnmatched = newFilesUnmatched;
    }

    public ObservableCollection<string> NewFilesUnmatched { get; set; } = new();

    public void Refresh(string searchStr, bool caseSensitive)
    {
        return;
    }
}
