using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_Manifest : VM
    {
        public VM_Manifest()
        {
            VM_PackagerOption root = new(Options, this) { Name = "Root" };
            Options.Add(root);
            SelectedNode = root;

            ImportCommand = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFile("", "Config files (*.json)|*.json", "Select manifest.json to import", out string path))
                    {
                        var model = JSONhandler<Manifest>.LoadJSONFile(path, out bool success, out string exceptionStr);
                        if (success)
                        {
                            GetViewModelFromModel(model);
                        }
                        else
                        {
                            CustomMessageBox.DisplayNotificationOK("Import Failed", "Manifest.json import failed with the following error: " + Environment.NewLine + exceptionStr);
                        }
                    }
                });

            ExportCommand = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFolder("", out string path))
                    {
                        string dest = System.IO.Path.Combine(path, "Manifest.json");
                        var model = DumpViewModelToModel();
                        JSONhandler<Manifest>.SaveJSONFile(model, dest, out bool success, out string exceptionStr);
                        if (success)
                        {
                            Logger.CallTimedNotifyStatusUpdateAsync("Saved to " + path, 3);
                        }
                        else
                        {
                            CustomMessageBox.DisplayNotificationOK("Export Failed", "Manifest.json export failed with the following error: " + Environment.NewLine + exceptionStr);
                        }
                    }
                });

            SelectedNodeChanged = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.SelectedNode = (VM_PackagerOption)x
                );

            SetRootDirectory = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFolder("", out string path))
                    {
                        RootDirectory = path;
                    }
                });
        }
        public string ConfigName { get; set; } = "New Config";
        public string ConfigDescription { get; set; } = "";
        public string ConfigPrefix { get; set; } = "Prefix";
        public ObservableCollection<string[]> FileExtensionMap { get; set; } = new();
        public ObservableCollection<VM_PackagerOption> Options { get; set; } = new();
        public string InstallationMessage { get; set; } = string.Empty;
        public string RootDirectory { get; set; } = string.Empty;
        public RelayCommand ExportCommand { get; set; }
        public RelayCommand ImportCommand { get; set; }
        public RelayCommand SelectedNodeChanged { get; set; }
        public RelayCommand SetRootDirectory { get; set; }
        public VM_PackagerOption SelectedNode { get; set; }

        public void GetViewModelFromModel(Manifest model)
        {
            ConfigName = model.ConfigName;
            ConfigDescription = model.ConfigDescription;
            DestinationModFolder = model.DestinationModFolder;
            ConfigPrefix = model.ConfigPrefix;
            FileExtensionMap = GetFileExtensionMapFromModel(model.FileExtensionMap);
            foreach (var item in model.DownloadInfo)
            {
                DownloadInfo.Add(VM_DownloadInfoContainer.GetViewModelFromModel(item, ));
            }
            OptionsDescription = model.OptionsDescription;
            foreach (var item in model.Options)
            {
                Options.Add(VM_PackagerOption.GetViewModelFromModel(item, Options, this));
            }
        }

        public static ObservableCollection<string[]> GetFileExtensionMapFromModel(Dictionary<string, string> model)
        {
            ObservableCollection<string[]> map = new();
            foreach (var entry in model)
            {
                string[] vm = new string[2] { entry.Key, entry.Value };
                map.Add(vm);
            }
            return map;
        }

        public Manifest DumpViewModelToModel()
        {
            Manifest model = new();
            model.ConfigName = ConfigName;
            model.ConfigDescription = ConfigDescription;
            model.DestinationModFolder = DestinationModFolder;
            model.ConfigPrefix = ConfigPrefix;
            foreach (var entry in FileExtensionMap)
            {
                if (model.FileExtensionMap.ContainsKey(entry[0]))
                {
                    model.FileExtensionMap[entry[0]] = entry[1];
                }
                else
                {
                    model.FileExtensionMap.Add(entry[0], entry[1]);
                }
            }
            model.DownloadInfo = DownloadInfo.Select(x => x.DumpViewModelToModel()).ToHashSet();
            model.OptionsDescription = OptionsDescription;
            model.Options = Options.Select(x => x.DumpViewModelToModel()).ToHashSet();
            model.InstallationMessage = InstallationMessage;
            return model;
        }
    }
}
