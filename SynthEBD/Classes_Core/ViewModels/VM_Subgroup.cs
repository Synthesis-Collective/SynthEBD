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
        public VM_Subgroup(ObservableCollection<VM_RaceGrouping> RaceGroupingVMs, ObservableCollection<VM_Subgroup> parentCollection, VM_AssetPack parentAssetPack, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu, bool setExplicitReferenceNPC)
        {
            this.ID = "";
            this.Name = "New";
            this.Enabled = true;
            this.DistributionEnabled = true;
            this.AllowedRaces = new ObservableCollection<FormKey>();
            this.AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(RaceGroupingVMs);
            this.DisallowedRaces = new ObservableCollection<FormKey>();
            this.DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(RaceGroupingVMs);
            this.AllowedAttributes = new ObservableCollection<VM_NPCAttribute>();
            this.DisallowedAttributes = new ObservableCollection<VM_NPCAttribute>();
            this.AllowUnique = true;
            this.AllowNonUnique = true;
            this.RequiredSubgroups = new ObservableCollection<VM_Subgroup>();
            this.ExcludedSubgroups = new ObservableCollection<VM_Subgroup>();
            this.AddKeywords = new ObservableCollection<VM_CollectionMemberString>();
            this.ProbabilityWeighting = 1;
            if (parentAssetPack.TrackedBodyGenConfig != null)
            {
                this.AllowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(parentAssetPack.TrackedBodyGenConfig.DescriptorUI);
                this.DisallowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(parentAssetPack.TrackedBodyGenConfig.DescriptorUI);
            }
            AllowedBodySlideDescriptors = new VM_BodyShapeDescriptorSelectionMenu(OBodyDescriptorMenu);
            DisallowedBodySlideDescriptors = new VM_BodyShapeDescriptorSelectionMenu(OBodyDescriptorMenu);

            this.WeightRange = new NPCWeightRange();
            this.Subgroups = new ObservableCollection<VM_Subgroup>();

            //UI-related
            this.LinkCache = GameEnvironmentProvider.MyEnvironment.LinkCache;
            this.RacePickerFormKeys = typeof(IRaceGetter).AsEnumerable();
            this.RequiredSubgroupIDs = new HashSet<string>();
            this.ExcludedSubgroupIDs = new HashSet<string>();
            this.ParentCollection = parentCollection;
            this.ParentAssetPack = parentAssetPack;

            // must be set after Parent Asset Pack
            if (setExplicitReferenceNPC)
            {
                this.PathsMenu = new VM_FilePathReplacementMenu(this, setExplicitReferenceNPC, this.LinkCache);
            }
            else
            {
                this.PathsMenu = new VM_FilePathReplacementMenu(this, setExplicitReferenceNPC, parentAssetPack.RecordTemplateLinkCache);
                parentAssetPack.WhenAnyValue(x => x.RecordTemplateLinkCache).Subscribe(x => this.PathsMenu.ReferenceLinkCache = parentAssetPack.RecordTemplateLinkCache);
            }

            AddAllowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.AllowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.AllowedAttributes, true, parentAssetPack.AttributeGroupMenu.Groups))
                );

            AddDisallowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.DisallowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.DisallowedAttributes, false, parentAssetPack.AttributeGroupMenu.Groups))
                );

            AddNPCKeyword = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.AddKeywords.Add(new VM_CollectionMemberString("", this.AddKeywords))
                );

            AddPath = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.PathsMenu.Paths.Add(new VM_FilePathReplacement(this.PathsMenu))
                );

            AddSubgroup = new SynthEBD.RelayCommand(
               canExecute: _ => true,
               execute: _ => this.Subgroups.Add(new VM_Subgroup(RaceGroupingVMs, this.Subgroups, this.ParentAssetPack, OBodyDescriptorMenu, setExplicitReferenceNPC))
               );

            DeleteMe = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.ParentCollection.Remove(this)
                );

            DeleteRequiredSubgroup = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.RequiredSubgroups.Remove((VM_Subgroup)x)
                );

            DeleteExcludedSubgroup = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.ExcludedSubgroups.Remove((VM_Subgroup)x)
                );
        }

        public string ID { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public bool DistributionEnabled { get; set; }
        public ObservableCollection<FormKey> AllowedRaces { get; set; }
        public VM_RaceGroupingCheckboxList AllowedRaceGroupings { get; set; }
        public ObservableCollection<FormKey> DisallowedRaces { get; set; }
        public VM_RaceGroupingCheckboxList DisallowedRaceGroupings { get; set; }
        public ObservableCollection<VM_NPCAttribute> AllowedAttributes { get; set; }
        public ObservableCollection<VM_NPCAttribute> DisallowedAttributes { get; set; }
        public bool AllowUnique { get; set; }
        public bool AllowNonUnique { get; set; }
        public ObservableCollection<VM_Subgroup> RequiredSubgroups { get; set; }
        public ObservableCollection<VM_Subgroup> ExcludedSubgroups { get; set; }
        public ObservableCollection<VM_CollectionMemberString> AddKeywords { get; set; }
        public int ProbabilityWeighting { get; set; }
        public VM_FilePathReplacementMenu PathsMenu { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu AllowedBodyGenDescriptors { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu DisallowedBodyGenDescriptors { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu AllowedBodySlideDescriptors { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu DisallowedBodySlideDescriptors { get; set; }
        public NPCWeightRange WeightRange { get; set; }
        public ObservableCollection<VM_Subgroup> Subgroups { get; set; }

        //UI-related
        public ILinkCache LinkCache { get; set; }
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

        public static VM_Subgroup GetViewModelFromModel(AssetPack.Subgroup model, VM_Settings_General generalSettingsVM, ObservableCollection<VM_Subgroup> parentCollection, VM_AssetPack parentAssetPack, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu, bool setExplicitReferenceNPC)
        {
            var viewModel = new VM_Subgroup(generalSettingsVM.RaceGroupings, parentCollection, parentAssetPack, OBodyDescriptorMenu, setExplicitReferenceNPC);

            viewModel.ID = model.id;
            viewModel.Name = model.name;
            viewModel.Enabled = model.enabled;
            viewModel.DistributionEnabled = model.distributionEnabled;
            viewModel.AllowedRaces = new ObservableCollection<FormKey>(model.allowedRaces);
            viewModel.AllowedRaceGroupings = GetRaceGroupingsByLabel(model.allowedRaceGroupings, generalSettingsVM.RaceGroupings);
            viewModel.DisallowedRaces = new ObservableCollection<FormKey>(model.disallowedRaces);
            viewModel.DisallowedRaceGroupings = GetRaceGroupingsByLabel(model.disallowedRaceGroupings, generalSettingsVM.RaceGroupings);
            viewModel.AllowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.allowedAttributes, parentAssetPack.AttributeGroupMenu.Groups, true);
            viewModel.DisallowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.disallowedAttributes, parentAssetPack.AttributeGroupMenu.Groups, false);
            foreach (var x in viewModel.DisallowedAttributes) { x.DisplayForceIfOption = false; }
            viewModel.AllowUnique = model.bAllowUnique;
            viewModel.AllowNonUnique = model.bAllowNonUnique;
            viewModel.RequiredSubgroupIDs = model.requiredSubgroups;
            viewModel.RequiredSubgroups = new ObservableCollection<VM_Subgroup>();
            viewModel.ExcludedSubgroupIDs = model.excludedSubgroups;
            viewModel.ExcludedSubgroups = new ObservableCollection<VM_Subgroup>();
            viewModel.AddKeywords = new ObservableCollection<VM_CollectionMemberString>();
            getModelKeywords(model, viewModel);
            viewModel.ProbabilityWeighting = model.ProbabilityWeighting;
            viewModel.PathsMenu = VM_FilePathReplacementMenu.GetViewModelFromModels(model.paths, viewModel, setExplicitReferenceNPC);
            viewModel.WeightRange = model.weightRange;

            if (parentAssetPack.TrackedBodyGenConfig != null)
            {
                viewModel.AllowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.allowedBodyGenDescriptors, parentAssetPack.TrackedBodyGenConfig.DescriptorUI);
                viewModel.DisallowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.disallowedBodyGenDescriptors, parentAssetPack.TrackedBodyGenConfig.DescriptorUI);
            }

            foreach (var sg in model.subgroups)
            {
                viewModel.Subgroups.Add(GetViewModelFromModel(sg, generalSettingsVM, viewModel.Subgroups, parentAssetPack, OBodyDescriptorMenu, setExplicitReferenceNPC));
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
                viewmodel.AddKeywords.Add(new VM_CollectionMemberString(kw, viewmodel.AddKeywords));
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
                this.AllowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(this.ParentAssetPack.TrackedBodyGenConfig.DescriptorUI);
                this.DisallowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(this.ParentAssetPack.TrackedBodyGenConfig.DescriptorUI);
            }
        }

        public static AssetPack.Subgroup DumpViewModelToModel(VM_Subgroup viewModel)
        {
            var model = new AssetPack.Subgroup();

            model.id = viewModel.ID;
            model.name = viewModel.Name;
            model.enabled = viewModel.Enabled;
            model.distributionEnabled = viewModel.DistributionEnabled;
            model.allowedRaces = viewModel.AllowedRaces.ToHashSet();
            model.allowedRaceGroupings = viewModel.AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet();
            model.disallowedRaces = viewModel.DisallowedRaces.ToHashSet();
            model.disallowedRaceGroupings = viewModel.DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet();
            model.allowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.AllowedAttributes);
            model.disallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.DisallowedAttributes);
            model.bAllowUnique = viewModel.AllowUnique;
            model.bAllowNonUnique = viewModel.AllowNonUnique;
            model.requiredSubgroups = viewModel.RequiredSubgroups.Select(x => x.ID).ToHashSet();
            model.excludedSubgroups = viewModel.ExcludedSubgroups.Select(x => x.ID).ToHashSet();
            model.addKeywords = viewModel.AddKeywords.Select(x => x.Content).ToHashSet();
            model.ProbabilityWeighting = viewModel.ProbabilityWeighting;
            model.paths = VM_FilePathReplacementMenu.DumpViewModelToModels(viewModel.PathsMenu);
            model.weightRange = viewModel.WeightRange;

            model.allowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.AllowedBodyGenDescriptors);
            model.disallowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.DisallowedBodyGenDescriptors);

            foreach (var sg in viewModel.Subgroups)
            {
                model.subgroups.Add(DumpViewModelToModel(sg));
            }

            return model;
        }
    }
}
