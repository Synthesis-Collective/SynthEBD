using Mutagen.Bethesda;
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
    class MainWindow_ViewModel : INotifyPropertyChanged
    {
        public GameEnvironmentProvider GameEnvironmentProvider { get; }
        public Paths Paths { get; }
        public VM_Settings_General SGVM { get; } = new();
        public VM_SettingsTexMesh TMVM { get; } = new();
        public VM_SettingsBodyGen BGVM { get; }
        public VM_SettingsHeight HVM { get; } = new();
        public VM_SpecificNPCAssignmentsUI SAUIVM { get; }
        public VM_BlockListUI BUIVM { get; } = new();

        public VM_NavPanel NavPanel { get; }

        public object DisplayedViewModel { get; set; }
        public object NavViewModel { get; set; }

        public List<AssetPack> AssetPacks { get; }

        public Settings_General GeneralSettings { get; }
        public Settings_TexMesh TexMeshSettings { get; }
        public Settings_Height HeightSettings { get; }
        public HashSet<HeightConfig> HeightConfigs { get; }

        public Settings_BodyGen BodyGenSettings { get; }
        public BodyGenConfigs BodyGenConfigs { get; }
        public HashSet<SpecificNPCAssignment> SpecificNPCAssignments { get; }
        public BlockList BlockList { get; }


        public MainWindow_ViewModel()
        {
            var gameRelease = SkyrimRelease.SkyrimSE;
            var env = GameEnvironment.Typical.Skyrim(gameRelease, LinkCachePreferences.OnlyIdentifiers());
            var LinkCache = env.LinkCache;
            var LoadOrder = env.LoadOrder;

            BGVM = new VM_SettingsBodyGen(SGVM.RaceGroupings);
            SAUIVM = new VM_SpecificNPCAssignmentsUI(TMVM, BGVM);

            NavPanel = new SynthEBD.VM_NavPanel(this, SGVM, TMVM, BGVM, HVM, SAUIVM, BUIVM);

            // Load general settings
            GeneralSettings = SettingsIO_General.loadGeneralSettings();
            VM_Settings_General.GetViewModelFromModel(SGVM, GeneralSettings);

            // get paths
            Paths = new Paths(GeneralSettings.bLoadSettingsFromDataFolder);

            // Load texture and mesh settings
            TexMeshSettings = SettingsIO_AssetPack.LoadTexMeshSettings(Paths);
            VM_SettingsTexMesh.GetViewModelFromModel(TMVM, TexMeshSettings);

            // load asset packs
            List<string> assetPackPaths = new List<string>();
            AssetPacks = SettingsIO_AssetPack.loadAssetPacks(GeneralSettings.RaceGroupings, assetPackPaths, Paths); // load asset pack models from json
            TMVM.AssetPacks = VM_AssetPack.GetViewModelsFromModels(AssetPacks, assetPackPaths, SGVM); // add asset pack view models to TexMesh shell view model here

            // load heights
            HeightSettings = SettingsIO_Height.LoadHeightSettings(Paths);
            VM_SettingsHeight.GetViewModelFromModel(HVM, HeightSettings, LinkCache);
            HeightConfigs = SettingsIO_Height.loadHeightConfig(Paths.HeightConfigCurrentPath);
            HVM.HeightConfigs = VM_HeightConfig.GetViewModelsFromModels(HeightConfigs, LinkCache);

            // load bodygen configs
            BodyGenSettings = SettingsIO_BodyGen.LoadBodyGenSettings(Paths);
            BodyGenConfigs = SettingsIO_BodyGen.loadBodyGenConfigs(GeneralSettings.RaceGroupings, Paths);
            VM_SettingsBodyGen.GetViewModelFromModel(BodyGenConfigs, BodyGenSettings, BGVM, SGVM.RaceGroupings);

            // load specific assignments
            SpecificNPCAssignments = SettingsIO_SpecificNPCAssignments.LoadAssignments(Paths);
            VM_SpecificNPCAssignmentsUI.GetViewModelFromModels(SAUIVM, SpecificNPCAssignments);

            // load BlockList
            BlockList = SettingsIO_BlockList.LoadBlockList(Paths);
            VM_BlockListUI.GetViewModelFromModel(BlockList, BUIVM);

            // Start on the settings VM
            DisplayedViewModel = SGVM;
            NavViewModel = NavPanel;

            Application.Current.MainWindow.Closing += new CancelEventHandler(MainWindow_Closing);
        }

        void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            VM_Settings_General.DumpViewModelToModel(SGVM, GeneralSettings);
            SerializeToJSON<Settings_General>.SaveJSONFile(GeneralSettings, Paths.GeneralSettingsPath);

            VM_SettingsTexMesh.DumpViewModelToModel(TMVM, TexMeshSettings);
            SerializeToJSON<Settings_TexMesh>.SaveJSONFile(TexMeshSettings, Paths.TexMeshSettingsPath);

            VM_SettingsHeight.DumpViewModelToModel(HVM, HeightSettings);
            SerializeToJSON<Settings_Height>.SaveJSONFile(HeightSettings, Paths.HeightSettingsPath);
            VM_HeightConfig.DumpViewModelsToModels(HeightConfigs, HVM.HeightConfigs);
            SerializeToJSON<HashSet<HeightConfig>>.SaveJSONFile(HeightConfigs, Paths.HeightConfigCurrentPath);

            VM_SettingsBodyGen.DumpViewModelToModel(BGVM, BodyGenSettings);
            SerializeToJSON<Settings_BodyGen>.SaveJSONFile(BodyGenSettings, Paths.BodyGenSettingsPath);
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

}
