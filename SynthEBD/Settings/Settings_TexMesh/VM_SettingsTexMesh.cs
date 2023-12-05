using ReactiveUI;
using Noggog;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Forms;
using DynamicData.Binding;
using DynamicData;
using Mutagen.Bethesda.Plugins;

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

        _environmentProvider.WhenAnyValue(x => x.LoadOrder)
            .Subscribe(x => LoadOrder = x.Where(y => y.Value != null && y.Value.Enabled).Select(x => x.Value.ModKey)).DisposeWith(this);

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

        general.WhenAnyValue(x => x.bShowTroubleshootingSettings).Subscribe(x => bShowTroubleshootingSettings = x).DisposeWith(this);

        AddStrippedWNAM = new RelayCommand(
            canExecute: _ => true,
            execute: _ => StrippedSkinWNAMs.Add(new VM_CollectionMemberString("", StrippedSkinWNAMs))
        );

        ImportStrippedWNAMsFromMod = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var mod = _environmentProvider.LoadOrder.PriorityOrder.Where(x => x.ModKey == SelectedStrippedWNAMmodKey).FirstOrDefault()?.Mod ?? null;
                List<string> noEditorIDs = new();
                StrippedSkinWNAMsHistory.Add(new(StrippedSkinWNAMs)); // shallow copy
                bool added = false;
                if (mod != null)
                {
                    foreach (var armorGetter in mod.Armors)
                    {
                        if (armorGetter.BodyTemplate != null && armorGetter.BodyTemplate.FirstPersonFlags.HasFlag(Mutagen.Bethesda.Skyrim.BipedObjectFlag.Body))
                        {
                            var edid = EditorIDHandler.GetEditorIDSafely(armorGetter);
                            if (edid.Contains("(No EditorID)"))
                            {
                                noEditorIDs.Add(armorGetter.FormKey.ToString());
                            }
                            else
                            {
                                StrippedSkinWNAMs.Add(new(edid, StrippedSkinWNAMs));
                                added = true;
                            }
                        }
                    }
                }

                if (!added)
                {
                    StrippedSkinWNAMsHistory.RemoveAt(StrippedSkinWNAMsHistory.Count - 1);
                }
            });

        UndoImportStrippedWNAMsFromMod = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var lastHistory = StrippedSkinWNAMsHistory.LastOrDefault();
                if (lastHistory != null)
                {
                    StrippedSkinWNAMs.Clear();
                    foreach (var item in lastHistory)
                    {
                        StrippedSkinWNAMs.Add(item);
                    }
                    StrippedSkinWNAMsHistory.RemoveAt(StrippedSkinWNAMsHistory.Count - 1);
                }
            });

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
                    MessageWindow.DisplayNotificationOK("", "There are no Asset Pack Config Files installed.");
                }
                else if (ValidateAllConfigs(bgConfigs, oBodySettings, out List<string> errors))
                {
                    MessageWindow.DisplayNotificationOK("", "No errors found.");
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

        InstallFromArchive = ReactiveCommand.CreateFromTask(
           execute: async _ =>
           {
               _patcherState.GeneralSettings = general.DumpViewModelToModel(); // make sure general settings are synced w/ latest settings
               modManager.UpdatePatcherSettings(); // make sure mod manager integration is synced w/ latest settings
               (var installedConfigs, var triggerGeneralVMRefresh) = await _configInstaller.InstallConfigFile();
               if (installedConfigs.Any())
               {
                   ConfigUpdateAll(installedConfigs);
                   RefreshInstalledConfigs(installedConfigs);
               }
               if (triggerGeneralVMRefresh)
               {
                   general.Refresh();
               }
           });

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
    public ObservableCollection<VM_CollectionMemberString> StrippedSkinWNAMs { get; set; } = new();
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

    public IEnumerable<ModKey> LoadOrder { get; private set; }
    public RelayCommand AddTrimPath { get; }
    public RelayCommand RemoveTrimPath { get; }
    public RelayCommand ValidateAll { get; }
    public RelayCommand AddNewAssetPackConfigFile { get; }
    public IReactiveCommand InstallFromArchive { get; }
    public RelayCommand InstallFromJson { get; }
    public RelayCommand CreateConfigArchive { get; }
    public RelayCommand SplitScreenToggle { get; }
    public RelayCommand MenuButtonsToggle { get; }
    public RelayCommand AddTriggerEvent { get; }
    public RelayCommand AddStrippedWNAM { get; }
    public RelayCommand ImportStrippedWNAMsFromMod { get; }
    public RelayCommand UndoImportStrippedWNAMsFromMod { get; }
    public ModKey SelectedStrippedWNAMmodKey { get; set; }
    public string LastViewedAssetPackName { get; set; }
    public bool bShowSecondaryAssetPack { get; set; } = false;
    public VM_AssetPresenter AssetPresenterPrimary { get; set; }
    public VM_AssetPresenter AssetPresenterSecondary { get; set; }
    public string DisplayedAssetPackStr { get; set; }
    public RelayCommand SelectConfigsAll { get; }
    public RelayCommand SelectConfigsNone { get; }
    public RelayCommand SimulateDistribution { get; }

    public VM_AssetOrderingMenu AssetOrderingMenu { get; set; }

    private List<ObservableCollection<VM_CollectionMemberString>> StrippedSkinWNAMsHistory = new();
    public bool bShowTroubleshootingSettings { get; set; } = false;

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
        StrippedSkinWNAMs = VM_CollectionMemberString.InitializeObservableCollectionFromICollection(model.StrippedSkinWNAMs);
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
        model.StrippedSkinWNAMs = StrippedSkinWNAMs.Select(x => x.Content).ToHashSet();
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
        distributionSimulator.Reinitialize();
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

            if (MessageWindow.DisplayNotificationYesNo("Config File Update", messageStr))
            {
                foreach (var ap in toUpdate)
                {
                    ap.VersionUpdate(version, UpdateMode.Perform);
                }
            }
        }
    }

    public List<string> ResetTroubleShootingToDefault(bool preparationMode)
    {
        var changes = new List<string>();

        if (!bChangeNPCTextures)
        {
            if (preparationMode)
            {
                changes.Add("Allow config files to change NPC textures: False --> True");
            }
            else
            {
                bChangeNPCTextures = true;
            }
        }

        if (!bChangeNPCMeshes)
        {
            if (preparationMode)
            {
                changes.Add("Allow config files to change NPC meshes: False --> True");
            }
            else
            {
                bChangeNPCMeshes = true;
            }
        }

        if (!bChangeNPCHeadParts)
        {
            if (preparationMode)
            {
                changes.Add("Allow config files to change NPC head parts: False --> True");
            }
            else
            {
                bChangeNPCHeadParts = true;
            }
        }

        HashSet<string> defaultSkinReplacements = _patcherState.TexMeshSettings.GetDefaultValue("StrippedSkinWNAMs");

        foreach (var skinEditorID in defaultSkinReplacements)
        {
            if (!StrippedSkinWNAMs.Select(x => x.Content).Contains(skinEditorID))
            {
                if (preparationMode)
                {
                    changes.Add("Total Skin Replacements: Add \"" + skinEditorID + "\"");
                }
                else
                {
                    StrippedSkinWNAMs.Add(new(skinEditorID, StrippedSkinWNAMs));
                }
            }
        }

        for (int i = 0; i < StrippedSkinWNAMs.Count; i++)
        {
            if (!defaultSkinReplacements.Contains(StrippedSkinWNAMs[i].Content))
            {
                if (preparationMode)
                {
                    changes.Add("Total Skin Replacements: Remove \"" + StrippedSkinWNAMs[i].Content + "\"");
                }
                else
                {
                    StrippedSkinWNAMs.RemoveAt(i);
                    i--;
                }
            }
        }

        if (!bEasyNPCCompatibilityMode)
        {
            if (preparationMode)
            {
                changes.Add("Easy NPC Compatibility Mode: False --> True");
            }
            else
            {
                bEasyNPCCompatibilityMode = true;
            }
        }

        if (!bApplyFixedScripts)
        {
            if (preparationMode)
            {
                changes.Add("Fix EBD Script: False --> True");
            }
            else
            {
                bApplyFixedScripts = true;
            }
        }

        if (bLegacyEBDMode)
        {
            if (preparationMode)
            {
                changes.Add("Use Original EBD Script: True --> False");
            }
            else
            {
                bLegacyEBDMode = false;
            }
        }

        List<string> defaultRefreshTriggers = _patcherState.TexMeshSettings.GetDefaultValue("TriggerEvents");

        foreach (var name in defaultRefreshTriggers)
        {
            if (!TriggerEvents.Select(x => x.Content).Contains(name))
            {
                if (preparationMode)
                {
                    changes.Add("Face Texture Refresh Triggers: Add \"" + name + "\"");
                }
                else
                {
                    TriggerEvents.Add(new(name, TriggerEvents));
                }
            }
        }

        for (int i = 0; i < TriggerEvents.Count; i++)
        {
            if (!defaultRefreshTriggers.Contains(TriggerEvents[i].Content))
            {
                if (preparationMode)
                {
                    changes.Add("Face Texture Refresh Triggers: Remove \"" + TriggerEvents[i].Content + "\"");
                }
                else
                {
                    TriggerEvents.RemoveAt(i);
                    i--;
                }
            }
        }

        if (bNewEBDModeVerbose)
        {
            if (preparationMode)
            {
                changes.Add("Face Textre Script Verbose Mode: True --> False");
            }
            else
            {
                bNewEBDModeVerbose = false;
            }
        }

        if (!bCacheRecords)
        {
            if (preparationMode)
            {
                changes.Add("Cache Generated Records: False --> True");
            }
            else
            {
                bCacheRecords = true;
            }
        }

        if (!bPatchSkinAltTextures)
        {
            if (preparationMode)
            {
                changes.Add("Match NPC Skin Alternate Textures: False --> True");
            }
            else
            {
                bPatchSkinAltTextures = true;
            }
        }

        if (!bPatchArmors)
        {
            if (preparationMode)
            {
                changes.Add("Match NPC Armor Textures: False --> True");
            }
            else
            {
                bPatchArmors = true;
            }
        }

        List<TrimPath> defaultTrimPaths = _patcherState.TexMeshSettings.GetDefaultValue("TrimPaths");
        foreach (var trimPath in defaultTrimPaths)
        {
            if (!TrimPaths.Where(x => x.PathToTrim == trimPath.PathToTrim && x.Extension == trimPath.Extension).Any())
            {
                if (preparationMode)
                {
                    changes.Add("Asset Path Trimming: Add [" + trimPath.PathToTrim + ": " + trimPath.Extension + "]");
                }
                else
                {
                    TrimPaths.Add(new() { Extension = trimPath.Extension, PathToTrim = trimPath.PathToTrim});
                }
            }
        }

        return changes;
    }
}