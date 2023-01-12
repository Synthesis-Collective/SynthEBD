using Mutagen.Bethesda.Plugins.Cache;
using System.Collections.ObjectModel;
using Noggog;
using ReactiveUI;
using DynamicData;

namespace SynthEBD;

public class VM_SettingsHeight : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly VM_HeightConfig.Factory _configFactory;
    public VM_SettingsHeight(IEnvironmentStateProvider environmentProvider, FileDialogs fileDialogs, VM_HeightConfig.Factory configFactory)
    {
        _environmentProvider = environmentProvider;
        _configFactory = configFactory;
        SelectedHeightConfig = _configFactory();

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
        
        AddHeightConfig = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                AvailableHeightConfigs.Add(_configFactory());
                SelectedHeightConfig = AvailableHeightConfigs.Last();
            }
        );

        DeleteCurrentHeightConfig = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (fileDialogs.ConfirmFileDeletion(SelectedHeightConfig.SourcePath, "Height Configuration File"))
                {
                    AvailableHeightConfigs.Remove(SelectedHeightConfig);
                    if (AvailableHeightConfigs.Count > 0)
                    {
                        SelectedHeightConfig = AvailableHeightConfigs[0];
                    }
                }
            }
        );
    }

    public bool bChangeNPCHeight { get; set; } = true;
    public bool bChangeRaceHeight { get; set; } = true;
    public bool bOverwriteNonDefaultNPCHeights { get; set; } = true;

    public VM_HeightConfig SelectedHeightConfig { get; set; }

    public ObservableCollection<VM_HeightConfig> AvailableHeightConfigs { get; set; } = new();

    public ILinkCache lk { get; private set; }

    public RelayCommand AddHeightConfig { get; }

    public RelayCommand DeleteCurrentHeightConfig { get; }

    public static void GetViewModelFromModel(VM_SettingsHeight viewModel, Settings_Height model)
    {
        viewModel.bChangeNPCHeight = model.bChangeNPCHeight;
        viewModel.bChangeRaceHeight = model.bChangeRaceHeight;
        viewModel.bOverwriteNonDefaultNPCHeights = model.bOverwriteNonDefaultNPCHeights;

        foreach (var hconfig in viewModel.AvailableHeightConfigs)
        {
            if (hconfig.Label == model.SelectedHeightConfig)
            {
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