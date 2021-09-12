using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
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
        public VM_Settings_General SGVM { get; } = new();
        public VM_SettingsTexMesh TMVM { get; } = new();
        public VM_BodyGenSettings BGVM { get; } = new();
        public VM_HeightSettings HVM { get; } = new();

        public VM_NavPanel NavPanel { get; }

        public object DisplayedViewModel { get; set; }
        public object NavViewModel { get; set; }

        public List<AssetPack> assetPacks { get; }

        public Settings_General generalSettings { get; }
        public Settings_TexMesh texMeshSettings { get; }

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

            // Load texture and mesh settings
            texMeshSettings = SettingsIO_AssetPack.LoadTexMeshSettings();
            VM_SettingsTexMesh.GetViewModelFromModel(TMVM, texMeshSettings);

            // load asset packs
            List<string> assetPackPaths = new List<string>();
            assetPacks = SettingsIO_AssetPack.loadAssetPacks(generalSettings.RaceGroupings, assetPackPaths); // load asset pack models from json
            TMVM.AssetPacks = VM_AssetPack.GetViewModelsFromModels(assetPacks, assetPackPaths); // add asset pack view models to TexMesh shell view model here


            // Start on the settings VM
            DisplayedViewModel = SGVM;
            NavViewModel = NavPanel;

            Application.Current.MainWindow.Closing += new CancelEventHandler(MainWindow_Closing);
        }

        void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            VM_Settings_General.DumpViewModelToModel(SGVM, generalSettings);
            SerializeToJSON<Settings_General>.SaveJSONFile(generalSettings, "Settings\\GeneralSettings.json");

            VM_SettingsTexMesh.DumpViewModelToModel(TMVM, texMeshSettings);
            SerializeToJSON<Settings_General>.SaveJSONFile(generalSettings, "Settings\\TexMeshSettings.json");
        }


        public event PropertyChangedEventHandler PropertyChanged;


    }

}
