using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_AssetPackDirectReplacerMenu : INotifyPropertyChanged
    {
        public VM_AssetPackDirectReplacerMenu(VM_AssetPack parent)
        {
            ReplacerGroups = new ObservableCollection<VM_AssetReplacerGroup>();
            ParentAssetPack = parent;

            AddGroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.ReplacerGroups.Add(new VM_AssetReplacerGroup(this))
            );
        }
        public ObservableCollection<VM_AssetReplacerGroup> ReplacerGroups { get; set; }
        public VM_AssetReplacerGroup DisplayedGroup { get; set; }
        public VM_AssetPack ParentAssetPack { get; set; }

        public RelayCommand AddGroup { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_AssetPackDirectReplacerMenu GetViewModelFromModels(List<AssetReplacerGroup> models, VM_AssetPack parentAssetPack, VM_Settings_General generalSettingsVM)
        {
            VM_AssetPackDirectReplacerMenu viewModel = new VM_AssetPackDirectReplacerMenu(parentAssetPack);
            foreach(var model in models)
            {
                viewModel.ReplacerGroups.Add(VM_AssetReplacerGroup.GetViewModelFromModel(model, viewModel, generalSettingsVM));
            }

            return viewModel;
        }

        public static List<AssetReplacerGroup> DumpViewModelToModels(VM_AssetPackDirectReplacerMenu viewModel)
        {
            List<AssetReplacerGroup> models = new List<AssetReplacerGroup>();
            foreach (var subViewModel in viewModel.ReplacerGroups)
            {
                models.Add(VM_AssetReplacerGroup.DumpViewModelToModel(subViewModel));
            }
            return models;
        }
    }

    public class VM_AssetReplacerGroup : INotifyPropertyChanged
    {
        public VM_AssetReplacerGroup(VM_AssetPackDirectReplacerMenu parent)
        {
            this.Label = "";
            this.Subgroups = new ObservableCollection<VM_Subgroup>();
            this.ParentMenu = parent;

            Remove = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => ParentMenu.ReplacerGroups.Remove(this)
            );

            AddTopLevelSubgroup = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.Subgroups.Add(new VM_Subgroup(parent.ParentAssetPack.RaceGroupingList, Subgroups, parent.ParentAssetPack))
                );
        }

        public string Label { get; set; }
        public ObservableCollection<VM_Subgroup> Subgroups { get; set; }

        public VM_AssetPackDirectReplacerMenu ParentMenu{ get; set; }

        public RelayCommand Remove { get; }

        public RelayCommand AddTopLevelSubgroup { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_AssetReplacerGroup GetViewModelFromModel(AssetReplacerGroup model, VM_AssetPackDirectReplacerMenu parentMenu, VM_Settings_General generalSettingsVM)
        {
            VM_AssetReplacerGroup viewModel = new VM_AssetReplacerGroup(parentMenu);
            viewModel.Label = model.Label;
            foreach (var sg in model.Subgroups)
            {
                viewModel.Subgroups.Add(VM_Subgroup.GetViewModelFromModel(sg, generalSettingsVM, viewModel.Subgroups, viewModel.ParentMenu.ParentAssetPack));
            }

            ObservableCollection<VM_Subgroup> flattenedSubgroupList = VM_AssetPack.FlattenSubgroupVMs(viewModel.Subgroups, new ObservableCollection<VM_Subgroup>());
            VM_AssetPack.LinkRequiredSubgroups(flattenedSubgroupList);
            VM_AssetPack.LinkExcludedSubgroups(flattenedSubgroupList);

            return viewModel;
        }

        public static AssetReplacerGroup DumpViewModelToModel(VM_AssetReplacerGroup viewModel)
        {
            AssetReplacerGroup model = new AssetReplacerGroup();
            model.Label = viewModel.Label;
            foreach (var svm in viewModel.Subgroups)
            {
                model.Subgroups.Add(VM_Subgroup.DumpViewModelToModel(svm));
            }
            return model;
        }
    }
}
