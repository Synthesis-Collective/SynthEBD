using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

public class VM_OBodyTrainer : VM
{
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
