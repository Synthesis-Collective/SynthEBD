using System.ComponentModel;
using Mutagen.Bethesda.Environments;

namespace SynthEBD;

public class VM_LogDisplay : VM
{
    private readonly Logger _logger;
    private readonly DisplayedItemVm _displayedItemVm;
    public string DispString { get; set; } = "";

    public RelayCommand Clear { get; set; }
    public RelayCommand Copy { get; set; }
    public RelayCommand Save { get; set; }
    public RelayCommand ShowEnvironment { get; set; }

    public VM_LogDisplay(
        Logger logger,
        DisplayedItemVm displayedItemVm)
    {
        _logger = logger;
        _displayedItemVm = displayedItemVm;
        logger.PropertyChanged += RefreshDisp;
        
        // Switch to log display if any errors
        logger.LoggedError.Subscribe(_ =>
        {
            SwitchViewToLogDisplay();
        });

        Clear = new RelayCommand(
            canExecute: _ => true,
            execute: x => logger.LogString = ""
        );

        Copy = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                try
                {
                    System.Windows.Clipboard.SetText(logger.LogString);
                }
                catch
                {
                    Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not copy log to clipboard", ErrorType.Error, 3);
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
                        System.IO.File.WriteAllText(dialog.FileName, logger.LogString);
                    }
                    catch
                    {
                        Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not write log to file", ErrorType.Error, 3);
                    }
                }
            }
        );

        ShowEnvironment = new RelayCommand(
            canExecute: _ => true,
            execute: x => PrintEnvironment(PatcherEnvironmentProvider.Instance.Environment)
        );
    }

    public void RefreshDisp(object sender, PropertyChangedEventArgs e)
    {
        this.DispString = _logger.LogString;
    }

    public void PrintEnvironment(IGameEnvironment environment)
    {
        _logger.LogString += "Data Folder: " + environment.DataFolderPath + Environment.NewLine;
        _logger.LogString += "Load Order Source: " + environment.LoadOrderFilePath + Environment.NewLine;
        _logger.LogString += "Creation Club Listings: " + environment.CreationClubListingsFilePath + Environment.NewLine;
        _logger.LogString += "Game Release: " + environment.GameRelease.ToString() + Environment.NewLine;
        _logger.LogString += "Load Order: " + Environment.NewLine;

        foreach (var mod in environment.LoadOrder.ListedOrder)
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
            _logger.LogString += dispStr + Environment.NewLine;
        }
        DispString = _logger.LogString;
    }

    public void SwitchViewToLogDisplay()
    {
        _displayedItemVm.DisplayedViewModel = this;
    }
}