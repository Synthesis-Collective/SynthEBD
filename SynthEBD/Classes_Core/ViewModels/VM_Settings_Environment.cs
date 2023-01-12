using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_Settings_Environment
    {
        public StandaloneRunEnvironmentStateProvider StateProvider { get; set; }
        public RelayCommand SelectGameDataFolder { get; }
        public RelayCommand ClearGameDataFolder { get; }
        public bool IsStandalone { get; }
        public VM_Settings_Environment(IEnvironmentStateProvider stateProvider)
        {
            StateProvider = stateProvider as StandaloneRunEnvironmentStateProvider;
            IsStandalone = StateProvider.RunMode == EnvironmentMode.Standalone;

            SelectGameDataFolder = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFolder("", out string selectedPath))
                    {
                        StateProvider.DataFolderPath = selectedPath;
                    }
                }
            );

            ClearGameDataFolder = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    StateProvider.DataFolderPath = string.Empty;
                }
            );
        }
    }
}
