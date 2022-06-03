using System.Windows.Input;

namespace SynthEBD;

public class VM_NavPanel : VM
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

    public VM_NavPanel(
        DisplayedItemVm displayedItemVm,
        VM_Settings_General settingsGeneral,
        VM_SettingsTexMesh texMesh,
        VM_SettingsBodyGen bodyGenSettingsVm,
        VM_SettingsOBody oBody,
        VM_SettingsHeight height,
        VM_SpecificNPCAssignmentsUI specificNpcAssignmentsUi,
        VM_ConsistencyUI consistencyUi,
        VM_LogDisplay logDisplay,
        VM_BlockListUI blockListUi,
        VM_SettingsModManager modManager)
    {
        ClickSG = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = settingsGeneral
        );

        ClickTM = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = texMesh
        ) ;
        ClickBG = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = bodyGenSettingsVm
        );
        ClickOB = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = oBody
        );
        ClickH = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = height
        );
        ClickSA = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = specificNpcAssignmentsUi
        );
        ClickC = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = consistencyUi
        );
        ClickBL = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = blockListUi
        );
        ClickLog = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = logDisplay
        );
        ClickMM = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = modManager
        );
    }
}