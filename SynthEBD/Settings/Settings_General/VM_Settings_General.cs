using System.Collections.ObjectModel;
using System.IO;
using System.Reactive.Linq;
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
        VM_AttributeGroupMenu.Factory attributeGroupFactory,
        VM_RaceAlias.Factory aliasFactory,
        VM_RaceGroupingEditor.Factory raceGroupingEditorFactory,
        VM_RaceGrouping.Factory groupingFactory,
        VM_LinkedNPCGroup.Factory linkedNPCFactory,
        SettingsIO_General generalIO,
        PatcherState patcherState,
        IEnvironmentStateProvider environmentProvider,
        FirstLaunch firstLaunch,
        SynthEBDPaths paths)
    {
        _environmentProvider = environmentProvider;
        IsStandalone = environmentProvider.RunMode == EnvironmentMode.Standalone;
        SettingsSourceProvider = settingsProvider;
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

        this.WhenAnyValue(x => x.bShowToolTips)
            .Subscribe(x => TooltipController.Instance.DisplayToolTips = x).DisposeWith(this);

        environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

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
                        CustomMessageBox.DisplayNotificationOK("Invalid Directory", "The folder name must be \"SynthEBD\"");
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
                    CustomMessageBox.DisplayNotificationOK("", "There is no settings folder path to clear.");
                    return;
                }
                SettingsSourceProvider.PortableSettingsFolder = String.Empty;
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
    }

    public VM_Settings_Environment EnvironmentSettingsVM { get; set; }
    public string OutputDataFolder { get; set; } = "";
    public bool bShowToolTips { get;  set;} = true;
    public bool bChangeMeshesOrTextures { get; set;  } = true;
    public BodyShapeSelectionMode BodySelectionMode { get; set;  } = BodyShapeSelectionMode.None;
    public BodySlideSelectionMode BSSelectionMode { get; set; } = BodySlideSelectionMode.OBody;
    public bool ExcludePlayerCharacter { get; set; } = true;
    public bool ExcludePresets { get; set; } = true;
    public bool bChangeHeight { get; set;  } = true;
    public bool bChangeHeadParts { get; set; } = true;
    public bool bHeadPartsExcludeCustomHeads { get; set; } = true;
    public bool bEnableConsistency { get; set;  } = true;
    public bool bLinkNPCsWithSameName { get; set;  } = true;
    public ObservableCollection<VM_CollectionMemberString> LinkedNameExclusions { get; set; } = new();
    public ObservableCollection<VM_LinkedNPCGroup> LinkedNPCGroups { get; set; } = new();
    public bool bVerboseModeAssetsNoncompliant { get; set;  } = false;
    public bool bVerboseModeAssetsAll { get; set;  } = false;
    public ObservableCollection<FormKey> verboseModeNPClist { get; set; } = new();
    public bool VerboseModeDetailedAttributes { get; set; } = false;
    public ObservableCollection<FormKey> patchableRaces { get; set; } = new();
    public VM_RaceGroupingEditor RaceGroupingEditor { get; set; }
    public bool OverwritePluginRaceGroups { get; set; } = true;
    public ObservableCollection<VM_RaceAlias> raceAliases { get; set;  } = new();
    public RelayCommand AddRaceAlias { get; }
    public VM_AttributeGroupMenu AttributeGroupMenu { get; }
    public bool OverwritePluginAttGroups { get; set; } = true;
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
    public bool IsStandalone { get; set; }
    
    public static void GetViewModelFromModel(VM_Settings_General viewModel, PatcherSettingsSourceProvider patcherSettingsProvider, PatcherState patcherState, VM_RaceAlias.Factory aliasFactory, VM_LinkedNPCGroup.Factory linkedNPCFactory, ILinkCache linkCache)
    {
        var model = patcherState.GeneralSettings;
        viewModel.OutputDataFolder = model.OutputDataFolder;
        viewModel.bShowToolTips = model.bShowToolTips;
        viewModel.bChangeMeshesOrTextures = model.bChangeMeshesOrTextures;
        viewModel.BodySelectionMode = model.BodySelectionMode;
        viewModel.BSSelectionMode = model.BSSelectionMode;
        viewModel.bChangeHeight = model.bChangeHeight;
        viewModel.bChangeHeadParts = model.bChangeHeadParts;
        viewModel.bHeadPartsExcludeCustomHeads = model.bHeadPartsExcludeCustomHeads;
        viewModel.bEnableConsistency = model.bEnableConsistency;
        viewModel.ExcludePlayerCharacter = model.ExcludePlayerCharacter;
        viewModel.ExcludePresets = model.ExcludePresets;
        viewModel.bLinkNPCsWithSameName = model.bLinkNPCsWithSameName;
        viewModel.LinkedNameExclusions = VM_CollectionMemberString.InitializeObservableCollectionFromICollection(model.LinkedNPCNameExclusions);
        viewModel.LinkedNPCGroups = VM_LinkedNPCGroup.GetViewModelsFromModels(model.LinkedNPCGroups, linkedNPCFactory, linkCache);
        viewModel.bVerboseModeAssetsNoncompliant = model.bVerboseModeAssetsNoncompliant;
        viewModel.bVerboseModeAssetsAll = model.bVerboseModeAssetsAll;
        viewModel.verboseModeNPClist = new ObservableCollection<FormKey>(model.VerboseModeNPClist);
        viewModel.VerboseModeDetailedAttributes = model.VerboseModeDetailedAttributes;
        viewModel.patchableRaces = new ObservableCollection<FormKey>(model.PatchableRaces);
        viewModel.raceAliases = VM_RaceAlias.GetViewModelsFromModels(model.RaceAliases, viewModel, aliasFactory);
        viewModel.RaceGroupingEditor.CopyInFromModel(model.RaceGroupings, null);
        viewModel.OverwritePluginRaceGroups = model.OverwritePluginRaceGroups;
        viewModel.AttributeGroupMenu.CopyInViewModelFromModels(model.AttributeGroups);
        viewModel.OverwritePluginAttGroups = model.OverwritePluginAttGroups;

        viewModel._bFirstRun = model.bFirstRun;
    }
    public void DumpViewModelToModel(VM_Settings_General viewModel)
    {
        _patcherState.GeneralSettings.OutputDataFolder = viewModel.OutputDataFolder;
        _patcherState.GeneralSettings.bShowToolTips = viewModel.bShowToolTips;
        _patcherState.GeneralSettings.bChangeMeshesOrTextures = viewModel.bChangeMeshesOrTextures;
        _patcherState.GeneralSettings.BodySelectionMode = viewModel.BodySelectionMode;
        _patcherState.GeneralSettings.BSSelectionMode = viewModel.BSSelectionMode;
        _patcherState.GeneralSettings.bChangeHeight = viewModel.bChangeHeight;
        _patcherState.GeneralSettings.bChangeHeadParts = viewModel.bChangeHeadParts;
        _patcherState.GeneralSettings.bHeadPartsExcludeCustomHeads = viewModel.bHeadPartsExcludeCustomHeads;
        _patcherState.GeneralSettings.OutputDataFolder = viewModel.OutputDataFolder;
        _patcherState.GeneralSettings.bEnableConsistency = viewModel.bEnableConsistency;
        _patcherState.GeneralSettings.ExcludePlayerCharacter = viewModel.ExcludePlayerCharacter;
        _patcherState.GeneralSettings.ExcludePresets = viewModel.ExcludePresets;
        _patcherState.GeneralSettings.bLinkNPCsWithSameName = viewModel.bLinkNPCsWithSameName;
        _patcherState.GeneralSettings.LinkedNPCNameExclusions = viewModel.LinkedNameExclusions.Select(x => x.Content).ToList();
        VM_LinkedNPCGroup.DumpViewModelsToModels(_patcherState.GeneralSettings.LinkedNPCGroups, viewModel.LinkedNPCGroups);
        _patcherState.GeneralSettings.bVerboseModeAssetsNoncompliant = viewModel.bVerboseModeAssetsNoncompliant;
        _patcherState.GeneralSettings.bVerboseModeAssetsAll = viewModel.bVerboseModeAssetsAll;
        _patcherState.GeneralSettings.VerboseModeNPClist = viewModel.verboseModeNPClist.ToList();
        _patcherState.GeneralSettings.VerboseModeDetailedAttributes = viewModel.VerboseModeDetailedAttributes;
        _patcherState.GeneralSettings.PatchableRaces = viewModel.patchableRaces.ToList();
        _patcherState.GeneralSettings.RaceGroupings = viewModel.RaceGroupingEditor.DumpToModel();
        _patcherState.GeneralSettings.OverwritePluginRaceGroups = viewModel.OverwritePluginRaceGroups;
        _patcherState.GeneralSettings.RaceAliases.Clear();
        foreach (var x in viewModel.raceAliases)
        {
            _patcherState.GeneralSettings.RaceAliases.Add(VM_RaceAlias.DumpViewModelToModel(x));
        }
        VM_AttributeGroupMenu.DumpViewModelToModels(viewModel.AttributeGroupMenu, _patcherState.GeneralSettings.AttributeGroups);
        _patcherState.GeneralSettings.OverwritePluginAttGroups = viewModel.OverwritePluginAttGroups;

        _patcherState.GeneralSettings.bFirstRun = false;
    }
}