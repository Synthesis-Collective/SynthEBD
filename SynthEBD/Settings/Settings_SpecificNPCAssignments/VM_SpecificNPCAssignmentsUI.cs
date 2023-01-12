using Mutagen.Bethesda.Skyrim;
using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_SpecificNPCAssignmentsUI : VM
{
    private readonly IEnvironmentStateProvider _stateProvider;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly VM_SpecificNPCAssignment.Factory _specificNpcAssignmentFactory;
    private readonly VM_SettingsTexMesh _texMeshSettings;
    private readonly VM_SettingsBodyGen _bodyGenSettings; 
    private readonly VM_Settings_Headparts _headPartSettings;
    private readonly VM_AssetPack.Factory _assetPackFactory;
    private readonly Converters _converters;
    private readonly SettingsIO_SpecificNPCAssignments _specificAssignmentIO;
    private readonly SettingsIO_BodyGen _bodyGenIO;

    public VM_SpecificNPCAssignmentsUI(
        IEnvironmentStateProvider stateProvider,
        VM_SettingsTexMesh texMeshSettings,
        VM_SettingsBodyGen bodyGenSettings, 
        VM_Settings_General generalSettingsVM,
        VM_Settings_Headparts headPartSettings,
        VM_AssetPack.Factory assetPackFactory, 
        VM_SpecificNPCAssignment.Factory specificNpcAssignmentFactory,
        Logger logger,
        Converters converters,
        SettingsIO_SpecificNPCAssignments specificAssignmentIO,
        SettingsIO_BodyGen bodyGenIO)
    {
        _stateProvider = stateProvider;
        _logger = logger;
        _converters = converters;
        _specificAssignmentIO = specificAssignmentIO;
        _bodyGenIO = bodyGenIO;
        _specificNpcAssignmentFactory = specificNpcAssignmentFactory;
        _assetPackFactory = assetPackFactory;

        _texMeshSettings = texMeshSettings;
        _bodyGenSettings = bodyGenSettings;
        _headPartSettings = headPartSettings;

        BodyGenSettings = bodyGenSettings;
        TexMeshSettings = texMeshSettings;
        SubscribedGeneralSettings = generalSettingsVM;

        AddAssignment = new RelayCommand(
            canExecute: _ => true,
            execute: _ => this.Assignments.Add(specificNpcAssignmentFactory())
        );

        RemoveAssignment = new RelayCommand(
            canExecute: _ => true,
            execute: x => this.Assignments.Remove((VM_SpecificNPCAssignment)x)
        );

        ImportFromZEBDcommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => ImportFromZEBD()
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

                    var assignmentTuples = _bodyGenIO.LoadMorphsINI(morphsPath);
                    foreach (var assignment in assignmentTuples)
                    {
                        if (_stateProvider.LinkCache.TryResolve<INpcGetter>(assignment.Item1, out var npcGetter))
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
                                var specificAssignment = Assignments.FirstOrDefault(x => x.NPCFormKey.Equals(assignment.Item1));
                                if (specificAssignment == null)
                                {
                                    specificAssignment = specificNpcAssignmentFactory();
                                    specificAssignment.NPCFormKey = assignment.Item1;
                                    specificAssignment.DispName = _converters.CreateNPCDispNameFromFormKey(assignment.Item1);
                                    Assignments.Add(specificAssignment);
                                }
                                foreach (var morph in morphs) { specificAssignment.ForcedBodyGenMorphs.Add(morph); }
                            }
                        }
                    }
                }
            }
        );

        Save = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                HashSet<NPCAssignment> modelsToSave = new HashSet<NPCAssignment>();
                DumpViewModelToModels(this, modelsToSave);
                _specificAssignmentIO.SaveAssignments(modelsToSave, out bool saveSuccess);
                if (saveSuccess)
                {
                    _logger.CallTimedNotifyStatusUpdateAsync("Specific NPC Assignments Saved.", 2, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Yellow));
                }
                else
                {
                    _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save Specific NPC Assignments.", ErrorType.Error, 5);
                }
            }
        );

        Alphabetizer = new(Assignments, x => x.DispName, new(System.Windows.Media.Colors.MediumPurple));
    }

    public ObservableCollection<VM_SpecificNPCAssignment> Assignments { get; set; } = new();
    public VM_SpecificNPCAssignment CurrentlyDisplayedAssignment { get; set; } = null;

    public VM_Alphabetizer<VM_SpecificNPCAssignment, string> Alphabetizer { get; set; }

    public VM_SettingsBodyGen BodyGenSettings { get; set; }

    public VM_SettingsTexMesh TexMeshSettings { get; set; }
    public VM_Settings_General SubscribedGeneralSettings { get; set; }

    public RelayCommand AddAssignment { get; set; }
    public RelayCommand RemoveAssignment { get; set; }

    public RelayCommand ImportFromZEBDcommand { get; set; }
    public RelayCommand ImportBodyGenMorphsIni { get; set; }
    public RelayCommand Save { get; }

    public static void GetViewModelFromModels(
        VM_AssetPack.Factory assetPackFactory, 
        VM_SettingsTexMesh texMesh,
        VM_SettingsBodyGen bodyGen,
        VM_Settings_Headparts headParts,
        VM_SpecificNPCAssignment.Factory specificNpcAssignmentFactory,
        VM_SpecificNPCAssignmentsUI viewModel, 
        HashSet<NPCAssignment> models,
        Logger logger,
        Converters converters,
        IEnvironmentStateProvider stateProvider)
    {
        viewModel.Assignments.Clear();
        foreach (var assignment in models)
        {
            viewModel.Assignments.Add(VM_SpecificNPCAssignment.GetViewModelFromModel(assignment, assetPackFactory, texMesh, bodyGen, headParts, specificNpcAssignmentFactory, logger, converters, stateProvider));
        }
    }

    public static void DumpViewModelToModels(VM_SpecificNPCAssignmentsUI viewModel, HashSet<NPCAssignment> models)
    {
        models.Clear();
        foreach (var vm in viewModel.Assignments.Where(x => x is not null)) // null check needed for when user leaves blank specific assignment
        {
            models.Add(vm.DumpViewModelToModel());
        }
    }

    public void ImportFromZEBD()
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
                var newModels = zEBDSpecificNPCAssignment.ToSynthEBDNPCAssignments(zSpecificNPCAssignments, _logger, _converters, _stateProvider);

                foreach (var model in newModels)
                {
                    var assignmentVM = VM_SpecificNPCAssignment.GetViewModelFromModel(model, _assetPackFactory, _texMeshSettings, _bodyGenSettings, _headPartSettings, _specificNpcAssignmentFactory, _logger, _converters, _stateProvider);
                    if (assignmentVM != null) // null if the imported NPC doesn't exist in the current load order
                    {
                        this.Assignments.Add(assignmentVM);
                    }
                }
            }
            else
            {
                _logger.LogError("Could not parse zEBD Specific NPC Assignments. Error: " + exceptionStr);
            }
        }
    }
}