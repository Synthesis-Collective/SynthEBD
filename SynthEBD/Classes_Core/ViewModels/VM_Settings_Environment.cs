using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_Settings_Environment
    {
        public StandaloneRunStateProvider StateProvider { get; set; }
        public RelayCommand SelectGameDataFolder { get; }
        public RelayCommand ClearGameDataFolder { get; }
        public bool IsStandalone { get; }
        public VM_Settings_Environment(IStateProvider stateProvider)
        {
            StateProvider = stateProvider as StandaloneRunStateProvider;
            IsStandalone = StateProvider.RunMode == Mode.Standalone;

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
