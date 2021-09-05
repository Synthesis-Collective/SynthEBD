using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class MainWindow_ViewModel
    {
        public Settings_General.VM_Settings_General SettingsVm { get; } = new();
        public NavPanel.VM_NavPanel NavPanel { get; } = new();

        public object DisplayedViewModel { get; set; }
        public object NavViewModel { get; set; }

        public MainWindow_ViewModel()
        {
            // Start on the settings VM
            DisplayedViewModel = SettingsVm;
            NavViewModel = NavPanel;
        }
    }

}
