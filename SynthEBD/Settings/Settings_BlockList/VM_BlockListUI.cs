using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_BlockListUI : INotifyPropertyChanged
    {
        public VM_BlockListUI()
        {
            this.BlockedNPCs = new ObservableCollection<VM_BlockedNPC>();
            this.BlockedPlugins = new ObservableCollection<VM_BlockedPlugin>();

            AddBlockedNPC = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.BlockedNPCs.Add(new VM_BlockedNPC())
                );

            RemoveBlockedNPC = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.BlockedNPCs.Remove((VM_BlockedNPC)x)
                );

            AddBlockedPlugin = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.BlockedPlugins.Add(new VM_BlockedPlugin())
                );

            RemoveBlockedPlugin = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.BlockedPlugins.Remove((VM_BlockedPlugin)x)
                );

            ImportFromZEBDcommand = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => ImportFromZEBD()
                );

        }

        public ObservableCollection<VM_BlockedNPC> BlockedNPCs { get; set; }
        public ObservableCollection<VM_BlockedPlugin> BlockedPlugins { get; set; }

        public VM_BlockedNPC DisplayedNPC { get; set; }
        public VM_BlockedPlugin DisplayedPlugin { get; set; }

        public RelayCommand AddBlockedNPC { get; set; }
        public RelayCommand RemoveBlockedNPC { get; set; }
        public RelayCommand AddBlockedPlugin { get; set; }
        public RelayCommand RemoveBlockedPlugin { get; set; }
        public RelayCommand ImportFromZEBDcommand { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static void GetViewModelFromModel(BlockList model, VM_BlockListUI viewModel)
        {
            viewModel.BlockedNPCs.Clear();
            foreach (var blockedNPC in model.NPCs)
            {
                viewModel.BlockedNPCs.Add(VM_BlockedNPC.GetViewModelFromModel(blockedNPC));
            }

            viewModel.BlockedPlugins.Clear();
            foreach (var blockedPlugin in model.Plugins)
            {
                viewModel.BlockedPlugins.Add(VM_BlockedPlugin.GetViewModelFromModel(blockedPlugin));
            }
        }

        public static void DumpViewModelToModel(VM_BlockListUI viewModel, BlockList model)
        {
            model.NPCs.Clear();
            foreach (var npc in viewModel.BlockedNPCs)
            {
                model.NPCs.Add(VM_BlockedNPC.DumpViewModelToModel(npc));
            }
            model.Plugins.Clear();
            foreach (var plugin in viewModel.BlockedPlugins)
            {
                model.Plugins.Add(VM_BlockedPlugin.DumpViewModelToModel(plugin));
            }
        }

        public void ImportFromZEBD()
        {
            // Configure open file dialog box
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.FileName = "BlockList"; // Default file name
            dialog.DefaultExt = ".json"; // Default file extension
            dialog.Filter = "JSON files (.json|*.json"; // Filter files by extension

            // Show open file dialog box
            bool? result = dialog.ShowDialog();

            // Process open file dialog box results
            if (result == true)
            {
                // Open document
                string filename = dialog.FileName;

                try
                {
                    var loadedZList = JSONhandler<zEBDBlockList>.loadJSONFile(filename);
                    var loadedList = zEBDBlockList.ToSynthEBD(loadedZList);
                    GetViewModelFromModel(loadedList, this);
                }
                catch
                {
                    // Warn user
                }
            }
        }
    }
}
