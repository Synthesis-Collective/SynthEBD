using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Specialized;

namespace SynthEBD
{
    public class VM_Subgroup : INotifyPropertyChanged
    {
        public VM_Subgroup(ObservableCollection<VM_RaceGrouping> RaceGroupingVMs, ObservableCollection<VM_Subgroup> parentCollection, VM_AssetPack parentAssetPack)
        {
            this.id = "";
            this.name = "New";
            this.enabled = true;
            this.distributionEnabled = true;
            this.allowedRaces = new ObservableCollection<FormKey>();
            this.AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(RaceGroupingVMs);
            this.disallowedRaces = new ObservableCollection<FormKey>();
            this.DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(RaceGroupingVMs);
            this.allowedAttributes = new ObservableCollection<VM_NPCAttribute>();
            this.disallowedAttributes = new ObservableCollection<VM_NPCAttribute>();
            this.forceIfAttributes = new ObservableCollection<VM_NPCAttribute>();
            this.bAllowUnique = true;
            this.bAllowNonUnique = true;
            this.requiredSubgroups = new ObservableCollection<VM_Subgroup>();
            this.excludedSubgroups = new ObservableCollection<VM_Subgroup>();
            this.addKeywords = new ObservableCollection<VM_CollectionMemberString>();
            this.probabilityWeighting = 1;
            this.paths = new ObservableCollection<VM_FilePathReplacement>();
            if (parentAssetPack.TrackedBodyGenConfig != null)
            {
                this.allowedBodyGenDescriptors = new VM_BodyGenMorphDescriptorSelectionMenu(parentAssetPack.TrackedBodyGenConfig.DescriptorUI);
                this.disallowedBodyGenDescriptors = new VM_BodyGenMorphDescriptorSelectionMenu(parentAssetPack.TrackedBodyGenConfig.DescriptorUI);
            }
            this.weightRange = new NPCWeightRange();
            this.subgroups = new ObservableCollection<VM_Subgroup>();

            //UI-related
            this.lk = GameEnvironmentProvider.MyEnvironment.LinkCache;
            this.RacePickerFormKeys = typeof(IRaceGetter).AsEnumerable();
            this.RequiredSubgroupIDs = new HashSet<string>();
            this.ExcludedSubgroupIDs = new HashSet<string>();
            this.ParentCollection = parentCollection;
            this.ParentAssetPack = parentAssetPack;

            //ParentAssetPack.TrackedBodyGenConfig.DescriptorUI.TemplateDescriptorList.CollectionChanged += CallRefreshTrackedMorphDescriptorsC;
            //ParentAssetPack.TrackedBodyGenConfig.DescriptorUI.PropertyChanged += CallRefreshTrackedMorphDescriptorsP;
            //ParentAssetPack.TrackedBodyGenConfig.PropertyChanged += CallRefreshTrackedMorphDescriptorsP;
            //ParentAssetPack.PropertyChanged += CallRefreshTrackedMorphDescriptorsP; // this is required for now, but see if there's a way to target it so that it's only triggered when ParentAssetPack.TrackedBodyGenConfig (but not one of its sub-properties) changes.

            AddAllowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.allowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.allowedAttributes))
                );

            AddDisallowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.disallowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.disallowedAttributes))
                );

            AddForceIfAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.forceIfAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.forceIfAttributes))
                );

            AddNPCKeyword = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.addKeywords.Add(new VM_CollectionMemberString("", this.addKeywords))
                );

            AddPath = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.paths.Add(new VM_FilePathReplacement(this.paths))
                );

            AddSubgroup = new SynthEBD.RelayCommand(
               canExecute: _ => true,
               execute: _ => this.subgroups.Add(new VM_Subgroup(RaceGroupingVMs, this.subgroups, this.ParentAssetPack))
               );

            DeleteMe = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.ParentCollection.Remove(this)
                );

            DeleteRequiredSubgroup = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.requiredSubgroups.Remove((VM_Subgroup)x)
                );

            DeleteExcludedSubgroup = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.excludedSubgroups.Remove((VM_Subgroup)x)
                );
        }

        public string id { get; set; }
        public string name { get; set; }
        public bool enabled { get; set; }
        public bool distributionEnabled { get; set; }
        public ObservableCollection<FormKey> allowedRaces { get; set; }
        public VM_RaceGroupingCheckboxList AllowedRaceGroupings { get; set; }
        public ObservableCollection<FormKey> disallowedRaces { get; set; }
        public VM_RaceGroupingCheckboxList DisallowedRaceGroupings { get; set; }
        public ObservableCollection<VM_NPCAttribute> allowedAttributes { get; set; }
        public ObservableCollection<VM_NPCAttribute> disallowedAttributes { get; set; }
        public ObservableCollection<VM_NPCAttribute> forceIfAttributes { get; set; }
        public bool bAllowUnique { get; set; }
        public bool bAllowNonUnique { get; set; }
        public ObservableCollection<VM_Subgroup> requiredSubgroups { get; set; }
        public ObservableCollection<VM_Subgroup> excludedSubgroups { get; set; }
        public ObservableCollection<VM_CollectionMemberString> addKeywords { get; set; }
        public int probabilityWeighting { get; set; }
        public ObservableCollection<VM_FilePathReplacement> paths { get; set; }
        public VM_BodyGenMorphDescriptorSelectionMenu allowedBodyGenDescriptors { get; set; }
        public VM_BodyGenMorphDescriptorSelectionMenu disallowedBodyGenDescriptors { get; set; }
        public NPCWeightRange weightRange { get; set; }
        public ObservableCollection<VM_Subgroup> subgroups { get; set; }

        //UI-related
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> RacePickerFormKeys { get; set; }

        public string TopLevelSubgroupID { get; set; }

        public RelayCommand AddAllowedAttribute { get; }
        public RelayCommand AddDisallowedAttribute { get; }
        public RelayCommand AddForceIfAttribute { get; }
        public RelayCommand AddNPCKeyword { get; }
        public RelayCommand AddPath { get; }
        public RelayCommand AddSubgroup { get; }
        public RelayCommand DeleteMe { get; }
        public RelayCommand DeleteRequiredSubgroup { get; }
        public RelayCommand DeleteExcludedSubgroup { get; }

        public HashSet<string> RequiredSubgroupIDs { get; set; } // temporary placeholder for RequiredSubgroups until all subgroups are loaded in
        public HashSet<string> ExcludedSubgroupIDs { get; set; } // temporary placeholder for ExcludedSubgroups until all subgroups are loaded in

        ObservableCollection<VM_Subgroup> ParentCollection { get; set; }
        VM_AssetPack ParentAssetPack { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_Subgroup GetViewModelFromModel(AssetPack.Subgroup model, VM_Settings_General generalSettingsVM, ObservableCollection<VM_Subgroup> parentCollection, VM_AssetPack parentAssetPack)
        {
            var viewModel = new VM_Subgroup(generalSettingsVM.RaceGroupings, parentCollection, parentAssetPack);

            viewModel.id = model.id;
            viewModel.name = model.name;
            viewModel.enabled = model.enabled;
            viewModel.distributionEnabled = model.distributionEnabled;
            viewModel.allowedRaces = new ObservableCollection<FormKey>(model.allowedRaces);
            viewModel.AllowedRaceGroupings = GetRaceGroupingsByLabel(model.allowedRaceGroupings, generalSettingsVM.RaceGroupings);
            viewModel.disallowedRaces = new ObservableCollection<FormKey>(model.disallowedRaces);
            viewModel.DisallowedRaceGroupings = GetRaceGroupingsByLabel(model.disallowedRaceGroupings, generalSettingsVM.RaceGroupings);
            viewModel.allowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.allowedAttributes);
            viewModel.disallowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.disallowedAttributes);
            viewModel.forceIfAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.forceIfAttributes);
            viewModel.bAllowUnique = model.bAllowUnique;
            viewModel.bAllowNonUnique = model.bAllowNonUnique;
            viewModel.RequiredSubgroupIDs = model.requiredSubgroups;
            viewModel.requiredSubgroups = new ObservableCollection<VM_Subgroup>();
            viewModel.ExcludedSubgroupIDs = model.excludedSubgroups;
            viewModel.excludedSubgroups = new ObservableCollection<VM_Subgroup>();
            viewModel.addKeywords = new ObservableCollection<VM_CollectionMemberString>();
            getModelKeywords(model, viewModel);
            viewModel.probabilityWeighting = model.probabilityWeighting;
            viewModel.paths = VM_FilePathReplacement.GetViewModelsFromModels(model.paths);
            viewModel.weightRange = model.weightRange;

            viewModel.allowedBodyGenDescriptors = VM_BodyGenMorphDescriptorSelectionMenu.InitializeFromHashSet(model.allowedBodyGenDescriptors, parentAssetPack.TrackedBodyGenConfig.DescriptorUI);
            viewModel.disallowedBodyGenDescriptors = VM_BodyGenMorphDescriptorSelectionMenu.InitializeFromHashSet(model.disallowedBodyGenDescriptors, parentAssetPack.TrackedBodyGenConfig.DescriptorUI);

            foreach (var sg in model.subgroups)
            {
                viewModel.subgroups.Add(GetViewModelFromModel(sg, generalSettingsVM, viewModel.subgroups, parentAssetPack));
            }

            return viewModel;
        }

        public static VM_RaceGroupingCheckboxList GetRaceGroupingsByLabel(HashSet<string> groupingStrings, ObservableCollection<VM_RaceGrouping> allRaceGroupings)
        {
            VM_RaceGroupingCheckboxList checkBoxList = new VM_RaceGroupingCheckboxList(allRaceGroupings);

            foreach (var raceGroupingSelection in checkBoxList.RaceGroupingSelections) // loop through all available RaceGroupings
            {
                foreach (string s in groupingStrings) // loop through all of the RaceGrouping labels stored in the models
                {
                    if (raceGroupingSelection.Label == s)
                    {
                        raceGroupingSelection.IsSelected = true;
                        break;
                    }
                }
            }

            return checkBoxList;
        }

        public static void getModelKeywords(AssetPack.Subgroup model, VM_Subgroup viewmodel)
        {
            foreach (string kw in model.addKeywords)
            {
                viewmodel.addKeywords.Add(new VM_CollectionMemberString(kw, viewmodel.addKeywords));
            }
        }

        public void CallRefreshTrackedMorphDescriptorsC(object sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshTrackedMorphDescriptors();
        }

        public void CallRefreshTrackedMorphDescriptorsP(object sender, PropertyChangedEventArgs e)
        {
            RefreshTrackedMorphDescriptors();
        }

        public void RefreshTrackedMorphDescriptors()
        {
            if (this.ParentAssetPack.TrackedBodyGenConfig != null)
            {
                this.allowedBodyGenDescriptors = new VM_BodyGenMorphDescriptorSelectionMenu(this.ParentAssetPack.TrackedBodyGenConfig.DescriptorUI);
                this.disallowedBodyGenDescriptors = new VM_BodyGenMorphDescriptorSelectionMenu(this.ParentAssetPack.TrackedBodyGenConfig.DescriptorUI);
            }
        }
    }
}
