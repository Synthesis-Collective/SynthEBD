using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using ReactiveUI;
using System.Reactive.Linq;

namespace SynthEBD;

/// <summary>
/// View model for the Consistency settings tab. Edits the persisted consistency dictionary
/// (<see cref="PatcherState.Consistency"/>, keyed by NPC FormKey string) that records each NPC's
/// previously-assigned appearance so re-runs stay stable. Selecting an NPC lazily materializes a
/// <see cref="VM_ConsistencyAssignment"/>; the "Delete All ..." commands wipe a single axis
/// (assets / body shape / height / head parts) or the whole file.
/// </summary>
public class VM_ConsistencyUI : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly VM_ConsistencyAssignment.Factory _consistencyFactory;
    private readonly VM_Settings_General _generalSettings;

    /// <summary>
    /// Wires the selected-NPC change subscription (dumps/disposes the outgoing assignment and
    /// reloads the new one), mirrors the environment link cache, and builds the delete-current
    /// and per-axis/all "Delete All" <see cref="RelayCommand"/>s. Seeds preview settings from
    /// general settings.
    /// </summary>
    public VM_ConsistencyUI(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, VM_ConsistencyAssignment.Factory consistencyFactory, VM_Settings_General generalSettings)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _consistencyFactory = consistencyFactory;
        _generalSettings = generalSettings;

        Show3DPreview = generalSettings.bShow3DPreview;
        PreviewerWidth = generalSettings.ConsistencyPreviewerWidth;

        this.WhenAnyValue(x => x.SelectedNPCFormKey)
            .Buffer(2, 1)
            .Select(b => (Previous: b[0], Current: b[1]))
            .Subscribe(x =>
            {
                if (x.Previous != null && !x.Previous.IsNull && CurrentlyDisplayedAssignment != null)
                {
                    CurrentlyDisplayedAssignment.DumpViewModelToModel();
                    CurrentlyDisplayedAssignment.Dispose();
                    CurrentlyDisplayedAssignment = null;
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
                if (CurrentlyDisplayedAssignment != null) { CurrentlyDisplayedAssignment.Dispose(); CurrentlyDisplayedAssignment = null; }
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
                if (MessageWindow.DisplayNotificationYesNo("Confirmation", "Are you sure you want to completely clear the consistency file?"))
                {
                     if (CurrentlyDisplayedAssignment != null) { CurrentlyDisplayedAssignment.Dispose(); CurrentlyDisplayedAssignment = null; }
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

    public bool Show3DPreview { get; set; } = true;
    public double PreviewerWidth { get; set; } = 525;

    /// <summary>Disposes the current assignment VM and rebuilds it from the consistency entry for the selected NPC, if any.</summary>
    public void ReloadActiveViewModel()
    {
        if (CurrentlyDisplayedAssignment != null)
        {
            CurrentlyDisplayedAssignment.Dispose();
            CurrentlyDisplayedAssignment = null;
        }

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
    /// <summary>VM → Models: syncs preview settings to general settings, flushes the displayed assignment, and returns the consistency dictionary.</summary>
    public Dictionary<string, NPCAssignment> DumpViewModelsToModels()
    {
        // Sync preview settings back to general settings for persistence.
        _generalSettings.bShow3DPreview = Show3DPreview;
        _generalSettings.ConsistencyPreviewerWidth = PreviewerWidth;

        if (CurrentlyDisplayedAssignment != null)
        {
            CurrentlyDisplayedAssignment.DumpViewModelToModel();
        }
        return _patcherState.Consistency;
    }
}