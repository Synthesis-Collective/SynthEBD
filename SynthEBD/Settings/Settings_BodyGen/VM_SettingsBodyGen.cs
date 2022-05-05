using System.Collections.ObjectModel;
using System.ComponentModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_SettingsBodyGen : INotifyPropertyChanged
{
    public VM_SettingsBodyGen(VM_Settings_General generalSettingsVM)
    {
        this.MaleConfigs = new ObservableCollection<VM_BodyGenConfig>();
        this.FemaleConfigs = new ObservableCollection<VM_BodyGenConfig>();
        this.CurrentMaleConfig = null;
        this.CurrentFemaleConfig = null;
        this.CurrentlyDisplayedConfig = null;
        this.DisplayedConfigIsFemale = true;
        this.DisplayedConfigIsMale = false;

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
                var newConfig = new VM_BodyGenConfig(generalSettingsVM, this.MaleConfigs, this) { Gender = Gender.Male};
                this.MaleConfigs.Add(newConfig);
                this.CurrentMaleConfig = newConfig;
                this.CurrentlyDisplayedConfig = newConfig;
                this.DisplayedConfigIsMale = true;
                this.DisplayedConfigIsFemale = false;
            });

        AddNewFemaleConfig = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var newConfig = new VM_BodyGenConfig(generalSettingsVM, this.FemaleConfigs, this) { Gender = Gender.Female};
                this.FemaleConfigs.Add(newConfig);
                this.CurrentFemaleConfig = newConfig;
                this.CurrentlyDisplayedConfig = newConfig;
                this.DisplayedConfigIsFemale = true;
                this.DisplayedConfigIsMale = false;
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

    public ObservableCollection<VM_BodyGenConfig> MaleConfigs { get; set; }
    public ObservableCollection<VM_BodyGenConfig> FemaleConfigs { get; set; }
    public VM_BodyGenConfig CurrentMaleConfig { get; set; }
    public VM_BodyGenConfig CurrentFemaleConfig { get; set; }
    public VM_BodyGenConfig CurrentlyDisplayedConfig { get; set; }

    public bool DisplayedConfigIsFemale { get; set; }
    public bool DisplayedConfigIsMale { get; set; }

    public RelayCommand DisplayMaleConfig { get; }
    public RelayCommand AddNewMaleConfig { get; }
    public RelayCommand DisplayFemaleConfig { get; }
    public RelayCommand AddNewFemaleConfig { get; }

    public event PropertyChangedEventHandler PropertyChanged;

    public static void GetViewModelFromModel(BodyGenConfigs configModels, Settings_BodyGen model, VM_SettingsBodyGen viewModel, VM_Settings_General generalSettingsVM)
    {
        foreach(var config in configModels.Female)
        {
            viewModel.FemaleConfigs.Add(VM_BodyGenConfig.GetViewModelFromModel(config, generalSettingsVM, viewModel.FemaleConfigs, viewModel));
        }

        foreach(var config in configModels.Male)
        {
            viewModel.MaleConfigs.Add(VM_BodyGenConfig.GetViewModelFromModel(config, generalSettingsVM, viewModel.MaleConfigs, viewModel));
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
}