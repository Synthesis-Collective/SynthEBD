using Mutagen.Bethesda.Plugins.Cache;
using System.Collections.ObjectModel;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

public class VM_SettingsHeight : VM
{
    public VM_SettingsHeight()
    {
        PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
        
        AddHeightConfig = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                this.AvailableHeightConfigs.Add(new VM_HeightConfig());
                this.SelectedHeightConfig = this.AvailableHeightConfigs.Last();
            }
        );

        DeleteCurrentHeightConfig = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (FileDialogs.ConfirmFileDeletion(SelectedHeightConfig.SourcePath, "Height Configuration File"))
                {
                    this.AvailableHeightConfigs.Remove(SelectedHeightConfig);
                    if (this.AvailableHeightConfigs.Count > 0)
                    {
                        this.SelectedHeightConfig = AvailableHeightConfigs[0];
                    }
                }
            }
        );
    }

    public bool bChangeNPCHeight { get; set; } = true;
    public bool bChangeRaceHeight { get; set; } = true;
    public bool bOverwriteNonDefaultNPCHeights { get; set; } = true;

    public VM_HeightConfig SelectedHeightConfig { get; set; } = new();

    public ObservableCollection<VM_HeightConfig> AvailableHeightConfigs { get; set; } = new();

    public ILinkCache lk { get; private set; }

    public RelayCommand AddHeightConfig { get; }

    public RelayCommand DeleteCurrentHeightConfig { get; }

    public static void GetViewModelFromModel(VM_SettingsHeight viewModel, Settings_Height model)
    {
        viewModel.bChangeNPCHeight = model.bChangeNPCHeight;
        viewModel.bChangeRaceHeight = model.bChangeRaceHeight;
        viewModel.bOverwriteNonDefaultNPCHeights = model.bOverwriteNonDefaultNPCHeights;

        bool currentConfigFound = false;
        foreach (var hconfig in viewModel.AvailableHeightConfigs)
        {
            if (hconfig.Label == model.SelectedHeightConfig)
            {
                currentConfigFound = true;
                viewModel.SelectedHeightConfig = hconfig;
                break;
            }
        }

        if (string.IsNullOrEmpty(model.SelectedHeightConfig) && viewModel.AvailableHeightConfigs.Any())
        {
            viewModel.SelectedHeightConfig = viewModel.AvailableHeightConfigs.First();
        }
    }

    public static void DumpViewModelToModel(VM_SettingsHeight viewModel, Settings_Height model)
    {
        model.bChangeNPCHeight = viewModel.bChangeNPCHeight;
        model.bChangeRaceHeight = viewModel.bChangeRaceHeight;
        model.bOverwriteNonDefaultNPCHeights = viewModel.bOverwriteNonDefaultNPCHeights;
        model.SelectedHeightConfig = viewModel.SelectedHeightConfig.Label;
    }
}