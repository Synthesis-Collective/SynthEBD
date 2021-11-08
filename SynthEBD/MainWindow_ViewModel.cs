using Mutagen.Bethesda;
using Mutagen.Bethesda.Cache.Implementations;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SynthEBD
{
    public class MainWindow_ViewModel : INotifyPropertyChanged
    {
        public GameEnvironmentProvider GameEnvironmentProvider { get; }
        public Paths Paths { get; }
        public VM_Settings_General SGVM { get; } = new();
        public VM_SettingsTexMesh TMVM { get; }
        public VM_SettingsBodyGen BGVM { get; }
        public VM_SettingsHeight HVM { get; } = new();
        public VM_SpecificNPCAssignmentsUI SAUIVM { get; }
        public VM_BlockListUI BUIVM { get; } = new();

        public VM_NavPanel NavPanel { get; }

        public VM_RunButton RunButton { get; }
        public object DisplayedViewModel { get; set; }
        public object NavViewModel { get; set; }

        public object StatusBarVM { get; set; }

        public VM_LogDisplay LogDisplayVM { get; set; } = new();
        public List<AssetPack> AssetPacks { get; }

        public Settings_General GeneralSettings { get; }
        public Settings_TexMesh TexMeshSettings { get; }
        public Settings_Height HeightSettings { get; }
        public List<HeightConfig> HeightConfigs { get; }

        public Settings_BodyGen BodyGenSettings { get; }
        public BodyGenConfigs BodyGenConfigs { get; }
        public HashSet<SpecificNPCAssignment> SpecificNPCAssignments { get; }
        public BlockList BlockList { get; }
        public HashSet<string> LinkedNPCNameExclusions { get; set; }
        public HashSet<LinkedNPCGroup> LinkedNPCGroups { get; set; }
        public HashSet<TrimPath> TrimPaths { get; set; }

        public List<SkyrimMod> RecordTemplatePlugins { get; set; }
        public ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> RecordTemplateLinkCache { get; set; }

        public MainWindow_ViewModel()
        {
            var gameRelease = SkyrimRelease.SkyrimSE;
            var env = GameEnvironment.Typical.Skyrim(gameRelease, LinkCachePreferences.OnlyIdentifiers());
            var LinkCache = env.LinkCache;
            var LoadOrder = env.LoadOrder;

            BGVM = new VM_SettingsBodyGen(SGVM.RaceGroupings);
            TMVM = new VM_SettingsTexMesh(BGVM);
            SAUIVM = new VM_SpecificNPCAssignmentsUI(TMVM, BGVM);

            NavPanel = new SynthEBD.VM_NavPanel(this, SGVM, TMVM, BGVM, HVM, SAUIVM, BUIVM);

            StatusBarVM = new VM_StatusBar();

            RunButton = new VM_RunButton(this);

            // Load general settings
            GeneralSettings = SettingsIO_General.loadGeneralSettings();
            VM_Settings_General.GetViewModelFromModel(SGVM, GeneralSettings);

            // get paths
            Paths = new Paths(GeneralSettings.bLoadSettingsFromDataFolder);

            // Load texture and mesh settings
            RecordTemplatePlugins = SettingsIO_AssetPack.LoadRecordTemplates(Paths);
            RecordTemplateLinkCache = RecordTemplatePlugins.ToImmutableLinkCache();
            TexMeshSettings = SettingsIO_AssetPack.LoadTexMeshSettings(Paths);
            VM_SettingsTexMesh.GetViewModelFromModel(TMVM, TexMeshSettings);

            // load bodygen configs before asset packs - asset packs depend on BodyGen but not vice versa
            BodyGenSettings = SettingsIO_BodyGen.LoadBodyGenSettings(Paths);
            BodyGenConfigs = SettingsIO_BodyGen.loadBodyGenConfigs(GeneralSettings.RaceGroupings, Paths);
            VM_SettingsBodyGen.GetViewModelFromModel(BodyGenConfigs, BodyGenSettings, BGVM, SGVM.RaceGroupings);

            // load asset packs
            List<string> loadedAssetPackPaths = new List<string>();
            AssetPacks = SettingsIO_AssetPack.loadAssetPacks(GeneralSettings.RaceGroupings, Paths, loadedAssetPackPaths, RecordTemplatePlugins, BodyGenConfigs); // load asset pack models from json
            TMVM.AssetPacks = VM_AssetPack.GetViewModelsFromModels(AssetPacks, SGVM, TexMeshSettings, loadedAssetPackPaths, BGVM, RecordTemplateLinkCache); // add asset pack view models to TexMesh shell view model here

            // load heights
            HeightSettings = SettingsIO_Height.LoadHeightSettings(Paths);
            List<string> loadedHeightPaths = new List<string>();
            HeightConfigs = SettingsIO_Height.loadHeightConfigs(Paths, loadedHeightPaths);
            VM_HeightConfig.GetViewModelsFromModels(HVM.AvailableHeightConfigs, HeightConfigs, loadedHeightPaths);
            VM_SettingsHeight.GetViewModelFromModel(HVM, HeightSettings); /// must do after populating configs

            // load specific assignments
            SpecificNPCAssignments = SettingsIO_SpecificNPCAssignments.LoadAssignments(Paths);
            VM_SpecificNPCAssignmentsUI.GetViewModelFromModels(SAUIVM, SpecificNPCAssignments);

            // load BlockList
            BlockList = SettingsIO_BlockList.LoadBlockList(Paths);
            VM_BlockListUI.GetViewModelFromModel(BlockList, BUIVM);

            // load Misc settings
            LinkedNPCNameExclusions = SettingsIO_Misc.LoadNPCNameExclusions(Paths);
            SGVM.LinkedNameExclusions = VM_CollectionMemberString.InitializeCollectionFromHashSet(LinkedNPCNameExclusions);
            LinkedNPCGroups = SettingsIO_Misc.LoadLinkedNPCGroups(Paths);
            SGVM.LinkedNPCGroups = VM_LinkedNPCGroup.GetViewModelsFromModels(LinkedNPCGroups);
            TrimPaths = SettingsIO_Misc.LoadTrimPaths(Paths);
            TMVM.TrimPaths = new ObservableCollection<TrimPath>(TrimPaths);

            // Start on the settings VM
            DisplayedViewModel = SGVM;
            NavViewModel = NavPanel;
            Logger.Instance.RunButton = RunButton;

            Application.Current.MainWindow.Closing += new CancelEventHandler(MainWindow_Closing);
        }

        void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            VM_Settings_General.DumpViewModelToModel(SGVM, GeneralSettings);
            SerializeToJSON<Settings_General>.SaveJSONFile(GeneralSettings, Paths.GeneralSettingsPath);

            VM_SettingsTexMesh.DumpViewModelToModel(TMVM, TexMeshSettings);
            SerializeToJSON<Settings_TexMesh>.SaveJSONFile(TexMeshSettings, Paths.TexMeshSettingsPath);
            var assetPackPaths = VM_AssetPack.DumpViewModelsToModels(TMVM.AssetPacks, AssetPacks, Paths);
            SettingsIO_AssetPack.SaveAssetPacks(AssetPacks, assetPackPaths, Paths);

            // Need code here to dump assset packs and save - see height configs for analogy

            VM_SettingsHeight.DumpViewModelToModel(HVM, HeightSettings);
            SerializeToJSON<Settings_Height>.SaveJSONFile(HeightSettings, Paths.HeightSettingsPath);
            var heightConfigPaths = VM_HeightConfig.DumpViewModelsToModels(HVM.AvailableHeightConfigs, HeightConfigs);
            SettingsIO_Height.SaveHeightConfigs(HeightConfigs, heightConfigPaths, Paths);

            VM_SettingsBodyGen.DumpViewModelToModel(BGVM, BodyGenSettings);
            SerializeToJSON<Settings_BodyGen>.SaveJSONFile(BodyGenSettings, Paths.BodyGenSettingsPath);
            // Need code here to dump assset packs and save - see height configs for analogy

            VM_SpecificNPCAssignmentsUI.DumpViewModelToModels(SAUIVM, SpecificNPCAssignments);
            SerializeToJSON<HashSet<SpecificNPCAssignment>>.SaveJSONFile(SpecificNPCAssignments, Paths.SpecificNPCAssignmentsPath);

            VM_LinkedNPCGroup.DumpViewModelsToModels(LinkedNPCGroups, SGVM.LinkedNPCGroups);
            SerializeToJSON<HashSet<LinkedNPCGroup>>.SaveJSONFile(LinkedNPCGroups, Paths.LinkedNPCsPath);

            SerializeToJSON<HashSet<string>>.SaveJSONFile(SGVM.LinkedNameExclusions.Select(cms => cms.Content).ToHashSet(), Paths.LinkedNPCNameExclusionsPath);

            SerializeToJSON<HashSet<TrimPath>>.SaveJSONFile(TMVM.TrimPaths.ToHashSet(), Paths.TrimPathsPath);
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

}
