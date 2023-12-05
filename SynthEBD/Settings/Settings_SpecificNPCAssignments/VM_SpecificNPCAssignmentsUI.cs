using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive.Linq;

namespace SynthEBD;

public class VM_SpecificNPCAssignmentsUI : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly VM_SpecificNPCAssignmentPlaceHolder.Factory _placeHolderFactory;
    private readonly VM_SpecificNPCAssignment.Factory _specificNpcAssignmentFactory;
    private readonly VM_SettingsTexMesh _texMeshSettings;
    private readonly VM_SettingsBodyGen _bodyGenSettings; 
    private readonly VM_Settings_Headparts _headPartSettings;
    private readonly VM_AssetPack.Factory _assetPackFactory;
    private readonly Converters _converters;
    private readonly SettingsIO_SpecificNPCAssignments _specificAssignmentIO;
    private readonly SettingsIO_BodyGen _bodyGenIO;

    public VM_SpecificNPCAssignmentsUI(
        IEnvironmentStateProvider environmentProvider,
        VM_SettingsTexMesh texMeshSettings,
        VM_SettingsBodyGen bodyGenSettings, 
        VM_Settings_General generalSettingsVM,
        VM_Settings_Headparts headPartSettings,
        VM_AssetPack.Factory assetPackFactory,
        VM_SpecificNPCAssignmentPlaceHolder.Factory placeHolderFactory,
        VM_SpecificNPCAssignment.Factory specificNpcAssignmentFactory,
        Logger logger,
        Converters converters,
        SettingsIO_SpecificNPCAssignments specificAssignmentIO,
        SettingsIO_BodyGen bodyGenIO)
    {
        _environmentProvider = environmentProvider;
        _logger = logger;
        _converters = converters;
        _specificAssignmentIO = specificAssignmentIO;
        _bodyGenIO = bodyGenIO;
        _placeHolderFactory = placeHolderFactory;
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
            execute: _ => {
                var newPlaceHolder = _placeHolderFactory(new NPCAssignment(), Assignments);
                Assignments.Add(newPlaceHolder);
                CurrentlyDisplayedAssignment = specificNpcAssignmentFactory(newPlaceHolder);
            }
        );

        RemoveAssignment = new RelayCommand(
            canExecute: _ => true,
            execute: x => Assignments.Remove((VM_SpecificNPCAssignmentPlaceHolder)x)
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
                    if (System.IO.Path.GetFileName(morphsPath).Equals("templates.ini", StringComparison.OrdinalIgnoreCase) && !MessageWindow.DisplayNotificationYesNo("Confirm File Name", "Expecting morphs.ini but this file is templates.ini, which should be imported in the BodyGen Menu. Are you sure you want to continue?"))
                    {
                        return;
                    }

                    var assignmentTuples = _bodyGenIO.LoadMorphsINI(morphsPath);
                    foreach (var assignment in assignmentTuples)
                    {
                        if (_environmentProvider.LinkCache.TryResolve<INpcGetter>(assignment.Item1, out var npcGetter))
                        {
                            var morphNames = assignment.Item2.Split('|').Select(s => s.Trim());
                            var morphs = new List<VM_BodyGenTemplatePlaceHolder>();
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
                                var specificAssignment = Assignments.FirstOrDefault(x => x.AssociatedModel.NPCFormKey.Equals(assignment.Item1));
                                if (specificAssignment == null)
                                {
                                    NPCAssignment newAssignment = new();
                                    newAssignment.NPCFormKey = assignment.Item1;
                                    newAssignment.DispName = _converters.CreateNPCDispNameFromFormKey(assignment.Item1);
                                    specificAssignment = _placeHolderFactory(newAssignment, Assignments);
                                    Assignments.Add(specificAssignment);
                                }
                                foreach (var morph in morphs) { specificAssignment.AssociatedModel.BodyGenMorphNames.Add(morph.Label); }
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
                var models = DumpViewModelToModels();
                _specificAssignmentIO.SaveAssignments(models, out bool saveSuccess);
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

        this.WhenAnyValue(vm => vm.SelectedPlaceHolder)
         .Buffer(2, 1)
         .Select(b => (Previous: b[0], Current: b[1]))
         .Subscribe(t => {
             if (t.Previous != null && t.Previous.AssociatedViewModel != null)
             {
                 t.Previous.AssociatedModel = t.Previous.AssociatedViewModel.DumpViewModelToModel();
             }

             if (t.Current != null)
             {
                 CurrentlyDisplayedAssignment = _specificNpcAssignmentFactory(t.Current);
                 CurrentlyDisplayedAssignment.CopyInFromModel(t.Current.AssociatedModel);
             }
         });
    }

    public ObservableCollection<VM_SpecificNPCAssignmentPlaceHolder> Assignments { get; set; } = new();
    public VM_SpecificNPCAssignmentPlaceHolder SelectedPlaceHolder { get; set; }
    public VM_SpecificNPCAssignment CurrentlyDisplayedAssignment { get; set; } = null;

    public VM_Alphabetizer<VM_SpecificNPCAssignmentPlaceHolder, string> Alphabetizer { get; set; }

    public VM_SettingsBodyGen BodyGenSettings { get; set; }

    public VM_SettingsTexMesh TexMeshSettings { get; set; }
    public VM_Settings_General SubscribedGeneralSettings { get; set; }

    public RelayCommand AddAssignment { get; set; }
    public RelayCommand RemoveAssignment { get; set; }

    public RelayCommand ImportFromZEBDcommand { get; set; }
    public RelayCommand ImportBodyGenMorphsIni { get; set; }
    public RelayCommand Save { get; }

    public void GetViewModelFromModels(HashSet<NPCAssignment> models)
    {
        if (models == null)
        {
            return;
        }

        _logger.LogStartupEventStart("Loading UI for Specific NPC Assignments Menu");
        Assignments.Clear();
        foreach (var assignment in models)
        {
            if (assignment.NPCFormKey == null || assignment.NPCFormKey.IsNull)
            {
                continue;
            }

            var placeHolder = _placeHolderFactory(assignment, Assignments);
            placeHolder.AssociatedModel = assignment;
            Assignments.Add(placeHolder);
        }
        _logger.LogStartupEventEnd("Loading UI for Specific NPC Assignments Menu");
    }

    public HashSet<NPCAssignment> DumpViewModelToModels()
    {
        if (CurrentlyDisplayedAssignment != null)
        {
            CurrentlyDisplayedAssignment.AssociatedPlaceHolder.AssociatedModel = CurrentlyDisplayedAssignment.AssociatedPlaceHolder.AssociatedViewModel.DumpViewModelToModel();
        }

        HashSet<NPCAssignment> models = new();
        foreach (var vm in Assignments.Where(x => x is not null).ToArray()) // null check needed for when user leaves blank specific assignment
        {
            models.Add(vm.AssociatedModel);
        }
        return models;
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
                var newModels = zEBDSpecificNPCAssignment.ToSynthEBDNPCAssignments(zSpecificNPCAssignments, _logger, _converters, _environmentProvider);

                foreach (var model in newModels)
                {
                    if (model.NPCFormKey == null || model.NPCFormKey.IsNull)
                    {
                        continue;
                    }
                    var placeHolder = _placeHolderFactory(model, Assignments);
                    placeHolder.AssociatedModel = model;
                    Assignments.Add(placeHolder);
                }
            }
            else
            {
                _logger.LogError("Could not parse zEBD Specific NPC Assignments. Error: " + exceptionStr);
            }
        }
    }
}