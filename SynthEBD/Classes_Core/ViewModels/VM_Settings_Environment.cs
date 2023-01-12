using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_Settings_Environment
    {
        public StandaloneRunEnvironmentStateProvider EnvironmentProvider { get; set; }
        public RelayCommand SelectGameDataFolder { get; }
        public RelayCommand ClearGameDataFolder { get; }
        public bool IsStandalone { get; }
        public VM_Settings_Environment(IEnvironmentStateProvider environmentProvider)
        {
            EnvironmentProvider = environmentProvider as StandaloneRunEnvironmentStateProvider;
            IsStandalone = environmentProvider.RunMode == EnvironmentMode.Standalone;

            SelectGameDataFolder = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFolder("", out string selectedPath))
                    {
                        environmentProvider.DataFolderPath = selectedPath;
                    }
                }
            );

            ClearGameDataFolder = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    environmentProvider.DataFolderPath = string.Empty;
                }
            );
        }
    }
}
