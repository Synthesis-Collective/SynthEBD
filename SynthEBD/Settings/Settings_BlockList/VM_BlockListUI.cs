using Mutagen.Bethesda.Skyrim;
using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_BlockListUI : VM
{
    private readonly Logger _logger;
    private readonly SettingsIO_BlockList _blockListIO;
    private readonly Converters _converters;
    public VM_BlockListUI(Logger logger, SettingsIO_BlockList blockListIO, Converters converters)
    {
        _logger = logger;
        _blockListIO = blockListIO;
        _converters = converters;

        AddBlockedNPC = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => this.BlockedNPCs.Add(new VM_BlockedNPC(_converters))
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

        Save = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var tmpModel = new BlockList ();
                DumpViewModelToModel(this, tmpModel);
                _blockListIO.SaveBlockList(tmpModel, out bool saveSuccess);
                if (saveSuccess)
                {
                    _logger.CallTimedNotifyStatusUpdateAsync("BlockList Saved.", 2, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Yellow));
                }
                else
                {
                    _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save Block List.", ErrorType.Error, 5);
                }
            }
        );
    }

    public ObservableCollection<VM_BlockedNPC> BlockedNPCs { get; set; } = new();
    public ObservableCollection<VM_BlockedPlugin> BlockedPlugins { get; set; } = new();

    public VM_BlockedNPC DisplayedNPC { get; set; }
    public VM_BlockedPlugin DisplayedPlugin { get; set; }

    public RelayCommand AddBlockedNPC { get; set; }
    public RelayCommand RemoveBlockedNPC { get; set; }
    public RelayCommand AddBlockedPlugin { get; set; }
    public RelayCommand RemoveBlockedPlugin { get; set; }
    public RelayCommand ImportFromZEBDcommand { get; set; }
    public RelayCommand Save { get; }

    public static void GetViewModelFromModel(BlockList model, VM_BlockListUI viewModel, Converters converters)
    {
        viewModel.BlockedNPCs.Clear();
        foreach (var blockedNPC in model.NPCs)
        {
            viewModel.BlockedNPCs.Add(VM_BlockedNPC.GetViewModelFromModel(blockedNPC, converters));
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

            var loadedZList = JSONhandler<zEBDBlockList>.LoadJSONFile(filename, out bool parsed, out string exceptionStr);
            if (parsed)
            {
                var loadedList = loadedZList.ToSynthEBD();
                GetViewModelFromModel(loadedList, this, _converters);
            }
            else
            {
                _logger.LogError("Block list parsing failed with the following exception: " + exceptionStr);
            }
        }
    }
}

public class VM_HeadPartBlock : VM
{
    public VM_HeadPartBlock(HeadPart.TypeEnum type, bool isBlocked)
    {
        Type = type;
        Block = isBlocked;
    }
    public HeadPart.TypeEnum Type { get; set; }
    public bool Block { get; set; }
}