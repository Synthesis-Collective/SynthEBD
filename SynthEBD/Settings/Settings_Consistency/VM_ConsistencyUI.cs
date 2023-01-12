using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_ConsistencyUI : VM
{
    private readonly IEnvironmentStateProvider _stateProvider;
    public readonly Logger _logger;

    public VM_ConsistencyUI(IEnvironmentStateProvider stateProvider, Logger logger)
    {
        _stateProvider = stateProvider;
        _logger = logger;

        this.PropertyChanged += RefereshCurrentAssignment;
        
        _stateProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        DeleteCurrentNPC = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                this.CurrentlyDisplayedAssignment = null;
                this.Assignments.Remove(CurrentlyDisplayedAssignment);
            }
        );

        DeleteAllAssets = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                foreach (var assignment in Assignments)
                {
                    assignment.AssetPackName = "";
                    assignment.SubgroupIDs.Clear();
                    assignment.AssetReplacements.Clear();
                    assignment.MixInAssignments.Clear();
                }
                _logger.CallTimedLogErrorWithStatusUpdateAsync("Cleared asset consistency", ErrorType.Warning, 2);
            }
        );

        DeleteAllBodyShape = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                foreach (var assignment in Assignments)
                {
                    assignment.BodyGenMorphNames.Clear();
                    assignment.BodySlidePreset = "";
                }
                _logger.CallTimedLogErrorWithStatusUpdateAsync("Cleared body shape consistency", ErrorType.Warning, 2);
            }
        );

        DeleteAllHeight = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                foreach (var assignment in Assignments)
                {
                    assignment.Height = "";
                }
                _logger.CallTimedLogErrorWithStatusUpdateAsync("Cleared height consistency", ErrorType.Warning, 2);
            }
        );

        DeleteAllHeadParts = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                foreach (var assignment in Assignments)
                {
                    foreach (var headpart in assignment.HeadParts)
                    {
                        if (headpart.Value != null)
                        {
                            headpart.Value.ClearAssignment();
                        }
                    }
                }
                _logger.CallTimedLogErrorWithStatusUpdateAsync("Cleared head part consistency", ErrorType.Warning, 2);
            }
        );

        DeleteAllNPCs = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                if (CustomMessageBox.DisplayNotificationYesNo("Confirmation", "Are you sure you want to completely clear the consistency file?"))
                {
                    CurrentlyDisplayedAssignment = null;
                    Assignments.Clear();
                }
                _logger.CallTimedLogErrorWithStatusUpdateAsync("Cleared all consistency", ErrorType.Warning, 2);
            }
        );
    }

    public ObservableCollection<VM_ConsistencyAssignment> Assignments { get; set; } = new();
    public VM_ConsistencyAssignment CurrentlyDisplayedAssignment { get; set; } = null;
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> AllowedFormKeyTypes { get; } = typeof(INpcGetter).AsEnumerable();
    public FormKey SelectedNPCFormKey { get; set; } = new();

    public RelayCommand DeleteCurrentNPC { get; set; }

    public RelayCommand DeleteAllAssets { get; set; }
    public RelayCommand DeleteAllBodyShape { get; set; }
    public RelayCommand DeleteAllHeight { get; set; }
    public RelayCommand DeleteAllHeadParts { get; set; }
    public RelayCommand DeleteAllNPCs { get; set; }

    public void RefereshCurrentAssignment(object sender, PropertyChangedEventArgs e)
    {
        CurrentlyDisplayedAssignment = this.Assignments.Where(x => x.NPCFormKey.ToString() == SelectedNPCFormKey.ToString()).FirstOrDefault();
    }

    public static void GetViewModelsFromModels(Dictionary<string, NPCAssignment> models, ObservableCollection<VM_ConsistencyAssignment> viewModels, ObservableCollection<VM_AssetPack> AssetPackVMs, VM_Settings_Headparts headParts, Logger logger)
    {
        viewModels.Clear();
        foreach (var model in models)
        {
            if (model.Value == null) { continue; }
            viewModels.Add(VM_ConsistencyAssignment.GetViewModelFromModel(model.Value, AssetPackVMs, logger));
        }
    }

    public static void DumpViewModelsToModels(ObservableCollection<VM_ConsistencyAssignment> viewModels, Dictionary<string, NPCAssignment> models)
    {
        models.Clear();
        foreach (var viewModel in viewModels)
        {
            models.Add(viewModel.NPCFormKey.ToString(), viewModel.DumpViewModelToModel());
        }
    }
}