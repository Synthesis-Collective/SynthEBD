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
        public VM_BodyGenSettings BGVM { get; } = new();
        public VM_SettingsHeight HVM { get; } = new();

        public VM_NavPanel NavPanel { get; }

        public object DisplayedViewModel { get; set; }
        public object NavViewModel { get; set; }

        public List<AssetPack> assetPacks { get; }

        public Settings_General generalSettings { get; }
        public Settings_TexMesh texMeshSettings { get; }
        public Settings_Height heightSettings { get; }
        public HashSet<HeightConfig> heightConfigs { get; }

        public MainWindow_ViewModel()
        {
            var gameRelease = SkyrimRelease.SkyrimSE;
            var env = GameEnvironment.Typical.Skyrim(gameRelease, LinkCachePreferences.OnlyIdentifiers());
            var LinkCache = env.LinkCache;
            var LoadOrder = env.LoadOrder;

            NavPanel = new SynthEBD.VM_NavPanel(this, SGVM, TMVM, BGVM, HVM);

            // Load general settings
            generalSettings = SettingsIO_General.loadGeneralSettings();
            VM_Settings_General.GetViewModelFromModel(SGVM, generalSettings);

            // get paths
            Paths = new Paths(generalSettings.bLoadSettingsFromDataFolder);

            // Load texture and mesh settings
            texMeshSettings = SettingsIO_AssetPack.LoadTexMeshSettings(Paths);
            VM_SettingsTexMesh.GetViewModelFromModel(TMVM, texMeshSettings);

            // load asset packs
            List<string> assetPackPaths = new List<string>();
            assetPacks = SettingsIO_AssetPack.loadAssetPacks(generalSettings.RaceGroupings, assetPackPaths, Paths); // load asset pack models from json
            TMVM.AssetPacks = VM_AssetPack.GetViewModelsFromModels(assetPacks, assetPackPaths, SGVM); // add asset pack view models to TexMesh shell view model here

            // load heights
            heightSettings = SettingsIO_Height.LoadHeightSettings(Paths);
            VM_SettingsHeight.GetViewModelFromModel(HVM, heightSettings, LinkCache);
            heightConfigs = SettingsIO_Height.loadHeightConfig(Paths.HeightConfigCurrentPath);
            HVM.HeightConfigs = VM_HeightConfig.GetViewModelsFromModels(heightConfigs, LinkCache);

            // Start on the settings VM
            DisplayedViewModel = SGVM;
            NavViewModel = NavPanel;

            Application.Current.MainWindow.Closing += new CancelEventHandler(MainWindow_Closing);
        }

        void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            VM_Settings_General.DumpViewModelToModel(SGVM, generalSettings);
            SerializeToJSON<Settings_General>.SaveJSONFile(generalSettings, Paths.GeneralSettingsPath);

            VM_SettingsTexMesh.DumpViewModelToModel(TMVM, texMeshSettings);
            SerializeToJSON<Settings_TexMesh>.SaveJSONFile(texMeshSettings, Paths.TexMeshSettingsPath);

            VM_SettingsHeight.DumpViewModelToModel(HVM, heightSettings);
            SerializeToJSON<Settings_Height>.SaveJSONFile(heightSettings, Paths.HeightSettingsPath);
            VM_HeightConfig.DumpViewModelsToModels(heightConfigs, HVM.HeightConfigs);
            SerializeToJSON<HashSet<HeightConfig>>.SaveJSONFile(heightConfigs, Paths.HeightConfigCurrentPath);
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

}
