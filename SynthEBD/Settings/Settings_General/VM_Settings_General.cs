using System.Collections.ObjectModel;
using System.IO;
using System.Reactive.Linq;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// View model for the General settings tab, backing the <see cref="Settings_General"/> model.
/// This is the broadest settings VM: it owns the global feature toggles (which axes to patch,
/// consistency, NPC linking), the shared <see cref="VM_AttributeGroupMenu"/> and
/// <see cref="VM_RaceGroupingEditor"/> consumed by other tabs, patchable races / race aliases,
/// output folder and portable-settings folder selection, appearance-merger (EasyNPC/NPC2)
/// integration, troubleshooting settings, Character Viewer lighting/preview state, and the
/// preview-NPC mappings. Hosts the standalone environment settings VM when run standalone.
/// </summary>
public class VM_Settings_General : VM, IHasAttributeGroupMenu, IHasRaceGroupingEditor
{
    public IEnvironmentStateProvider _environmentProvider { get; }
    public PatcherSettingsSourceProvider SettingsSourceProvider { get; }
    private readonly Logger _logger;
    private bool _bFirstRun { get; set; } = false;
    private readonly SettingsIO_General _generalIO;
    private readonly PatcherState _patcherState;
    private readonly VM_RaceAlias.Factory _aliasFactory;
    private readonly VM_RaceGroupingEditor.Factory _raceGroupingEditorFactory;
    private readonly VM_LinkedNPCGroup.Factory _linkedNPCFactory;
    private readonly FirstLaunch _firstLaunch;
    private readonly SynthEBDPaths _paths;
    /// <summary>
    /// Builds the attribute-group menu, race-grouping editor, and detailed-report selector;
    /// mirrors the environment link cache and load order; and wires the many General-tab
    /// <see cref="RelayCommand"/>s (add race alias / linked NPC group, output and portable
    /// settings folder selection, EasyNPC/NPC2 path selection, troubleshooting toggle/reset)
    /// plus guard subscriptions (head-part and validation warnings, first-run launch, tooltips).
    /// </summary>
    public VM_Settings_General(
        VM_SettingsModManager modManagerSettings,
        PatcherSettingsSourceProvider settingsProvider,
        Logger logger,
        VM_AttributeGroupMenu.Factory attributeGroupFactory,
        VM_RaceAlias.Factory aliasFactory,
        VM_RaceGroupingEditor.Factory raceGroupingEditorFactory,
        VM_RaceGrouping.Factory groupingFactory,
        VM_LinkedNPCGroup.Factory linkedNPCFactory,
        VM_DetailedReportNPCSelector.Factory detailedReportNPCSelectorFactory,
        SettingsIO_General generalIO,
        PatcherState patcherState,
        IEnvironmentStateProvider environmentProvider,
        FirstLaunch firstLaunch,
        SynthEBDPaths paths,
        VM_NifPreviewNpcSettings previewNpcSettings,
        Func<VM_SettingsTexMesh> getTexMesh,
        Func<VM_SettingsOBody> getOBody
        )
    {
        _environmentProvider = environmentProvider;
        IsStandalone = environmentProvider.RunMode == EnvironmentMode.Standalone;
        SettingsSourceProvider = settingsProvider;
        _logger = logger;
        _generalIO = generalIO;
        _patcherState = patcherState;
        _firstLaunch = firstLaunch;
        _aliasFactory = aliasFactory;
        _raceGroupingEditorFactory = raceGroupingEditorFactory;
        _linkedNPCFactory = linkedNPCFactory;
        _paths = paths;
        PreviewNpcs = previewNpcSettings;

        if (IsStandalone)
        {
            EnvironmentSettingsVM = new(environmentProvider);
        }

        AttributeGroupMenu = attributeGroupFactory(null, false);
        RaceGroupingEditor = _raceGroupingEditorFactory(this, false);
        DetailedReportSelector = detailedReportNPCSelectorFactory(AttributeGroupMenu, RaceGroupingEditor);

        this.WhenAnyValue(x => x.bShowToolTips)
            .Subscribe(x => TooltipController.Instance.DisplayToolTips = x).DisposeWith(this);

        this.WhenAnyValue(x => x.SelectedThemeName)
            .Subscribe(x => ThemeManager.ApplyTheme(x)).DisposeWith(this);

        this.WhenAnyValue(x => x.Close7ZipWhenFinished)
            .Subscribe(x => {
                if (_patcherState.GeneralSettings != null)
                {
                    _patcherState.GeneralSettings.Close7ZipWhenFinished = x;
                }
            }).DisposeWith(this);

        environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
        
        _environmentProvider.WhenAnyValue(x => x.LoadOrder)
            .Subscribe(x => LoadOrder = x)
            .DisposeWith(this);

        this.WhenAnyValue(x => x.bChangeHeadParts).Subscribe(y =>
        {
            if (!IsCurrentlyLoading && y && !_bHeadPartWarningDisplayed)
            {
                if(!MessageWindow.DisplayNotificationYesNo("Warning", "Head Part functionality is experimental and some Head Parts can change the faces of custom face sculpted NPCs. Are you sure you want to enable headpart distribution?"))
                {
                    bChangeHeadParts = false;
                }
                else
                {
                    _bHeadPartWarningDisplayed = true;
                }
            }
        }).DisposeWith(this);

        // Update visibility properties when AppearanceMergerType changes
        this.WhenAnyValue(x => x.AppearanceMergerType)
            .Subscribe(x =>
            {
                ShowEasyNPCPath = x == AppearanceMergeType.EasyNPC;
                ShowNPC2Path = x == AppearanceMergeType.NPC2;
            }).DisposeWith(this);

        AddRaceAlias = new RelayCommand(
            canExecute: _ => true,
            execute: _ => raceAliases.Add(_aliasFactory(new RaceAlias(), this))
        );

        AddLinkedNPCNameExclusion = new RelayCommand(
            canExecute: _ => true,
            execute: _ => LinkedNameExclusions.Add(new VM_CollectionMemberString("", this.LinkedNameExclusions))
        );

        AddLinkedNPCGroup = new RelayCommand(
            canExecute: _ => true,
            execute: _ => LinkedNPCGroups.Add(_linkedNPCFactory())
        );

        RemoveLinkedNPCGroup = new RelayCommand(
            canExecute: _ => true,
            execute: x => LinkedNPCGroups.Remove((VM_LinkedNPCGroup)x)
        );

        SelectOutputFolder = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFolder(environmentProvider.DataFolderPath, out var tmpFolder))
                    {
                        OutputDataFolder = tmpFolder;
                    }
                }
                );

        ClearOutputFolder = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    OutputDataFolder = "";
                }
                );

        SelectPortableSettingsFolder = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                string initDir = "";
                if (modManagerSettings.ModManagerType == ModManager.ModOrganizer2)
                {
                    if (!string.IsNullOrEmpty(modManagerSettings.MO2IntegrationVM.ModFolderPath) && Directory.Exists(modManagerSettings.MO2IntegrationVM.ModFolderPath))
                    {
                        initDir = modManagerSettings.MO2IntegrationVM.ModFolderPath;
                    }
                }
                else if (modManagerSettings.ModManagerType == ModManager.Vortex)
                {
                    if (!string.IsNullOrEmpty(modManagerSettings.VortexIntegrationVM.StagingFolderPath) && Directory.Exists(modManagerSettings.VortexIntegrationVM.StagingFolderPath))
                    {
                        initDir = modManagerSettings.VortexIntegrationVM.StagingFolderPath;
                    }
                }

                if (IO_Aux.SelectFolder(initDir, out string selectedPath))
                {
                    if (!string.Equals(new DirectoryInfo(selectedPath).Name, "SynthEBD", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageWindow.DisplayNotificationOK("Invalid Directory", "The folder name must be \"SynthEBD\"");
                    }
                    else
                    {
                        SettingsSourceProvider.PortableSettingsFolder = selectedPath;
                    }
                }
            }
        );

        ClearPortableSettingsFolder = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (string.IsNullOrWhiteSpace(SettingsSourceProvider.PortableSettingsFolder))
                {
                    MessageWindow.DisplayNotificationOK("", "There is no settings folder path to clear.");
                    return;
                }
                SettingsSourceProvider.PortableSettingsFolder = String.Empty;
            }
        );

        SelectEasyNPCProfile= new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFile(environmentProvider.DataFolderPath, "Text Files (*.txt)|*.txt", "Select your exported EasyNPC Profile",  out string path))
                    {
                        EasyNPCprofilePath = path;
                    }
                }
                );

        ClearEasyNPCProfile = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                EasyNPCprofilePath = string.Empty;
            }
        );

        SelectNPC2Token = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFile(environmentProvider.DataFolderPath, "JSON Files (*.json)|*.json", "Select your NPC_Token.json file", out string path))
                {
                    NPC2TokenPath = path;
                }
            }
        );

        ClearNPC2Token = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                NPC2TokenPath = string.Empty;
            }
        );

        // One-time warning when entering Troubleshoot mode via the global mode selector (the
        // successor of the old Show Troubleshooting Settings toggle's confirmation); declining
        // reverts to Customize. Only fires on USER-initiated changes: the IsCurrentlyLoading guard
        // (same pattern as the head-part warning) keeps the settings-load migration from popping a
        // modal at startup, and the harness suppresses it so automated sweeps can't block.
        UiModeController.Instance.WhenAnyValue(x => x.DisplayMode).Subscribe(mode =>
        {
            if (mode == UiDisplayMode.Troubleshoot
                && !IsCurrentlyLoading
                && !_bTroubleshootingWarningDisplayed
                && !UiModeController.Instance.SuppressModeChangeWarnings)
            {
                if (MessageWindow.DisplayNotificationYesNo("Are you sure?", "These settings are only meant for troubleshooting. Do not change them unless you know what you're doing or have been instructed to do so."))
                {
                    _bTroubleshootingWarningDisplayed = true;
                }
                else
                {
                    UiModeController.Instance.DisplayMode = UiDisplayMode.Customize;
                }
            }
        }).DisposeWith(this);

        ResetTroubleShootingToDefaultCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var changes = ResetTroubleShootingToDefault(true);
                changes.AddRange(getTexMesh().ResetTroubleShootingToDefault(true));
                changes.AddRange(getOBody().MiscUI.ResetTroubleShootingToDefault(true));

                if (!changes.Any())
                {
                    MessageWindow.DisplayNotificationOK("Reverting Settings", "All troubleshooting settings are already at their default values.");
                }
                else if (MessageWindow.DisplayNotificationYesNo("Are you sure?", string.Join(Environment.NewLine, changes)))
                {
                    ResetTroubleShootingToDefault(false);
                    getTexMesh().ResetTroubleShootingToDefault(false);
                    getOBody().MiscUI.ResetTroubleShootingToDefault(false);
                }
            }
        );

        this.WhenAnyValue(x => x._bFirstRun).Subscribe(x =>
        {
            if (x)
            {
                _firstLaunch.OnFirstLaunch();
            }
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.OutputDataFolder).Subscribe(x =>
        {
            if (!x.IsNullOrEmpty())
            {
                _paths.OutputDataFolder = x;
            }
            else
            {
                _paths.OutputDataFolder = _environmentProvider.DataFolderPath;
            }
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.DisableValidation).Skip(1).Subscribe(y =>
        {
            if (!IsCurrentlyLoading && y)
            {
                if (!MessageWindow.DisplayNotificationYesNo("Are you sure?", "SynthEBD can ignore validation, but Skyrim itself cannot. If you disable validation, you may run into issues such as NPCs missing textures and turning blue, or even Papyrus script issues. This option is mainly intended for config file devs to share and troubleshoot configs without having to download the corresponding large texture mods. Are you sure you meant to disable validation?"))
                {
                    DisableValidation = false;
                }
            }
        }).DisposeWith(this);
    }

    public VM_Settings_Environment EnvironmentSettingsVM { get; set; }
    public string OutputDataFolder { get; set; } = "";
    public AppearanceMergeType AppearanceMergerType { get; set; } = AppearanceMergeType.None;
    public string EasyNPCprofilePath { get; set; } = "";
    public string NPC2TokenPath { get; set; } = "";
    public bool ShowEasyNPCPath { get; set; } = false;
    public bool ShowNPC2Path { get; set; } = false;
    public bool bShowToolTips { get; set; } = true;
    public string SelectedThemeName { get; set; } = ThemeManager.DefaultThemeName;
    public ObservableCollection<string> AvailableThemes { get; set; } = new(ThemeManager.GetAvailableThemes());
    public bool bChangeMeshesOrTextures { get; set; } = true;
    public BodyShapeSelectionMode BodySelectionMode { get; set; } = BodyShapeSelectionMode.None;
    public BodyShapeSelectionMode LastBodySelectionMode { get; set; } = BodyShapeSelectionMode.BodySlide;
    public BodySlideSelectionMode BSSelectionMode { get; set; } = BodySlideSelectionMode.OBody;
    public bool ExcludePlayerCharacter { get; set; } = true;
    public bool ExcludePresets { get; set; } = true;
    public bool bChangeHeight { get; set; } = true;
    public bool bChangeHeadParts { get; set; } = false;
    public bool bHeadPartsExcludeCustomHeads { get; set; } = true;
    public bool bEnableConsistency { get; set; } = true;
    public bool AutoSplitOutput { get; set; } = true;
    public bool bLinkNPCsWithSameName { get; set; } = true;
    public ObservableCollection<VM_CollectionMemberString> LinkedNameExclusions { get; set; } = new();
    public ObservableCollection<VM_LinkedNPCGroup> LinkedNPCGroups { get; set; } = new();
    public bool bVerboseModeAssetsNoncompliant { get; set; } = false;
    public bool bVerboseModeAssetsAll { get; set; } = false;
    public ObservableCollection<FormKey> verboseModeNPClist { get; set; } = new();
    public bool VerboseModeDetailedAttributes { get; set; } = false;
    public bool Close7ZipWhenFinished { get; set; } = true;
    public ObservableCollection<FormKey> patchableRaces { get; set; } = new();
    public VM_RaceGroupingEditor RaceGroupingEditor { get; set; }
    public bool OverwritePluginRaceGroups { get; set; } = true;
    public ObservableCollection<VM_RaceAlias> raceAliases { get; set; } = new();
    public RelayCommand AddRaceAlias { get; }
    public VM_AttributeGroupMenu AttributeGroupMenu { get; }
    public bool OverwritePluginAttGroups { get; set; } = true;
    public bool DisableValidation { get; set; } = false;
    public bool bUseDetailedReportSelection { get; set; } = false;
    public VM_DetailedReportNPCSelector DetailedReportSelector {get; set;}
    public bool IsCurrentlyLoading { get; set; } = false;
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> RacePickerFormKeys { get; } = typeof(IRaceGetter).AsEnumerable();
    public IEnumerable<Type> NPCPickerFormKeys { get; } = typeof(INpcGetter).AsEnumerable();
    public RelayCommand AddLinkedNPCNameExclusion { get; }
    public RelayCommand AddLinkedNPCGroup { get; }
    public RelayCommand RemoveLinkedNPCGroup { get; }
    public RelayCommand SelectOutputFolder { get; }
    public RelayCommand ClearOutputFolder { get; }
    public RelayCommand SelectPortableSettingsFolder { get; }
    public RelayCommand ClearPortableSettingsFolder { get; }
    public RelayCommand SelectEasyNPCProfile { get; }
    public RelayCommand ClearEasyNPCProfile { get; }
    public RelayCommand SelectNPC2Token { get; }
    public RelayCommand ClearNPC2Token { get; }
    public bool IsStandalone { get; set; }
    public bool bFilterNPCsByArmature { get; set; } = true;
    private bool _bTroubleshootingWarningDisplayed { get; set; } = false;
    public RelayCommand ResetTroubleShootingToDefaultCommand { get; }
    private bool _bHeadPartWarningDisplayed { get; set; } = false;
    public ObservableCollection<ModKey> BlockedModsFromImport { get; set; } = new();
    public bool bShow3DPreview { get; set; } = true;
    public double SpecificNPCPreviewerWidth { get; set; } = 525;
    public double ConsistencyPreviewerWidth { get; set; } = 525;
    public string CharacterViewerLightingLayout { get; set; } = "";
    public string CharacterViewerLightingColorScheme { get; set; } = "";
    public ObservableCollection<CharacterViewerLightingLayout> UserLightingLayouts { get; set; } = new();
    public ObservableCollection<CharacterViewerLightingColorScheme> UserLightingColorSchemes { get; set; } = new();
    public bool CharacterViewerVerboseLog { get; set; } = false;
    public bool CharacterViewerRenderMissingTextureAsWireframe { get; set; } = true;
    public bool CharacterViewerEnableToneMapping { get; set; } = true;
    public bool CharacterViewerEnableShadows { get; set; } = true;
    public bool CharacterViewerEnableAmbientOcclusion { get; set; } = true;
    public float CharacterViewerSsaoRadius { get; set; } = 4.0f;
    public float CharacterViewerSsaoBias { get; set; } = 0.05f;
    public float CharacterViewerSsaoIntensity { get; set; } = 1.5f;
    public float CharacterViewerSsaoThickness { get; set; } = 1.5f;
    public float CharacterViewerSsaoHairGap { get; set; } = 0.8f;
    public bool CharacterViewerEnableEyeCatchlight { get; set; } = true;
    public float CharacterViewerSubsurfaceStrength { get; set; } = 1.0f;
    public float CharacterViewerVignetteRadius { get; set; } = 0.7f;
    public float CharacterViewerVignetteIntensity { get; set; } = 0.3f;
    public float CharacterViewerSkinSaturationBoost { get; set; } = 1.0f;
    public float CharacterViewerExposure { get; set; } = 1.0f;
    public bool CharacterViewerTonemapHairRelief { get; set; } = true;
    public bool CharacterViewerDaylightBoost { get; set; } = true;
    public float CharacterViewerDaylightBoostIntensity { get; set; } = 1.1f;
    public bool CharacterViewerEnableBloom { get; set; } = true;
    public float CharacterViewerBloomIntensity { get; set; } = 0.7f;
    public RenderCacheMode CacheMode { get; set; } = RenderCacheMode.PercentFreeRam;
    public double CacheFixedBudgetGB { get; set; } = 4.0;
    public double CacheFreeRamPercent { get; set; } = 85.0;
    public IEnumerable<RenderCacheMode> CacheModeChoices { get; } =
        Enum.GetValues(typeof(RenderCacheMode)).Cast<RenderCacheMode>();
    public string TextureLoadStrategy { get; set; } = "BmpStream";
    public VM_NifPreviewNpcSettings PreviewNpcs { get; set; }
    public ILoadOrderGetter LoadOrder { get; private set; }

    /// <summary>
    /// Model → VM: loads every General setting from <paramref name="model"/> into the VM
    /// (toggles, race/alias/grouping/attribute collections, preview NPCs, Character Viewer
    /// lighting/preview state). Sets <see cref="IsCurrentlyLoading"/> around the copy so guard
    /// subscriptions don't fire confirmation prompts during load.
    /// </summary>
    public void CopyInFromModel(Settings_General model, VM_RaceAlias.Factory aliasFactory, VM_LinkedNPCGroup.Factory linkedNPCFactory, ILinkCache linkCache)
    {
        if (model == null)
        {
            return;
        }
        _logger.LogStartupEventStart("Loading General Settings UI");
        IsCurrentlyLoading = true;

        OutputDataFolder = model.OutputDataFolder;
        AppearanceMergerType = model.AppearanceMergerType;
        EasyNPCprofilePath = model.EasyNPCprofilePath;
        NPC2TokenPath = model.NPC2TokenPath;
        bShowToolTips = model.bShowToolTips;
        SelectedThemeName = model.ThemeName;
        bChangeMeshesOrTextures = model.bChangeMeshesOrTextures;
        BodySelectionMode = model.BodySelectionMode;
        LastBodySelectionMode = model.LastBodySelectionMode == BodyShapeSelectionMode.None ? BodyShapeSelectionMode.BodySlide : model.LastBodySelectionMode;
        BSSelectionMode = model.BSSelectionMode;
        bChangeHeight = model.bChangeHeight;
        _bHeadPartWarningDisplayed = model.bHeadPartWarningDisplayed; // has to copy in before bChangeHeadParts
        bChangeHeadParts = model.bChangeHeadParts;
        bHeadPartsExcludeCustomHeads = model.bHeadPartsExcludeCustomHeads;
        bEnableConsistency = model.bEnableConsistency;
        AutoSplitOutput = model.AutoSplitOutput;
        ExcludePlayerCharacter = model.ExcludePlayerCharacter;
        ExcludePresets = model.ExcludePresets;
        bLinkNPCsWithSameName = model.bLinkNPCsWithSameName;
        LinkedNameExclusions = VM_CollectionMemberString.InitializeObservableCollectionFromICollection(model.LinkedNPCNameExclusions);
        LinkedNPCGroups = VM_LinkedNPCGroup.GetViewModelsFromModels(model.LinkedNPCGroups, linkedNPCFactory, linkCache, _logger);
        bVerboseModeAssetsNoncompliant = model.bVerboseModeAssetsNoncompliant;
        bVerboseModeAssetsAll = model.bVerboseModeAssetsAll;
        verboseModeNPClist = new ObservableCollection<FormKey>(model.VerboseModeNPClist);
        VerboseModeDetailedAttributes = model.VerboseModeDetailedAttributes;
        patchableRaces = new ObservableCollection<FormKey>(model.PatchableRaces);
        PreviewNpcs.CopyInFromModel(model.PreviewNpcs, patchableRaces);
        raceAliases = VM_RaceAlias.GetViewModelsFromModels(model.RaceAliases, this, aliasFactory);
        RaceGroupingEditor.CopyInFromModel(model.RaceGroupings, null);
        OverwritePluginRaceGroups = model.OverwritePluginRaceGroups;
        AttributeGroupMenu.CopyInViewModelFromModels(model.AttributeGroups);
        OverwritePluginAttGroups = model.OverwritePluginAttGroups;
        DisableValidation = model.bDisableValidation;
        _bFirstRun = model.bFirstRun;
        bUseDetailedReportSelection = model.bUseDetailedReportSelection;
        DetailedReportSelector.CopyInFromModel(model.DetailedReportSelector);
        bFilterNPCsByArmature = model.bFilterNPCsByArmature;
        Close7ZipWhenFinished = model.Close7ZipWhenFinished;
        BlockedModsFromImport = new(model.BlockedModsFromImport);
        _bTroubleshootingWarningDisplayed = model.bTroubleShootingWarningDisplayed;
        // Global disclosure mode. Migration for settings predating DisplayMode: users who had
        // troubleshooting settings shown land in Troubleshoot; everyone else lands in Customize
        // (today's default view IS the full customization view). Fresh installs start in Use.
        // The warning flag is read above so entering Troubleshoot here can't re-prompt.
        UiModeController.Instance.DisplayMode =
            model.bFirstRun ? UiDisplayMode.Use
            : model.DisplayMode ?? (model.bShowTroubleshootingSettings ? UiDisplayMode.Troubleshoot : UiDisplayMode.Customize);
        bShow3DPreview = model.bShow3DPreview;
        SpecificNPCPreviewerWidth = model.SpecificNPCPreviewerWidth;
        ConsistencyPreviewerWidth = model.ConsistencyPreviewerWidth;
        CharacterViewerLightingLayout = model.CharacterViewerLightingLayout;
        CharacterViewerLightingColorScheme = model.CharacterViewerLightingColorScheme;
        UserLightingLayouts = new ObservableCollection<CharacterViewerLightingLayout>(
            model.UserLightingLayouts ?? new List<CharacterViewerLightingLayout>());
        UserLightingColorSchemes = new ObservableCollection<CharacterViewerLightingColorScheme>(
            model.UserLightingColorSchemes ?? new List<CharacterViewerLightingColorScheme>());
        CharacterViewerVerboseLog = model.CharacterViewerVerboseLog;
        CharacterViewerRenderMissingTextureAsWireframe = model.CharacterViewerRenderMissingTextureAsWireframe;
        CharacterViewerEnableToneMapping = model.CharacterViewerEnableToneMapping;
        CharacterViewerEnableShadows = model.CharacterViewerEnableShadows;
        CharacterViewerEnableAmbientOcclusion = model.CharacterViewerEnableAmbientOcclusion;
        CharacterViewerSsaoRadius = model.CharacterViewerSsaoRadius;
        CharacterViewerSsaoBias = model.CharacterViewerSsaoBias;
        CharacterViewerSsaoIntensity = model.CharacterViewerSsaoIntensity;
        CharacterViewerSsaoThickness = model.CharacterViewerSsaoThickness;
        CharacterViewerSsaoHairGap = model.CharacterViewerSsaoHairGap;
        CharacterViewerEnableEyeCatchlight = model.CharacterViewerEnableEyeCatchlight;
        CharacterViewerSubsurfaceStrength = model.CharacterViewerSubsurfaceStrength;
        CharacterViewerVignetteRadius = model.CharacterViewerVignetteRadius;
        CharacterViewerVignetteIntensity = model.CharacterViewerVignetteIntensity;
        CharacterViewerSkinSaturationBoost = model.CharacterViewerSkinSaturationBoost;
        CharacterViewerExposure = model.CharacterViewerExposure;
        CharacterViewerTonemapHairRelief = model.CharacterViewerTonemapHairRelief;
        CharacterViewerDaylightBoost = model.CharacterViewerDaylightBoost;
        CharacterViewerDaylightBoostIntensity = model.CharacterViewerDaylightBoostIntensity;
        CharacterViewerEnableBloom = model.CharacterViewerEnableBloom;
        CharacterViewerBloomIntensity = model.CharacterViewerBloomIntensity;
        CacheMode = model.CacheMode;
        CacheFixedBudgetGB = model.CacheFixedBudgetGB;
        CacheFreeRamPercent = model.CacheFreeRamPercent;
        TextureLoadStrategy = model.TextureLoadStrategy;
        IsCurrentlyLoading = false;
        _logger.LogStartupEventEnd("Loading General Settings UI");
    }

    /// <summary>Reloads the VM from the current persisted <see cref="PatcherState.GeneralSettings"/> model.</summary>
    public void Refresh()
    {
        CopyInFromModel(_patcherState.GeneralSettings, _aliasFactory, _linkedNPCFactory, lk);
    }
    /// <summary>VM → Model: writes every General setting back to a new <see cref="Settings_General"/> (clears the first-run flag, marks the UI as opened).</summary>
    public Settings_General DumpViewModelToModel()
    {
        Settings_General model = new();
        model.OutputDataFolder = OutputDataFolder;
        model.AppearanceMergerType = AppearanceMergerType;
        model.EasyNPCprofilePath = EasyNPCprofilePath;
        model.NPC2TokenPath = NPC2TokenPath;
        model.bShowToolTips = bShowToolTips;
        model.ThemeName = SelectedThemeName;
        model.bChangeMeshesOrTextures = bChangeMeshesOrTextures;
        model.BodySelectionMode = BodySelectionMode;
        model.LastBodySelectionMode = LastBodySelectionMode;
        model.BSSelectionMode = BSSelectionMode;
        model.bChangeHeight = bChangeHeight;
        model.bChangeHeadParts = bChangeHeadParts;
        model.bHeadPartsExcludeCustomHeads = bHeadPartsExcludeCustomHeads;
        model.OutputDataFolder = OutputDataFolder;
        model.bEnableConsistency = bEnableConsistency;
        model.AutoSplitOutput = AutoSplitOutput;
        model.ExcludePlayerCharacter = ExcludePlayerCharacter;
        model.ExcludePresets = ExcludePresets;
        model.bLinkNPCsWithSameName = bLinkNPCsWithSameName;
        model.LinkedNPCNameExclusions = LinkedNameExclusions.Select(x => x.Content).ToList();
        VM_LinkedNPCGroup.DumpViewModelsToModels(model.LinkedNPCGroups, LinkedNPCGroups);
        model.bVerboseModeAssetsNoncompliant = bVerboseModeAssetsNoncompliant;
        model.bVerboseModeAssetsAll = bVerboseModeAssetsAll;
        model.VerboseModeNPClist = verboseModeNPClist.ToList();
        model.VerboseModeDetailedAttributes = VerboseModeDetailedAttributes;
        model.PatchableRaces = patchableRaces.ToList();
        model.RaceGroupings = RaceGroupingEditor.DumpToModel();
        model.OverwritePluginRaceGroups = OverwritePluginRaceGroups;
        model.RaceAliases.Clear();
        foreach (var x in raceAliases)
        {
            model.RaceAliases.Add(VM_RaceAlias.DumpViewModelToModel(x));
        }
        VM_AttributeGroupMenu.DumpViewModelToModels(AttributeGroupMenu, model.AttributeGroups);
        model.OverwritePluginAttGroups = OverwritePluginAttGroups;
        model.bDisableValidation = DisableValidation;
        model.bFirstRun = false;
        model.bUseDetailedReportSelection = bUseDetailedReportSelection;
        model.DetailedReportSelector = DetailedReportSelector.DumpToModel();
        model.bFilterNPCsByArmature = bFilterNPCsByArmature;
        model.Close7ZipWhenFinished = Close7ZipWhenFinished;
        model.DisplayMode = UiModeController.Instance.DisplayMode;
        // Keep writing the legacy flag so downgrading to an older SynthEBD behaves sanely.
        model.bShowTroubleshootingSettings = UiModeController.Instance.DisplayMode == UiDisplayMode.Troubleshoot;
        model.BlockedModsFromImport = new(BlockedModsFromImport);
        model.bTroubleShootingWarningDisplayed = _bTroubleshootingWarningDisplayed;
        model.bHeadPartWarningDisplayed = _bHeadPartWarningDisplayed;
        model.bShow3DPreview = bShow3DPreview;
        model.SpecificNPCPreviewerWidth = SpecificNPCPreviewerWidth;
        model.ConsistencyPreviewerWidth = ConsistencyPreviewerWidth;
        model.CharacterViewerLightingLayout = CharacterViewerLightingLayout;
        model.CharacterViewerLightingColorScheme = CharacterViewerLightingColorScheme;
        model.UserLightingLayouts = UserLightingLayouts.ToList();
        model.UserLightingColorSchemes = UserLightingColorSchemes.ToList();
        model.CharacterViewerVerboseLog = CharacterViewerVerboseLog;
        model.CharacterViewerRenderMissingTextureAsWireframe = CharacterViewerRenderMissingTextureAsWireframe;
        model.CharacterViewerEnableToneMapping = CharacterViewerEnableToneMapping;
        model.CharacterViewerEnableShadows = CharacterViewerEnableShadows;
        model.CharacterViewerEnableAmbientOcclusion = CharacterViewerEnableAmbientOcclusion;
        model.CharacterViewerSsaoRadius = CharacterViewerSsaoRadius;
        model.CharacterViewerSsaoBias = CharacterViewerSsaoBias;
        model.CharacterViewerSsaoIntensity = CharacterViewerSsaoIntensity;
        model.CharacterViewerSsaoThickness = CharacterViewerSsaoThickness;
        model.CharacterViewerSsaoHairGap = CharacterViewerSsaoHairGap;
        model.CharacterViewerEnableEyeCatchlight = CharacterViewerEnableEyeCatchlight;
        model.CharacterViewerSubsurfaceStrength = CharacterViewerSubsurfaceStrength;
        model.CharacterViewerVignetteRadius = CharacterViewerVignetteRadius;
        model.CharacterViewerVignetteIntensity = CharacterViewerVignetteIntensity;
        model.CharacterViewerSkinSaturationBoost = CharacterViewerSkinSaturationBoost;
        model.CharacterViewerExposure = CharacterViewerExposure;
        model.CharacterViewerTonemapHairRelief = CharacterViewerTonemapHairRelief;
        model.CharacterViewerDaylightBoost = CharacterViewerDaylightBoost;
        model.CharacterViewerDaylightBoostIntensity = CharacterViewerDaylightBoostIntensity;
        model.CharacterViewerEnableBloom = CharacterViewerEnableBloom;
        model.CharacterViewerBloomIntensity = CharacterViewerBloomIntensity;
        model.CacheMode = CacheMode;
        model.CacheFixedBudgetGB = CacheFixedBudgetGB;
        model.CacheFreeRamPercent = CacheFreeRamPercent;
        model.TextureLoadStrategy = TextureLoadStrategy;
        model.PreviewNpcs = PreviewNpcs.DumpToModel();

        model.bUIopened = true;
        return model;
    }

    /// <summary>
    /// Reverts the General-tab troubleshooting settings to their defaults. In
    /// <paramref name="preparationMode"/> only collects and returns a human-readable list of the
    /// changes that would be made (for a confirmation prompt) without mutating state; otherwise
    /// applies them. Returns the list of pending/applied changes.
    /// </summary>
    private List<string> ResetTroubleShootingToDefault(bool preparationMode)
    {
        var changes = new List<string>();

        if (!ExcludePlayerCharacter)
        {
            if (preparationMode)
            {
                changes.Add("Exclude Player Character: False --> True");
            }
            else
            {
                ExcludePlayerCharacter = true;
            }
        }

        if (!ExcludePresets)
        {
            if (preparationMode)
            {
                changes.Add("Exclude Racemenu Presets: False --> True");
            }
            else
            {
                ExcludePresets = true;
            }
        }

        if (!bFilterNPCsByArmature)
        {
            if (preparationMode)
            {
                changes.Add("Exclude Partially Skinned NPCs: False --> True");
            }
            else
            {
                bFilterNPCsByArmature = true;
            }
        }

        if (!bLinkNPCsWithSameName)
        {
            if (preparationMode)
            {
                changes.Add("Link NPCs with Same Name: False --> True");
            }
            else
            {
                bLinkNPCsWithSameName = true;
            }
        }

        List<string> defaultLinkedNPCExclusions = _patcherState.GeneralSettings.GetDefaultValue("LinkedNPCNameExclusions");

        foreach (var name in defaultLinkedNPCExclusions)
        {
            if (!LinkedNameExclusions.Select(x => x.Content).Contains(name))
            {
                if (preparationMode)
                {
                    changes.Add("Linked NPC Name Exclusions: Add \"" + name + "\"");
                }
                else
                {
                    LinkedNameExclusions.Add(new(name, LinkedNameExclusions));
                }
            }
        }

        for (int i = 0; i < LinkedNameExclusions.Count; i++)
        {
            if (!defaultLinkedNPCExclusions.Contains(LinkedNameExclusions[i].Content))
            {
                if (preparationMode)
                {
                    changes.Add("Linked NPC Name Exclusions: Remove \"" + LinkedNameExclusions[i].Content + "\"");
                }
                else
                {
                    LinkedNameExclusions.RemoveAt(i);
                    i--;
                }
            }
        }

        if (bVerboseModeAssetsNoncompliant)
        {
            if (preparationMode)
            {
                changes.Add("Verbose Mode for Conflict NPCs: True --> False");
            }
            else
            {
                bVerboseModeAssetsNoncompliant = false;
            }
        }

        if (bVerboseModeAssetsAll)
        {
            if (preparationMode)
            {
                changes.Add("Verbose Mode for All NPCs: True --> False");
            }
            else
            {
                bVerboseModeAssetsAll = false;
            }
        }

        if (DisableValidation)
        {
            if (preparationMode)
            {
                changes.Add("Disable Pre-run Validation: True --> False");
            }
            else
            {
                DisableValidation = false;
            }
        }

        if (!Close7ZipWhenFinished)
        {
            if (preparationMode)
            {
                changes.Add("Close Archive Extractor When Done: False --> True");
            }
            else
            {
                Close7ZipWhenFinished = true;
            }
        }

        return changes;
    }
}