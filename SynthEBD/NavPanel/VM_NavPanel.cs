using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SynthEBD.NavPanel
{
    class VM_NavPanel : INotifyPropertyChanged
    {
        public ICommand ClickSG { get; }
        public ICommand ClickTM { get; }
        public ICommand ClickBG { get; }
        public ICommand ClickH { get; }
        public Settings_General.VM_Settings_General SettingsGeneralVm { get; } = new( new Settings_General.Settings_General() );
        public Settings_AssetPack.VM_AssetPackSettings APVm { get; } = new();
        public Settings_BodyGen.VM_BodyGenSettings BGvm { get; } = new();
        public Settings_Height.VM_HeightSettings Hvm { get; } = new();

        public VM_NavPanel(MainWindow_ViewModel MWVM)
        {
            ClickSG = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => MWVM.DisplayedViewModel = SettingsGeneralVm
                );

            ClickTM = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => MWVM.DisplayedViewModel = APVm
                ) ;
            ClickBG = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => MWVM.DisplayedViewModel = BGvm
                );
            ClickH = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => MWVM.DisplayedViewModel = Hvm
                );
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}

