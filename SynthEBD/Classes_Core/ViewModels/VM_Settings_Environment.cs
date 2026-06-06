using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// View model backing the Environment settings UI: lets a standalone run pick or clear the
    /// game Data folder via the underlying <see cref="StandaloneRunEnvironmentStateProvider"/>.
    /// </summary>
    public class VM_Settings_Environment : VM
    {
        public StandaloneRunEnvironmentStateProvider EnvironmentProvider { get; set; }
        public RelayCommand SelectGameDataFolder { get; }
        public RelayCommand ClearGameDataFolder { get; }
        public bool IsStandalone { get; }
        /// <summary>
        /// Seeds <see cref="IsStandalone"/> and the standalone-cast provider, and wires the
        /// SelectGameDataFolder (folder-picker) and ClearGameDataFolder commands to set/clear
        /// the provider's DataFolderPath.
        /// </summary>
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
