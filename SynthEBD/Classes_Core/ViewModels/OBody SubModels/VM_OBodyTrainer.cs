using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

// ============================================================================================
// DEPRECATED / LEGACY - not under active maintenance or development.
//
// This is the host VM for the "Annotator Training" tab: a feature that trained an ML.NET classifier
// on BodySlide slider-value vectors to auto-assign body-shape descriptors to presets. It was never
// fully implemented. The project instead adopted the 3D geometry / measurement-based classifier
// (Body Type Registry + Body Type Profiles), chosen as a more straightforward and reliable approach
// than ML training on slider values. The UI entry point (the "Annotator Training" button in
// UC_SettingsOBody.xaml) is hidden (Collapsed); this code is retained only so the avenue can be
// revisited later. Do not extend or depend on it without reconsidering the whole approach.
// ============================================================================================
/// <summary>
/// DEPRECATED / LEGACY (see banner above). Host view model for the never-completed OBody ML
/// "trainer" tab. Switches the displayed sub-UI; exposes the command that activates and reinitializes
/// the <see cref="VM_OBodyTrainerExporter"/>.
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
