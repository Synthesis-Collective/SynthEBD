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
        public VM_AssetPackSettings APVM { get; } = new();
        public VM_BodyGenSettings BGVM { get; } = new();
        public VM_HeightSettings HVM { get; } = new();

        public VM_NavPanel NavPanel { get; }

        public object DisplayedViewModel { get; set; }
        public object NavViewModel { get; set; }


        public Settings_General generalSettings { get; }

        public MainWindow_ViewModel()
        {
            var gameRelease = SkyrimRelease.SkyrimSE;
            var env = GameEnvironment.Typical.Skyrim(gameRelease, LinkCachePreferences.OnlyIdentifiers());
            var LinkCache = env.LinkCache;
            var LoadOrder = env.LoadOrder;

            NavPanel = new SynthEBD.VM_NavPanel(this, SGVM, APVM, BGVM, HVM);

            // Start on the settings VM
            
            generalSettings = SettingsIO_General.loadGeneralSettings();
            VM_Settings_General.GetViewModelFromModel(SGVM, generalSettings);

            DisplayedViewModel = SGVM;
            NavViewModel = NavPanel;

            Application.Current.MainWindow.Closing += new CancelEventHandler(MainWindow_Closing);
        }

        void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            VM_Settings_General.DumpViewModelToModel(SGVM, generalSettings);
            SettingsIO_General.saveGeneralSettings(generalSettings);
        }


        public event PropertyChangedEventHandler PropertyChanged;


    }

}
