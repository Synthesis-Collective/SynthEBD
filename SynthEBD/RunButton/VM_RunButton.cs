using System.Windows.Media;

namespace SynthEBD;

public class VM_RunButton : VM
{
    private readonly StandaloneRunStateProvider _stateProvider;
    private readonly VM_SettingsTexMesh _texMeshSettingsVm;
    private readonly MainState _state;
    private readonly MiscValidation _miscValidation;
    private readonly Logger _logger;

    public VM_RunButton(
        StandaloneRunStateProvider stateProvider,
        VM_SettingsTexMesh texMeshSettingsVM, 
        VM_ConsistencyUI consistencyUi,
        VM_Settings_Headparts headParts,
        MainState state,
        SaveLoader saveLoader,
        MiscValidation miscValidation,
        Logger logger,
        VM_LogDisplay logDisplay,
        Func<Patcher> getPatcher)
    {
        _stateProvider = stateProvider;
        _texMeshSettingsVm = texMeshSettingsVM;
        _state = state;
        _miscValidation = miscValidation;
        _logger = logger;
        // synchronous version for debugging only
        /*
        ClickRun = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                PatcherEnvironmentProvider.Instance.Environment.Refresh(PatcherSettings.General.PatchFileName, true); // re-filter load order (in case of multiple runs in same session, to get rid of generated output plugin from link cache & load order)
                ParentWindow.DisplayedViewModel = ParentWindow.LogDisplayVM;
                ParentWindow.DumpViewModelsToModels();
                if (!PreRunValidation()) { return; }
                if (HasBeenRun) { PatcherEnvironmentProvider.Instance.UpdateEnvironment(); } // resets the output mod to a new state so that previous patcher runs from current session get overwritten instead of added on to.
                Patcher.RunPatcher(
                    ParentWindow.AssetPacks.Where(x => PatcherSettings.TexMesh.SelectedAssetPacks.Contains(x.GroupName)).ToList(), ParentWindow.BodyGenConfigs, ParentWindow.HeightConfigs, ParentWindow.Consistency, ParentWindow.SpecificNPCAssignments,
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
                saveLoader.DumpViewModelsToModels();
                if (!PreRunValidation()) { return; }
                if (HasBeenRun) { _stateProvider.UpdateEnvironment(); } // resets the output mod to a new state so that previous patcher runs from current session get overwritten instead of added on to.
                _logger.ClearStatusError();
                await Task.Run(() => getPatcher().RunPatcher());
                VM_ConsistencyUI.GetViewModelsFromModels(state.Consistency, consistencyUi.Assignments, texMeshSettingsVM.AssetPacks, headParts, _logger); // refresh consistency after running patcher. Otherwise the pre-patching consistency will get reapplied from the view model upon patcher exit
                HasBeenRun = true;
            });
    }
    public SolidColorBrush BackgroundColor { get; set; } = new(Colors.Green);
    public bool HasBeenRun { get; set; } = false;
    public ReactiveUI.IReactiveCommand ClickRun { get; }

    public bool PreRunValidation()
    {
        bool valid = true;

        if (PatcherSettings.General.bChangeMeshesOrTextures)
        {
            if (!_miscValidation.VerifyEBDInstalled())
            {
                valid = false;
            }

            if (!_texMeshSettingsVm.ValidateAllConfigs(_state.BodyGenConfigs, PatcherSettings.OBody, out var configErrors)) // check config files for errors
            {
                _logger.LogMessage(configErrors);
                valid = false;
            }

        }

        if (PatcherSettings.General.BodySelectionMode != BodyShapeSelectionMode.None)
        {
            if (!_miscValidation.VerifyRaceMenuInstalled(_stateProvider.DataFolderPath))
            {
                valid = false;
            }
            else if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodyGen && !_miscValidation.VerifyRaceMenuIniForBodyGen())
            {
                valid = false;
            }
            else if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodySlide && !_miscValidation.VerifyRaceMenuIniForBodySlide())
            {
                valid = false;
            }

            if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodySlide)
            {
                if (PatcherSettings.General.BSSelectionMode == BodySlideSelectionMode.OBody)
                {
                    if (!_miscValidation.VerifyOBodyInstalled(_stateProvider.DataFolderPath))
                    {
                        valid = false;
                    }

                    if (!_miscValidation.VerifyJContainersInstalled(_stateProvider.DataFolderPath, false))
                    {
                        valid = false;
                    }
                }
                else if (PatcherSettings.General.BSSelectionMode == BodySlideSelectionMode.AutoBody)
                {
                    if (!_miscValidation.VerifyAutoBodyInstalled(_stateProvider.DataFolderPath))
                    {
                        valid = false;
                    }

                    if (PatcherSettings.OBody.AutoBodySelectionMode == AutoBodySelectionMode.JSON && !_miscValidation.VerifyJContainersInstalled(_stateProvider.DataFolderPath, false))
                    {
                        valid = false;
                    }
                }

                if (!_miscValidation.VerifyBodySlideAnnotations(PatcherSettings.OBody))
                {
                    valid = false;
                }

                if (!_miscValidation.VerifyGeneratedTriFilesForOBody(PatcherSettings.OBody))
                {
                    valid = false;
                }

                //if (!MiscValidation.VerifySPIDInstalled(env.DataFolderPath, false))
                //{
                //    valid = false;
                //}
            }
            else if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodyGen)
            {
                if (!_miscValidation.VerifyBodyGenAnnotations(_state.AssetPacks, _state.BodyGenConfigs))
                {
                    valid = false;
                }

                if (!_miscValidation.VerifyGeneratedTriFilesForBodyGen(_state.AssetPacks, _state.BodyGenConfigs))
                {
                    valid = false;
                }
            }
        }

        if (PatcherSettings.General.bChangeHeadParts)
        {
            if (!_miscValidation.VerifyEBDInstalled())
            {
                valid = false;
            }

            //if (!MiscValidation.VerifySPIDInstalled(env.DataFolderPath, false))
            //{
            //    valid = false;
            //}

            if (!_miscValidation.VerifyJContainersInstalled(_stateProvider.DataFolderPath, false))
            {
                valid = false;
            }
        }

        if (!valid)
        {
            _logger.LogErrorWithStatusUpdate("Could not run the patcher. Please correct the errors above.", ErrorType.Error);
        }

        return valid;
    }
}