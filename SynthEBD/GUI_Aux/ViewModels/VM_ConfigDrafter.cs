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
    private readonly PatcherState _patcherState;
    private readonly VM_DrafterArchiveContainer.Factory _archiveContainerFactory;
    private readonly _7ZipInterface _7ZipInterface;

    public VM_ConfigDrafter(ConfigDrafter configDrafter, PatcherState patcherState, VM_DrafterArchiveContainer.Factory archiveContainerFactory, _7ZipInterface sevenZipInterface)
    {
        _configDrafter = configDrafter;
        _patcherState = patcherState;
        _archiveContainerFactory = archiveContainerFactory;
        _7ZipInterface = sevenZipInterface;

        DraftConfigButton = new RelayCommand(
        canExecute: _ => true,
        execute: _ =>
        {
            switch (SelectedSource)
            {
                case DrafterTextureSource.Archives: 
                    if (ValidateContainers())
                    {

                    }
                    break;
                case DrafterTextureSource.Directory:
                    if (Directory.Exists(SelectedTextureFolder))
                    {
                        _configDrafter.DraftConfigFromTextures(CurrentConfig, SelectedTextureFolder, out var unmatchedTextures);
                        UnmatchedTextures = string.Join(Environment.NewLine, unmatchedTextures);
                        HasUnmatchedTextures = unmatchedTextures.Any();
                    }
                    else
                    {
                        CustomMessageBox.DisplayNotificationOK("Drafter error", "The selected folder does not exist: " + SelectedTextureFolder);
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

    public string GeneratedModName { get; set; }
    public bool LockGeneratedModName { get; set; }
    public string ModNameToolTip { get; set; }

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

        LockGeneratedModName = !(_patcherState.ModManagerSettings.ModManagerType == ModManager.ModOrganizer2 && !_patcherState.ModManagerSettings.MO2Settings.ModFolderPath.IsNullOrWhitespace() && Directory.Exists(_patcherState.ModManagerSettings.MO2Settings.ModFolderPath)) &&
            !(_patcherState.ModManagerSettings.ModManagerType == ModManager.Vortex && !_patcherState.ModManagerSettings.VortexSettings.StagingFolderPath.IsNullOrWhitespace() && Directory.Exists(_patcherState.ModManagerSettings.VortexSettings.StagingFolderPath));
        if (LockGeneratedModName)
        {
            GeneratedModName = "You can only select a mod name if you have filled out your mods folder in the Mod Manager Integration Settings";
            ModNameToolTip = "Textures will be extracted to your Data folder (or overwrite if using MO2)";
        }
        else
        {
            GeneratedModName = String.Empty;
            ModNameToolTip = "A new mod with this name will appear in your mod manager's mod list after drafting";
        }

        if (!SelectedFileArchives.Any())
        {
            SelectedFileArchives.Add(_archiveContainerFactory());
        }
        UnmatchedTextures = "";
        HasUnmatchedTextures = false;
    }

    public bool ValidateContainers()
    {
        if (!SelectedFileArchives.Any())
        {
            CustomMessageBox.DisplayNotificationOK("Drafter Error", "No file archives were selected");
            return false;
        }
        for (int i = 0; i < SelectedFileArchives.Count; i++)
        {
            var container = SelectedFileArchives[i];
            if (!File.Exists(container.FilePath))
            {
                CustomMessageBox.DisplayNotificationOK("Drafter Error", "The following file from Archive #" + (i + 1).ToString() + " does not exist: " + Environment.NewLine + container.FilePath);
                return false;
            }
            if (container.Prefix.IsNullOrWhitespace())
            {
                CustomMessageBox.DisplayNotificationOK("Drafter Error", "You need to assign a prefix to Archive #" + (i + 1).ToString());
                return false;
            }
        }

        return true;
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
                if (IO_Aux.SelectFile("", "Archive Files (*.7z;*.zip;*.rar)|*.7z;*.zip;*.rar|" + "All files (*.*)|*.*", "Select a zipped archive", out string path))
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
