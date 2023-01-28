using System.Collections.ObjectModel;
using System.IO;

namespace SynthEBD;

public class VM_DownloadCoordinator : VM
{
    public VM_DownloadCoordinator(HashSet<Manifest.DownloadInfoContainer> downloadInfo, Window_ConfigInstaller window, VM_ConfigInstaller parentVM)
    {
        foreach (var di in downloadInfo)
        {
            DownloadInfo.Add(VM_DownloadInfo.GetViewModelFromModel(di));
        }

        AssociatedWindow = window;

        Cancel = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                parentVM.Cancelled = true;
                AssociatedWindow.Close();
            }
        );

        OK = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                bool allFound = true;
                foreach (var DI in DownloadInfo)
                {
                    if (string.IsNullOrWhiteSpace(DI.Path))
                    {
                        CustomMessageBox.DisplayNotificationOK("Path required", "Please enter a path to the required archive for " + DI.ModDownloadName);
                        allFound = false;
                        break;
                    }
                    else if (!File.Exists(DI.Path))
                    {
                        CustomMessageBox.DisplayNotificationOK("File not found", "Could not find the archive at " + DI.Path + " for mod " + DI.ModDownloadName);
                        allFound= false;
                        break;
                    }
                }
                if(allFound)
                {
                    parentVM.Completed = true;
                    AssociatedWindow.Close();
                }
            }
        );
    }

    public ObservableCollection<VM_DownloadInfo> DownloadInfo { get; set; } = new();
    public RelayCommand Cancel { get; }
    public RelayCommand OK { get; }
    Window_ConfigInstaller AssociatedWindow { get; set; }

    public class VM_DownloadInfo : VM
    {
        public VM_DownloadInfo()
        {
            FindPath = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFile("", "Archive Files (*.7z;*.zip;*.rar)|*.7z;*.zip;*.rar|" + "All files (*.*)|*.*", "Select mod archive", out string filePath))
                    {
                        Path = filePath;
                    }
                }
            );
        }
        public string ModName { get; set; } = "";
        public string ModDownloadName { get; set; }
        public string URL { get; set; } = "";
        public string ExpectedFileName { get; set; } = "";
        public string Path { get; set; }
        public string ExtractionSubPath { get; set; } = "";
        public RelayCommand FindPath { get; set; }

        public static VM_DownloadInfo GetViewModelFromModel(Manifest.DownloadInfoContainer downloadInfo)
        {
            VM_DownloadInfo viewModel = new VM_DownloadInfo();
            viewModel.ModName = downloadInfo.ModPageName;
            viewModel.ModDownloadName = downloadInfo.ModDownloadName;
            viewModel.URL = downloadInfo.URL;
            viewModel.ExpectedFileName = downloadInfo.ExpectedFileName;
            viewModel.ExtractionSubPath = downloadInfo.ExtractionSubPath;
            return viewModel;
        }
    }
}