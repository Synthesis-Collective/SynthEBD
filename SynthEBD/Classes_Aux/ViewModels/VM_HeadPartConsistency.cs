using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;

namespace SynthEBD;
/// <summary>View model for a persisted per-NPC head-part consistency assignment, with a command to clear it.</summary>
public class VM_HeadPartConsistency : VM
{
    /// <summary>Creates the VM and wires the clear-selection command.</summary>
    public VM_HeadPartConsistency()
    {
        ClearSelection = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                ClearAssignment();
            }
        );
    }
    public string Label { get; set; }
    public FormKey FormKey { get; private set; }
    private bool Initialized { get; set; } // for storage only to make sure it doesn't get lost after saving dumping to the DTO
    public bool RandomizedToNone { get; set;} = false;
    public RelayCommand ClearSelection { get; set; }

    /// <summary>Builds a view model from a persisted <see cref="HeadPartConsistency"/> model.</summary>
    /// <param name="model">The model to project.</param>
    /// <returns>A populated view model.</returns>
    public static VM_HeadPartConsistency GetViewModelFromModel(HeadPartConsistency model)
    {
        var viewModel = new VM_HeadPartConsistency();
        viewModel.Label = model.EditorID;
        viewModel.FormKey = model.FormKey;
        viewModel.Initialized = model.Initialized;
        viewModel.RandomizedToNone = model.RandomizedToNone;
        return viewModel;
    }

    /// <summary>Projects this view model back into a <see cref="HeadPartConsistency"/> model for saving.</summary>
    /// <returns>The populated model.</returns>
    public HeadPartConsistency DumpToModel()
    {
        var model = new HeadPartConsistency();
        model.EditorID = Label;
        model.FormKey = FormKey;
        model.Initialized = Initialized;
        model.RandomizedToNone = RandomizedToNone;
        return model;
    }

    /// <summary>Clears the assignment (FormKey, label, flags) back to empty/uninitialized.</summary>
    public void ClearAssignment()
    {
        FormKey = new();
        Label = String.Empty;
        RandomizedToNone = false;
        Initialized = false;
    }
}
