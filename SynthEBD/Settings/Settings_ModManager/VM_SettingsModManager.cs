using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;

namespace SynthEBD
{
    public class VM_SettingsModManager : INotifyPropertyChanged
    {
        public VM_SettingsModManager()
        {
            ModManagerType = ModManager.None;
            MO2IntegrationVM = new VM_MO2Integration();
            VortexIntegrationVM = new VM_VortexIntergation();
            DisplayedSubVM = null;
            TempFolder = "";

            SelectTempFolder = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFolder(TempFolder, out var tmpFolder))
                    {
                        TempFolder = tmpFolder;
                    }
                }
                );

            this.WhenAnyValue(x => x.ModManagerType).Subscribe(x => UpdateDisplayedVM());
        }

        public ModManager ModManagerType { get; set; }
        public VM_MO2Integration MO2IntegrationVM { get; set; }
        public VM_VortexIntergation VortexIntegrationVM { get; set; }

        public object DisplayedSubVM { get; set; }

        public string TempFolder { get; set; }

        public RelayCommand SelectTempFolder { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public void UpdateDisplayedVM()
        {
            switch(ModManagerType)
            {
                case ModManager.None: DisplayedSubVM = null; break;
                case ModManager.ModOrganizer2: DisplayedSubVM = MO2IntegrationVM; break;
                case ModManager.Vortex: DisplayedSubVM = VortexIntegrationVM; break;
            }
        }

        public static void GetViewModelFromModel(Settings_ModManager model, VM_SettingsModManager viewModel)
        {
            viewModel.ModManagerType = model.ModManagerType;
            VM_MO2Integration.GetViewModelFromModel(model.MO2Settings, viewModel.MO2IntegrationVM);
            VM_VortexIntergation.GetViewModelFromModel(model.VortexSettings, viewModel.VortexIntegrationVM);
            viewModel.TempFolder = model.TempExtractionFolder;
        }

        public static void DumpViewModelToModel(Settings_ModManager model, VM_SettingsModManager viewModel)
        {
            model.ModManagerType = viewModel.ModManagerType;
            VM_MO2Integration.DumpViewModelToModel(model.MO2Settings, viewModel.MO2IntegrationVM);
            VM_VortexIntergation.DumpViewModelToModel(model.VortexSettings, viewModel.VortexIntegrationVM);
            model.TempExtractionFolder = viewModel.TempFolder;
        }
    }

    public class VM_MO2Integration : INotifyPropertyChanged
    {
        public VM_MO2Integration()
        {
            this.ModFolderPath = "";
            this.ExecutablePath = "";

            FindModFolder = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFolder("", out var modFolder))
                    {
                        ModFolderPath = modFolder;
                    }
                }
                );

            FindExecutable = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFile("", "Executable files (*.exe)|*.exe", out var execPath))
                    {
                        ExecutablePath = execPath;
                    }
                }
                );
        }
        public string ModFolderPath { get; set; }
        public string ExecutablePath { get; set; }

        public RelayCommand FindModFolder { get; set; }
        public RelayCommand FindExecutable { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static void GetViewModelFromModel(Settings_ModManager.MO2 model, VM_MO2Integration viewModel)
        {
            viewModel.ModFolderPath = model.ModFolderPath;
            viewModel.ExecutablePath = model.ExecutablePath;
        }
        public static void DumpViewModelToModel(Settings_ModManager.MO2 model, VM_MO2Integration viewModel)
        {
            model.ModFolderPath = viewModel.ModFolderPath;
            model.ExecutablePath = viewModel.ExecutablePath;
        }
    }

    public class VM_VortexIntergation : INotifyPropertyChanged
    {
        public VM_VortexIntergation()
        {
            this.StagingFolderPath = "";

            FindStagingFolder = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFolder("", out var stagingFolder))
                    {
                        StagingFolderPath = stagingFolder;
                    }
                }
                );
        }
        public string StagingFolderPath { get; set; }
        public RelayCommand FindStagingFolder { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;

        public static void GetViewModelFromModel(Settings_ModManager.Vortex model, VM_VortexIntergation viewModel)
        {
            viewModel.StagingFolderPath = model.StagingFolderPath;
        }
        public static void DumpViewModelToModel(Settings_ModManager.Vortex model, VM_VortexIntergation viewModel)
        {
            model.StagingFolderPath = viewModel.StagingFolderPath;
        }
    }
}
