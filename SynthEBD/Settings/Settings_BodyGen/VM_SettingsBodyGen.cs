using System.Collections.ObjectModel;
using ReactiveUI;
using Noggog;

namespace SynthEBD;

public class VM_SettingsBodyGen : VM
{
    private readonly PatcherState _patcherState;
    private readonly VM_BodyGenRacialMapping.Factory _mappingFactory;
    public VM_SettingsBodyGen(
        PatcherState patcherState,
        VM_BodyGenConfig.Factory bodyGenConfigFactory,
        VM_BodyGenRacialMapping.Factory mappingFactory,
        VM_Settings_General generalSettingsVM)
    {
        _patcherState = patcherState;
        _mappingFactory = mappingFactory;

        DisplayMaleConfig = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                CurrentlyDisplayedConfig = CurrentMaleConfig;
                DisplayedConfigIsFemale = false;
                DisplayedConfigIsMale = true;
            }
        );

        DisplayFemaleConfig = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                CurrentlyDisplayedConfig = CurrentFemaleConfig;
                DisplayedConfigIsFemale = true;
                DisplayedConfigIsMale = false;
            }
        );

        AddNewMaleConfig = new SynthEBD.RelayCommand(
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

        AddNewFemaleConfig = new SynthEBD.RelayCommand(
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

    public RelayCommand DisplayMaleConfig { get; }
    public RelayCommand AddNewMaleConfig { get; }
    public RelayCommand DisplayFemaleConfig { get; }
    public RelayCommand AddNewFemaleConfig { get; }

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
        FemaleConfigs.Clear();
        MaleConfigs.Clear();

        foreach(var config in configModels.Female)
        {
            var subConfig = bodyGenConfigFactory(FemaleConfigs);
            subConfig.CopyInViewModelFromModel(config, mainRaceGroupings);
            FemaleConfigs.Add(subConfig);
        }

        foreach(var config in configModels.Male)
        {
            var subConfig = bodyGenConfigFactory(MaleConfigs);
            subConfig.CopyInViewModelFromModel(config, mainRaceGroupings);
            MaleConfigs.Add(subConfig);
        }

        CurrentMaleConfig = MaleConfigs.Where(x => x.Label == model.CurrentMaleConfig).FirstOrDefault();
        CurrentFemaleConfig = FemaleConfigs.Where(x => x.Label == model.CurrentFemaleConfig).FirstOrDefault();

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
        return model;
    }

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

    public void InitializeNewBodyGenConfig(VM_BodyGenConfig newConfig, VM_Settings_General generalSettingsVM)
    {
        var starterGroup = new VM_CollectionMemberString("Group 1", newConfig.GroupUI.TemplateGroups);
        newConfig.GroupUI.TemplateGroups.Add(starterGroup);

        var starterMapping = _mappingFactory(newConfig.GroupUI, generalSettingsVM.RaceGroupingEditor.RaceGroupings);
        starterMapping.Label = "Mapping 1";
        var humanoidRaces = starterMapping.RaceGroupings.RaceGroupingSelections.Where(x => x.SubscribedMasterRaceGrouping.Label.Equals("humanoid", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
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