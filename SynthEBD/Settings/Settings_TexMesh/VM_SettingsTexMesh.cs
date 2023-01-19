using ReactiveUI;
using Noggog;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Forms;

namespace SynthEBD;

public class VM_SettingsTexMesh : VM
{
    private List<string> InstalledConfigsInCurrentSession = new List<string>();
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly ConfigInstaller _configInstaller;
    private readonly VM_AssetDistributionSimulator.Factory _simulatorFactory;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Func<SaveLoader> _getSaveLoader;

    public VM_SettingsTexMesh(
        PatcherState patcherState,
        Func<SaveLoader> getSaveLoader,
        VM_Settings_General general,
        VM_SettingsBodyGen bodyGen,
        VM_SettingsOBody oBody,
        VM_BlockListUI blockListUI,
        VM_SettingsModManager modManager,
        VM_AssetPack.Factory assetPackFactory,
        VM_Subgroup.Factory subgroupFactory,
        VM_RaceGrouping.Factory raceGroupingFactory,
        IEnvironmentStateProvider environmentProvider,
        Logger logger,
        SynthEBDPaths paths,
        ConfigInstaller configInstaller,
        SettingsIO_AssetPack assetIO,
        VM_AssetDistributionSimulator.Factory simulatorFactory)
    {
        _logger = logger;
        _paths = paths;
        _configInstaller = configInstaller;
        _simulatorFactory = simulatorFactory;
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _getSaveLoader = getSaveLoader;

        Observable.CombineLatest(
                this.WhenAnyValue(x => x.bApplyFixedScripts),
                _environmentProvider.WhenAnyValue(x => x.SkyrimVersion),
                (_, _) => { return 0; })
            .Subscribe(_ => UpdateSKSESelectionVisibility()).DisposeWith(this);

        AddTrimPath = new RelayCommand(
            canExecute: _ => true,
            execute: _ => TrimPaths.Add(new TrimPath())
        );
        RemoveTrimPath = new RelayCommand(
            canExecute: _ => true,
            execute: x => TrimPaths.Remove((TrimPath)x)
        );
        ValidateAll = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                // dump view models to models so that latest are available for validation
                BodyGenConfigs bgConfigs = new();
                bgConfigs.Male = bodyGen.MaleConfigs.Select(x => x.DumpViewModelToModel()).ToHashSet();
                bgConfigs.Female = bodyGen.FemaleConfigs.Select(x => x.DumpViewModelToModel()).ToHashSet();
                Settings_OBody oBodySettings = oBody.DumpViewModelToModel();

                if (!AssetPacks.Any())
                {
                    CustomMessageBox.DisplayNotificationOK("", "There are no Asset Pack Config Files installed.");
                }
                else if (ValidateAllConfigs(bgConfigs, oBodySettings, out List<string> errors))
                {
                    CustomMessageBox.DisplayNotificationOK("", "No errors found.");
                }
                else
                {
                    _logger.LogError(String.Join(Environment.NewLine, errors));
                }
            }
        );

        AddNewAssetPackConfigFile = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var newAP = assetPackFactory();
                var newSG = subgroupFactory(general.RaceGroupingEditor.RaceGroupings, newAP.Subgroups, newAP, null, false);
                newSG.ID = "FS";
                newSG.Name = "First Subgroup";
                newAP.Subgroups.Add(newSG);
                this.AssetPacks.Add(newAP);
                AssetPresenterPrimary.AssetPack = newAP;
            }
        );

        CreateConfigArchive = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var packagerWindow = new Window_ConfigPackager();
                var packagerVM = new VM_Manifest(_logger);
                packagerWindow.DataContext = packagerVM;
                packagerWindow.ShowDialog();
            }
        );

        InstallFromArchive = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                modManager.UpdatePatcherSettings(); // make sure mod manager integration is synced w/ latest settings
                var installedConfigs = _configInstaller.InstallConfigFile();
                if (installedConfigs.Any())
                {
                    ConfigVersionUpdate(Version.v090, installedConfigs);
                    RefreshInstalledConfigs(installedConfigs);
                }
            }
        );

        InstallFromJson = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFile(_paths.AssetPackDirPath, "Config files (*.json)|*.json", "Select the config json file", out string path))
                {
                    var newAssetPack = assetIO.LoadAssetPack(path, _patcherState.GeneralSettings.RaceGroupings, patcherState.RecordTemplatePlugins, patcherState.BodyGenConfigs, out bool loadSuccess);
                    if (loadSuccess)
                    {
                        newAssetPack.FilePath = System.IO.Path.Combine(_paths.AssetPackDirPath, System.IO.Path.GetFileName(newAssetPack.FilePath)); // overwrite existing filepath so it doesn't get deleted from source
                        var newAssetPackVM = assetPackFactory();
                        newAssetPackVM.CopyInViewModelFromModel(newAssetPack, general.RaceGroupingEditor.RaceGroupings);
                        newAssetPackVM.IsSelected = true;
                        ConfigVersionUpdate(Version.v090, new() { newAssetPackVM.GroupName});
                        AssetPacks.Add(newAssetPackVM);
                    }
                }
            }
        );

        SplitScreenToggle = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (bShowSecondaryAssetPack) { bShowSecondaryAssetPack = false; }
                else { bShowSecondaryAssetPack = true; }
            }
        );

        AssetPresenterPrimary = new VM_AssetPresenter(this, logger);
        AssetPresenterSecondary = new VM_AssetPresenter(this, logger);

        SelectConfigsAll = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                foreach (var config in AssetPacks) { config.IsSelected = true; }
            }
        );

        SelectConfigsNone = new RelayCommand(
           canExecute: _ => true,
           execute: _ =>
           {
               foreach (var config in AssetPacks) { config.IsSelected = false; }
           }
       );

        SimulateDistribution = new RelayCommand(
           canExecute: _ => true,
           execute: _ =>
           {
               SimulateAssetAssignment();
           }
       );

        MenuButtonsToggle = new RelayCommand(
           canExecute: _ => true,
           execute: _ =>
           {
               bShowMenuButtons = !bShowMenuButtons;
               switch (bShowMenuButtons)
               {
                   case true: MenuButtonToggleStr = "Full Height Config Editor"; break;
                   case false: MenuButtonToggleStr = "Split Height Config Editor"; break;
               }
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
    public bool bApplyFixedScripts { get; set; } = true;
    public bool bCacheRecords { get; set; } = true;

    private static string oldSKSEversion = "< 1.5.97";
    private static string newSKSEversion = "1.5.97 or higher";
    public string SKSEversionSSE { get; set; } = newSKSEversion;
    public bool bShowSKSEversionOptions { get; set; } = false;
    public List<string> SKSEversionOptions { get; set; } = new() { newSKSEversion, oldSKSEversion };
    public bool bShowPreviewImages { get; set; } = true;
    public int MaxPreviewImageSize { get; set; } = 1024;
    public bool bShowMenuButtons { get; set; } = true;
    public string MenuButtonToggleStr { get; set; } = "Full Height Config Editor";
    public ObservableCollection<TrimPath> TrimPaths { get; set; } = new();
    public ObservableCollection<VM_AssetPack> AssetPacks { get; set; } = new();

    public RelayCommand AddTrimPath { get; }
    public RelayCommand RemoveTrimPath { get; }
    public RelayCommand ValidateAll { get; }
    public RelayCommand AddNewAssetPackConfigFile { get; }
    public RelayCommand InstallFromArchive { get; }
    public RelayCommand InstallFromJson { get; }
    public RelayCommand CreateConfigArchive { get; }
    public RelayCommand SplitScreenToggle { get; }
    public RelayCommand MenuButtonsToggle { get; }
    public string LastViewedAssetPackName { get; set; }
    public bool bShowSecondaryAssetPack { get; set; } = false;
    public VM_AssetPresenter AssetPresenterPrimary { get; set; }
    public VM_AssetPresenter AssetPresenterSecondary { get; set; }
    public string DisplayedAssetPackStr { get; set; }
    public RelayCommand SelectConfigsAll { get; }
    public RelayCommand SelectConfigsNone { get; }
    public RelayCommand SimulateDistribution { get; }

    private VM_Settings_General _generalSettingsVM { get; }

    public bool ValidateAllConfigs(BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings, out List<string> errors)
    {
        bool isValid = true;
        errors = new List<string>();
        foreach (var config in AssetPacks)
        {
            if (config.IsSelected && !config.Validate(bodyGenConfigs, oBodySettings, out var configErrors))
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
        viewModel.bApplyFixedScripts = model.bApplyFixedScripts;

        if (model.bFixedScriptsOldSKSEversion)
        {
            viewModel.SKSEversionSSE = oldSKSEversion;
        }
        else
        {
            viewModel.SKSEversionSSE = newSKSEversion;
        }

        viewModel.bCacheRecords = model.bCacheRecords;
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
        model.bApplyFixedScripts = viewModel.bApplyFixedScripts;
        model.bFixedScriptsOldSKSEversion = viewModel.SKSEversionSSE == oldSKSEversion;
        model.bCacheRecords = viewModel.bCacheRecords;
    }

    public void RefreshInstalledConfigs(List<string> installedConfigs)
    {
        InstalledConfigsInCurrentSession.AddRange(installedConfigs);
        Cursor.Current = Cursors.WaitCursor;
        _getSaveLoader().SaveAndRefreshPlugins();
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

    public void SimulateAssetAssignment()
    {
        Window_AssetDistributionSimulator simWindow = new();
        VM_AssetDistributionSimulator distributionSimulator = _simulatorFactory();
        simWindow.DataContext = distributionSimulator;
        simWindow.ShowDialog();
    }

    private void UpdateSKSESelectionVisibility()
    {
        if (bApplyFixedScripts && _environmentProvider.SkyrimVersion == Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimSE)
        {
            bShowSKSEversionOptions = true;
        }
        else
        {
            bShowSKSEversionOptions = false;
        }
    }

    public void ConfigVersionUpdate(Version version, List<string> assetPacks)
    {
        List<VM_AssetPack> toUpdate = new();

        HashSet<VM_AssetPack> toSearch = new();
        if (assetPacks.Any())
        {
            toSearch = AssetPacks.Where(x => assetPacks.Contains(x.GroupName)).ToHashSet();
        }
        else
        {
            toSearch = AssetPacks.ToHashSet();
        }

        foreach (var ap in toSearch)
        {
            if (ap.CheckForVersionUpdate(version))
            {
                toUpdate.Add(ap);
            }
        }

        if (toUpdate.Any())
        {
            string messageStr = "The following Config Files appear to have been generated prior to Version " + version + "." +
                Environment.NewLine + "Do you want to update them for compatibility with the current SynthEBD version?" +
                Environment.NewLine + string.Join(Environment.NewLine, toUpdate.Select(x => x.GroupName)) +
                Environment.NewLine + Environment.NewLine + "Press Yes unless you know what you're doing or SynthEBD may not be able to use these config files.";
            
            if(CustomMessageBox.DisplayNotificationYesNo("Config File Update", messageStr))
            {
                foreach (var ap in toUpdate)
                {
                    ap.PerformVersionUpdate(version);
                }
            }
        }
    }
}