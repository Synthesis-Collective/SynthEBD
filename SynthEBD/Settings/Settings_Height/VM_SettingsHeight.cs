using Mutagen.Bethesda.Plugins.Cache;
using System.Collections.ObjectModel;
using Noggog;
using ReactiveUI;
using DynamicData;

namespace SynthEBD;

/// <summary>
/// View model for the Height settings tab, backing the <see cref="Settings_Height"/>
/// model. Holds the global height-patching toggles plus the collection of available
/// <see cref="VM_HeightConfig"/> height-distribution configs and the currently selected one.
/// </summary>
public class VM_SettingsHeight : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly VM_HeightConfig.Factory _configFactory;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    /// <summary>
    /// Seeds an empty selected config, mirrors the environment link cache, and wires the
    /// add/delete height-config <see cref="RelayCommand"/>s (delete prompts for file removal).
    /// </summary>
    public VM_SettingsHeight(IEnvironmentStateProvider environmentProvider, FileDialogs fileDialogs, VM_HeightConfig.Factory configFactory, PatcherState patcherState, Logger logger)
    {
        _environmentProvider = environmentProvider;
        _configFactory = configFactory;
        _patcherState = patcherState;
        _logger = logger;
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
    public bool bApplyWithoutOverride { get; set; } = false;

    /// <summary>Number of height-group tiles per row in the config editor's <c>UniformGrid</c> layout (UI-only preference).</summary>
    public int HeightGroupsPerRow { get; set; } = 4;

    public VM_HeightConfig SelectedHeightConfig { get; set; }

    public ObservableCollection<VM_HeightConfig> AvailableHeightConfigs { get; set; } = new();

    public ILinkCache lk { get; private set; }

    public RelayCommand AddHeightConfig { get; }

    public RelayCommand DeleteCurrentHeightConfig { get; }

    /// <summary>Model → VM: loads the height toggles and resolves the selected height config by label.</summary>
    public void CopyInFromModel(Settings_Height model)
    {
        if (model == null)
        {
            return;
        }

        _logger.LogStartupEventStart("Loading UI for Height Menu");

        bChangeNPCHeight = model.bChangeNPCHeight;
        bChangeRaceHeight = model.bChangeRaceHeight;
        bOverwriteNonDefaultNPCHeights = model.bOverwriteNonDefaultNPCHeights;
        bApplyWithoutOverride = model.bApplyWithoutOverride;
        HeightGroupsPerRow = Math.Max(1, model.HeightGroupsPerRow);

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
        _logger.LogStartupEventEnd("Loading UI for Height Menu");
    }

    /// <summary>VM → Model: writes the height toggles and the selected config's label back to a new <see cref="Settings_Height"/>.</summary>
    public Settings_Height DumpViewModelToModel()
    {
        Settings_Height model = new();
        model.bChangeNPCHeight = bChangeNPCHeight;
        model.bChangeRaceHeight = bChangeRaceHeight;
        model.bOverwriteNonDefaultNPCHeights = bOverwriteNonDefaultNPCHeights;
        model.SelectedHeightConfig = SelectedHeightConfig.Label;
        model.bApplyWithoutOverride = bApplyWithoutOverride;
        model.HeightGroupsPerRow = HeightGroupsPerRow;
        return model;
    }
}