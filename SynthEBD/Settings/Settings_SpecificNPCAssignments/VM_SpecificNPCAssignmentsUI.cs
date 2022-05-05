using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_SpecificNPCAssignmentsUI : INotifyPropertyChanged
    {
        public VM_SpecificNPCAssignmentsUI(VM_SettingsTexMesh texMeshSettings, VM_SettingsBodyGen bodyGenSettings, VM_SettingsOBody oBodySettings, VM_Settings_General generalSettingsVM, MainWindow_ViewModel mainVM)
        {
            this.Assignments = new ObservableCollection<VM_SpecificNPCAssignment>();
            this.CurrentlyDisplayedAssignment = null;
            this.BodyGenSettings = bodyGenSettings;
            this.TexMeshSettings = texMeshSettings;

            AddAssignment = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.Assignments.Add(new VM_SpecificNPCAssignment(mainVM))
                );

            RemoveAssignment = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.Assignments.Remove((VM_SpecificNPCAssignment)x)
                );

            ImportFromZEBDcommand = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => ImportFromZEBD(oBodySettings, generalSettingsVM, mainVM)
                );

            Save = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    HashSet<NPCAssignment> modelsToSave = new HashSet<NPCAssignment>();
                    DumpViewModelToModels(this, modelsToSave);
                    SettingsIO_SpecificNPCAssignments.SaveAssignments(modelsToSave, out bool saveSuccess);
                    if (saveSuccess)
                    {
                        Logger.CallTimedNotifyStatusUpdateAsync("Specific NPC Assignments Saved.", 2, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Yellow));
                    }
                    else
                    {
                        Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save Specific NPC Assignments.", ErrorType.Error, 5);
                        Logger.SwitchViewToLogDisplay();
                    }
                }
                );
        }

        public ObservableCollection<VM_SpecificNPCAssignment> Assignments { get; set; }
        public VM_SpecificNPCAssignment CurrentlyDisplayedAssignment { get; set; }

        public VM_SettingsBodyGen BodyGenSettings { get; set; }

        public VM_SettingsTexMesh TexMeshSettings { get; set; }

        public RelayCommand AddAssignment { get; set; }
        public RelayCommand RemoveAssignment { get; set; }

        public RelayCommand ImportFromZEBDcommand { get; set; }
        public RelayCommand Save { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static void GetViewModelFromModels(VM_SpecificNPCAssignmentsUI viewModel, HashSet<NPCAssignment> models, VM_SettingsOBody oBodySettings, VM_Settings_General generalSettingsVM, MainWindow_ViewModel mainVM)
        {
            viewModel.Assignments.Clear();
            foreach (var assignment in models)
            {
                viewModel.Assignments.Add(VM_SpecificNPCAssignment.GetViewModelFromModel(assignment, mainVM));
            }
        }

        public static void DumpViewModelToModels(VM_SpecificNPCAssignmentsUI viewModel, HashSet<NPCAssignment> models)
        {
            models.Clear();
            foreach (var vm in viewModel.Assignments)
            {
                models.Add(VM_SpecificNPCAssignment.DumpViewModelToModel(vm));
            }
        }

        public void ImportFromZEBD(VM_SettingsOBody oBodySettings, VM_Settings_General generalSettingsVM, MainWindow_ViewModel mainVM)
        {
            // Configure open file dialog box
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.FileName = "ForceNPCList"; // Default file name
            dialog.DefaultExt = ".json"; // Default file extension
            dialog.Filter = "JSON files (.json|*.json"; // Filter files by extension

            // Show open file dialog box
            bool? result = dialog.ShowDialog();

            // Process open file dialog box results
            if (result == true)
            {
                // Open document
                string filename = dialog.FileName;

                var zSpecificNPCAssignments = JSONhandler<HashSet<zEBDSpecificNPCAssignment>>.LoadJSONFile(filename, out bool loadSuccess, out string exceptionStr);
                if (loadSuccess)
                {
                    var newModels = zEBDSpecificNPCAssignment.ToSynthEBDNPCAssignments(zSpecificNPCAssignments);

                    foreach (var model in newModels)
                    {
                        var assignmentVM = VM_SpecificNPCAssignment.GetViewModelFromModel(model, mainVM);
                        if (assignmentVM != null) // null if the imported NPC doesn't exist in the current load order
                        {
                            this.Assignments.Add(assignmentVM);
                        }
                    }
                }
                else
                {
                    Logger.LogError("Could not parse zEBD Specific NPC Assignments. Error: " + exceptionStr);
                }
            }
        }
    }
}
