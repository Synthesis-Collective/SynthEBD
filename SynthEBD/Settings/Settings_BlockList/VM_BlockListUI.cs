using Mutagen.Bethesda.Skyrim;
using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_BlockListUI : VM
{
    private readonly Logger _logger;
    private readonly SettingsIO_BlockList _blockListIO;
    private readonly VM_BlockedNPC.Factory _blockedNPCFactory;
    private readonly VM_BlockedPlugin.Factory _blockedPluginFactory;
    public VM_BlockListUI(Logger logger, SettingsIO_BlockList blockListIO, VM_BlockedNPC.Factory blockedNPCFactory, VM_BlockedPlugin.Factory blockedPluginFactory)
    {
        _logger = logger;
        _blockListIO = blockListIO;
        _blockedNPCFactory = blockedNPCFactory;
        _blockedPluginFactory = blockedPluginFactory;

        AddBlockedNPC = new RelayCommand(
            canExecute: _ => true,
            execute: x => BlockedNPCs.Add(blockedNPCFactory())
        );

        RemoveBlockedNPC = new RelayCommand(
            canExecute: _ => true,
            execute: x => BlockedNPCs.Remove((VM_BlockedNPC)x)
        );

        AddBlockedPlugin = new RelayCommand(
            canExecute: _ => true,
            execute: x => BlockedPlugins.Add(_blockedPluginFactory())
        );

        RemoveBlockedPlugin = new RelayCommand(
            canExecute: _ => true,
            execute: x => BlockedPlugins.Remove((VM_BlockedPlugin)x)
        );

        ImportFromZEBDcommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => ImportFromZEBD()
        );

        Save = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var tmpModel = DumpViewModelToModel();
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

    public void CopyInViewModelFromModel(BlockList model, VM_BlockedNPC.Factory blockedNPCFactory, VM_BlockedPlugin.Factory blockedPluginFactory)
    {
        if (model == null)
        {
            return;
        }
        _logger.LogStartupEventStart("Loading BlockList UI");
        BlockedNPCs.Clear();
        foreach (var blockedNPC in model.NPCs)
        {
            BlockedNPCs.Add(VM_BlockedNPC.GetViewModelFromModel(blockedNPC, blockedNPCFactory));
        }

        BlockedPlugins.Clear();
        foreach (var blockedPlugin in model.Plugins)
        {
            BlockedPlugins.Add(VM_BlockedPlugin.GetViewModelFromModel(blockedPlugin, blockedPluginFactory));
        }
        _logger.LogStartupEventEnd("Loading BlockList UI");
    }

    public BlockList DumpViewModelToModel()
    {
        BlockList model = new();
        model.NPCs.Clear();
        foreach (var npc in BlockedNPCs)
        {
            model.NPCs.Add(VM_BlockedNPC.DumpViewModelToModel(npc));
        }
        model.Plugins.Clear();
        foreach (var plugin in BlockedPlugins)
        {
            model.Plugins.Add(VM_BlockedPlugin.DumpViewModelToModel(plugin));
        }
        return model;
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
                CopyInViewModelFromModel(loadedList, _blockedNPCFactory, _blockedPluginFactory);
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