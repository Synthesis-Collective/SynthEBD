using System.Windows.Input;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// Backs the left-hand navigation panel. Each <c>Click*</c> command swaps the shared
/// <see cref="DisplayedItemVm.DisplayedViewModel"/> to the corresponding settings view model
/// (General, TexMesh, BodyGen, OBody, Height, HeadParts, Specific Assignments, Consistency,
/// Block List, Log, Mod Manager). Registered as a singleton in <see cref="MainModule"/>.
/// </summary>
public class VM_NavPanel : VM
{
    public ICommand ClickDash { get; }
    public ICommand ClickSG { get; }
    public ICommand ClickTM { get; }
    public ICommand ClickCE { get; }
    public ICommand ClickDS { get; }
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

    /// <summary>Whether the BodyGen Integration nav item is shown: true when BodyGen is the system
    /// selected on the Dashboard Body Shape tile, or when the UI is in Troubleshoot mode (which
    /// reveals both body-settings menus regardless of the selection).</summary>
    public bool ShowBodyGenNav { get; private set; } = true;

    /// <summary>Whether the (O/Auto)Body Integration nav item is shown: true when BodySlide is the
    /// system selected on the Dashboard Body Shape tile, or in Troubleshoot mode.</summary>
    public bool ShowOBodyNav { get; private set; } = true;

    private readonly VM_Dashboard _dashboard;
    private readonly DisplayedItemVm _displayedItemVM;

    /// <summary>
    /// Captures the target settings view models and builds the navigation
    /// <see cref="RelayCommand"/>s, each of which sets the shared display slot to its VM.
    /// </summary>
    public VM_NavPanel(
        DisplayedItemVm displayedItemVm,
        VM_Dashboard dashboard,
        VM_Settings_General settingsGeneral,
        PatcherState patcherState,
        VM_SettingsTexMesh texMesh,
        VM_ConfigEditor configEditor,
        VM_SettingsDestandalone destandalone,
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
        _dashboard = dashboard;
        _displayedItemVM = displayedItemVm;
        Version = PatcherState.Version;

        ClickDash = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = dashboard
        );

        ClickSG = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = settingsGeneral
        );

        ClickTM = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = texMesh
        ) ;
        ClickCE = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = configEditor
        );
        ClickDS = new RelayCommand(
            canExecute: _ => true,
            execute: _ => displayedItemVm.DisplayedViewModel = destandalone
        );
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

        // Show only the body-settings menu (BodyGen vs OBody) matching the system selected on the
        // Dashboard Body Shape tile - EXCEPT in Troubleshoot mode, which reveals both. Driven by
        // LastBodySelectionMode (the Dashboard combo's value, always BodyGen or BodySlide) so the nav
        // tracks the combo even while the Body Shape switch is off.
        void RecomputeBodyNav()
        {
            bool troubleshoot = UiModeController.Instance.DisplayMode == UiDisplayMode.Troubleshoot;
            var selected = settingsGeneral.LastBodySelectionMode;
            ShowBodyGenNav = troubleshoot || selected == BodyShapeSelectionMode.BodyGen;
            ShowOBodyNav = troubleshoot || selected == BodyShapeSelectionMode.BodySlide;
        }
        settingsGeneral.WhenAnyValue(x => x.LastBodySelectionMode)
            .Subscribe(_ => RecomputeBodyNav()).DisposeWith(this);
        UiModeController.Instance.WhenAnyValue(x => x.DisplayMode)
            .Subscribe(_ => RecomputeBodyNav()).DisposeWith(this);
    }

    /// <summary>Navigates to the Dashboard (the home page).</summary>
    public void GoToMainMenu()
    {
        _displayedItemVM.DisplayedViewModel = _dashboard;
    }
}