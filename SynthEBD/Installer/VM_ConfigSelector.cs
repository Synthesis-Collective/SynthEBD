using System.Collections.ObjectModel;
using Noggog.WPF;
using ReactiveUI;

namespace SynthEBD;

public class VM_ConfigSelector : ViewModel, IHasInstallerOptions
{
    public VM_ConfigSelector(Manifest manifest, Window_ConfigInstaller window, VM_ConfigInstaller parentVM)
    {
        Manifest = manifest;
        AssociatedWindow = window;
        Name = manifest.ConfigName;
        Description = manifest.ConfigDescription;
        AssetPackPaths = new ObservableCollection<string>(manifest.AssetPackPaths);
        RecordTemplatePaths = new ObservableCollection<string>(manifest.RecordTemplatePaths);
        BodyGenConfigPaths = new ObservableCollection<string>(manifest.BodyGenConfigPaths);
        DownloadInfo = manifest.DownloadInfo;
        OptionsDescription = manifest.OptionsDescription;
        Options = new ObservableCollection<VM_Option>();
        foreach (var option in manifest.Options)
        {
            Options.Add(new VM_Option(option, this, this));
        }

        DisplayedOptionsDescription = OptionsDescription;
        DisplayedOptions = new ObservableCollection<VM_Option>(Options);
        SelectedOption = null;
        Parent = null;
        Installer = this;
        this.SelectedAssetPackPaths = new HashSet<string>();
        this.SelectedRecordTemplatePaths = new HashSet<string>();
        this.SelectedBodyGenConfigPaths = new HashSet<string>();

        OKvisibility = this.Options == null || !this.Options.Any();
        BackVisibility = false;

        BackFlag = false;

        SelectionChain = new List<IHasInstallerOptions>();

        this.WhenAnyValue(x => x.SelectedOption).Subscribe(_ =>
        {
            if (SelectedOption is not null && BackFlag == false)
            {
                SelectionChain.Add(SelectedOption);
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
            }

            if (SelectionChain.Count > 1)
            {
                BackVisibility = true;
            }
            else
            {
                BackVisibility = false;
            }
        });

        Back = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                BackFlag = true;
                SelectionChain.Remove(SelectionChain.Last());
                foreach (var option in SelectionChain.Last().Options)
                {
                    option.IsSelected = false;
                }
                SelectedOption = SelectionChain.Last(); // Triggers subscription immediately; subscription happens before moving on to next line
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
                foreach (var selection in SelectionChain)
                {
                    SelectedAssetPackPaths.UnionWith(selection.AssetPackPaths);
                    SelectedRecordTemplatePaths.UnionWith(selection.RecordTemplatePaths);
                    SelectedBodyGenConfigPaths.UnionWith(selection.BodyGenConfigPaths);
                    DownloadInfo.UnionWith(selection.DownloadInfo);
                    if (!string.IsNullOrWhiteSpace(selection.DestinationModFolder))
                    {
                        Manifest.DestinationModFolder = selection.DestinationModFolder;
                    }
                }
                parentVM.DownloadMenu = new VM_DownloadCoordinator(DownloadInfo, AssociatedWindow, parentVM);
                parentVM.DisplayedViewModel = parentVM.DownloadMenu;
            }
        );
    }

    public Manifest Manifest { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string DestinationModFolder { get; set; }
    public ObservableCollection<string> AssetPackPaths { get; set; }
    public ObservableCollection<string> RecordTemplatePaths { get; set; }
    public ObservableCollection<string> BodyGenConfigPaths { get; set; }
    public HashSet<Manifest.DownloadInfoContainer> DownloadInfo { get; set; }
    public string OptionsDescription { get; set; }
    public ObservableCollection<VM_Option> Options { get; set; }
    public string DisplayedOptionsDescription { get; set; }
    public ObservableCollection<VM_Option> DisplayedOptions { get; set; }
    public IHasInstallerOptions SelectedOption { get; set; }
    public IHasInstallerOptions Parent { get; set; }
    public bool IsSelected { get; set; }
    public VM_ConfigSelector Installer { get; set; }
    public RelayCommand Back { get; }
    public bool BackVisibility { get; set; }
    public RelayCommand OK { get; }
    public bool OKvisibility { get; set; }

    public List<IHasInstallerOptions> SelectionChain { get; set; }

    public RelayCommand Cancel { get; }

    private bool BackFlag { get; set; }

    public HashSet<string> SelectedAssetPackPaths { get; set; }
    public HashSet<string> SelectedRecordTemplatePaths { get; set; }
    public HashSet<string> SelectedBodyGenConfigPaths { get; set; }

    public Window_ConfigInstaller AssociatedWindow { get; set; }
}
public class VM_Option : ViewModel, IHasInstallerOptions
{
    public VM_Option(Manifest.Option option, IHasInstallerOptions parent, VM_ConfigSelector installer)
    {
        Name = option.Name;
        Description = option.Description;
        AssetPackPaths = new ObservableCollection<string>(option.AssetPackPaths);
        RecordTemplatePaths = new ObservableCollection<string>(option.RecordTemplatePaths);
        BodyGenConfigPaths = new ObservableCollection<string>(option.BodyGenConfigPaths);
        DownloadInfo = option.DownloadInfo;
        OptionsDescription = option.OptionsDescription;
        Options = new ObservableCollection<VM_Option>() ?? new ObservableCollection<VM_Option>();
        foreach (var subOption in option.Options)
        {
            Options.Add(new VM_Option(subOption, this, installer));
        }
        SelectedOption = null;
        Parent = parent;
        Installer = installer;

        DestinationModFolder = option.DestinationModFolder;
    }

    public string Name { get; set; }
    public string Description { get; set; }
    public ObservableCollection<string> AssetPackPaths { get; set; }
    public ObservableCollection<string> RecordTemplatePaths { get; set; }
    public ObservableCollection<string> BodyGenConfigPaths { get; set; }
    public HashSet<Manifest.DownloadInfoContainer> DownloadInfo { get; set; }
    public ObservableCollection<VM_Option> Options { get; set; }
    public string OptionsDescription { get; set; }
    public IHasInstallerOptions SelectedOption { get; set; }
    public IHasInstallerOptions Parent { get; set; }
    public bool IsSelected { get; set; }
    public VM_ConfigSelector Installer { get; set; }
    public string DestinationModFolder { get; set; }
}

public interface IHasInstallerOptions
{
    public string Name { get; set; }
    public string Description { get; set; }
    public ObservableCollection<string> AssetPackPaths { get; set; }
    public ObservableCollection<string> RecordTemplatePaths { get; set; }
    public ObservableCollection<string> BodyGenConfigPaths { get; set; }
    public HashSet<Manifest.DownloadInfoContainer> DownloadInfo { get; set; }
    public string OptionsDescription { get; set; }
    public ObservableCollection<VM_Option> Options { get; set; }
    public IHasInstallerOptions SelectedOption { get; set; }
    public IHasInstallerOptions Parent { get; set; }
    public bool IsSelected { get; set; }
    public VM_ConfigSelector Installer { get; set; }
    public string DestinationModFolder { get; set; }
}