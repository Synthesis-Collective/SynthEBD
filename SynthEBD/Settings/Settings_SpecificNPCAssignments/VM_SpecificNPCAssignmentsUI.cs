using Mutagen.Bethesda.Skyrim;
using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_SpecificNPCAssignmentsUI : VM
{
    public VM_SpecificNPCAssignmentsUI(VM_SettingsTexMesh texMeshSettings, VM_SettingsBodyGen bodyGenSettings, VM_SettingsOBody oBodySettings, VM_Settings_General generalSettingsVM, MainWindow_ViewModel mainVM)
    {
        this.BodyGenSettings = bodyGenSettings;
        this.TexMeshSettings = texMeshSettings;
        this.SubscribedGeneralSettings = mainVM.GeneralSettingsVM;

        AddAssignment = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.Assignments.Add(new VM_SpecificNPCAssignment(mainVM, mainVM.State))
        );

        RemoveAssignment = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => this.Assignments.Remove((VM_SpecificNPCAssignment)x)
        );

        ImportFromZEBDcommand = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => ImportFromZEBD(oBodySettings, generalSettingsVM, mainVM)
        );

        ImportBodyGenMorphsIni = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                if (IO_Aux.SelectFile("", "INI files (*.ini)|*.ini", "Select the Morphs.ini file", out string morphsPath))
                {
                    if (System.IO.Path.GetFileName(morphsPath).Equals("templates.ini", StringComparison.OrdinalIgnoreCase) && !CustomMessageBox.DisplayNotificationYesNo("Confirm File Name", "Expecting morphs.ini but this file is templates.ini, which should be imported in the BodyGen Menu. Are you sure you want to continue?"))
                    {
                        return;
                    }

                    var assignmentTuples = SettingsIO_BodyGen.LoadMorphsINI(morphsPath);
                    foreach (var assignment in assignmentTuples)
                    {
                        if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<INpcGetter>(assignment.Item1, out var npcGetter))
                        {
                            var morphNames = assignment.Item2.Split('|').Select(s => s.Trim());
                            var morphs = new List<VM_BodyGenTemplate>();
                            var gender = NPCInfo.GetGender(npcGetter);
                            switch(gender)
                            {
                                case Gender.Male:
                                    foreach (var name in morphNames)
                                    {
                                        var morph = BodyGenSettings.CurrentMaleConfig.TemplateMorphUI.Templates.Where(x => x.Label == name).FirstOrDefault();
                                        if (morph != null)
                                        {
                                            morphs.Add(morph);
                                        }
                                    }
                                    break;
                                case Gender.Female:
                                    foreach (var name in morphNames)
                                    {
                                        var morph = BodyGenSettings.CurrentFemaleConfig.TemplateMorphUI.Templates.Where(x => x.Label == name).FirstOrDefault();
                                        if (morph != null)
                                        {
                                            morphs.Add(morph);
                                        }
                                    }
                                    break;
                            }

                            if (morphs.Any())
                            {
                                var specificAssignment = this.Assignments.FirstOrDefault(x => x.NPCFormKey.Equals(assignment.Item1));
                                if (specificAssignment == null)
                                {
                                    specificAssignment = new VM_SpecificNPCAssignment(mainVM, mainVM.State) { NPCFormKey = assignment.Item1 };
                                    specificAssignment.DispName = Converters.CreateNPCDispNameFromFormKey(assignment.Item1);
                                    Assignments.Add(specificAssignment);
                                }
                                foreach (var morph in morphs) { specificAssignment.ForcedBodyGenMorphs.Add(morph); }
                            }
                        }
                    }
                }
            }
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

    public ObservableCollection<VM_SpecificNPCAssignment> Assignments { get; set; } = new();
    public VM_SpecificNPCAssignment CurrentlyDisplayedAssignment { get; set; } = null;

    public VM_SettingsBodyGen BodyGenSettings { get; set; }

    public VM_SettingsTexMesh TexMeshSettings { get; set; }
    public VM_Settings_General SubscribedGeneralSettings { get; set; }

    public RelayCommand AddAssignment { get; set; }
    public RelayCommand RemoveAssignment { get; set; }

    public RelayCommand ImportFromZEBDcommand { get; set; }
    public RelayCommand ImportBodyGenMorphsIni { get; set; }
    public RelayCommand Save { get; }

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