using System.Collections.ObjectModel;
using System.IO;
using System.Reactive.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

public class VM_Settings_General : VM, IHasAttributeGroupMenu
{
    public SaveLoader SaveLoader { get; set; }
    private bool _bFirstRun { get; set;} = false;
    private readonly Patcher _patcher;
    private readonly SettingsIO_General _generalIO;
    public VM_Settings_General(
        VM_SettingsModManager modManagerSettings,
        PatcherSettingsSourceProvider settingsProvider,
        VM_AttributeGroupMenu.Factory attributeGroupFactory,
        SettingsIO_General generalIO,
        Patcher patcher)
    {
        _patcher = patcher;
        _generalIO = generalIO;

        AttributeGroupMenu = attributeGroupFactory(null, false);

        if (settingsProvider.SourceSettings.Value.Initialized)
        {
            bLoadSettingsFromDataFolder = settingsProvider.SourceSettings.Value.LoadFromDataDir;
            PortableSettingsFolder = settingsProvider.SourceSettings.Value.PortableSettingsFolder;
        }

        this.WhenAnyValue(x => x.bShowToolTips)
            .Subscribe(x => TooltipController.Instance.DisplayToolTips = x);
        
        PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.SkyrimVersion).Skip(1).Subscribe(_ => PatcherEnvironmentProvider.Instance.GameDataFolder = String.Empty);

        AddRaceAlias = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => raceAliases.Add(new VM_raceAlias(new RaceAlias(), this))
        );

        AddRaceGrouping = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => RaceGroupings.Add(new VM_RaceGrouping(new RaceGrouping(), this))
        );

        AddLinkedNPCNameExclusion = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => LinkedNameExclusions.Add(new VM_CollectionMemberString("", this.LinkedNameExclusions))
        );

        AddLinkedNPCGroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => LinkedNPCGroups.Add(new VM_LinkedNPCGroup())
        );

        RemoveLinkedNPCGroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => LinkedNPCGroups.Remove((VM_LinkedNPCGroup)x)
        );

        this.WhenAnyValue(x => x.bLoadSettingsFromDataFolder).Skip(1).Subscribe(x =>
        {
            _patcher.ResolvePatchableRaces();
            SaveLoader.Reinitialize();
        });

        SelectOutputFolder = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFolder(PatcherEnvironmentProvider.Instance.Environment.DataFolderPath, out var tmpFolder))
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
                        SwitchPortableSettingsFolder(selectedPath, settingsProvider);
                    }
                }
            }
        );

        ClearPortableSettingsFolder = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (string.IsNullOrWhiteSpace(PortableSettingsFolder))
                {
                    CustomMessageBox.DisplayNotificationOK("", "There is no settings folder path to clear.");
                    return;
                }
                SwitchPortableSettingsFolder(string.Empty, settingsProvider);
            }
        );

        this.WhenAnyValue(x => x._bFirstRun).Subscribe(x => { 
        if (x) { 
                ShowFirstRunMessage(); 
            }
        });
    }

    public string OutputDataFolder { get; set; } = "";
    public bool bShowToolTips { get;  set;} = true;
    public bool bChangeMeshesOrTextures { get; set;  } = true;
    public PatcherEnvironmentProvider Environment { get; set; }
    public BodyShapeSelectionMode BodySelectionMode { get; set;  } = BodyShapeSelectionMode.None;
    public BodySlideSelectionMode BSSelectionMode { get; set; } = BodySlideSelectionMode.OBody;
    public bool ExcludePlayerCharacter { get; set; } = true;
    public bool ExcludePresets { get; set; } = true;
    public bool bChangeHeight { get; set;  } = true;
    public bool bChangeHeadParts { get; set; } = true;
    public string PortableSettingsFolder { get; set; } = string.Empty;
    public bool bEnableConsistency { get; set;  } = true;
    public bool bLinkNPCsWithSameName { get; set;  } = true;
    public ObservableCollection<VM_CollectionMemberString> LinkedNameExclusions { get; set; } = new();
    public ObservableCollection<VM_LinkedNPCGroup> LinkedNPCGroups { get; set; } = new();
    public bool bVerboseModeAssetsNoncompliant { get; set;  } = false;
    public bool bVerboseModeAssetsAll { get; set;  } = false;
    public ObservableCollection<FormKey> verboseModeNPClist { get; set; } = new();
    public bool VerboseModeDetailedAttributes { get; set; } = false;
    public bool bLoadSettingsFromDataFolder { get; set;  } = false;
    public ObservableCollection<FormKey> patchableRaces { get; set; } = new();

    public ObservableCollection<VM_raceAlias> raceAliases { get; set;  } = new();

    public RelayCommand AddRaceAlias { get; }

    public VM_AttributeGroupMenu AttributeGroupMenu { get; }
    public bool OverwritePluginAttGroups { get; set; } = true;
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> RacePickerFormKeys { get; } = typeof(IRaceGetter).AsEnumerable();
    public IEnumerable<Type> NPCPickerFormKeys { get; } = typeof(INpcGetter).AsEnumerable();

    public ObservableCollection<VM_RaceGrouping> RaceGroupings { get; set; } = new();
    public RelayCommand AddRaceGrouping { get; }
    public RelayCommand AddLinkedNPCNameExclusion { get; }
    public RelayCommand AddLinkedNPCGroup { get; }
    public RelayCommand RemoveLinkedNPCGroup { get; }
    public RelayCommand SelectOutputFolder { get; }
    public RelayCommand ClearOutputFolder { get; }
    public RelayCommand SelectPortableSettingsFolder { get; }
    public RelayCommand ClearPortableSettingsFolder { get; }
    
    public static void GetViewModelFromModel(VM_Settings_General viewModel, PatcherSettingsSourceProvider patcherSettingsProvider)
    {
        var model = PatcherSettings.General;
        viewModel.OutputDataFolder = model.OutputDataFolder;
        viewModel.bShowToolTips = model.bShowToolTips;
        viewModel.bChangeMeshesOrTextures = model.bChangeMeshesOrTextures;
        viewModel.BodySelectionMode = model.BodySelectionMode;
        viewModel.BSSelectionMode = model.BSSelectionMode;
        viewModel.bChangeHeight = model.bChangeHeight;
        viewModel.bChangeHeadParts = model.bChangeHeadParts;
        viewModel.bEnableConsistency = model.bEnableConsistency;
        viewModel.ExcludePlayerCharacter = model.ExcludePlayerCharacter;
        viewModel.ExcludePresets = model.ExcludePresets;
        viewModel.bLinkNPCsWithSameName = model.bLinkNPCsWithSameName;
        viewModel.LinkedNameExclusions = VM_CollectionMemberString.InitializeObservableCollectionFromICollection(model.LinkedNPCNameExclusions);
        viewModel.LinkedNPCGroups = VM_LinkedNPCGroup.GetViewModelsFromModels(model.LinkedNPCGroups);
        viewModel.Environment.PatchFileName = model.PatchFileName;
        viewModel.bVerboseModeAssetsNoncompliant = model.bVerboseModeAssetsNoncompliant;
        viewModel.bVerboseModeAssetsAll = model.bVerboseModeAssetsAll;
        viewModel.verboseModeNPClist = new ObservableCollection<FormKey>(model.VerboseModeNPClist);
        viewModel.VerboseModeDetailedAttributes = model.VerboseModeDetailedAttributes;
        viewModel.patchableRaces = new ObservableCollection<FormKey>(model.PatchableRaces);
        viewModel.raceAliases = VM_raceAlias.GetViewModelsFromModels(model.RaceAliases, viewModel);
        viewModel.RaceGroupings = VM_RaceGrouping.GetViewModelsFromModels(model.RaceGroupings, viewModel);
        viewModel.AttributeGroupMenu.CopyInViewModelFromModels(model.AttributeGroups);
        viewModel.OverwritePluginAttGroups = model.OverwritePluginAttGroups;

        if (patcherSettingsProvider.SourceSettings.Value.Initialized)
        {
            viewModel.PortableSettingsFolder = patcherSettingsProvider.SourceSettings.Value.PortableSettingsFolder;
        }

        viewModel._bFirstRun = model.bFirstRun;
    }
    public static void DumpViewModelToModel(VM_Settings_General viewModel, Settings_General model)
    {
        model.OutputDataFolder = viewModel.OutputDataFolder;
        model.bShowToolTips = viewModel.bShowToolTips;
        model.bChangeMeshesOrTextures = viewModel.bChangeMeshesOrTextures;
        model.BodySelectionMode = viewModel.BodySelectionMode;
        model.BSSelectionMode = viewModel.BSSelectionMode;
        model.bChangeHeight = viewModel.bChangeHeight;
        model.bChangeHeadParts = viewModel.bChangeHeadParts;
        model.OutputDataFolder = viewModel.OutputDataFolder;
        model.bEnableConsistency = viewModel.bEnableConsistency;
        model.ExcludePlayerCharacter = viewModel.ExcludePlayerCharacter;
        model.ExcludePresets = viewModel.ExcludePresets;
        model.bLinkNPCsWithSameName = viewModel.bLinkNPCsWithSameName;
        model.LinkedNPCNameExclusions = viewModel.LinkedNameExclusions.Select(x => x.Content).ToList();
        VM_LinkedNPCGroup.DumpViewModelsToModels(model.LinkedNPCGroups, viewModel.LinkedNPCGroups);
        model.PatchFileName = viewModel.Environment.PatchFileName;
        model.bVerboseModeAssetsNoncompliant = viewModel.bVerboseModeAssetsNoncompliant;
        model.bVerboseModeAssetsAll = viewModel.bVerboseModeAssetsAll;
        model.VerboseModeNPClist = viewModel.verboseModeNPClist.ToList();
        model.VerboseModeDetailedAttributes = viewModel.VerboseModeDetailedAttributes;
        model.PatchableRaces = viewModel.patchableRaces.ToList();

        model.RaceAliases.Clear();
        foreach (var x in viewModel.raceAliases)
        {
            model.RaceAliases.Add(VM_raceAlias.DumpViewModelToModel(x));
        }

        model.RaceGroupings.Clear();
        foreach (var x in viewModel.RaceGroupings)
        {
            model.RaceGroupings.Add(VM_RaceGrouping.DumpViewModelToModel(x));
        }

        VM_AttributeGroupMenu.DumpViewModelToModels(viewModel.AttributeGroupMenu, model.AttributeGroups);
        model.OverwritePluginAttGroups = viewModel.OverwritePluginAttGroups;

        model.bFirstRun = false;

        PatcherSettings.General = model;
    }

    private void SwitchPortableSettingsFolder(string folderPath, PatcherSettingsSourceProvider settingsProvider)
    {
        PortableSettingsFolder = folderPath;
        _generalIO.DumpVMandSave(this);
        settingsProvider.SetNewDataDir(PortableSettingsFolder);
        SaveLoader.Reinitialize();
    }

    private void ShowFirstRunMessage()
    {
        string message = @"Welcome to SynthEBD
If you are using a mod manager, start by going to the Mod Manager Integration menu and setting up your paths.
If you don't want your patcher outuput going straight to your Data or Overwrite folder, set your desired Output Path in this menu.";

        CustomMessageBox.DisplayNotificationOK("", message);
    }
}