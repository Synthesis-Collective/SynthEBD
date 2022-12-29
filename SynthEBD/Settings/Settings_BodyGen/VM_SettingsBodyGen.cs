using System.Collections.ObjectModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_SettingsBodyGen : VM
{
    private readonly VM_BodyGenRacialMapping.Factory _mappingFactory;
    public VM_SettingsBodyGen(
        VM_BodyGenConfig.Factory bodyGenConfigFactory,
        VM_BodyGenRacialMapping.Factory mappingFactory,
        VM_Settings_General generalSettingsVM)
    {
        _mappingFactory = mappingFactory;

        DisplayMaleConfig = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                this.CurrentlyDisplayedConfig = this.CurrentMaleConfig;
                this.DisplayedConfigIsFemale = false;
                this.DisplayedConfigIsMale = true;
            }
        );

        DisplayFemaleConfig = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                this.CurrentlyDisplayedConfig = this.CurrentFemaleConfig;
                this.DisplayedConfigIsFemale = true;
                this.DisplayedConfigIsMale = false;
            }
        );

        AddNewMaleConfig = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var newConfig = bodyGenConfigFactory(this.MaleConfigs);
                newConfig.Gender = Gender.Male;
                this.MaleConfigs.Add(newConfig);
                this.CurrentMaleConfig = newConfig;
                this.CurrentlyDisplayedConfig = newConfig;
                this.DisplayedConfigIsMale = true;
                this.DisplayedConfigIsFemale = false;
                InitializeNewBodyGenConfig(newConfig, generalSettingsVM);
            });

        AddNewFemaleConfig = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var newConfig = bodyGenConfigFactory(this.FemaleConfigs);
                newConfig.Gender = Gender.Female;
                this.FemaleConfigs.Add(newConfig);
                this.CurrentFemaleConfig = newConfig;
                this.CurrentlyDisplayedConfig = newConfig;
                this.DisplayedConfigIsFemale = true;
                this.DisplayedConfigIsMale = false;
                InitializeNewBodyGenConfig(newConfig, generalSettingsVM);
            });

        this.WhenAnyValue(x => x.CurrentMaleConfig).Subscribe(x =>
        {
            if (DisplayedConfigIsMale)
            {
                CurrentlyDisplayedConfig = CurrentMaleConfig;
            }
        });

        this.WhenAnyValue(x => x.CurrentFemaleConfig).Subscribe(x =>
        {
            if (DisplayedConfigIsFemale)
            {
                CurrentlyDisplayedConfig = CurrentFemaleConfig;
            }
        });
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

    public static void GetViewModelFromModel(
        BodyGenConfigs configModels,
        Settings_BodyGen model,
        VM_SettingsBodyGen viewModel,
        VM_BodyGenConfig.Factory bodyGenConfigFactory,
        VM_Settings_General generalSettingsVM)
    {
        foreach(var config in configModels.Female)
        {
            var subConfig = bodyGenConfigFactory(viewModel.FemaleConfigs);
            subConfig.CopyInViewModelFromModel(config, generalSettingsVM);
            viewModel.FemaleConfigs.Add(subConfig);
        }

        foreach(var config in configModels.Male)
        {
            var subConfig = bodyGenConfigFactory(viewModel.MaleConfigs);
            subConfig.CopyInViewModelFromModel(config, generalSettingsVM);
            viewModel.MaleConfigs.Add(subConfig);
        }

        viewModel.CurrentMaleConfig = GetConfigByLabel(model.CurrentMaleConfig, viewModel.MaleConfigs);
        viewModel.CurrentFemaleConfig = GetConfigByLabel(model.CurrentFemaleConfig, viewModel.FemaleConfigs);

        if (viewModel.CurrentFemaleConfig == null)
        {
            if (viewModel.FemaleConfigs.Count > 0)
            {
                viewModel.CurrentFemaleConfig = viewModel.FemaleConfigs[0];
            }
        }

        if (viewModel.CurrentMaleConfig == null)
        {
            if (viewModel.MaleConfigs.Count > 0)
            {
                viewModel.CurrentMaleConfig = viewModel.MaleConfigs[0];
            }
        }

        if (viewModel.CurrentFemaleConfig != null)
        {
            viewModel.CurrentlyDisplayedConfig = viewModel.CurrentFemaleConfig;
            viewModel.DisplayedConfigIsFemale = true;
            viewModel.DisplayedConfigIsMale = false;
        }
        else if (viewModel.CurrentMaleConfig != null)
        {
            viewModel.CurrentlyDisplayedConfig = viewModel.CurrentMaleConfig;
            viewModel.DisplayedConfigIsFemale = false;
            viewModel.DisplayedConfigIsMale = true;
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

    public static void DumpViewModelToModel(VM_SettingsBodyGen viewModel, Settings_BodyGen model, BodyGenConfigs configModels)
    {
        if (viewModel.CurrentMaleConfig != null)
        {
            model.CurrentMaleConfig = viewModel.CurrentMaleConfig.Label;
        }
        else
        {
            model.CurrentMaleConfig = null;
        }

        if (viewModel.CurrentFemaleConfig != null)
        {
            model.CurrentFemaleConfig = viewModel.CurrentFemaleConfig.Label;
        }
        else
        {
            model.CurrentFemaleConfig = null;
        }


        configModels.Male.Clear();
        configModels.Female.Clear();

        foreach (var maleVM in viewModel.MaleConfigs)
        {
            configModels.Male.Add(VM_BodyGenConfig.DumpViewModelToModel(maleVM));    
        }
        foreach (var femaleVM in viewModel.FemaleConfigs)
        {
            configModels.Female.Add(VM_BodyGenConfig.DumpViewModelToModel(femaleVM));
        }
    }

    public void InitializeNewBodyGenConfig(VM_BodyGenConfig newConfig, VM_Settings_General generalSettingsVM)
    {
        var starterGroup = new VM_CollectionMemberString("Group 1", newConfig.GroupUI.TemplateGroups);
        newConfig.GroupUI.TemplateGroups.Add(starterGroup);

        var starterMapping = _mappingFactory(newConfig.GroupUI, generalSettingsVM.RaceGroupings);
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