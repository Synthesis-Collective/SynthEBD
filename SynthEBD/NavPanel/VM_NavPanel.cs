using System.Windows.Input;
using Noggog.WPF;

namespace SynthEBD;

public class VM_NavPanel : ViewModel
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
            execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.GeneralSettingsVM
        );

        ClickTM = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.TexMeshSettingsVM
        ) ;
        ClickBG = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.BodyGenSettingsVM
        );
        ClickOB = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.OBodySettingsVM
        );
        ClickH = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.HeightSettingsVM
        );
        ClickSA = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.SpecificAssignmentsUIVM
        );
        ClickC = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.ConsistencyUIVM
        );
        ClickBL = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.BlockListVM
        );
        ClickLog = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.LogDisplayVM
        );
        ClickMM = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => mainWindowVM.DisplayedViewModel = mainWindowVM.ModManagerSettingsVM
        );
    }
}