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
            this.HeightConfigs = new ObservableCollection<VM_HeightConfig>();

            this.lk = new GameEnvironmentProvider().MyEnvironment.LinkCache;
            AddHeightConfig = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.HeightConfigs.Add(new VM_HeightConfig(this.HeightConfigs, lk))
                );
        }

        public bool bChangeNPCHeight { get; set; }
        public bool bChangeRaceHeight { get; set; }
        public bool bOverwriteNonDefaultNPCHeights { get; set; }

        public ObservableCollection<VM_HeightConfig> HeightConfigs { get; set; }

        public ILinkCache lk { get; set; }

        public RelayCommand AddHeightConfig { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static void GetViewModelFromModel(VM_SettingsHeight viewModel, Settings_Height model, ILinkCache lk)
        {
            viewModel.bChangeNPCHeight = model.bChangeNPCHeight;
            viewModel.bChangeRaceHeight = model.bChangeRaceHeight;
            viewModel.bOverwriteNonDefaultNPCHeights = model.bOverwriteNonDefaultNPCHeights;
        }

        public static void DumpViewModelToModel(VM_SettingsHeight viewModel, Settings_Height model)
        {
            model.bChangeNPCHeight = viewModel.bChangeNPCHeight;
            model.bChangeRaceHeight = viewModel.bChangeRaceHeight;
            model.bOverwriteNonDefaultNPCHeights = viewModel.bOverwriteNonDefaultNPCHeights;
        }
    }
}
