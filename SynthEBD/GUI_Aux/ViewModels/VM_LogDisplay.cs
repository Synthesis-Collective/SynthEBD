using System.ComponentModel;
using Mutagen.Bethesda.Environments;
using Noggog.WPF;

namespace SynthEBD;

public class VM_LogDisplay : ViewModel
{
    private Logger SubscribedLogger { get; set; } = Logger.Instance;

    public string DispString { get; set; } = "";

    public RelayCommand Clear { get; set; }
    public RelayCommand ShowEnvironment { get; set; }

    public VM_LogDisplay()
    {
        this.SubscribedLogger.PropertyChanged += RefreshDisp;

        Clear = new RelayCommand(
            canExecute: _ => true,
            execute: x => SubscribedLogger.LogString = ""
        );

        ShowEnvironment = new RelayCommand(
            canExecute: _ => true,
            execute: x => PrintEnvironment(PatcherEnvironmentProvider.Instance.Environment)
        );
    }

    public void RefreshDisp(object sender, PropertyChangedEventArgs e)
    {
        this.DispString = SubscribedLogger.LogString;
    }

    public void PrintEnvironment(IGameEnvironment environment)
    {
        SubscribedLogger.LogString += "Data Folder: " + environment.DataFolderPath + Environment.NewLine;
        SubscribedLogger.LogString += "Load Order Source: " + environment.LoadOrderFilePath + Environment.NewLine;
        SubscribedLogger.LogString += "Creation Club Listings: " + environment.CreationClubListingsFilePath + Environment.NewLine;
        SubscribedLogger.LogString += "Game Release: " + environment.GameRelease.ToString() + Environment.NewLine;
        SubscribedLogger.LogString += "Load Order: " + Environment.NewLine;

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
            SubscribedLogger.LogString += dispStr + Environment.NewLine;
        }
        DispString = SubscribedLogger.LogString;
    }
}