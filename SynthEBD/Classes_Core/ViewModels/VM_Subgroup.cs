using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    public class VM_Subgroup : INotifyPropertyChanged, ICloneable
    {
        public VM_Subgroup(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, ObservableCollection<VM_Subgroup> parentCollection, VM_AssetPack parentAssetPack, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu, bool setExplicitReferenceNPC)
        {
            SubscribedRaceGroupings = raceGroupingVMs;
            ParentAssetPack = parentAssetPack;

            this.ID = "";
            this.Name = "New";
            this.Enabled = true;
            this.DistributionEnabled = true;
            this.AllowedRaces = new ObservableCollection<FormKey>();
            this.AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(SubscribedRaceGroupings);
            this.DisallowedRaces = new ObservableCollection<FormKey>();
            this.DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(SubscribedRaceGroupings);
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
                this.AllowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(parentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, parentAssetPack);
                this.DisallowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(parentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, parentAssetPack);
            }
            AllowedBodySlideDescriptors = new VM_BodyShapeDescriptorSelectionMenu(OBodyDescriptorMenu, SubscribedRaceGroupings, parentAssetPack);
            DisallowedBodySlideDescriptors = new VM_BodyShapeDescriptorSelectionMenu(OBodyDescriptorMenu, SubscribedRaceGroupings, parentAssetPack);

            this.WeightRange = new NPCWeightRange();
            this.Subgroups = new ObservableCollection<VM_Subgroup>();

            //UI-related
            this.LinkCache = PatcherEnvironmentProvider.Environment.LinkCache;
            this.RacePickerFormKeys = typeof(IRaceGetter).AsEnumerable();
            this.RequiredSubgroupIDs = new HashSet<string>();
            this.ExcludedSubgroupIDs = new HashSet<string>();
            this.ParentCollection = parentCollection;

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
                execute: _ => this.AllowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.AllowedAttributes, true, null, parentAssetPack.AttributeGroupMenu.Groups))
                );

            AddDisallowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.DisallowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.DisallowedAttributes, false, null, parentAssetPack.AttributeGroupMenu.Groups))
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
               execute: _ => this.Subgroups.Add(new VM_Subgroup(raceGroupingVMs, this.Subgroups, this.ParentAssetPack, OBodyDescriptorMenu, setExplicitReferenceNPC))
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
        public double ProbabilityWeighting { get; set; }
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
        public ObservableCollection<VM_RaceGrouping> SubscribedRaceGroupings { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_Subgroup GetViewModelFromModel(AssetPack.Subgroup model, VM_Settings_General generalSettingsVM, ObservableCollection<VM_Subgroup> parentCollection, VM_AssetPack parentAssetPack, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu, bool setExplicitReferenceNPC)
        {
            var viewModel = new VM_Subgroup(generalSettingsVM.RaceGroupings, parentCollection, parentAssetPack, OBodyDescriptorMenu, setExplicitReferenceNPC);

            viewModel.ID = model.ID;
            viewModel.Name = model.Name;
            viewModel.Enabled = model.Enabled;
            viewModel.DistributionEnabled = model.DistributionEnabled;
            viewModel.AllowedRaces = new ObservableCollection<FormKey>(model.AllowedRaces);
            viewModel.AllowedRaceGroupings = VM_RaceGroupingCheckboxList.GetRaceGroupingsByLabel(model.AllowedRaceGroupings, generalSettingsVM.RaceGroupings);
            viewModel.DisallowedRaces = new ObservableCollection<FormKey>(model.DisallowedRaces);
            viewModel.DisallowedRaceGroupings = VM_RaceGroupingCheckboxList.GetRaceGroupingsByLabel(model.DisallowedRaceGroupings, generalSettingsVM.RaceGroupings);
            viewModel.AllowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.AllowedAttributes, parentAssetPack.AttributeGroupMenu.Groups, true, null);
            viewModel.DisallowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.DisallowedAttributes, parentAssetPack.AttributeGroupMenu.Groups, false, null);
            foreach (var x in viewModel.DisallowedAttributes) { x.DisplayForceIfOption = false; }
            viewModel.AllowUnique = model.AllowUnique;
            viewModel.AllowNonUnique = model.AllowNonUnique;
            viewModel.RequiredSubgroupIDs = model.RequiredSubgroups;
            viewModel.RequiredSubgroups = new ObservableCollection<VM_Subgroup>();
            viewModel.ExcludedSubgroupIDs = model.ExcludedSubgroups;
            viewModel.ExcludedSubgroups = new ObservableCollection<VM_Subgroup>();
            viewModel.AddKeywords = VM_CollectionMemberString.InitializeCollectionFromHashSet(model.AddKeywords);
            viewModel.ProbabilityWeighting = model.ProbabilityWeighting;
            viewModel.PathsMenu = VM_FilePathReplacementMenu.GetViewModelFromModels(model.Paths, viewModel, setExplicitReferenceNPC);
            viewModel.WeightRange = model.WeightRange;

            if (parentAssetPack.TrackedBodyGenConfig != null)
            {
                viewModel.AllowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.AllowedBodyGenDescriptors, parentAssetPack.TrackedBodyGenConfig.DescriptorUI, viewModel.SubscribedRaceGroupings, parentAssetPack);
                viewModel.DisallowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.DisallowedBodyGenDescriptors, parentAssetPack.TrackedBodyGenConfig.DescriptorUI, viewModel.SubscribedRaceGroupings, parentAssetPack);
            }

            foreach (var sg in model.Subgroups)
            {
                viewModel.Subgroups.Add(GetViewModelFromModel(sg, generalSettingsVM, viewModel.Subgroups, parentAssetPack, OBodyDescriptorMenu, setExplicitReferenceNPC));
            }

            return viewModel;
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
                this.AllowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(this.ParentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack);
                this.DisallowedBodyGenDescriptors = new VM_BodyShapeDescriptorSelectionMenu(this.ParentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack);
            }
        }

        public static AssetPack.Subgroup DumpViewModelToModel(VM_Subgroup viewModel)
        {
            var model = new AssetPack.Subgroup();

            model.ID = viewModel.ID;
            model.Name = viewModel.Name;
            model.Enabled = viewModel.Enabled;
            model.DistributionEnabled = viewModel.DistributionEnabled;
            model.AllowedRaces = viewModel.AllowedRaces.ToHashSet();
            model.AllowedRaceGroupings = viewModel.AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet();
            model.DisallowedRaces = viewModel.DisallowedRaces.ToHashSet();
            model.DisallowedRaceGroupings = viewModel.DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.Label).ToHashSet();
            model.AllowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.AllowedAttributes);
            model.DisallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.DisallowedAttributes);
            model.AllowUnique = viewModel.AllowUnique;
            model.AllowNonUnique = viewModel.AllowNonUnique;
            model.RequiredSubgroups = viewModel.RequiredSubgroups.Select(x => x.ID).ToHashSet();
            model.ExcludedSubgroups = viewModel.ExcludedSubgroups.Select(x => x.ID).ToHashSet();
            model.AddKeywords = viewModel.AddKeywords.Select(x => x.Content).ToHashSet();
            model.ProbabilityWeighting = viewModel.ProbabilityWeighting;
            model.Paths = VM_FilePathReplacementMenu.DumpViewModelToModels(viewModel.PathsMenu);
            model.WeightRange = viewModel.WeightRange;

            model.AllowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.AllowedBodyGenDescriptors);
            model.DisallowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.DumpToHashSet(viewModel.DisallowedBodyGenDescriptors);

            foreach (var sg in viewModel.Subgroups)
            {
                model.Subgroups.Add(DumpViewModelToModel(sg));
            }

            return model;
        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}
