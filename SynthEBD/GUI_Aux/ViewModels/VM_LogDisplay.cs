using System.ComponentModel;
using DynamicData;
using Mutagen.Bethesda.Environments;
using Noggog;

namespace SynthEBD;

public class VM_LogDisplay : VM
{
    private readonly IStateProvider _stateProvider;
    private readonly Logger _logger;
    private readonly DisplayedItemVm _displayedItemVm;
    public string DispString { get; set; } = "";

    public RelayCommand Clear { get; set; }
    public RelayCommand Copy { get; set; }
    public RelayCommand Save { get; set; }
    public RelayCommand ShowEnvironment { get; set; }

    public VM_LogDisplay(
        IStateProvider stateProvider,
        Logger logger,
        DisplayedItemVm displayedItemVm)
    {
        _stateProvider = stateProvider;
        _logger = logger;
        _displayedItemVm = displayedItemVm;
        _logger.PropertyChanged += RefreshDisp;
        
        // Switch to log display if any errors
        _logger.LoggedError.Subscribe(_ =>
        {
            SwitchViewToLogDisplay();
        });

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
    }

    public void RefreshDisp(object sender, PropertyChangedEventArgs e)
    {
        this.DispString = _logger.LogString;
    }

    public void PrintState()
    {
        /*
        _logger.LogString += "Data Folder: " + _stateProvider.DataFolderPath + Environment.NewLine;
        _logger.LogString += "Load Order Source: " + _stateProvider.LoadOrderFilePath + Environment.NewLine;
        _logger.LogString += "Creation Club Listings: " + _stateProvider.CreationClubListingsFilePath + Environment.NewLine;
        _logger.LogString += "Game Release: " + _stateProvider.SkyrimVersion.ToString() + Environment.NewLine;
        _logger.LogString += "Load Order: " + Environment.NewLine;
        */
        _logger.LogMessage("Data Folder: " + _stateProvider.DataFolderPath);
        _logger.LogMessage("Load Order Source: " + _stateProvider.LoadOrderFilePath);
        _logger.LogMessage("Creation Club Listings: " + _stateProvider.CreationClubListingsFilePath);
        _logger.LogMessage("Game Release: " + _stateProvider.SkyrimVersion.ToString());
        _logger.LogMessage("Load Order: ");


        foreach (var mod in _stateProvider.LoadOrder.ListedOrder)
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
            //_logger.LogString += dispStr + Environment.NewLine;
            _logger.LogMessage(dispStr);
        }
        DispString = _logger.LogString;
    }

    public void SwitchViewToLogDisplay()
    {
        _displayedItemVm.DisplayedViewModel = this;
    }
}