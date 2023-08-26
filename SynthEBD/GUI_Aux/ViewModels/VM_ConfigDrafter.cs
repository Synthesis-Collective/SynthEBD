using DynamicData;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Mutagen.Bethesda.Plugins.Binary.Processing.BinaryFileProcessor;

namespace SynthEBD;

public class VM_ConfigDrafter : VM
{
    private readonly ConfigDrafter _configDrafter;
    private readonly VM_DrafterArchiveContainer.Factory _archiveContainerFactory;

    public VM_ConfigDrafter(ConfigDrafter configDrafter, VM_DrafterArchiveContainer.Factory archiveContainerFactory)
    {
        _configDrafter = configDrafter;
        _archiveContainerFactory = archiveContainerFactory;

        DraftConfigButton = new RelayCommand(
        canExecute: _ => true,
        execute: _ =>
        {
            switch (SelectedSource)
            {
                case DrafterTextureSource.Archives: break;
                case DrafterTextureSource.Directory:
                    if (Directory.Exists(SelectedTextureFolder))
                    {
                        _configDrafter.DraftConfigFromTextures(CurrentConfig, SelectedTextureFolder, out var unmatchedTextures);
                        UnmatchedTextures = string.Join(Environment.NewLine, unmatchedTextures);
                        HasUnmatchedTextures = unmatchedTextures.Any();
                    }
                    break;
                default: throw new NotImplementedException();
            }
        });

        AddFileArchiveButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                SelectedFileArchives.Add(_archiveContainerFactory());
            });

        SelectDirectoryButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFolder("", out string path))
                {
                    SelectedTextureFolder = path;
                }
            });
    }

    public VM_AssetPack CurrentConfig { get; set; }

    public DrafterTextureSource SelectedSource { get; set; } = DrafterTextureSource.Archives;
    public ObservableCollection<VM_DrafterArchiveContainer> SelectedFileArchives { get; set; } = new();
    public string SelectedTextureFolder { get; set; }

    public string UnmatchedTextures { get; set; }
    public bool HasUnmatchedTextures { get; set; } = false;

    public RelayCommand DraftConfigButton { get; }
    public RelayCommand AddFileArchiveButton { get; }
    public RelayCommand SelectDirectoryButton { get; }

    public void InitializeTo(VM_AssetPack config)
    {
        CurrentConfig = config;
        if (!SelectedFileArchives.Any())
        {
            SelectedFileArchives.Add(_archiveContainerFactory());
        }
        UnmatchedTextures = "";
        HasUnmatchedTextures = false;
    }
}

public class VM_DrafterArchiveContainer : VM
{
    public delegate VM_DrafterArchiveContainer Factory();
    private readonly VM_ConfigDrafter _configDrafter;
    public VM_DrafterArchiveContainer(VM_ConfigDrafter drafter)
    {
        _configDrafter = drafter;

        SelectArchive = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFile("", "", "Select a zipped archive", out string path))
                {
                    FilePath = path;
                }
            });

        DeleteMe = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                _configDrafter.SelectedFileArchives.Remove(this);
            });
    }
    public string FilePath { get; set; }
    public string Prefix { get; set; }
    public RelayCommand SelectArchive { get; }
    public RelayCommand DeleteMe { get; }
}

public enum DrafterTextureSource
{
    Archives,
    Directory
}
