using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

/// <summary>
/// Host view model for the OBody ML "trainer" tab. Switches the displayed sub-UI; currently exposes the
/// command that activates and reinitializes the <see cref="VM_OBodyTrainerExporter"/>.
/// </summary>
public class VM_OBodyTrainer : VM
{
    /// <summary>Wires the command that selects the exporter as the displayed UI and reinitializes it.</summary>
    public VM_OBodyTrainer(VM_OBodyTrainerExporter exporter)
    {
        ClickExporterMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                DisplayedUI = exporter;
                exporter.Reinitialize();
            }
        );
    }
    public object DisplayedUI { get; set; }
    public RelayCommand ClickExporterMenu { get; }
}
