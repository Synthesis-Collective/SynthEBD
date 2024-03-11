using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;
using Noggog;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Mutagen.Bethesda.Synthesis;
using System.Windows.Controls;
using System.IO;
using Mutagen.Bethesda.Skyrim;
using System.Windows.Media;

namespace SynthEBD;

public class VM_DestinationFolderSelector : VM
{
    public delegate VM_DestinationFolderSelector Factory(Manifest manifest, VM_ConfigInstaller parentVM);
    public VM_DestinationFolderSelector(Manifest manifest, VM_ConfigInstaller parentVM, PatcherState patcherState, SettingsIO_AssetPack assetPackIO, ConfigInstaller configInstaller, VM_SettingsModManager modManagerVM) 
    {
        _parentVM = parentVM;
        _patcherState = patcherState;
        _assetPackIO = assetPackIO;
        _configInstaller = configInstaller;
        _modManagerVM = modManagerVM;

        this.WhenAnyValue(x => x.DestinationFolderName).Subscribe(_ => UpdateWarningMessage(manifest)).DisposeWith(this);

        SetDestinationFolder(manifest);

        Finalize = new RelayCommand(
            canExecute: _ => _destinationFolderValid,
            execute: _ =>
            {
                IsFinalized = true;
                manifest.DestinationModFolder = DestinationFolderName;
                parentVM.ConcludeInstallation();
            }
        );

        Cancel = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                parentVM.Cancelled = true;
                parentVM.ConcludeInstallation();
            }
        );
    }
    private readonly VM_ConfigInstaller _parentVM;
    private readonly PatcherState _patcherState;
    private readonly SettingsIO_AssetPack _assetPackIO;
    private readonly ConfigInstaller _configInstaller;
    private readonly VM_SettingsModManager _modManagerVM;

    public string DestinationFolderName { get; set; }
    private bool _destinationFolderValid { get; set; } = false;
    public string WarningMessage { get; set; } = string.Empty;
    public SolidColorBrush WarningMessageColor { get; set; } = CommonColors.Green;
    public RelayCommand Finalize { get; }
    public bool IsFinalized { get; set; } = false;
    public RelayCommand Cancel { get; }

    private void SetDestinationFolder(Manifest manifest)
    {
        if (!manifest.DestinationModFolder.IsNullOrWhitespace())
        {
            DestinationFolderName = manifest.DestinationModFolder;
        }
        else if (!manifest.ConfigName.IsNullOrWhitespace())
        {
            string tempName = manifest.ConfigName;
            tempName = tempName.Replace("SynthEBD", "").Trim();
            if (tempName.StartsWith("-") || tempName.StartsWith("_"))
            {
                tempName = tempName.Substring(1).Trim();
            }

            DestinationFolderName = "SynthEBD - " + tempName;
        }

        if (DestinationFolderName.IsNullOrWhitespace())
        {
            DestinationFolderName = _configInstaller.DefaultDestinationFolderName;
        }
    }

    public void UpdateWarningMessage(Manifest manifest)
    {
        if (!MiscFunctions.IsValidPath(DestinationFolderName))
        {
            WarningMessageColor = CommonColors.Red;
            WarningMessage = DestinationFolderName + " is not a valid directory name";
            _destinationFolderValid = false;
            return;
        }
        else
        {
            _destinationFolderValid = true;
        }

        bool exceedsPathLimit = false;

        foreach (var configPath in manifest.AssetPackPaths)
        {
            string extractedPath = Path.Combine(_parentVM.TempFolderPath, configPath);
            var tempAP = _assetPackIO.LoadAssetPack(extractedPath, _patcherState.GeneralSettings.RaceGroupings, new List<SkyrimMod>(), new BodyGenConfigs(), out bool loadSuccess);
            if (!loadSuccess)
            {
                continue; // fail silently; this isn't critical and any failure will be caught in the main ConfigInstaller
            }

            manifest.DestinationModFolder = DestinationFolderName;
            int longestPath = _configInstaller.GetLongestPathLength(tempAP, manifest, out _);

            if (longestPath > _modManagerVM.FilePathLimit)
            {
                exceedsPathLimit = true;
                break;
            }
        }

        if (exceedsPathLimit)
        {
            WarningMessage = "Some asset files will be renamed and moved to comply with the Path Length Limit" + Environment.NewLine + "This message is purely informational; there is no error";
            WarningMessageColor = CommonColors.Yellow;
        }
        else
        {
            WarningMessage = string.Empty;
        }
    }
}
