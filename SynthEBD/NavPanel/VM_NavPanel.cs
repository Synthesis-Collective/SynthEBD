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
        public ICommand ClickOB { get; }
        public ICommand ClickH { get; }
        public ICommand ClickSA { get; }
        public ICommand ClickC { get; }
        public ICommand ClickBL { get; }
        public ICommand ClickLog { get; }
        public ICommand ClickMM { get; }

        public VM_NavPanel(MainWindow_ViewModel mainWindowVM)
        {
            ClickSG = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.SGVM
                );

            ClickTM = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.TMVM
                ) ;
            ClickBG = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.BGVM
                );
            ClickOB = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.OBVM
                );
            ClickH = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.HVM
                );
            ClickSA = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.SAUIVM
                );
            ClickC = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.CUIVM
                );
            ClickBL = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.BUIVM
                );
            ClickLog = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.LogDisplayVM
                );
            ClickMM = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.MMVM
                );
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}

