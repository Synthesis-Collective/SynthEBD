using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using Mutagen.Bethesda.Environments;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

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
                dialog.Filter = "Text files (.txt|*.txt"; // Filter files by extension

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

    public void PrintState()
    {
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

    public void SwitchViewToLogDisplay()
    {
        _displayedItemVm.DisplayedViewModel = this;
    }
}