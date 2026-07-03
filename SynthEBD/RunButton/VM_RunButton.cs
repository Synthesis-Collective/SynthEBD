using Mutagen.Bethesda.Synthesis;
using System;
using System.Windows.Media;

namespace SynthEBD;

/// <summary>
/// Backs the standalone-mode "Run" button. <see cref="ClickRun"/> switches to the log view,
/// dumps view models to models, runs pre-run and body-shape annotation validation, refreshes
/// the output environment on repeat runs, and invokes <see cref="Patcher.RunPatcher"/> on a
/// background task. Registered as a singleton in <see cref="MainModule"/>.
/// </summary>
public class VM_RunButton : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly VM_SettingsTexMesh _texMeshSettingsVm;
    private readonly PatcherState _state;
    private readonly PreRunValidation _preRunValidation;
    private readonly MiscValidation _miscValidation;
    private readonly Logger _logger;

    /// <summary>
    /// Captures the environment/state/validation dependencies and builds the asynchronous
    /// <see cref="ClickRun"/> command. (A commented-out synchronous variant is retained for
    /// debugging.)
    /// </summary>
    public VM_RunButton(
        IEnvironmentStateProvider environmentProvider,
        PatcherState patcherState,
        VM_SettingsTexMesh texMeshSettingsVM, 
        VM_ConsistencyUI consistencyUi,
        VM_Settings_Headparts headParts,
        PatcherState state,
        ViewModelLoader viewModelLoader,
        PreRunValidation preRunValidation,
        MiscValidation miscValidation,
        Logger logger,
        VM_LogDisplay logDisplay,
        Func<Patcher> getPatcher)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _texMeshSettingsVm = texMeshSettingsVM;
        _state = state;
        _preRunValidation = preRunValidation;
        _miscValidation = miscValidation;
        _logger = logger;
        // synchronous version for debugging only
        /*
        ClickRun = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                PatcherEnvironmentProvider.Instance.Environment.Refresh(_patcherState.GeneralSettings.PatchFileName, true); // re-filter load order (in case of multiple runs in same session, to get rid of generated output plugin from link cache & load order)
                ParentWindow.DisplayedViewModel = ParentWindow.LogDisplayVM;
                ParentWindow.DumpViewModelsToModels();
                if (!PreRunValidation()) { return; }
                if (HasBeenRun) { PatcherEnvironmentProvider.Instance.UpdateEnvironment(); } // resets the output mod to a new state so that previous patcher runs from current session get overwritten instead of added on to.
                Patcher.RunPatcher(
                    ParentWindow.AssetPacks.Where(x => _patcherState.TexMeshSettings.SelectedAssetPacks.Contains(x.GroupName)).ToList(), ParentWindow.BodyGenConfigs, ParentWindow.HeightConfigs, ParentWindow.Consistency, ParentWindow.SpecificNPCAssignments,
                    ParentWindow.BlockList, ParentWindow.LinkedNPCNameExclusions, ParentWindow.LinkedNPCGroups, ParentWindow.RecordTemplateLinkCache, ParentWindow.RecordTemplatePlugins, ParentWindow.StatusBarVM);
                VM_ConsistencyUI.GetViewModelsFromModels(ParentWindow.Consistency, ParentWindow.CUIVM.Assignments, ParentWindow.TMVM.AssetPacks); // refresh consistency after running patcher. Otherwise the pre-patching consistency will get reapplied from the view model upon patcher exit
                HasBeenRun = true;
            }
            );
        */
        ClickRun = ReactiveUI.ReactiveCommand.CreateFromTask(
                
            execute: async _ =>
            {
                logDisplay.SwitchViewToLogDisplay();
                viewModelLoader.DumpViewModelsToModels();
                if (!_preRunValidation.ValidatePatcherState()) { return; }
                // Resets the output mod to a new state so that previous patcher runs from the current session get
                // overwritten instead of added on to. UpdateEnvironment is standalone-specific (the interface is
                // injected so non-GUI hosts like the CLI screenshot harness can construct this VM).
                if (HasBeenRun && _environmentProvider is StandaloneRunEnvironmentStateProvider standaloneEnvironment) { standaloneEnvironment.UpdateEnvironment(); }
                _logger.ClearStatusError();

                if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodySlide && !_miscValidation.VerifyBodySlideAnnotations(_patcherState.OBodySettings))
                {
                    return;
                }
                else if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodyGen && !miscValidation.VerifyBodyGenAnnotations(_state.AssetPacks, _state.BodyGenConfigs))
                {
                    return;
                }

                await Task.Run(() => getPatcher().RunPatcher());
                consistencyUi.ReloadActiveViewModel(); // if a consistency entry is open, refresh it with the latest values from this patcher run
                HasBeenRun = true;
            });
    }
    /// <summary>Button background brush (defaults to green).</summary>
    public SolidColorBrush BackgroundColor { get; set; } = new(Colors.Green);
    /// <summary>True once the patcher has run at least once this session; gates environment refresh.</summary>
    public bool HasBeenRun { get; set; } = false;
    /// <summary>Command that runs the patcher (see <see cref="VM_RunButton"/> summary).</summary>
    public ReactiveUI.IReactiveCommand ClickRun { get; }
}