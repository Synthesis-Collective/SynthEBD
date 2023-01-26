using System.Collections.ObjectModel;
using System.Windows.Documents;
using ReactiveUI;
using Noggog;

namespace SynthEBD;

public class VM_ConfigSelector : VM
{
    public VM_ConfigSelector(Manifest manifest, Window_ConfigInstaller window, VM_ConfigInstaller parentVM)
    {
        Manifest = manifest;
        UpgradeVersion0(); // upgrade pre-0.8.3 version of the manifest if necessary to maintain compatibilty
        AssociatedWindow = window;
        Name = manifest.ConfigName;
        Description = manifest.ConfigDescription;
        LastSelectionChainIndex = manifest.Options.Count - 1;

        InitializeOptions(manifest);

        Installer = this;

        OKvisibility = this.Options == null || !this.Options.Any();

        parentVM.InstallationMessage = manifest.InstallationMessage;

        this.WhenAnyValue(x => x.SelectedOption).Subscribe(_ =>
        {
            SelectionMade();
        }).DisposeWith(this);

        Back = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                BackTrack();
            }
        );

        Cancel = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                parentVM.Cancelled = true;
                AssociatedWindow.Close();
            }
        );

        OK = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                Finalize(parentVM);
            }
        );
    }

    public Manifest Manifest { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string DestinationModFolder { get; set; }
    public ObservableCollection<VM_ConfigSelectorOption> Options { get; set; } = new();
    public string DisplayedOptionsDescription { get; set; }
    public ObservableCollection<VM_ConfigSelectorOption> DisplayedOptions { get; set; }
    public VM_ConfigSelectorOption SelectedOption { get; set; } = null;
    public VM_ConfigSelector Installer { get; set; }
    public RelayCommand Back { get; }
    public bool BackVisibility { get; set; } = false;
    public RelayCommand OK { get; }
    public bool OKvisibility { get; set; }

    public List<VM_ConfigSelectorOption> CurrentSelectionChain { get; set; } = new();
    public List<List<VM_ConfigSelectorOption>> SelectionChains { get; set; } = new();
    public int CurrentSelectionChainIndex { get; set; } = 0;
    private int CurrentSelectionChainDepth { get; set; } = 0;
    private int LastSelectionChainIndex { get; set; } = 0;
    public RelayCommand Cancel { get; }

    private bool BackFlag { get; set; } = false;

    public Window_ConfigInstaller AssociatedWindow { get; set; }

    private void UpgradeVersion0()
    {
        if (Manifest.Version == 0)
        {
            Manifest.Option rootOption = new();
            rootOption.OptionsDescription = Manifest.OptionsDescription;
            rootOption.FileExtensionMap = Manifest.FileExtensionMap;
            rootOption.DownloadInfo = Manifest.DownloadInfo;
            rootOption.DestinationModFolder = Manifest.DestinationModFolder;
            rootOption.AssetPackPaths = Manifest.AssetPackPaths;
            rootOption.BodyGenConfigPaths = Manifest.BodyGenConfigPaths;
            rootOption.RecordTemplatePaths = Manifest.RecordTemplatePaths;
            rootOption.Options.AddRange(Manifest.Options);
            Manifest.Options.Clear();
            Manifest.Options.Add(rootOption);
        }
    }
    private void InitializeOptions(Manifest manifest)
    {
        if (manifest.Options.Any())
        {
            foreach (var option in manifest.Options) { SelectionChains.Add(new List<VM_ConfigSelectorOption>()); }
            CurrentSelectionChain = SelectionChains.First();
            SelectedOption = new VM_ConfigSelectorOption(manifest.Options.First(), null, this);
        }
    }
    private void SelectionMade()
    {
        if (SelectedOption is not null && BackFlag == false)
        {
            CurrentSelectionChain.Add(SelectedOption);
            CurrentSelectionChainDepth++;
            DisplayedOptionsDescription = SelectedOption.OptionsDescription;
            DisplayedOptions = SelectedOption.Options;

            bool hasDisplayedOptions = DisplayedOptions != null && DisplayedOptions.Any();

            if (CurrentSelectionChainIndex == LastSelectionChainIndex && !hasDisplayedOptions)
            {
                OKvisibility = true;
            }
            else
            {
                OKvisibility = false;

                if (!hasDisplayedOptions)
                {
                    CurrentSelectionChainIndex++;
                    CurrentSelectionChain = SelectionChains[CurrentSelectionChainIndex];
                    SelectedOption = new VM_ConfigSelectorOption(this.Manifest.Options[CurrentSelectionChainIndex], null, this);
                }
            }
        }

        if (CurrentSelectionChain.Count > 1 || CurrentSelectionChainIndex > 0)
        {
            BackVisibility = true;
        }
        else
        {
            BackVisibility = false;
        }
    }
    
    private void BackTrack()
    {
        BackFlag = true;
        CurrentSelectionChain.Remove(CurrentSelectionChain.Last());
        foreach (var option in CurrentSelectionChain.Last().Options)
        {
            option.IsSelected = false;
        }
        if (CurrentSelectionChainDepth > 0)
        {
            CurrentSelectionChainDepth--;
        }
        else if (CurrentSelectionChainIndex > 0)
        {
            CurrentSelectionChainIndex--;
            CurrentSelectionChain = SelectionChains[CurrentSelectionChainIndex];
            CurrentSelectionChainDepth = CurrentSelectionChain.Count - 1;
            SelectedOption = CurrentSelectionChain.Last();
        }
        else
        {
            return; // should never be reached because the back button isn't visible in this case
        }
        SelectedOption = CurrentSelectionChain.Last(); // Triggers subscription immediately; subscription happens before moving on to next line
        DisplayedOptionsDescription = SelectedOption.OptionsDescription;
        DisplayedOptions = SelectedOption.Options;

        if (DisplayedOptions == null || !DisplayedOptions.Any())
        {
            OKvisibility = true;
        }
        else
        {
            OKvisibility = false;
        }

        BackFlag = false;
    }

    private void Finalize(VM_ConfigInstaller parentVM)
    {
        foreach (var selection in CurrentSelectionChain)
        {
            Manifest.AssetPackPaths.UnionWith(selection.AssociatedModel.AssetPackPaths);
            Manifest.RecordTemplatePaths.UnionWith(selection.AssociatedModel.RecordTemplatePaths);
            Manifest.BodyGenConfigPaths.UnionWith(selection.AssociatedModel.BodyGenConfigPaths);
            Manifest.DownloadInfo.UnionWith(selection.AssociatedModel.DownloadInfo);
            Manifest.AddPatchableRaces.UnionWith(selection.AssociatedModel.AddPatchableRaces);
            if (!string.IsNullOrWhiteSpace(selection.AssociatedModel.DestinationModFolder))
            {
                Manifest.DestinationModFolder = selection.AssociatedModel.DestinationModFolder;
            }
            foreach (var mapping in selection.AssociatedModel.FileExtensionMap)
            {
                if (!Manifest.FileExtensionMap.ContainsKey(mapping.Key))
                {
                    Manifest.FileExtensionMap.Add(mapping.Key, mapping.Value);
                }
                else
                {
                    Manifest.FileExtensionMap[mapping.Key] = mapping.Value;
                }
            }
        }
        parentVM.DownloadMenu = new VM_DownloadCoordinator(Manifest.DownloadInfo, AssociatedWindow, parentVM);
        parentVM.DisplayedViewModel = parentVM.DownloadMenu;
    }
}
public class VM_ConfigSelectorOption : VM
{
    public VM_ConfigSelectorOption(Manifest.Option option, VM_ConfigSelectorOption parent, VM_ConfigSelector installer)
    {
        Name = option.Name;
        Description = option.Description;
        AssociatedModel = option;
        OptionsDescription = option.OptionsDescription;
        foreach (var subOption in option.Options)
        {
            Options.Add(new VM_ConfigSelectorOption(subOption, this, installer));
        }

        Parent = parent;
        Installer = installer;
    }

    public string Name { get; set; }
    public string Description { get; set; }
    public Manifest.Option AssociatedModel { get; set; }
    public ObservableCollection<VM_ConfigSelectorOption> Options { get; set; } = new ObservableCollection<VM_ConfigSelectorOption>();
    public string OptionsDescription { get; set; }
    public VM_ConfigSelectorOption SelectedOption { get; set; } = null;
    public VM_ConfigSelectorOption Parent { get; set; }
    public bool IsSelected { get; set; }
    public VM_ConfigSelector Installer { get; set; }
}