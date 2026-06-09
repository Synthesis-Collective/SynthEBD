using System.Collections.ObjectModel;
using ReactiveUI;
using Noggog;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// View model for the BodyGen settings tab, backing the <see cref="Settings_BodyGen"/> model
/// (and the separate <see cref="BodyGenConfigs"/> store). Holds the male/female
/// <see cref="VM_BodyGenConfig"/> collections, tracks the current per-gender and currently
/// displayed config, and stores the per-gender preview NPC and slider group.
/// </summary>
public class VM_SettingsBodyGen : VM
{
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly VM_BodyGenRacialMapping.Factory _mappingFactory;
    /// <summary>
    /// Mirrors the environment link cache and wires the display-male/female, add-new-male/female
    /// config <see cref="RelayCommand"/>s plus subscriptions that keep the displayed config in
    /// sync when the current per-gender config changes.
    /// </summary>
    public VM_SettingsBodyGen(
        PatcherState patcherState,
        Logger logger,
        VM_BodyGenConfig.Factory bodyGenConfigFactory,
        VM_BodyGenRacialMapping.Factory mappingFactory,
        VM_Settings_General generalSettingsVM,
        IEnvironmentStateProvider environmentProvider)
    {
        _patcherState = patcherState;
        _logger = logger;
        _mappingFactory = mappingFactory;

        environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        DisplayMaleConfig = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                CurrentlyDisplayedConfig = CurrentMaleConfig;
                DisplayedConfigIsFemale = false;
                DisplayedConfigIsMale = true;
            }
        );

        DisplayFemaleConfig = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                CurrentlyDisplayedConfig = CurrentFemaleConfig;
                DisplayedConfigIsFemale = true;
                DisplayedConfigIsMale = false;
            }
        );

        AddNewMaleConfig = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var newConfig = bodyGenConfigFactory(MaleConfigs);
                newConfig.Gender = Gender.Male;
                MaleConfigs.Add(newConfig);
                CurrentMaleConfig = newConfig;
                CurrentlyDisplayedConfig = newConfig;
                DisplayedConfigIsMale = true;
                DisplayedConfigIsFemale = false;
                InitializeNewBodyGenConfig(newConfig, generalSettingsVM);
            });

        AddNewFemaleConfig = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var newConfig = bodyGenConfigFactory(FemaleConfigs);
                newConfig.Gender = Gender.Female;
                FemaleConfigs.Add(newConfig);
                CurrentFemaleConfig = newConfig;
                CurrentlyDisplayedConfig = newConfig;
                DisplayedConfigIsFemale = true;
                DisplayedConfigIsMale = false;
                InitializeNewBodyGenConfig(newConfig, generalSettingsVM);
            });

        this.WhenAnyValue(x => x.CurrentMaleConfig).Subscribe(x =>
        {
            if (DisplayedConfigIsMale)
            {
                CurrentlyDisplayedConfig = CurrentMaleConfig;
            }
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.CurrentFemaleConfig).Subscribe(x =>
        {
            if (DisplayedConfigIsFemale)
            {
                CurrentlyDisplayedConfig = CurrentFemaleConfig;
            }
        }).DisposeWith(this);
    }

    public ObservableCollection<VM_BodyGenConfig> MaleConfigs { get; set; } = new();
    public ObservableCollection<VM_BodyGenConfig> FemaleConfigs { get; set; } = new();
    public VM_BodyGenConfig CurrentMaleConfig { get; set; } = null;
    public VM_BodyGenConfig CurrentFemaleConfig { get; set; } = null;
    public VM_BodyGenConfig CurrentlyDisplayedConfig { get; set; } = null;

    public bool DisplayedConfigIsFemale { get; set; } = true;
    public bool DisplayedConfigIsMale { get; set; } = false;

    public FormKey PreviewNpcMale { get; set; } = FormKey.Null;
    public FormKey PreviewNpcFemale { get; set; } = FormKey.Null;
    public string PreviewSliderGroupMale { get; set; } = "";
    public string PreviewSliderGroupFemale { get; set; } = "";
    public IEnumerable<Type> NpcPickerFormKeys { get; set; } = typeof(INpcGetter).AsEnumerable();
    public ILinkCache lk { get; private set; }

    public RelayCommand DisplayMaleConfig { get; }
    public RelayCommand AddNewMaleConfig { get; }
    public RelayCommand DisplayFemaleConfig { get; }
    public RelayCommand AddNewFemaleConfig { get; }

    /// <summary>
    /// Model → VM: loads preview settings, disposes and rebuilds the male/female config VMs from
    /// <paramref name="configModels"/>, and resolves/initializes the current and displayed configs.
    /// </summary>
    public void CopyInViewModelFromModel(
        BodyGenConfigs configModels,
        Settings_BodyGen model,
        VM_BodyGenConfig.Factory bodyGenConfigFactory,
        ObservableCollection<VM_RaceGrouping> mainRaceGroupings)
    {
        if (model == null)
        {
            return;
        }
        PreviewNpcMale = model.PreviewNpcMale;
        PreviewNpcFemale = model.PreviewNpcFemale;
        PreviewSliderGroupMale = model.PreviewSliderGroupMale ?? "";
        PreviewSliderGroupFemale = model.PreviewSliderGroupFemale ?? "";

        // Explicitly dispose the outgoing configs before dropping them. Each
        // VM_BodyGenConfig owns a VM_BodyGenTemplateMenu which owns a VM_CharacterViewer;
        // without this, re-loading settings leaks the GL context and any in-flight NPC
        // load from the previous session's configs.
        foreach (var config in FemaleConfigs) config.Dispose();
        foreach (var config in MaleConfigs) config.Dispose();
        FemaleConfigs.Clear();
        MaleConfigs.Clear();

        foreach(var config in configModels.Female)
        {
            _logger.LogStartupEventStart("Loading BodyGen Config UI for " + config.Label);
            var subConfig = bodyGenConfigFactory(FemaleConfigs);
            subConfig.CopyInViewModelFromModel(config, mainRaceGroupings);
            FemaleConfigs.Add(subConfig);
            _logger.LogStartupEventEnd("Loading BodyGen Config UI for " + config.Label);
        }

        foreach(var config in configModels.Male)
        {
            _logger.LogStartupEventStart("Loading BodyGen Config UI for " + config.Label);
            var subConfig = bodyGenConfigFactory(MaleConfigs);
            subConfig.CopyInViewModelFromModel(config, mainRaceGroupings);
            MaleConfigs.Add(subConfig);
            _logger.LogStartupEventEnd("Loading BodyGen Config UI for " + config.Label);
        }

        CurrentMaleConfig = MaleConfigs.FirstOrDefault(x => x.Label == model.CurrentMaleConfig);
        CurrentFemaleConfig = FemaleConfigs.FirstOrDefault(x => x.Label == model.CurrentFemaleConfig);

        if (CurrentFemaleConfig == null)
        {
            if (FemaleConfigs.Count > 0)
            {
                CurrentFemaleConfig = FemaleConfigs[0];
            }
        }

        if (CurrentMaleConfig == null)
        {
            if (MaleConfigs.Count > 0)
            {
                CurrentMaleConfig = MaleConfigs[0];
            }
        }

        if (CurrentFemaleConfig != null)
        {
            CurrentlyDisplayedConfig = CurrentFemaleConfig;
            DisplayedConfigIsFemale = true;
            DisplayedConfigIsMale = false;
        }
        else if (CurrentMaleConfig != null)
        {
            CurrentlyDisplayedConfig = CurrentMaleConfig;
            DisplayedConfigIsFemale = false;
            DisplayedConfigIsMale = true;
        }
    }

    /// <summary>Returns true if any config in the collection has the given label.</summary>
    public static bool ConfigExists(string label, ObservableCollection<VM_BodyGenConfig> configs)
    {
        foreach (var config in configs)
        {
            if (config.Label == label)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Returns the first config with the given label, or null if none matches.</summary>
    public static VM_BodyGenConfig GetConfigByLabel(string label, ObservableCollection<VM_BodyGenConfig> configs)
    {
        foreach (var config in configs)
        {
            if (config.Label == label)
            {
                return config;
            }
        }
        return null;
    }

    /// <summary>VM → Model: writes the current male/female config labels and per-gender preview NPC/slider-group settings to a new <see cref="Settings_BodyGen"/>.</summary>
    public Settings_BodyGen DumpViewModelToModel()
    {
        Settings_BodyGen model = new();
        if (CurrentMaleConfig != null)
        {
            model.CurrentMaleConfig = CurrentMaleConfig.Label;
        }
        else
        {
            model.CurrentMaleConfig = null;
        }

        if (CurrentFemaleConfig != null)
        {
            model.CurrentFemaleConfig = CurrentFemaleConfig.Label;
        }
        else
        {
            model.CurrentFemaleConfig = null;
        }

        model.PreviewNpcMale = PreviewNpcMale;
        model.PreviewNpcFemale = PreviewNpcFemale;
        model.PreviewSliderGroupMale = PreviewSliderGroupMale ?? "";
        model.PreviewSliderGroupFemale = PreviewSliderGroupFemale ?? "";

        return model;
    }

    /// <summary>VM → Models: dumps every male/female config VM into a new <see cref="BodyGenConfigs"/> store.</summary>
    public BodyGenConfigs DumpBodyGenConfigsToModels()
    {
        BodyGenConfigs cfgs = new();

        foreach (var maleVM in MaleConfigs)
        {
            cfgs.Male.Add(maleVM.DumpViewModelToModel());
        }
        foreach (var femaleVM in FemaleConfigs)
        {
            cfgs.Female.Add(femaleVM.DumpViewModelToModel());
        }
        return cfgs;
    }

    /// <summary>Seeds a freshly-created config with a starter template group, a humanoid-race mapping, and a starter combination.</summary>
    public void InitializeNewBodyGenConfig(VM_BodyGenConfig newConfig, VM_Settings_General generalSettingsVM)
    {
        var starterGroup = new VM_CollectionMemberString("Group 1", newConfig.GroupUI.TemplateGroups);
        newConfig.GroupUI.TemplateGroups.Add(starterGroup);

        var starterMapping = _mappingFactory(newConfig.GroupUI, generalSettingsVM.RaceGroupingEditor.RaceGroupings);
        starterMapping.Label = "Mapping 1";
        var humanoidRaces = starterMapping.RaceGroupings.RaceGroupingSelections.FirstOrDefault(x => x.SubscribedMasterRaceGrouping.Label.Equals("humanoid", StringComparison.OrdinalIgnoreCase));
        if (humanoidRaces != null)
        {
            humanoidRaces.IsSelected = true;
        }
        var starterCombination = new VM_BodyGenCombination(newConfig.GroupUI, starterMapping);
        starterCombination.Members.Add(new VM_CollectionMemberString("Group 1", starterCombination.Members));
        starterMapping.Combinations.Add(starterCombination);
        newConfig.GroupMappingUI.RacialTemplateGroupMap.Add(starterMapping);
    }
}