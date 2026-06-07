using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using Mutagen.Bethesda.Environments;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// Backs the log panel: mirrors the <see cref="Logger"/>'s accumulated events into a display string and
/// exposes commands to clear, copy, save, dump environment state, and open the log folder. Switches the
/// active view to itself whenever an error is logged.
/// </summary>
public class VM_LogDisplay : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly DisplayedItemVm _displayedItemVm;
    public string DispString { get; set; } = "";
    public RelayCommand Clear { get; set; }
    public RelayCommand Copy { get; set; }
    public RelayCommand Save { get; set; }
    public RelayCommand ShowEnvironment { get; set; }
    public RelayCommand OpenLogFolder { get; set; }

    /// <summary>
    /// Wires the live log subscription, the auto-switch-on-error subscription, and the Clear/Copy/Save/ShowEnvironment/OpenLogFolder commands.
    /// </summary>
    public VM_LogDisplay(
        IEnvironmentStateProvider environmentProvider,
        PatcherState patcherState,
        Logger logger,
        SynthEBDPaths paths,
        DisplayedItemVm displayedItemVm)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _paths = paths;
        _displayedItemVm = displayedItemVm;

        _logger.LoggedEvents.ToObservableChangeSet().Subscribe(x => DispString = String.Join(Environment.NewLine, _logger.LoggedEvents.ToList())).DisposeWith(this);
        
        // Switch to log display if any errors
        _logger.LoggedError.Subscribe(_ =>
        {
            SwitchViewToLogDisplay();
        }).DisposeWith(this);

        Clear = new RelayCommand(
            canExecute: _ => true,
            execute: x => _logger.Clear()
        );

        Copy = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                try
                {
                    System.Windows.Clipboard.SetText(_logger.LogString);
                }
                catch
                {
                    _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not copy log to clipboard", ErrorType.Error, 3);
                }
            }
        );

        Save = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog();
                dialog.DefaultExt = ".txt"; // Default file extension
                dialog.Filter = "Text files (*.txt)|*.txt"; // Filter files by extension

                // Show open file dialog box
                bool? result = dialog.ShowDialog();

                // Process open file dialog box results
                if (result == true)
                {
                    try
                    {
                        System.IO.File.WriteAllText(dialog.FileName, _logger.LogString);
                    }
                    catch
                    {
                        _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not write log to file", ErrorType.Error, 3);
                    }
                }
            }
        );

        ShowEnvironment = new RelayCommand(
            canExecute: _ => true,
            execute: x => PrintState()
        );

        OpenLogFolder = new RelayCommand(
            canExecute: _ => true,
            execute: x => WinExplorerOpener.OpenFolder(_paths.LogFolderPath)
        );
    }

    /// <summary>Logs the current patcher mode, patcher state, environment paths, game release, and full load order.</summary>
    public void PrintState()
    {
        _logger.LogMessage("Patcher Mode: " + GetPatcherModeString());
        _logger.LogMessage(_patcherState.GetStateLogStr());
        _logger.LogMessage("Data Folder: " + _environmentProvider.DataFolderPath);
        _logger.LogMessage("Load Order Source: " + _environmentProvider.LoadOrderFilePath);
        _logger.LogMessage("Creation Club Listings: " + _environmentProvider.CreationClubListingsFilePath);
        _logger.LogMessage("Game Release: " + _environmentProvider.SkyrimVersion.ToString());
        _logger.LogMessage("Load Order: ");


        foreach (var mod in _environmentProvider.LoadOrder.ListedOrder)
        {
            var dispStr = "(";
            if (mod.Enabled)
            {
                dispStr += "+) ";
            }
            else
            {
                dispStr += "-) ";
            }
            dispStr += mod.ModKey.FileName;
            _logger.LogMessage(dispStr);
        }
    }

    /// <summary>Maps the environment's run mode to a display string ("Standalone", "Synthesis", or "Unknown").</summary>
    private string GetPatcherModeString()
    {
        switch (_environmentProvider.RunMode) {
            case EnvironmentMode.Standalone: return "Standalone";
            case EnvironmentMode.Synthesis: return "Synthesis";
            default: return "Unknown";
        }
    }

    /// <summary>Makes this log panel the displayed view model.</summary>
    public void SwitchViewToLogDisplay()
    {
        _displayedItemVm.DisplayedViewModel = this;
    }
}