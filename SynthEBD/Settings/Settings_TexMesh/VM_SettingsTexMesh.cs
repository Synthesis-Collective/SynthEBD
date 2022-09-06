using ReactiveUI;
using System.Collections.ObjectModel;
using System.Windows.Forms;
using System.Windows.Media;

namespace SynthEBD;

public class VM_SettingsTexMesh : VM
{
    public SaveLoader SaveLoader { get; set; }
    private List<string> InstalledConfigsInCurrentSession = new List<string>();

    public VM_SettingsTexMesh(
        MainState state,
        VM_Settings_General general,
        VM_SettingsModManager modManager,
        VM_AssetPack.Factory assetPackFactory,
        VM_Subgroup.Factory subgroupFactory)
    {
        AddTrimPath = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.TrimPaths.Add(new TrimPath())
        );
        RemoveTrimPath = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => this.TrimPaths.Remove((TrimPath)x)
        );
        ValidateAll = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (!AssetPacks.Any())
                {
                    CustomMessageBox.DisplayNotificationOK("", "There are no Asset Pack Config Files installed.");
                }
                else if (ValidateAllConfigs(state.BodyGenConfigs, out List<string> errors))
                {
                    CustomMessageBox.DisplayNotificationOK("", "No errors found.");
                }
                else
                {
                    Logger.LogError(String.Join(Environment.NewLine, errors));
                }
            }
        );

        AddNewAssetPackConfigFile = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var newAP = assetPackFactory();
                var newSG = subgroupFactory(general.RaceGroupings, newAP.Subgroups, newAP, null, false);
                newSG.ID = "FS";
                newSG.Name = "First Subgroup";
                newAP.Subgroups.Add(newSG);
                this.AssetPacks.Add(newAP);
                AssetPresenterPrimary.AssetPack = newAP;
            }
        );

        InstallFromArchive = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                modManager.UpdatePatcherSettings(); // make sure mod manager integration is synced w/ latest settings
                var installedConfigs = ConfigInstaller.InstallConfigFile();
                if (installedConfigs.Any())
                {
                    RefreshInstalledConfigs(installedConfigs);
                }
            }
        );

        InstallFromJson = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFile(PatcherSettings.Paths.AssetPackDirPath, "Config files (*.json)|*.json", "Select the config json file", out string path))
                {
                    var newAssetPack = SettingsIO_AssetPack.LoadAssetPack(path, PatcherSettings.General.RaceGroupings, state.RecordTemplatePlugins, state.BodyGenConfigs, out bool loadSuccess);
                    if (loadSuccess)
                    {
                        newAssetPack.FilePath = System.IO.Path.Combine(PatcherSettings.Paths.AssetPackDirPath, System.IO.Path.GetFileName(newAssetPack.FilePath)); // overwrite existing filepath so it doesn't get deleted from source
                        var newAssetPackVM = assetPackFactory();
                        newAssetPackVM.CopyInViewModelFromModel(newAssetPack);
                        newAssetPackVM.IsSelected = true;
                        AssetPacks.Add(newAssetPackVM);
                    }
                }
            }
        );

        SplitScreenToggle = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (bShowSecondaryAssetPack) { bShowSecondaryAssetPack = false; }
                else { bShowSecondaryAssetPack = true; }
            }
        );

        AssetPresenterPrimary = new VM_AssetPresenter(this);
        AssetPresenterSecondary = new VM_AssetPresenter(this);

        SelectConfigsAll = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                foreach (var config in AssetPacks) { config.IsSelected = true; }
            }
        );

        SelectConfigsNone = new SynthEBD.RelayCommand(
           canExecute: _ => true,
           execute: _ =>
           {
               foreach (var config in AssetPacks) { config.IsSelected = false; }
           }
       );
    }

    public bool bChangeNPCTextures { get; set; } = true;
    public bool bChangeNPCMeshes { get; set; } = true;
    public bool bChangeNPCHeadParts { get; set; } = true;
    public bool bApplyToNPCsWithCustomSkins { get; set; } = true;
    public bool bApplyToNPCsWithCustomFaces { get; set; } = true;
    public bool bForceVanillaBodyMeshPath { get; set; } = false;
    public bool bDisplayPopupAlerts { get; set; } = true;
    public bool bGenerateAssignmentLog { get; set; } = true;
    public bool bEasyNPCCompatibilityMode { get; set; } = true;
    public bool bShowPreviewImages { get; set; } = true;
    public int MaxPreviewImageSize { get; set; } = 1024;
    public ObservableCollection<TrimPath> TrimPaths { get; set; } = new();

    public ObservableCollection<VM_AssetPack> AssetPacks { get; set; } = new();

    public RelayCommand AddTrimPath { get; }
    public RelayCommand RemoveTrimPath { get; }
    public RelayCommand ValidateAll { get; }
    public RelayCommand AddNewAssetPackConfigFile { get; }
    public RelayCommand InstallFromArchive { get; }
    public RelayCommand InstallFromJson { get; }
    public RelayCommand SplitScreenToggle { get; }
    public string LastViewedAssetPackName { get; set; }
    public bool bShowSecondaryAssetPack { get; set; } = false;
    public VM_AssetPresenter AssetPresenterPrimary { get; set; }
    public VM_AssetPresenter AssetPresenterSecondary { get; set; }
    public string DisplayedAssetPackStr { get; set; }
    public RelayCommand SelectConfigsAll { get; }
    public RelayCommand SelectConfigsNone { get; }

    public bool ValidateAllConfigs(BodyGenConfigs bodyGenConfigs, out List<string> errors)
    {
        bool isValid = true;
        errors = new List<string>();
        foreach (var config in AssetPacks)
        {
            if (config.IsSelected && !config.Validate(bodyGenConfigs, out var configErrors))
            {
                isValid = false;
                errors.AddRange(configErrors);
                errors.Add("");
            }
        }
        return isValid;
    }

    public static void GetViewModelFromModel(VM_SettingsTexMesh viewModel, Settings_TexMesh model)
    {
        viewModel.bChangeNPCTextures = model.bChangeNPCTextures;
        viewModel.bChangeNPCMeshes = model.bChangeNPCMeshes;
        viewModel.bChangeNPCHeadParts = model.bChangeNPCHeadParts;
        viewModel.bApplyToNPCsWithCustomSkins = model.bApplyToNPCsWithCustomSkins;
        viewModel.bApplyToNPCsWithCustomFaces = model.bApplyToNPCsWithCustomFaces;
        viewModel.bForceVanillaBodyMeshPath = model.bForceVanillaBodyMeshPath;
        viewModel.bDisplayPopupAlerts = model.bDisplayPopupAlerts;
        viewModel.bGenerateAssignmentLog = model.bGenerateAssignmentLog;
        viewModel.bShowPreviewImages = model.bShowPreviewImages;
        viewModel.MaxPreviewImageSize = model.MaxPreviewImageSize;
        viewModel.TrimPaths = new ObservableCollection<TrimPath>(model.TrimPaths);
        viewModel.LastViewedAssetPackName = model.LastViewedAssetPack;
        viewModel.bEasyNPCCompatibilityMode = model.bEasyNPCCompatibilityMode;
    }

    public static void DumpViewModelToModel(VM_SettingsTexMesh viewModel, Settings_TexMesh model)
    {
        model.bChangeNPCTextures = viewModel.bChangeNPCTextures;
        model.bChangeNPCMeshes = viewModel.bChangeNPCMeshes;
        model.bChangeNPCHeadParts = viewModel.bChangeNPCHeadParts;
        model.bApplyToNPCsWithCustomSkins = viewModel.bApplyToNPCsWithCustomSkins;
        model.bApplyToNPCsWithCustomFaces = viewModel.bApplyToNPCsWithCustomFaces;
        model.bForceVanillaBodyMeshPath = viewModel.bForceVanillaBodyMeshPath;
        model.bDisplayPopupAlerts = viewModel.bDisplayPopupAlerts;
        model.bGenerateAssignmentLog = viewModel.bGenerateAssignmentLog;
        model.bShowPreviewImages = viewModel.bShowPreviewImages;
        model.MaxPreviewImageSize = viewModel.MaxPreviewImageSize;
        model.TrimPaths = viewModel.TrimPaths.ToList();
        model.SelectedAssetPacks = viewModel.AssetPacks.Where(x => x.IsSelected).Select(x => x.GroupName).ToHashSet();
        if (viewModel.AssetPresenterPrimary.AssetPack is not null)
        {
            model.LastViewedAssetPack = viewModel.AssetPresenterPrimary.AssetPack.GroupName;
        }
        model.bEasyNPCCompatibilityMode = viewModel.bEasyNPCCompatibilityMode;
    }

    public void RefreshInstalledConfigs(List<string> installedConfigs)
    {
        InstalledConfigsInCurrentSession.AddRange(installedConfigs);
        //Logger.ArchiveStatus();
        //Task.Run(() => Logger.UpdateStatusAsync("Refreshing loaded settings - please wait.", false));
        Cursor.Current = Cursors.WaitCursor;
        SaveLoader.SaveAndRefreshPlugins();
        //Logger.UnarchiveStatus();
        foreach (var newConfig in AssetPacks.Where(x => InstalledConfigsInCurrentSession.Contains(x.GroupName)))
        {
            newConfig.IsSelected = true;
        }
        Cursor.Current = Cursors.Default;
    }

    public void RefreshDisplayedAssetPackString()
    {
        DisplayedAssetPackStr = string.Join(" | ", AssetPacks.Where(x => x.IsSelected).Select(x => x.ShortName));
    }
}