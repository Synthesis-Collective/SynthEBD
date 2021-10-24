using Mutagen.Bethesda.Plugins.Cache;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class VM_SettingsHeight : INotifyPropertyChanged
    {
        public VM_SettingsHeight()
        {
            this.bChangeNPCHeight = true;
            this.bChangeRaceHeight = true;
            this.bOverwriteNonDefaultNPCHeights = true;
            this.SelectedHeightConfig = new VM_HeightConfig();
            this.AvailableHeightConfigs = new ObservableCollection<VM_HeightConfig>();

            this.lk = new GameEnvironmentProvider().MyEnvironment.LinkCache;
            AddHeightConfig = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.AvailableHeightConfigs.Add(new VM_HeightConfig())
                );

            DeleteCurrentHeightConfig = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => {
                    try
                    {
                        System.IO.File.Delete(SelectedHeightConfig.SourcePath);
                        //debug
                        int[] apple = new int[2];
                        var error = apple[-1];
                    }
                    catch
                    {
                        Logger.TimedNotifyStatusUpdate("Could not delete file at " + this.SelectedHeightConfig.SourcePath, ErrorType.Warning, 5);
                    }

                    this.AvailableHeightConfigs.Remove(SelectedHeightConfig);
                    if (this.AvailableHeightConfigs.Count > 0)
                    {
                        this.SelectedHeightConfig = AvailableHeightConfigs[0];
                    }
                    }
                );
        }

        public bool bChangeNPCHeight { get; set; }
        public bool bChangeRaceHeight { get; set; }
        public bool bOverwriteNonDefaultNPCHeights { get; set; }

        public VM_HeightConfig SelectedHeightConfig { get; set; }

        public ObservableCollection<VM_HeightConfig> AvailableHeightConfigs { get; set; }

        public ILinkCache lk { get; set; }

        public RelayCommand AddHeightConfig { get; }

        public RelayCommand DeleteCurrentHeightConfig { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static void GetViewModelFromModel(VM_SettingsHeight viewModel, Settings_Height model)
        {
            viewModel.bChangeNPCHeight = model.bChangeNPCHeight;
            viewModel.bChangeRaceHeight = model.bChangeRaceHeight;
            viewModel.bOverwriteNonDefaultNPCHeights = model.bOverwriteNonDefaultNPCHeights;

            bool currentConfigFound = false;
            foreach (var hconfig in viewModel.AvailableHeightConfigs)
            {
                if (hconfig.Label == model.SelectedHeightConfig)
                {
                    currentConfigFound = true;
                    viewModel.SelectedHeightConfig = hconfig;
                    break;
                }
            }
            if (currentConfigFound == false)
            {
                // Warn User
            }
        }

        public static void DumpViewModelToModel(VM_SettingsHeight viewModel, Settings_Height model)
        {
            model.bChangeNPCHeight = viewModel.bChangeNPCHeight;
            model.bChangeRaceHeight = viewModel.bChangeRaceHeight;
            model.bOverwriteNonDefaultNPCHeights = viewModel.bOverwriteNonDefaultNPCHeights;
            model.SelectedHeightConfig = viewModel.SelectedHeightConfig.Label;
        }
    }
}
