using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using ReactiveUI;
using System.Reactive.Linq;

namespace SynthEBD;

public class VM_ConsistencyUI : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly VM_ConsistencyAssignment.Factory _consistencyFactory;

    public VM_ConsistencyUI(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, VM_ConsistencyAssignment.Factory consistencyFactory)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _consistencyFactory = consistencyFactory;

        this.WhenAnyValue(x => x.SelectedNPCFormKey)
            .Buffer(2, 1)
            .Select(b => (Previous: b[0], Current: b[1]))
            .Subscribe(x =>
            {
                if (x.Previous != null && !x.Previous.IsNull && CurrentlyDisplayedAssignment != null)
                {
                    CurrentlyDisplayedAssignment.DumpViewModelToModel();
                }
                if (x.Current != null && !x.Current.IsNull)
                {
                    ReloadActiveViewModel();
                }
            }).DisposeWith(this);
        
        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        DeleteCurrentNPC = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
               CurrentlyDisplayedAssignment = null;
                var currentFKstr = SelectedNPCFormKey.ToString();
                if (_patcherState.Consistency.ContainsKey(currentFKstr))
                {
                    _patcherState.Consistency.Remove(currentFKstr);
                }
            }
        );

        DeleteAllAssets = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                foreach (var assignment in _patcherState.Consistency.Values)
                {
                    assignment.AssetPackName = "";
                    assignment.SubgroupIDs?.Clear();
                    assignment.AssetReplacerAssignments?.Clear();
                    assignment.MixInAssignments?.Clear();
                }
                _logger.CallTimedLogErrorWithStatusUpdateAsync("Cleared asset consistency", ErrorType.Warning, 2);
                ReloadActiveViewModel();
            }
        );

        DeleteAllBodyShape = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                foreach (var assignment in _patcherState.Consistency.Values)
                {
                    assignment.BodyGenMorphNames?.Clear();
                    assignment.BodySlidePreset = "";
                }
                _logger.CallTimedLogErrorWithStatusUpdateAsync("Cleared body shape consistency", ErrorType.Warning, 2);
                ReloadActiveViewModel();
            }
        );

        DeleteAllHeight = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                foreach (var assignment in _patcherState.Consistency.Values)
                {
                    assignment.Height = null;
                }
                _logger.CallTimedLogErrorWithStatusUpdateAsync("Cleared height consistency", ErrorType.Warning, 2);
                ReloadActiveViewModel();
            }
        );

        DeleteAllHeadParts = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                foreach (var assignment in _patcherState.Consistency.Values)
                {
                    assignment.HeadParts?.Clear();
                }
                _logger.CallTimedLogErrorWithStatusUpdateAsync("Cleared head part consistency", ErrorType.Warning, 2);
                ReloadActiveViewModel();
            }
        );

        DeleteAllNPCs = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                if (CustomMessageBox.DisplayNotificationYesNo("Confirmation", "Are you sure you want to completely clear the consistency file?"))
                {
                    CurrentlyDisplayedAssignment = null;
                    _patcherState.Consistency.Clear();
                }
                _logger.CallTimedLogErrorWithStatusUpdateAsync("Cleared all consistency", ErrorType.Warning, 2);
                ReloadActiveViewModel();
            }
        );
    }

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

    public void ReloadActiveViewModel()
    {
        if (SelectedNPCFormKey != null && !SelectedNPCFormKey.IsNull)
        {
            var key = SelectedNPCFormKey.ToString();
            if (_patcherState.Consistency.ContainsKey(key))
            {
                CurrentlyDisplayedAssignment = _consistencyFactory(_patcherState.Consistency[key]);
                CurrentlyDisplayedAssignment.GetViewModelFromModel(_patcherState.Consistency[key]);
            }
        }
    }
    /*
    public static void GetViewModelsFromModels(Dictionary<string, NPCAssignment> models, ObservableCollection<VM_ConsistencyAssignment> viewModels, ObservableCollection<VM_AssetPack> AssetPackVMs, VM_Settings_Headparts headParts, Logger logger)
    {
        if (models == null)
        {
            return;
        }

        logger.LogStartupEventStart("Loading UI for Consistency Menu");
        viewModels.Clear();
        foreach (var model in models)
        {
            if (model.Value == null) { continue; }
            viewModels.Add(VM_ConsistencyAssignment.GetViewModelFromModel(model.Value, AssetPackVMs, logger));
        }
        logger.LogStartupEventEnd("Loading UI for Consistency Menu");
    }
    */
    public Dictionary<string, NPCAssignment> DumpViewModelsToModels()
    {
        if (CurrentlyDisplayedAssignment != null)
        {
            CurrentlyDisplayedAssignment.DumpViewModelToModel();
        }
        return _patcherState.Consistency;
    }
}