using System.Collections.ObjectModel;
using System.IO;
using System.Reactive.Linq;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

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

        if (IsStandalone)
        {
            EnvironmentSettingsVM = new(environmentProvider);
        }

        AttributeGroupMenu = attributeGroupFactory(null, false);
        RaceGroupingEditor = _raceGroupingEditorFactory(this, false);
        DetailedReportSelector = detailedReportNPCSelectorFactory(AttributeGroupMenu, RaceGroupingEditor);

        this.WhenAnyValue(x => x.bShowToolTips)
            .Subscribe(x => TooltipController.Instance.DisplayToolTips = x).DisposeWith(this);

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
                    if (IO_Aux.SelectFile(environmentProvider.DataFolderPath, "Select your exported EasyNPC Profile", ".txt", out string path))
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

        ToggleTroubleShootingSettingsDisplay = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (bShowTroubleshootingSettings)
                {
                    bShowTroubleshootingSettings = false;
                    TroubleShootingSettingsToggleLabel = _troubleShootingSettingsShowText;
                }
                else if (_bTroubleshootingWarningDisplayed || MessageWindow.DisplayNotificationYesNo("Are you sure?", "These settings are only meant for troubleshooting. Do not change them unless you know what you're doing or have been instructed to do so."))
                {
                    bShowTroubleshootingSettings = true;
                    _bTroubleshootingWarningDisplayed = true;
                    TroubleShootingSettingsToggleLabel = _troubleShootingSettingsHideText;
                }
            }
        );

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
    public string EasyNPCprofilePath { get; set; } = "";
    public bool bShowToolTips { get; set; } = true;
    public bool bChangeMeshesOrTextures { get; set; } = true;
    public BodyShapeSelectionMode BodySelectionMode { get; set; } = BodyShapeSelectionMode.None;
    public BodySlideSelectionMode BSSelectionMode { get; set; } = BodySlideSelectionMode.OBody;
    public bool ExcludePlayerCharacter { get; set; } = true;
    public bool ExcludePresets { get; set; } = true;
    public bool bChangeHeight { get; set; } = true;
    public bool bChangeHeadParts { get; set; } = false;
    public bool bHeadPartsExcludeCustomHeads { get; set; } = true;
    public bool bEnableConsistency { get; set; } = true;
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
    public bool IsStandalone { get; set; }
    public bool bFilterNPCsByArmature { get; set; } = true;
    public bool bShowTroubleshootingSettings { get; set; } = false;
    private bool _bTroubleshootingWarningDisplayed { get; set; } = false;
    public RelayCommand ToggleTroubleShootingSettingsDisplay { get; }
    public RelayCommand ResetTroubleShootingToDefaultCommand { get; }
    public string TroubleShootingSettingsToggleLabel { get; set; } = _troubleShootingSettingsShowText;
    private const string _troubleShootingSettingsShowText = "Show Troubleshooting Settings";
    private const string _troubleShootingSettingsHideText = "Hide Troubleshooting Settings";
    private bool _bHeadPartWarningDisplayed { get; set; } = false;

    public void CopyInFromModel(Settings_General model, VM_RaceAlias.Factory aliasFactory, VM_LinkedNPCGroup.Factory linkedNPCFactory, ILinkCache linkCache)
    {
        if (model == null)
        {
            return;
        }
        _logger.LogStartupEventStart("Loading General Settings UI");
        IsCurrentlyLoading = true;

        OutputDataFolder = model.OutputDataFolder;
        EasyNPCprofilePath = model.EasyNPCprofilePath;
        bShowToolTips = model.bShowToolTips;
        bChangeMeshesOrTextures = model.bChangeMeshesOrTextures;
        BodySelectionMode = model.BodySelectionMode;
        BSSelectionMode = model.BSSelectionMode;
        bChangeHeight = model.bChangeHeight;
        _bHeadPartWarningDisplayed = model.bHeadPartWarningDisplayed; // has to copy in before bChangeHeadParts
        bChangeHeadParts = model.bChangeHeadParts;
        bHeadPartsExcludeCustomHeads = model.bHeadPartsExcludeCustomHeads;
        bEnableConsistency = model.bEnableConsistency;
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
        bShowTroubleshootingSettings = model.bShowTroubleshootingSettings;
        _bTroubleshootingWarningDisplayed = model.bTroubleShootingWarningDisplayed;
        if (bShowTroubleshootingSettings)
        {
            TroubleShootingSettingsToggleLabel = _troubleShootingSettingsHideText;
        }
        IsCurrentlyLoading = false;
        _logger.LogStartupEventEnd("Loading General Settings UI");
    }

    public void Refresh()
    {
        CopyInFromModel(_patcherState.GeneralSettings, _aliasFactory, _linkedNPCFactory, lk);
    }
    public Settings_General DumpViewModelToModel()
    {
        Settings_General model = new();
        model.OutputDataFolder = OutputDataFolder;
        model.EasyNPCprofilePath = EasyNPCprofilePath;
        model.bShowToolTips = bShowToolTips;
        model.bChangeMeshesOrTextures = bChangeMeshesOrTextures;
        model.BodySelectionMode = BodySelectionMode;
        model.BSSelectionMode = BSSelectionMode;
        model.bChangeHeight = bChangeHeight;
        model.bChangeHeadParts = bChangeHeadParts;
        model.bHeadPartsExcludeCustomHeads = bHeadPartsExcludeCustomHeads;
        model.OutputDataFolder = OutputDataFolder;
        model.bEnableConsistency = bEnableConsistency;
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
        model.bShowTroubleshootingSettings = bShowTroubleshootingSettings;
        model.bTroubleShootingWarningDisplayed = _bTroubleshootingWarningDisplayed;
        model.bHeadPartWarningDisplayed = _bHeadPartWarningDisplayed;

        model.bUIopened = true;
        return model;
    }

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