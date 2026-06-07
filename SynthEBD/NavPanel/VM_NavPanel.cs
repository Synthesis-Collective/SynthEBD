using System.Windows.Input;

namespace SynthEBD;

/// <summary>
/// Backs the left-hand navigation panel. Each <c>Click*</c> command swaps the shared
/// <see cref="DisplayedItemVm.DisplayedViewModel"/> to the corresponding settings view model
/// (General, TexMesh, BodyGen, OBody, Height, HeadParts, Specific Assignments, Consistency,
/// Block List, Log, Mod Manager). Registered as a singleton in <see cref="MainModule"/>.
/// </summary>
public class VM_NavPanel : VM
{
    public ICommand ClickSG { get; }
    public ICommand ClickTM { get; }
    public ICommand ClickBG { get; }
    public ICommand ClickOB { get; }
    public ICommand ClickH { get; }
    public ICommand ClickHP { get; }
    public ICommand ClickSA { get; }
    public ICommand ClickC { get; }
    public ICommand ClickBL { get; }
    public ICommand ClickLog { get; }
    public ICommand ClickMM { get; }

    /// <summary>SynthEBD version string shown in the nav panel (from <see cref="PatcherState.Version"/>).</summary>
    public string Version { get; }

    private readonly VM_Settings_General _settingsGeneral;
    private readonly DisplayedItemVm _displayedItemVM;

    /// <summary>
    /// Captures the target settings view models and builds the navigation
    /// <see cref="RelayCommand"/>s, each of which sets the shared display slot to its VM.
    /// </summary>
    public VM_NavPanel(
        DisplayedItemVm displayedItemVm,
        VM_Settings_General settingsGeneral,
        PatcherState patcherState,
        VM_SettingsTexMesh texMesh,
        VM_SettingsBodyGen bodyGenSettingsVm,
        VM_SettingsOBody oBody,
        VM_SettingsHeight height,
        VM_Settings_Headparts headparts,
        VM_SpecificNPCAssignmentsUI specificNpcAssignmentsUi,
        VM_ConsistencyUI consistencyUi,
        VM_LogDisplay logDisplay,
        VM_BlockListUI blockListUi,
        VM_SettingsModManager modManager)
    {
        _settingsGeneral = settingsGeneral;
        _displayedItemVM = displayedItemVm;
        Version = PatcherState.Version;

        ClickSG = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = settingsGeneral
        );

        ClickTM = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = texMesh
        ) ;
        ClickBG = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = bodyGenSettingsVm
        );
        ClickOB = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = oBody
        );
        ClickH = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = height
        );
        ClickHP = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = headparts
        );
        ClickSA = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = specificNpcAssignmentsUi
        );
        ClickC = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = consistencyUi
        );
        ClickBL = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = blockListUi
        );
        ClickLog = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = logDisplay
        );
        ClickMM = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = modManager
        );
    }

    /// <summary>Navigates to the General Settings view (the main menu).</summary>
    public void GoToMainMenu()
    {
        _displayedItemVM.DisplayedViewModel = _settingsGeneral;
    }
}