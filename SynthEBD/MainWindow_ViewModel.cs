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
        public GUI_Aux.GameEnvironmentProvider GameEnvironmentProvider { get; }
        public Settings_General.VM_Settings_General SGVM { get; } = new(new Settings_General.Settings_General());
        public Settings_AssetPack.VM_AssetPackSettings APVM { get; } = new();
        public Settings_BodyGen.VM_BodyGenSettings BGVM { get; } = new();
        public Settings_Height.VM_HeightSettings HVM { get; } = new();

        public NavPanel.VM_NavPanel NavPanel { get; }

        public object DisplayedViewModel { get; set; }
        public object NavViewModel { get; set; }


        public Settings_General.Settings_General generalSettings { get; }

        public MainWindow_ViewModel()
        {
            var gameRelease = SkyrimRelease.SkyrimSE;
            var env = GameEnvironment.Typical.Skyrim(gameRelease, LinkCachePreferences.OnlyIdentifiers());
            var LinkCache = env.LinkCache;
            var LoadOrder = env.LoadOrder;

            NavPanel = new SynthEBD.NavPanel.VM_NavPanel(this, SGVM, APVM, BGVM, HVM);

            // Start on the settings VM
            
            generalSettings = SettingsIO.SettingsIO_General.loadGeneralSettings();
            SGVM = new Settings_General.VM_Settings_General(generalSettings);

            DisplayedViewModel = SGVM;
            NavViewModel = NavPanel;

            Application.Current.MainWindow.Closing += new CancelEventHandler(MainWindow_Closing);
        }

        void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            Settings_General.VM_Settings_General.DumpViewModelToModel(SGVM, generalSettings);
            SettingsIO.SettingsIO_General.saveGeneralSettings(generalSettings);
        }


        public event PropertyChangedEventHandler PropertyChanged;


    }

}
