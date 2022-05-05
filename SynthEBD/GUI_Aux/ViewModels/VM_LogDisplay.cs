using System.ComponentModel;

namespace SynthEBD;

public class VM_LogDisplay : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    private Logger SubscribedLogger { get; set; }

    public string DispString { get; set; }

    public RelayCommand Clear { get; set; }
    public RelayCommand ShowEnvironment { get; set; }

    public VM_LogDisplay()
    {
        this.SubscribedLogger = Logger.Instance;
        this.DispString = "";

        this.SubscribedLogger.PropertyChanged += RefreshDisp;

        Clear = new RelayCommand(
            canExecute: _ => true,
            execute: x => SubscribedLogger.LogString = ""
        );

        ShowEnvironment = new RelayCommand(
            canExecute: _ => true,
            execute: x => PrintEnvironment()
        );
    }

    public void RefreshDisp(object sender, PropertyChangedEventArgs e)
    {
        this.DispString = SubscribedLogger.LogString;
    }

    public void PrintEnvironment()
    {
        SubscribedLogger.LogString += "Data Folder: " + PatcherEnvironmentProvider.Environment.DataFolderPath + Environment.NewLine;
        SubscribedLogger.LogString += "Load Order Source: " + PatcherEnvironmentProvider.Environment.LoadOrderFilePath + Environment.NewLine;
        SubscribedLogger.LogString += "Creation Club Listings: " + PatcherEnvironmentProvider.Environment.CreationClubListingsFilePath + Environment.NewLine;
        SubscribedLogger.LogString += "Game Release: " + PatcherEnvironmentProvider.Environment.GameRelease.ToString() + Environment.NewLine;
        SubscribedLogger.LogString += "Load Order: " + Environment.NewLine;

        foreach (var mod in PatcherEnvironmentProvider.Environment.LoadOrder.ListedOrder)
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
            SubscribedLogger.LogString += dispStr + Environment.NewLine;
        }
        DispString = SubscribedLogger.LogString;
    }
}