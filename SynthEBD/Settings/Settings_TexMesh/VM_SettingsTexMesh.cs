using ReactiveUI;
using Noggog;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Forms;
using DynamicData.Binding;
using DynamicData;

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
    private readonly Func<ViewModelLoader> _getVMLoader;

    public VM_SettingsTexMesh(
        PatcherState patcherState,
        Func<ViewModelLoader> getVMLoader,
        VM_Settings_General general,
        VM_SettingsBodyGen bodyGen,
        VM_SettingsOBody oBody,
        VM_SettingsModManager modManager,
        VM_AssetPack.Factory assetPackFactory,
        VM_SubgroupPlaceHolder.Factory subgroupPlaceHolderFactory,
        VM_Subgroup.Factory subgroupFactory,
        IEnvironmentStateProvider environmentProvider,
        Logger logger,
        SynthEBDPaths paths,
        ConfigInstaller configInstaller,
        SettingsIO_AssetPack assetIO,
        VM_AssetDistributionSimulator.Factory simulatorFactory,
        VM_Manifest.Factory manifestFactory)
    {
        _logger = logger;
        _paths = paths;
        _configInstaller = configInstaller;
        _simulatorFactory = simulatorFactory;
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _getVMLoader = getVMLoader;

        AssetOrderingMenu = new(this);

        Observable.CombineLatest(
                this.WhenAnyValue(x => x.bApplyFixedScripts),
                _environmentProvider.WhenAnyValue(x => x.SkyrimVersion),
                (_, _) => { return 0; })
            .Subscribe(_ => UpdateSKSESelectionVisibility()).DisposeWith(this);

        AssetPacks
            .ToObservableChangeSet()
            .Transform(x =>
                x.WhenAnyObservable(y => y.UpdateActiveHeader)
                .Subscribe(_ => RefreshDisplayedAssetPackString()))
            .DisposeMany() // Dispose subscriptions related to removed attributes
            .Subscribe()  // Execute my instructions
            .DisposeWith(this);

        AssetPacks.ToObservableChangeSet().Subscribe(_ => RefreshDisplayedAssetPackString()).DisposeWith(this);

        AddTriggerEvent = new RelayCommand(
            canExecute: _ => true,
            execute: _ => TriggerEvents.Add(new VM_CollectionMemberString("", TriggerEvents))
        );

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
                var newAP = assetPackFactory(new AssetPack());
                var newSG = subgroupPlaceHolderFactory(new AssetPack.Subgroup { ID = "FS", Name = "First Subgroup" }, null, newAP, newAP.Subgroups);
                newAP.Subgroups.Add(newSG);
                AssetPacks.Add(newAP);
                AssetPresenterPrimary.AssetPack = newAP;
                var sVM = subgroupFactory(newSG, newAP, false);
                sVM.CopyInViewModelFromModel();
                AssetPresenterPrimary.AssetPack.DisplayedSubgroup = sVM;
            }
        );

        CreateConfigArchive = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var packagerWindow = new Window_ConfigPackager();
                var packagerVM = manifestFactory();
                packagerWindow.DataContext = packagerVM;
                packagerWindow.ShowDialog();
            }
        );

        InstallFromArchive = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                _patcherState.GeneralSettings = general.DumpViewModelToModel(); // make sure general settings are synced w/ latest settings
                modManager.UpdatePatcherSettings(); // make sure mod manager integration is synced w/ latest settings
                var installedConfigs = _configInstaller.InstallConfigFile(out bool triggerGeneralVMRefresh);
                if (installedConfigs.Any())
                {
                    ConfigUpdateAll(installedConfigs);
                    RefreshInstalledConfigs(installedConfigs);
                }
                if (triggerGeneralVMRefresh)
                {
                    general.Refresh();
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
                        List<string> installedConfigsSingle = new() { newAssetPack.GroupName };
                        var newAssetPackVM = assetPackFactory(newAssetPack);
                        newAssetPackVM.CopyInViewModelFromModel(newAssetPack, general.RaceGroupingEditor.RaceGroupings);
                        newAssetPackVM.IsSelected = true;
                        AssetPacks.Add(newAssetPackVM);
                        ConfigUpdateAll(installedConfigsSingle);

                        AssetPresenterPrimary.AssetPack = newAssetPackVM;
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
    public bool bEnableAssetReplacers { get; set; } = true;
    public bool bDisplayPopupAlerts { get; set; } = true;
    public bool bGenerateAssignmentLog { get; set; } = true;
    public bool bEasyNPCCompatibilityMode { get; set; } = true;
    public bool bApplyFixedScripts { get; set; } = true;
    public bool bCacheRecords { get; set; } = true;
    public bool bLegacyEBDMode { get; set; } = true;
    public bool bNewEBDModeVerbose { get; set; } = false;

    private static string oldSKSEversion = "< 1.5.97";
    private static string newSKSEversion = "1.5.97 or higher";
    public string SKSEversionSSE { get; set; } = newSKSEversion;
    public bool bShowSKSEversionOptions { get; set; } = false;
    public List<string> SKSEversionOptions { get; set; } = new() { newSKSEversion, oldSKSEversion };
    public bool bShowPreviewImages { get; set; } = true;
    public int MaxPreviewImageSize { get; set; } = 1024;
    public bool bShowMenuButtons { get; set; } = true;
    public string MenuButtonToggleStr { get; set; } = "Full Height Config Editor";
    public bool bPatchArmors { get; set; } = true;
    public bool bPatchSkinAltTextures { get; set; } = true;
    public ObservableCollection<TrimPath> TrimPaths { get; set; } = new();
    public ObservableCollection<VM_AssetPack> AssetPacks { get; set; } = new();

    public ObservableCollection<VM_CollectionMemberString> TriggerEvents { get; set; } = new();

    public RelayCommand AddTrimPath { get; }
    public RelayCommand RemoveTrimPath { get; }
    public RelayCommand ValidateAll { get; }
    public RelayCommand AddNewAssetPackConfigFile { get; }
    public RelayCommand InstallFromArchive { get; }
    public RelayCommand InstallFromJson { get; }
    public RelayCommand CreateConfigArchive { get; }
    public RelayCommand SplitScreenToggle { get; }
    public RelayCommand MenuButtonsToggle { get; }
    public RelayCommand AddTriggerEvent { get; }
    public string LastViewedAssetPackName { get; set; }
    public bool bShowSecondaryAssetPack { get; set; } = false;
    public VM_AssetPresenter AssetPresenterPrimary { get; set; }
    public VM_AssetPresenter AssetPresenterSecondary { get; set; }
    public string DisplayedAssetPackStr { get; set; }
    public RelayCommand SelectConfigsAll { get; }
    public RelayCommand SelectConfigsNone { get; }
    public RelayCommand SimulateDistribution { get; }

    public VM_AssetOrderingMenu AssetOrderingMenu { get; set; }

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

    public void CopyInViewModelFromModel(Settings_TexMesh model)
    {
        if (model == null)
        {
            return;
        }
        _logger.LogStartupEventStart("Loading TexMesh Settings UI");
        bChangeNPCTextures = model.bChangeNPCTextures;
        bChangeNPCMeshes = model.bChangeNPCMeshes;
        bChangeNPCHeadParts = model.bChangeNPCHeadParts;
        bApplyToNPCsWithCustomSkins = model.bApplyToNPCsWithCustomSkins;
        bApplyToNPCsWithCustomFaces = model.bApplyToNPCsWithCustomFaces;
        bEnableAssetReplacers = model.bEnableAssetReplacers;
        bForceVanillaBodyMeshPath = model.bForceVanillaBodyMeshPath;
        bDisplayPopupAlerts = model.bDisplayPopupAlerts;
        bGenerateAssignmentLog = model.bGenerateAssignmentLog;
        bShowPreviewImages = model.bShowPreviewImages;
        MaxPreviewImageSize = model.MaxPreviewImageSize;
        TrimPaths = new ObservableCollection<TrimPath>(model.TrimPaths);
        LastViewedAssetPackName = model.LastViewedAssetPack;
        bEasyNPCCompatibilityMode = model.bEasyNPCCompatibilityMode;
        bApplyFixedScripts = model.bApplyFixedScripts;
        bLegacyEBDMode = model.bLegacyEBDMode;
        bNewEBDModeVerbose = model.bNewEBDModeVerbose;
        TriggerEvents = VM_CollectionMemberString.InitializeObservableCollectionFromICollection(model.TriggerEvents);

        if (model.bFixedScriptsOldSKSEversion)
        {
            SKSEversionSSE = oldSKSEversion;
        }
        else
        {
            SKSEversionSSE = newSKSEversion;
        }

        bCacheRecords = model.bCacheRecords;
        bPatchArmors = model.bPatchArmors;
        bPatchSkinAltTextures = model.bPatchSkinAltTextures;
        _logger.LogStartupEventEnd("Loading TexMesh Settings UI");
    }

    public Settings_TexMesh DumpViewModelToModel()
    {
        Settings_TexMesh model = new();
        model.bChangeNPCTextures = bChangeNPCTextures;
        model.bChangeNPCMeshes = bChangeNPCMeshes;
        model.bChangeNPCHeadParts = bChangeNPCHeadParts;
        model.bApplyToNPCsWithCustomSkins = bApplyToNPCsWithCustomSkins;
        model.bApplyToNPCsWithCustomFaces = bApplyToNPCsWithCustomFaces;
        model.bEnableAssetReplacers = bEnableAssetReplacers;
        model.bForceVanillaBodyMeshPath = bForceVanillaBodyMeshPath;
        model.bDisplayPopupAlerts = bDisplayPopupAlerts;
        model.bGenerateAssignmentLog = bGenerateAssignmentLog;
        model.bShowPreviewImages = bShowPreviewImages;
        model.MaxPreviewImageSize = MaxPreviewImageSize;
        model.TrimPaths = TrimPaths.ToList();
        model.SelectedAssetPacks = AssetPacks.Where(x => x.IsSelected).Select(x => x.GroupName).ToHashSet();
        if (AssetPresenterPrimary.AssetPack is not null)
        {
            model.LastViewedAssetPack = AssetPresenterPrimary.AssetPack.GroupName;
        }
        model.bEasyNPCCompatibilityMode = bEasyNPCCompatibilityMode;
        model.bApplyFixedScripts = bApplyFixedScripts;
        model.bFixedScriptsOldSKSEversion = SKSEversionSSE == oldSKSEversion;
        model.bCacheRecords = bCacheRecords;
        model.bLegacyEBDMode = bLegacyEBDMode;
        model.bNewEBDModeVerbose = bNewEBDModeVerbose;
        model.AssetOrder = AssetOrderingMenu.DumpToModel();
        model.TriggerEvents = TriggerEvents.Select(x => x.Content).ToList();
        model.bPatchArmors = bPatchArmors;
        model.bPatchSkinAltTextures = bPatchSkinAltTextures;
        return model;
    }

    public void RefreshInstalledConfigs(List<string> installedConfigs)
    {
        InstalledConfigsInCurrentSession.AddRange(installedConfigs);
        Cursor.Current = Cursors.WaitCursor;
        _getVMLoader().SaveAndRefreshPlugins();
        foreach (var newConfig in AssetPacks.Where(x => InstalledConfigsInCurrentSession.Contains(x.GroupName)).ToArray())
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

    public void ConfigUpdateAll(List<string> assetPacks)
    {
        foreach (Version version in Enum.GetValues(typeof(Version)))
        {
            ConfigVersionUpdate(version, assetPacks);
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
            if (ap.VersionUpdate(version, UpdateMode.Check))
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
                    ap.VersionUpdate(version, UpdateMode.Perform);
                }
            }
        }
    }
}