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
using ReactiveUI;

namespace SynthEBD
{
    public class VM_Subgroup : INotifyPropertyChanged
    {
        public VM_Subgroup(ObservableCollection<VM_RaceGrouping> RaceGroupingVMs, ObservableCollection<VM_Subgroup> parentCollection, VM_AssetPack parentAssetPack, bool setExplicitReferenceNPC)
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
            this.bAllowUnique = true;
            this.bAllowNonUnique = true;
            this.requiredSubgroups = new ObservableCollection<VM_Subgroup>();
            this.excludedSubgroups = new ObservableCollection<VM_Subgroup>();
            this.addKeywords = new ObservableCollection<VM_CollectionMemberString>();
            this.probabilityWeighting = 1;
            if (parentAssetPack.TrackedBodyGenConfig != null)
            {
                this.allowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(parentAssetPack.TrackedBodyGenConfig.DescriptorUI);
                this.disallowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(parentAssetPack.TrackedBodyGenConfig.DescriptorUI);
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

            // must be set after Parent Asset Pack
            if (setExplicitReferenceNPC)
            {
                this.PathsMenu = new VM_FilePathReplacementMenu(this, setExplicitReferenceNPC, this.lk);
            }
            else
            {
                this.PathsMenu = new VM_FilePathReplacementMenu(this, setExplicitReferenceNPC, parentAssetPack.RecordTemplateLinkCache);
                parentAssetPack.WhenAnyValue(x => x.RecordTemplateLinkCache).Subscribe(x => this.PathsMenu.ReferenceLinkCache = parentAssetPack.RecordTemplateLinkCache);
            }

            AddAllowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.allowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.allowedAttributes, true, parentAssetPack.AttributeGroupMenu.Groups))
                );

            AddDisallowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.disallowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.disallowedAttributes, false, parentAssetPack.AttributeGroupMenu.Groups))
                );

            AddNPCKeyword = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.addKeywords.Add(new VM_CollectionMemberString("", this.addKeywords))
                );

            AddPath = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.PathsMenu.Paths.Add(new VM_FilePathReplacement(this.PathsMenu))
                );

            AddSubgroup = new SynthEBD.RelayCommand(
               canExecute: _ => true,
               execute: _ => this.subgroups.Add(new VM_Subgroup(RaceGroupingVMs, this.subgroups, this.ParentAssetPack, setExplicitReferenceNPC))
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
        public bool bAllowUnique { get; set; }
        public bool bAllowNonUnique { get; set; }
        public ObservableCollection<VM_Subgroup> requiredSubgroups { get; set; }
        public ObservableCollection<VM_Subgroup> excludedSubgroups { get; set; }
        public ObservableCollection<VM_CollectionMemberString> addKeywords { get; set; }
        public int probabilityWeighting { get; set; }
        //public ObservableCollection<VM_FilePathReplacement> paths { get; set; }
        public VM_FilePathReplacementMenu PathsMenu { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu allowedBodyGenDescriptors { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu disallowedBodyGenDescriptors { get; set; }
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
        public VM_AssetPack ParentAssetPack { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_Subgroup GetViewModelFromModel(AssetPack.Subgroup model, VM_Settings_General generalSettingsVM, ObservableCollection<VM_Subgroup> parentCollection, VM_AssetPack parentAssetPack, bool setExplicitReferenceNPC)
        {
            var viewModel = new VM_Subgroup(generalSettingsVM.RaceGroupings, parentCollection, parentAssetPack, setExplicitReferenceNPC);

            viewModel.id = model.id;
            viewModel.name = model.name;
            viewModel.enabled = model.enabled;
            viewModel.distributionEnabled = model.distributionEnabled;
            viewModel.allowedRaces = new ObservableCollection<FormKey>(model.allowedRaces);
            viewModel.AllowedRaceGroupings = GetRaceGroupingsByLabel(model.allowedRaceGroupings, generalSettingsVM.RaceGroupings);
            viewModel.disallowedRaces = new ObservableCollection<FormKey>(model.disallowedRaces);
            viewModel.DisallowedRaceGroupings = GetRaceGroupingsByLabel(model.disallowedRaceGroupings, generalSettingsVM.RaceGroupings);
            viewModel.allowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.allowedAttributes, parentAssetPack.AttributeGroupMenu.Groups, true);
            viewModel.disallowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.disallowedAttributes, parentAssetPack.AttributeGroupMenu.Groups, false);
            foreach (var x in viewModel.disallowedAttributes) { x.DisplayForceIfOption = false; }
            viewModel.bAllowUnique = model.bAllowUnique;
            viewModel.bAllowNonUnique = model.bAllowNonUnique;
            viewModel.RequiredSubgroupIDs = model.requiredSubgroups;
            viewModel.requiredSubgroups = new ObservableCollection<VM_Subgroup>();
            viewModel.ExcludedSubgroupIDs = model.excludedSubgroups;
            viewModel.excludedSubgroups = new ObservableCollection<VM_Subgroup>();
            viewModel.addKeywords = new ObservableCollection<VM_CollectionMemberString>();
            getModelKeywords(model, viewModel);
            viewModel.probabilityWeighting = model.ProbabilityWeighting;
            viewModel.PathsMenu = VM_FilePathReplacementMenu.GetViewModelFromModels(model.paths, viewModel, setExplicitReferenceNPC);
            viewModel.weightRange = model.weightRange;

            if (parentAssetPack.TrackedBodyGenConfig != null)
            {
                viewModel.allowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.allowedBodyGenDescriptors, parentAssetPack.TrackedBodyGenConfig.DescriptorUI);
                viewModel.disallowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.disallowedBodyGenDescriptors, parentAssetPack.TrackedBodyGenConfig.DescriptorUI);
            }

            foreach (var sg in model.subgroups)
            {
                viewModel.subgroups.Add(GetViewModelFromModel(sg, generalSettingsVM, viewModel.subgroups, parentAssetPack, setExplicitReferenceNPC));
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

        public void CallRefreshTrackedBodyShapeDescriptorsC(object sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshTrackedBodyShapeDescriptors();
        }

        public void CallRefreshTrackedBodyShapeDescriptorsP(object sender, PropertyChangedEventArgs e)
        {
            RefreshTrackedBodyShapeDescriptors();
        }

        public void RefreshTrackedBodyShapeDescriptors()
        {
            if (this.ParentAssetPack.TrackedBodyGenConfig != null)
            {
                this.allowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(this.ParentAssetPack.TrackedBodyGenConfig.DescriptorUI);
                this.disallowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(this.ParentAssetPack.TrackedBodyGenConfig.DescriptorUI);
            }
        }

        public static AssetPack.Subgroup DumpViewModelToModel(VM_Subgroup viewModel)
        {
            var model = new AssetPack.Subgroup();

            model.id = viewModel.id;
            model.name = viewModel.name;
            model.enabled = viewModel.enabled;
            model.distributionEnabled = viewModel.distributionEnabled;
            model.allowedRaces = viewModel.allowedRaces.ToHashSet();
            model.allowedRaceGroupings = viewModel.AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet();
            model.disallowedRaces = viewModel.disallowedRaces.ToHashSet();
            model.disallowedRaceGroupings = viewModel.DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet();
            model.allowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.allowedAttributes);
            model.disallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.disallowedAttributes);
            model.bAllowUnique = viewModel.bAllowUnique;
            model.bAllowNonUnique = viewModel.bAllowNonUnique;
            model.requiredSubgroups = viewModel.requiredSubgroups.Select(x => x.id).ToHashSet();
            model.excludedSubgroups = viewModel.excludedSubgroups.Select(x => x.id).ToHashSet();
            model.addKeywords = viewModel.addKeywords.Select(x => x.Content).ToHashSet();
            model.ProbabilityWeighting = viewModel.probabilityWeighting;
            model.paths = VM_FilePathReplacementMenu.DumpViewModelToModels(viewModel.PathsMenu);
            model.weightRange = viewModel.weightRange;

            model.allowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.allowedBodyGenDescriptors);
            model.disallowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.disallowedBodyGenDescriptors);

            foreach (var sg in viewModel.subgroups)
            {
                model.subgroups.Add(DumpViewModelToModel(sg));
            }

            return model;
        }
    }
}
