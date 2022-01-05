using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SynthEBD
{
    public class VM_NavPanel : INotifyPropertyChanged
    {
        public ICommand ClickSG { get; }
        public ICommand ClickTM { get; }
        public ICommand ClickBG { get; }
        public ICommand ClickH { get; }
        public ICommand ClickSA { get; }
        public ICommand ClickC { get; }
        public ICommand ClickBL { get; }
        public ICommand ClickLog { get; }
        public ICommand ClickMM { get; }

        public VM_NavPanel(MainWindow_ViewModel MWVM, VM_Settings_General SGVM, VM_SettingsTexMesh TMVM, VM_SettingsBodyGen BGVM, VM_SettingsHeight HVM, VM_SpecificNPCAssignmentsUI SAUIVM, VM_ConsistencyUI CUIVM, VM_BlockListUI BUIVM, VM_LogDisplay LogVM, VM_SettingsModManager MMVM)
        {
            ClickSG = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => MWVM.DisplayedViewModel = SGVM
                );

            ClickTM = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => MWVM.DisplayedViewModel = TMVM
                ) ;
            ClickBG = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => MWVM.DisplayedViewModel = BGVM
                );
            ClickH = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => MWVM.DisplayedViewModel = HVM
                );
            ClickSA = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => MWVM.DisplayedViewModel = SAUIVM
                );
            ClickC = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => MWVM.DisplayedViewModel = CUIVM
                );
            ClickBL = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => MWVM.DisplayedViewModel = BUIVM
                );
            ClickLog = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => MWVM.DisplayedViewModel = LogVM
                );
            ClickMM = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => MWVM.DisplayedViewModel = MMVM
                );
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}

