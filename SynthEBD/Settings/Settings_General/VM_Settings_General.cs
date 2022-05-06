using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reactive.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Noggog.WPF;
using ReactiveUI;

namespace SynthEBD;

public class VM_Settings_General : ViewModel, IHasAttributeGroupMenu
{
    public VM_Settings_General(MainWindow_ViewModel mainVM)
    {
        MainWindowVM = mainVM;

        this.bLoadSettingsFromDataFolder = PatcherSettings.LoadFromDataFolder;

        this.PropertyChanged += ToggleTooltipVisibility;

        AddRaceAlias = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.raceAliases.Add(new VM_raceAlias(new RaceAlias(), PatcherEnvironmentProvider.Environment, this))
        );

        AddRaceGrouping = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.RaceGroupings.Add(new VM_RaceGrouping(new RaceGrouping(), PatcherEnvironmentProvider.Environment, this))
        );

        AddLinkedNPCNameExclusion = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.LinkedNameExclusions.Add(new VM_CollectionMemberString("", this.LinkedNameExclusions))
        );

        AddLinkedNPCGroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.LinkedNPCGroups.Add(new VM_LinkedNPCGroup())
        );

        RemoveLinkedNPCGroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => this.LinkedNPCGroups.Remove((VM_LinkedNPCGroup)x)
        );

        SelectCustomGameFolder = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFile("", "Executable files (*.exe)|*.exe", "Select your game executable", out var gamePath))
                {
                    CustomGamePath = gamePath;
                    PatcherSettings.CustomGamePath = gamePath;
                    PatcherEnvironmentProvider.Environment.RefreshAndChangeGameType(SkyrimVersion, patchFileName);
                }
            }
        );

        ClearCustomGameFolder = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (string.IsNullOrWhiteSpace(PatcherSettings.CustomGamePath))
                {
                    CustomMessageBox.DisplayNotificationOK("", "There is no custom game path to clear.");
                    return;
                }
                CustomGamePath = "";
                PatcherSettings.CustomGamePath = "";
                PatcherEnvironmentProvider.Environment.RefreshAndChangeGameType(SkyrimVersion, patchFileName);
            }
        );

        this.WhenAnyValue(x => x.bLoadSettingsFromDataFolder).Skip(1).Subscribe(x =>
        {
            System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
            PatcherSettings.LoadFromDataFolder = bLoadSettingsFromDataFolder;
            PatcherSettings.Paths.UpdatePaths();
            Patcher.MainLinkCache = PatcherEnvironmentProvider.Environment.LinkCache;
            Patcher.ResolvePatchableRaces();
            MainWindowVM.LoadInitialSettingsViewModels();
            MainWindowVM.LoadPluginViewModels();
            MainWindowVM.LoadFinalSettingsViewModels();
            System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default;
        });

        SelectPortableSettingsFolder = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                string initDir = "";
                if (mainVM.ModManagerSettingsVM.ModManagerType == ModManager.ModOrganizer2)
                {
                    if (!string.IsNullOrEmpty(mainVM.ModManagerSettingsVM.MO2IntegrationVM.ModFolderPath) && Directory.Exists(mainVM.ModManagerSettingsVM.MO2IntegrationVM.ModFolderPath))
                    {
                        initDir = mainVM.ModManagerSettingsVM.MO2IntegrationVM.ModFolderPath;
                    }
                }
                else if (mainVM.ModManagerSettingsVM.ModManagerType == ModManager.Vortex)
                {
                    if (!string.IsNullOrEmpty(mainVM.ModManagerSettingsVM.VortexIntegrationVM.StagingFolderPath) && Directory.Exists(mainVM.ModManagerSettingsVM.VortexIntegrationVM.StagingFolderPath))
                    {
                        initDir = mainVM.ModManagerSettingsVM.VortexIntegrationVM.StagingFolderPath;
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
                        PortableSettingsFolder = selectedPath;
                        PatcherSettings.PortableSettingsFolder = PortableSettingsFolder;
                        PatcherSettings.Paths.UpdatePaths();
                        mainVM.SaveAndRefreshPlugins();
                    }
                }
            }
        );

        ClearPortableSettingsFolder = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (string.IsNullOrWhiteSpace(PatcherSettings.PortableSettingsFolder))
                {
                    CustomMessageBox.DisplayNotificationOK("", "There is no settings folder path to clear.");
                    return;
                }
                PortableSettingsFolder = "";
                PatcherSettings.PortableSettingsFolder = "";
                PatcherSettings.Paths.UpdatePaths();
                mainVM.SaveAndRefreshPlugins();
            }
        );

        this.WhenAnyValue(x => x.patchFileName).Subscribe(x => PatcherEnvironmentProvider.Environment.Refresh(patchFileName, false));

        this.WhenAnyValue(x => x.SkyrimVersion).Skip(1).Subscribe(x => PatcherEnvironmentProvider.Environment.RefreshAndChangeGameType(SkyrimVersion, patchFileName));
    }

    public MainWindow_ViewModel MainWindowVM { get; set; }
    public bool bShowToolTips { get;  set;} = true;
    public bool bChangeMeshesOrTextures { get; set;  } = true;

    public BodyShapeSelectionMode BodySelectionMode { get; set;  } = BodyShapeSelectionMode.None;
    public BodySlideSelectionMode BSSelectionMode { get; set; } = BodySlideSelectionMode.OBody;
    public bool ExcludePlayerCharacter { get; set; } = true;
    public bool ExcludePresets { get; set; } = true;
    public bool bChangeHeight { get; set;  } = true;
    public PathPickerVM OutputDataFolder { get; } = new()
    {
        ExistCheckOption = PathPickerVM.CheckOptions.IfPathNotEmpty,
        PathType = PathPickerVM.PathTypeOptions.Folder,
        PromptTitle = "Data Folder Override"
    };
    public string PortableSettingsFolder { get; set; } = "";
    public bool bEnableConsistency { get; set;  } = true;
    public bool bLinkNPCsWithSameName { get; set;  } = true;
    public ObservableCollection<VM_CollectionMemberString> LinkedNameExclusions { get; set; } = new();
    public ObservableCollection<VM_LinkedNPCGroup> LinkedNPCGroups { get; set; } = new();
    public string patchFileName { get; set;  } = "SynthEBD.esp";
    public SkyrimRelease SkyrimVersion { get; set; } = PatcherSettings.SkyrimVersion;
    public bool bVerboseModeAssetsNoncompliant { get; set;  } = false;
    public bool bVerboseModeAssetsAll { get; set;  } = false;
    public ObservableCollection<FormKey> verboseModeNPClist { get; set; } = new();
    public bool VerboseModeDetailedAttributes { get; set; } = false;
    public bool bLoadSettingsFromDataFolder { get; set;  } = false;
    public string CustomGamePath { get; set; } = "";
    public ObservableCollection<FormKey> patchableRaces { get; set; } = new();

    public ObservableCollection<VM_raceAlias> raceAliases { get; set;  } = new();

    public RelayCommand AddRaceAlias { get; }

    public VM_AttributeGroupMenu AttributeGroupMenu { get; set; } = new(null, false);
    public bool OverwritePluginAttGroups { get; set; } = true;
    public ILinkCache lk => PatcherEnvironmentProvider.Environment.LinkCache;
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();
    public IEnumerable<Type> NPCPickerFormKeys { get; set; } = typeof(INpcGetter).AsEnumerable();

    public ObservableCollection<VM_RaceGrouping> RaceGroupings { get; set; } = new();
    public RelayCommand AddRaceGrouping { get; }
    public RelayCommand AddLinkedNPCNameExclusion { get; }
    public RelayCommand AddLinkedNPCGroup { get; }
    public RelayCommand RemoveLinkedNPCGroup { get; }
    public RelayCommand SelectCustomGameFolder { get; }
    public RelayCommand ClearCustomGameFolder { get; }
    public RelayCommand SelectPortableSettingsFolder { get; }
    public RelayCommand ClearPortableSettingsFolder { get; }
    public static void GetViewModelFromModel(VM_Settings_General viewModel)
    {
        var model = PatcherSettings.General;
        viewModel.bShowToolTips = model.bShowToolTips;
        viewModel.bChangeMeshesOrTextures = model.bChangeMeshesOrTextures;
        viewModel.BodySelectionMode = model.BodySelectionMode;
        viewModel.BSSelectionMode = model.BSSelectionMode;
        viewModel.bChangeHeight = model.bChangeHeight;
        viewModel.OutputDataFolder.TargetPath = model.OutputDataFolder;
        viewModel.bEnableConsistency = model.bEnableConsistency;
        viewModel.ExcludePlayerCharacter = model.ExcludePlayerCharacter;
        viewModel.ExcludePresets = model.ExcludePresets;
        viewModel.bLinkNPCsWithSameName = model.bLinkNPCsWithSameName;
        viewModel.patchFileName = model.PatchFileName;
        viewModel.bVerboseModeAssetsNoncompliant = model.bVerboseModeAssetsNoncompliant;
        viewModel.bVerboseModeAssetsAll = model.bVerboseModeAssetsAll;
        viewModel.verboseModeNPClist = new ObservableCollection<FormKey>(model.VerboseModeNPClist);
        viewModel.VerboseModeDetailedAttributes = model.VerboseModeDetailedAttributes;
        viewModel.patchableRaces = new ObservableCollection<FormKey>(model.PatchableRaces);
        viewModel.raceAliases = VM_raceAlias.GetViewModelsFromModels(model.RaceAliases, PatcherEnvironmentProvider.Environment, viewModel);
        viewModel.RaceGroupings = VM_RaceGrouping.GetViewModelsFromModels(model.RaceGroupings, PatcherEnvironmentProvider.Environment, viewModel);
        VM_AttributeGroupMenu.GetViewModelFromModels(model.AttributeGroups, viewModel.AttributeGroupMenu);
        viewModel.OverwritePluginAttGroups = model.OverwritePluginAttGroups;            

        viewModel.bLoadSettingsFromDataFolder = PatcherSettings.LoadFromDataFolder;
        viewModel.SkyrimVersion = PatcherSettings.SkyrimVersion;
        viewModel.CustomGamePath = PatcherSettings.CustomGamePath;
        viewModel.PortableSettingsFolder = PatcherSettings.PortableSettingsFolder;
    }
    public static void DumpViewModelToModel(VM_Settings_General viewModel, Settings_General model)
    {
        model.bShowToolTips = viewModel.bShowToolTips;
        model.bChangeMeshesOrTextures = viewModel.bChangeMeshesOrTextures;
        model.BodySelectionMode = viewModel.BodySelectionMode;
        model.BSSelectionMode = viewModel.BSSelectionMode;
        model.bChangeHeight = viewModel.bChangeHeight;
        model.OutputDataFolder = viewModel.OutputDataFolder.TargetPath;
        model.bEnableConsistency = viewModel.bEnableConsistency;
        model.ExcludePlayerCharacter = viewModel.ExcludePlayerCharacter;
        model.ExcludePresets = viewModel.ExcludePresets;
        model.bLinkNPCsWithSameName = viewModel.bLinkNPCsWithSameName;
        model.PatchFileName = viewModel.patchFileName;
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

        PatcherSettings.General = model;

        PatcherSettings.LoadFromDataFolder = viewModel.bLoadSettingsFromDataFolder;
        PatcherSettings.SkyrimVersion = viewModel.SkyrimVersion;
        PatcherSettings.CustomGamePath = viewModel.CustomGamePath;
        PatcherSettings.PortableSettingsFolder = viewModel.PortableSettingsFolder;
    }

    public void ToggleTooltipVisibility(object sender, PropertyChangedEventArgs e)
    {
        switch(this.bShowToolTips)
        {
            case true:
                TooltipController.Instance.DisplayToolTips = true;
                break;
            case false:
                TooltipController.Instance.DisplayToolTips = false;
                break;
        }
    }   
}