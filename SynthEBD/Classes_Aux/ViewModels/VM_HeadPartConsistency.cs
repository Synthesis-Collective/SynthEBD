using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;
public class VM_HeadPartConsistency : VM
{
    public VM_HeadPartConsistency()
    {
        ClearSelection = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                FormKey = new();
                Label = String.Empty;
            }
        );
    }
    public string Label { get; set; }
    private FormKey FormKey { get; set; }
    public RelayCommand ClearSelection { get; set; }

    public static VM_HeadPartConsistency GetViewModelFromModel(FormKeyEditorIDPair model)
    {
        var viewModel = new VM_HeadPartConsistency();
        viewModel.Label = model.EditorID;
        viewModel.FormKey = model.FormKey;
        return viewModel;
    }

    public FormKeyEditorIDPair DumpToModel()
    {
        var model = new FormKeyEditorIDPair();
        model.FormKey = FormKey;
        model.EditorID = Label;
        return model;
    }
}
