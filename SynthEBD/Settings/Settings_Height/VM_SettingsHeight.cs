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
    private readonly PatcherState _patcherState;
    public VM_SettingsHeight(IEnvironmentStateProvider environmentProvider, FileDialogs fileDialogs, VM_HeightConfig.Factory configFactory, PatcherState patcherState)
    {
        _environmentProvider = environmentProvider;
        _configFactory = configFactory;
        _patcherState = patcherState;
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

    public void CopyInFromModel(Settings_Height model)
    {
        if (model == null)
        {
            return;
        }

        bChangeNPCHeight = model.bChangeNPCHeight;
        bChangeRaceHeight = model.bChangeRaceHeight;
        bOverwriteNonDefaultNPCHeights = model.bOverwriteNonDefaultNPCHeights;

        foreach (var hconfig in AvailableHeightConfigs)
        {
            if (hconfig.Label == model.SelectedHeightConfig)
            {
                SelectedHeightConfig = hconfig;
                break;
            }
        }

        if (string.IsNullOrEmpty(model.SelectedHeightConfig) && AvailableHeightConfigs.Any())
        {
            SelectedHeightConfig = AvailableHeightConfigs.First();
        }
    }

    public Settings_Height DumpViewModelToModel()
    {
        Settings_Height model = new();
        model.bChangeNPCHeight = bChangeNPCHeight;
        model.bChangeRaceHeight = bChangeRaceHeight;
        model.bOverwriteNonDefaultNPCHeights = bOverwriteNonDefaultNPCHeights;
        model.SelectedHeightConfig = SelectedHeightConfig.Label;
        return model;
    }
}