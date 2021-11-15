using Mutagen.Bethesda.Cache.Implementations;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SynthEBD
{
    public class VM_AssetPack : INotifyPropertyChanged
    {
        public VM_AssetPack(ObservableCollection<VM_AssetPack> parentCollection, VM_SettingsBodyGen bodygenSettingsVM)
        {
            this.groupName = "";
            this.gender = Gender.male;
            this.displayAlerts = true;
            this.userAlert = "";
            this.subgroups = new ObservableCollection<VM_Subgroup>();

            this.RaceGroupingList = new ObservableCollection<VM_RaceGrouping>();

            this.IsSelected = true;

            this.ParentCollection = parentCollection;

            this.SourcePath = "";

            this.CurrentBodyGenSettings = bodygenSettingsVM;
            switch (this.gender)
            {
                case Gender.female: this.AvailableBodyGenConfigs = this.CurrentBodyGenSettings.FemaleConfigs; break;
                case Gender.male: this.AvailableBodyGenConfigs = this.CurrentBodyGenSettings.MaleConfigs; break;
            }

            this.PropertyChanged += RefreshTrackedBodyGenConfig;
            this.CurrentBodyGenSettings.PropertyChanged += RefreshTrackedBodyGenConfig;

            this.DefaultTemplateFK = new FormKey();
            this.NPCFormKeyTypes = typeof(INpcGetter).AsEnumerable();

            this.AdditionalRecordTemplateAssignments = new ObservableCollection<VM_AdditionalRecordTemplate>();

            /*
            switch (this.gender)
            {
                case Gender.female: this.TrackedBodyGenConfig = this.CurrentBodyGenSettings.CurrentFemaleConfig; break;
                case Gender.male: this.TrackedBodyGenConfig = this.CurrentBodyGenSettings.CurrentMaleConfig; break;
            }*/

            RemoveAssetPackConfigFile = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => { FileDialogs.ConfirmFileDeletion(this.SourcePath, "Asset Pack Config File"); this.ParentCollection.Remove(this); }
                );

            AddAdditionalRecordTemplateAssignment = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => { this.AdditionalRecordTemplateAssignments.Add(new VM_AdditionalRecordTemplate(this.RecordTemplateLinkCache, this.AdditionalRecordTemplateAssignments)); }
                );
        }

        public string groupName { get; set; }
        public Gender gender { get; set; }
        public bool displayAlerts { get; set; }
        public string userAlert { get; set; }
        public ObservableCollection<VM_Subgroup> subgroups { get; set; }
        public ObservableCollection<VM_RaceGrouping> RaceGroupingList { get; set; }

        public VM_BodyGenConfig TrackedBodyGenConfig { get; set; }
        public ObservableCollection<VM_BodyGenConfig> AvailableBodyGenConfigs { get; set; }
        public VM_SettingsBodyGen CurrentBodyGenSettings { get; set; }

        public bool IsSelected { get; set; }

        public string SourcePath { get; set; }

        public ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> RecordTemplateLinkCache { get; set; }

        public FormKey DefaultTemplateFK { get; set; }

        public IEnumerable<Type> NPCFormKeyTypes { get; set; }

        public ObservableCollection<VM_AdditionalRecordTemplate> AdditionalRecordTemplateAssignments { get; set; }

        public ObservableCollection<VM_AssetPack> ParentCollection { get; set; }

        public RelayCommand RemoveAssetPackConfigFile { get; }

        public RelayCommand AddAdditionalRecordTemplateAssignment { get; }

        public Dictionary<Gender, string> GenderEnumDict { get; } = new Dictionary<Gender, string>() // referenced by xaml; don't trust VS reference count
        {
            {Gender.male, "Male"},
            {Gender.female, "Female"},
        };

        public static ObservableCollection<VM_AssetPack> GetViewModelsFromModels(List<AssetPack> assetPacks, VM_Settings_General generalSettingsVM, Settings_TexMesh texMeshSettings, List<string> loadedAssetPackPaths, VM_SettingsBodyGen bodygenSettingsVM, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache)
        {
            ObservableCollection<VM_AssetPack> viewModels = new ObservableCollection<VM_AssetPack>();

            for (int i = 0; i < assetPacks.Count; i++)
            {
                var viewModel = GetViewModelFromModel(assetPacks[i], generalSettingsVM, viewModels, bodygenSettingsVM, recordTemplateLinkCache);
                viewModel.IsSelected = texMeshSettings.SelectedAssetPacks.Contains(assetPacks[i].GroupName);

                viewModel.SourcePath = loadedAssetPackPaths[i];

                viewModel.RecordTemplateLinkCache = recordTemplateLinkCache;

                viewModels.Add(viewModel);
            }
            return viewModels;
        }
        public static VM_AssetPack GetViewModelFromModel(AssetPack model, VM_Settings_General generalSettingsVM, ObservableCollection<VM_AssetPack> parentCollection, VM_SettingsBodyGen bodygenSettingsVM, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache)
        {
            var viewModel = new VM_AssetPack(parentCollection, bodygenSettingsVM);
            viewModel.groupName = model.GroupName;
            viewModel.gender = model.Gender;
            viewModel.displayAlerts = model.DisplayAlerts;
            viewModel.userAlert = model.UserAlert;

            viewModel.RaceGroupingList = new ObservableCollection<VM_RaceGrouping>(generalSettingsVM.RaceGroupings);

            if (model.AssociatedBodyGenConfigName != "")
            {
                switch(viewModel.gender)
                {
                    case Gender.female:
                        viewModel.TrackedBodyGenConfig = bodygenSettingsVM.FemaleConfigs.Where(x => x.Label == model.AssociatedBodyGenConfigName).FirstOrDefault();
                        break;
                    case Gender.male:
                        viewModel.TrackedBodyGenConfig = bodygenSettingsVM.MaleConfigs.Where(x => x.Label == model.AssociatedBodyGenConfigName).FirstOrDefault();
                        break;
                }
            }
            else
            {
                viewModel.TrackedBodyGenConfig = new VM_BodyGenConfig(generalSettingsVM.RaceGroupings);
            }

            foreach (var sg in model.Subgroups)
            {
                viewModel.subgroups.Add(VM_Subgroup.GetViewModelFromModel(sg, generalSettingsVM, viewModel.subgroups, viewModel));
            }

            // go back through now that all subgroups have corresponding view models, and link the required and excluded subgroups
            ObservableCollection<VM_Subgroup> flattenedSubgroupList = flattenSubgroupVMs(viewModel.subgroups, new ObservableCollection<VM_Subgroup>());
            LinkRequiredSubgroups(flattenedSubgroupList);

            viewModel.DefaultTemplateFK = model.DefaultRecordTemplate;
            foreach(var additionalTemplateAssignment in model.AdditionalRecordTemplateAssignments)
            {
                var assignmentVM = new VM_AdditionalRecordTemplate(recordTemplateLinkCache, viewModel.AdditionalRecordTemplateAssignments);
                assignmentVM.RaceFormKeys = new ObservableCollection<FormKey>(additionalTemplateAssignment.Races);
                assignmentVM.TemplateNPC = additionalTemplateAssignment.TemplateNPC;
                viewModel.AdditionalRecordTemplateAssignments.Add(assignmentVM);
            }

            return viewModel;
        }

        public static List<string> DumpViewModelsToModels(ObservableCollection<VM_AssetPack> viewModels, List<AssetPack> models)
        {
            List<string> configPaths = new List<string>();
            models.Clear();

            foreach (var vm in viewModels)
            {
                AssetPack model = new AssetPack();
                model.GroupName = vm.groupName;
                model.Gender = vm.gender;
                model.DisplayAlerts = vm.displayAlerts;
                model.UserAlert = vm.userAlert;

                if (vm.TrackedBodyGenConfig != null)
                {
                    model.AssociatedBodyGenConfigName = vm.TrackedBodyGenConfig.Label;
                }

                model.DefaultRecordTemplate = vm.DefaultTemplateFK;
                model.AdditionalRecordTemplateAssignments = vm.AdditionalRecordTemplateAssignments.Select(x => VM_AdditionalRecordTemplate.DumpViewModelToModel(x)).ToHashSet();

                foreach (var svm in vm.subgroups)
                {
                    model.Subgroups.Add(VM_Subgroup.DumpViewModelToModel(svm));
                }

                models.Add(model);
                if (vm.SourcePath != "")
                {
                    configPaths.Add(vm.SourcePath);
                }
                else
                {
                    configPaths.Add("");
                }
            }
            return configPaths;
        }

        public static ObservableCollection<VM_Subgroup> flattenSubgroupVMs(ObservableCollection<VM_Subgroup> currentLevelSGs, ObservableCollection<VM_Subgroup> flattened)
        {
            foreach(var sg in currentLevelSGs)
            {
                flattened.Add(sg);
                flattenSubgroupVMs(sg.subgroups, flattened);
            }
            return flattened;
        }

        private static void LinkRequiredSubgroups(ObservableCollection<VM_Subgroup> flattenedSubgroups)
        {
            foreach (var sg in flattenedSubgroups)
            {
                foreach (string id in sg.RequiredSubgroupIDs)
                {
                    foreach (var candidate in flattenedSubgroups)
                    {
                        if (candidate.id == id)
                        {
                            sg.requiredSubgroups.Add(candidate);
                            break;
                        }
                    }
                }
            }
        }

        private static void LinkExcludedSubgroups(ObservableCollection<VM_Subgroup> flattenedSubgroups)
        {
            foreach (var sg in flattenedSubgroups)
            {
                foreach (string id in sg.ExcludedSubgroupIDs)
                {
                    foreach (var candidate in flattenedSubgroups)
                    {
                        if (candidate.id == id)
                        {
                            sg.excludedSubgroups.Add(candidate);
                            break;
                        }
                    }
                }
            }
        }


        public void RemoveAssetPackDialog()
        {
            MessageBoxResult result = MessageBox.Show("Are you sure you want to permanently delete this config file?", "", MessageBoxButton.YesNo);
            switch (result)
            {
                case MessageBoxResult.Yes:
                    if (File.Exists(this.SourcePath))
                    {
                        try
                        {
                            File.Delete(this.SourcePath);
                        }
                        catch
                        {
                            //Warn User
                        }
                    }
                    
                    break;
                case MessageBoxResult.No:
                    break;
            }
        }

        public void RefreshTrackedBodyGenConfig(object sender, PropertyChangedEventArgs e)
        {
            switch (this.gender)
            {
                case Gender.female: this.AvailableBodyGenConfigs = this.CurrentBodyGenSettings.FemaleConfigs; break;
                case Gender.male: this.AvailableBodyGenConfigs = this.CurrentBodyGenSettings.MaleConfigs; break;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
    
}
