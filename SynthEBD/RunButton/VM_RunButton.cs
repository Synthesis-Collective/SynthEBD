using System.Windows.Media;
using Noggog.WPF;

namespace SynthEBD;

public class VM_RunButton : ViewModel
{
    public VM_RunButton(MainWindow_ViewModel parentWindow)
    {
        this.ParentWindow = parentWindow;

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
                Patcher.RunPatcher(
                    ParentWindow.AssetPacks.Where(x => PatcherSettings.TexMesh.SelectedAssetPacks.Contains(x.GroupName)).ToList(), ParentWindow.BodyGenConfigs, ParentWindow.HeightConfigs, ParentWindow.Consistency, ParentWindow.SpecificNPCAssignments,
                    ParentWindow.BlockList, ParentWindow.LinkedNPCNameExclusions, ParentWindow.LinkedNPCGroups, ParentWindow.RecordTemplateLinkCache, ParentWindow.RecordTemplatePlugins, ParentWindow.StatusBarVM);
                VM_ConsistencyUI.GetViewModelsFromModels(ParentWindow.Consistency, ParentWindow.CUIVM.Assignments, ParentWindow.TMVM.AssetPacks); // refresh consistency after running patcher. Otherwise the pre-patching consistency will get reapplied from the view model upon patcher exit
            }
            );
        */
        ClickRun = ReactiveUI.ReactiveCommand.CreateFromTask(
                
            execute: async _ =>
            {
                ParentWindow.DisplayedViewModel = ParentWindow.LogDisplayVM;
                ParentWindow.DumpViewModelsToModels();
                if (!PreRunValidation()) { return; }
                await Task.Run(() => Patcher.RunPatcher(
                    ParentWindow.AssetPacks.Where(x => PatcherSettings.TexMesh.SelectedAssetPacks.Contains(x.GroupName)).ToList(), ParentWindow.BodyGenConfigs, ParentWindow.HeightConfigs, ParentWindow.Consistency, ParentWindow.SpecificNPCAssignments,
                    ParentWindow.BlockList, ParentWindow.LinkedNPCNameExclusions, ParentWindow.LinkedNPCGroups, ParentWindow.RecordTemplateLinkCache, ParentWindow.RecordTemplatePlugins, ParentWindow.StatusBarVM));
                VM_ConsistencyUI.GetViewModelsFromModels(ParentWindow.Consistency, ParentWindow.ConsistencyUIVM.Assignments, ParentWindow.TexMeshSettingsVM.AssetPacks); // refresh consistency after running patcher. Otherwise the pre-patching consistency will get reapplied from the view model upon patcher exit
            });
    }
    public SolidColorBrush BackgroundColor { get; set; } = new(Colors.Green);

    public MainWindow_ViewModel ParentWindow { get; set; }

    public ReactiveUI.IReactiveCommand ClickRun { get; }

    public bool PreRunValidation()
    {
        bool valid = true;
        var env = PatcherEnvironmentProvider.Instance.Environment;

        if (PatcherSettings.General.bChangeMeshesOrTextures)
        {
            if (!MiscValidation.VerifyEBDInstalled())
            {
                valid = false;
            }

            if (!ParentWindow.TexMeshSettingsVM.ValidateAllConfigs(ParentWindow.BodyGenConfigs, out var configErrors)) // check config files for errors
            {
                Logger.LogMessage(configErrors);
                valid = false;
            }

        }

        if (PatcherSettings.General.BodySelectionMode != BodyShapeSelectionMode.None)
        {
            if (!MiscValidation.VerifyRaceMenuInstalled(env.DataFolderPath))
            {
                valid = false;
            }
            else if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodyGen && !MiscValidation.VerifyRaceMenuIniForBodyGen())
            {
                valid = false;
            }
            else if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodySlide && !MiscValidation.VerifyRaceMenuIniForBodySlide())
            {
                valid = false;
            }

            if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodySlide)
            {
                if (PatcherSettings.General.BSSelectionMode == BodySlideSelectionMode.OBody)
                {
                    if (!MiscValidation.VerifyOBodyInstalled(env.DataFolderPath))
                    {
                        valid = false;
                    }
                }
                else if (PatcherSettings.General.BSSelectionMode == BodySlideSelectionMode.AutoBody)
                {
                    if (!MiscValidation.VerifyAutoBodyInstalled(env.DataFolderPath))
                    {
                        valid = false;
                    }
                }

                if (!MiscValidation.VerifyBodySlideAnnotations(PatcherSettings.OBody))
                {
                    valid = false;
                }

                if (!MiscValidation.VerifyGeneratedTriFilesForOBody(PatcherSettings.OBody))
                {
                    valid = false;
                }

                if (!MiscValidation.VerifySPIDInstalled(env.DataFolderPath))
                {
                    valid = false;
                }
            }
            else if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodyGen)
            {
                if (!MiscValidation.VerifyBodyGenAnnotations(ParentWindow.AssetPacks, ParentWindow.BodyGenConfigs))
                {
                    valid = false;
                }

                if (!MiscValidation.VerifyGeneratedTriFilesForBodyGen(ParentWindow.AssetPacks, ParentWindow.BodyGenConfigs))
                {
                    valid = false;
                }
            }
        }

        if (!valid)
        {
            Logger.LogErrorWithStatusUpdate("Could not run the patcher. Please correct the errors above.", ErrorType.Error);
        }

        return valid;
    }
}