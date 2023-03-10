using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive.Linq;

namespace SynthEBD;

public class VM_BlockListUI : VM
{
    private readonly Logger _logger;
    private readonly SettingsIO_BlockList _blockListIO;
    private readonly VM_BlockedNPC.Factory _blockedNPCFactory;
    private readonly VM_BlockedPlugin.Factory _blockedPluginFactory;
    private readonly VM_BlockedNPCPlaceHolder.Factory _blockedNPCPlaceHolderFactory;
    public VM_BlockListUI(Logger logger, SettingsIO_BlockList blockListIO, VM_BlockedNPC.Factory blockedNPCFactory, VM_BlockedPlugin.Factory blockedPluginFactory, VM_BlockedNPCPlaceHolder.Factory npcPlaceHolderFactory)
    {
        _logger = logger;
        _blockListIO = blockListIO;
        _blockedNPCFactory = blockedNPCFactory;
        _blockedPluginFactory = blockedPluginFactory;
        _blockedNPCPlaceHolderFactory = npcPlaceHolderFactory;

        this.WhenAnyValue(vm => vm.SelectedNPC)
             .Buffer(2, 1)
             .Select(b => (Previous: b[0], Current: b[1]))
             .Subscribe(t => {
                 if (t.Previous != null && t.Previous.AssociatedViewModel != null)
                 {
                     t.Previous.AssociatedModel = t.Previous.AssociatedViewModel.DumpViewModelToModel();
                 }

                 if (t.Current != null)
                 {
                     DisplayedNPC = VM_BlockedNPC.CreateViewModel(t.Current, blockedNPCFactory);
                 }
             }).DisposeWith(this);

        this.WhenAnyValue(vm => vm.SelectedPlugin)
             .Buffer(2, 1)
             .Select(b => (Previous: b[0], Current: b[1]))
             .Subscribe(t => {
                 if (t.Previous != null && t.Previous.AssociatedViewModel != null)
                 {
                     t.Previous.AssociatedModel = t.Previous.AssociatedViewModel.DumpViewModelToModel();
                 }

                 if (t.Current != null)
                 {
                     DisplayedPlugin = VM_BlockedPlugin.CreateViewModel(t.Current, blockedPluginFactory);
                 }
             }).DisposeWith(this);

        AddBlockedNPC = new RelayCommand(
            canExecute: _ => true,
            execute: x => {
                var newBlockedNPC = npcPlaceHolderFactory(new BlockedNPC());
                var newBlockedVM = blockedNPCFactory(newBlockedNPC);
                BlockedNPCs.Add(newBlockedNPC);
                SelectedNPC = newBlockedNPC;
                });

        RemoveBlockedNPC = new RelayCommand(
            canExecute: _ => true,
            execute: x => { 
                BlockedNPCs.Remove((VM_BlockedNPCPlaceHolder)x);
                DisplayedNPC = null;
            });

        AddBlockedPlugin = new RelayCommand(
            canExecute: _ => true,
            execute: x => {
                var newBlockedPlugin = new VM_BlockedPluginPlaceHolder(new BlockedPlugin());
                var newBlockedVM = blockedPluginFactory(newBlockedPlugin);
                BlockedPlugins.Add(newBlockedPlugin);
                SelectedPlugin = newBlockedPlugin;
            });

        RemoveBlockedPlugin = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                BlockedPlugins.Remove((VM_BlockedPluginPlaceHolder)x);
                DisplayedPlugin = null;
            });

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

    public ObservableCollection<VM_BlockedNPCPlaceHolder> BlockedNPCs { get; set; } = new();
    public ObservableCollection<VM_BlockedPluginPlaceHolder> BlockedPlugins { get; set; } = new();

    public VM_BlockedNPCPlaceHolder SelectedNPC { get; set; }
    public VM_BlockedPluginPlaceHolder SelectedPlugin { get; set; }

    public VM_BlockedNPC DisplayedNPC { get; set; }
    public VM_BlockedPlugin DisplayedPlugin { get; set; }

    public RelayCommand AddBlockedNPC { get; set; }
    public RelayCommand RemoveBlockedNPC { get; set; }
    public RelayCommand AddBlockedPlugin { get; set; }
    public RelayCommand RemoveBlockedPlugin { get; set; }
    public RelayCommand ImportFromZEBDcommand { get; set; }
    public RelayCommand Save { get; }

    public void CopyInViewModelFromModel(BlockList model)
    {
        if (model == null)
        {
            return;
        }
        _logger.LogStartupEventStart("Loading BlockList UI");
        BlockedNPCs.Clear();
        foreach (var blockedNPC in model.NPCs)
        {
            BlockedNPCs.Add(_blockedNPCPlaceHolderFactory(blockedNPC));
        }

        BlockedPlugins.Clear();
        foreach (var blockedPlugin in model.Plugins)
        {
            BlockedPlugins.Add(new VM_BlockedPluginPlaceHolder(blockedPlugin));
        }
        _logger.LogStartupEventEnd("Loading BlockList UI");
    }

    public BlockList DumpViewModelToModel()
    {
        if (DisplayedNPC != null)
        {
            DisplayedNPC.AssociatedPlaceHolder.AssociatedModel = DisplayedNPC.DumpViewModelToModel();
        }

        if (DisplayedPlugin != null)
        {
            DisplayedPlugin.AssociatedPlaceHolder.AssociatedModel = DisplayedPlugin.DumpViewModelToModel();
        }

        BlockList model = new();
        model.NPCs.Clear();
        foreach (var npc in BlockedNPCs)
        {
            model.NPCs.Add(npc.AssociatedModel);
        }
        model.Plugins.Clear();
        foreach (var plugin in BlockedPlugins)
        {
            model.Plugins.Add(plugin.AssociatedModel);
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
                CopyInViewModelFromModel(loadedList);
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