using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_DownloadCoordinator : INotifyPropertyChanged
    {
        public VM_DownloadCoordinator(HashSet<Manifest.DownloadInfoContainer> downloadInfo)
        {
            DownloadInfo = new ObservableCollection<VM_DownloadInfo>();
            foreach (var di in downloadInfo)
            {
                DownloadInfo.Add(VM_DownloadInfo.GetViewModelFromModel(di));
            }
        }

        public ObservableCollection<VM_DownloadInfo> DownloadInfo { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public class VM_DownloadInfo : INotifyPropertyChanged
        {
            public VM_DownloadInfo()
            {
                ModName = "";
                URL = "";
                ExpectedFileName = "";

                FindPath = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFile("", "Archive Files (*.7z;*.zip;*.rar)|*.7z;*.zip;*.rar|" + "All files (*.*)|*.*", out string filePath))
                    {
                        Path = filePath;
                    }
                }
                );
            }
            public string ModName { get; set; }
            public string ModDownloadName { get; set; }
            public string URL { get; set; }
            public string ExpectedFileName { get; set; }
            public string Path { get; set; }
            public RelayCommand FindPath { get; set; }

            public event PropertyChangedEventHandler PropertyChanged;

            public static VM_DownloadInfo GetViewModelFromModel(Manifest.DownloadInfoContainer downloadInfo)
            {
                VM_DownloadInfo viewModel = new VM_DownloadInfo();
                viewModel.ModName = downloadInfo.ModPageName;
                viewModel.ModDownloadName = downloadInfo.ModDownloadName;
                viewModel.URL = downloadInfo.URL;
                viewModel.ExpectedFileName = downloadInfo.ExpectedFileName;
                return viewModel;
            }
        }
    }
}
