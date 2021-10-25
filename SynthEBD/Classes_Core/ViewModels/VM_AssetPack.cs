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
        public VM_AssetPack(ObservableCollection<VM_AssetPack> parentCollection)
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

            RemoveAssetPackConfigFile = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => { FileDialogs.ConfirmFileDeletion(this.SourcePath, "Asset Pack Config File"); this.ParentCollection.Remove(this); }
                );
        }

        public string groupName { get; set; }
        public Gender gender { get; set; }
        public bool displayAlerts { get; set; }
        public string userAlert { get; set; }
        public ObservableCollection<VM_Subgroup> subgroups { get; set; }
        public ObservableCollection<VM_RaceGrouping> RaceGroupingList { get; set; }

        public bool IsSelected { get; set; }

        public string SourcePath { get; set; }

        public Dictionary<Gender, string> GenderEnumDict { get; } = new Dictionary<Gender, string>()
        {
            {Gender.male, "Male"},
            {Gender.female, "Female"},
        };

        public ObservableCollection<VM_AssetPack> ParentCollection { get; set; }

        public RelayCommand RemoveAssetPackConfigFile { get; }

        public static ObservableCollection<VM_AssetPack> GetViewModelsFromModels(List<AssetPack> assetPacks, VM_Settings_General generalSettingsVM, Settings_TexMesh texMeshSettings, List<string> loadedAssetPackPaths)
        {
            ObservableCollection<VM_AssetPack> viewModels = new ObservableCollection<VM_AssetPack>();

            for (int i = 0; i < assetPacks.Count; i++)
            {
                var viewModel = GetViewModelFromModel(assetPacks[i], generalSettingsVM, viewModels);
                viewModel.IsSelected = texMeshSettings.SelectedAssetPacks.Contains(assetPacks[i].groupName);

                viewModel.SourcePath = loadedAssetPackPaths[i];

                viewModels.Add(viewModel);
            }
            return viewModels;
        }
        public static VM_AssetPack GetViewModelFromModel(AssetPack model, VM_Settings_General generalSettingsVM, ObservableCollection<VM_AssetPack> parentCollection)
        {
            var viewModel = new VM_AssetPack(parentCollection);
            viewModel.groupName = model.groupName;
            viewModel.gender = model.gender;
            viewModel.displayAlerts = model.displayAlerts;
            viewModel.userAlert = model.userAlert;

            viewModel.RaceGroupingList = new ObservableCollection<VM_RaceGrouping>(generalSettingsVM.RaceGroupings);

            foreach (var sg in model.subgroups)
            {
                viewModel.subgroups.Add(VM_Subgroup.GetViewModelFromModel(sg, generalSettingsVM, viewModel.subgroups));
            }

            // go back through now that all subgroups have corresponding view models, and link the required and excluded subgroups
            ObservableCollection<VM_Subgroup> flattenedSubgroupList = flattenSubgroupVMs(viewModel.subgroups, new ObservableCollection<VM_Subgroup>());
            LinkRequiredSubgroups(flattenedSubgroupList);

            return viewModel;
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

        public event PropertyChangedEventHandler PropertyChanged;
    }
    
}
