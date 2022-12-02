using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;

namespace SynthEBD;
public class VM_HeadPartConsistency : VM
{
    public VM_HeadPartConsistency()
    {
        ClearSelection = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                ClearAssignment();
            }
        );
    }
    public string Label { get; set; }
    private FormKey FormKey { get; set; }
    private bool Initialized { get; set; } // for storage only to make sure it doesn't get lost after saving dumping to the DTO
    public bool RandomizedToNone { get; set;} = false;
    public RelayCommand ClearSelection { get; set; }

    public static VM_HeadPartConsistency GetViewModelFromModel(HeadPartConsistency model)
    {
        var viewModel = new VM_HeadPartConsistency();
        viewModel.Label = model.EditorID;
        viewModel.FormKey = model.FormKey;
        viewModel.Initialized = model.Initialized;
        viewModel.RandomizedToNone = model.RandomizedToNone;
        return viewModel;
    }

    public HeadPartConsistency DumpToModel()
    {
        var model = new HeadPartConsistency();
        model.EditorID = Label;
        model.FormKey = FormKey;
        model.Initialized = Initialized;
        model.RandomizedToNone = RandomizedToNone;
        return model;
    }

    public void ClearAssignment()
    {
        FormKey = new();
        Label = String.Empty;
        RandomizedToNone = false;
        Initialized = false;
    }
}
