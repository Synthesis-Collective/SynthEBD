using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class VM_BodyGenTemplateMenu : INotifyPropertyChanged
    {
        public VM_BodyGenTemplateMenu(VM_BodyGenConfig parentConfig, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
        {
            this.Templates = new ObservableCollection<VM_BodyGenTemplate>();
            this.CurrentlyDisplayedTemplate = new VM_BodyGenTemplate(parentConfig.GroupUI.TemplateGroups, parentConfig.DescriptorUI, raceGroupingVMs, this.Templates);
            AddTemplate = new SynthEBD.RelayCommand(
    canExecute: _ => true,
    execute: _ => this.Templates.Add(new VM_BodyGenTemplate(parentConfig.GroupUI.TemplateGroups, parentConfig.DescriptorUI, raceGroupingVMs, this.Templates))
    );

            RemoveTemplate = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.Templates.Remove(CurrentlyDisplayedTemplate)
                );
        }
        public ObservableCollection<VM_BodyGenTemplate> Templates { get; set; }

        public VM_BodyGenTemplate CurrentlyDisplayedTemplate { get; set; }

        public RelayCommand AddTemplate { get; }
        public RelayCommand RemoveTemplate { get; }

        public event PropertyChangedEventHandler PropertyChanged;
    }


    public class VM_BodyGenTemplate : INotifyPropertyChanged
    {
        public VM_BodyGenTemplate(ObservableCollection<VM_CollectionMemberString> templateGroups, VM_BodyGenMorphDescriptorMenu morphDescriptors, ObservableCollection<VM_RaceGrouping> raceGroupingVMs, ObservableCollection<VM_BodyGenTemplate> parentCollection)
        {
            this.Label = "";
            this.Notes = "";
            this.Specs = "";
            this.GroupSelectionCheckList = new VM_CollectionMemberStringCheckboxList(templateGroups);
            this.DescriptorsSelectionMenu = new VM_BodyGenMorphDescriptorSelectionMenu(morphDescriptors);
            this.AllowedRaces = new ObservableCollection<FormKey>();
            this.AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
            this.DisallowedRaces = new ObservableCollection<FormKey>();
            this.DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
            this.AllowedAttributes = new ObservableCollection<VM_NPCAttribute>();
            this.DisallowedAttributes = new ObservableCollection<VM_NPCAttribute>();
            this.ForceIfAttributes = new ObservableCollection<VM_NPCAttribute>();
            this.bAllowUnique = true;
            this.bAllowNonUnique = true;
            this.bAllowRandom = true;
            this.ProbabilityWeighting = 1;
            this.RequiredTemplates = new ObservableCollection<VM_CollectionMemberString>();
            this.WeightRange = new NPCWeightRange();

            this.Caption_MemberOfTemplateGroups = "";
            this.Caption_MorphDescriptors = "";

            this.lk = new GameEnvironmentProvider().MyEnvironment.LinkCache;
            this.RacePickerFormKeys = typeof(IRaceGetter).AsEnumerable();

            this.ParentCollection = parentCollection;
            this.OtherGroupsTemplateCollection = new ObservableCollection<VM_BodyGenTemplate>();
            parentCollection.CollectionChanged += UpdateOtherGroupsTemplateCollection;
            templateGroups.CollectionChanged += UpdateOtherGroupsTemplateCollection;
            this.GroupSelectionCheckList.PropertyChanged += UpdateOtherGroupsTemplateCollectionP;

            AddAllowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.AllowedAttributes.Add(new VM_NPCAttribute(this.AllowedAttributes))
                );

            AddDisallowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.DisallowedAttributes.Add(new VM_NPCAttribute(this.DisallowedAttributes))
                );

            AddForceIfAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.ForceIfAttributes.Add(new VM_NPCAttribute(this.ForceIfAttributes))
                );

            AddRequiredTemplate = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.RequiredTemplates.Add(new VM_CollectionMemberString("", this.RequiredTemplates))
                );

            DeleteMe = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.ParentCollection.Remove(this)
                );
        }

        public string Label { get; set; }
        public string Notes { get; set; }
        public string Specs { get; set; } // will need special logic during I/O because in zEBD settings this is called "params" which is reserved in C#
        public VM_CollectionMemberStringCheckboxList GroupSelectionCheckList { get; set; }
        public VM_BodyGenMorphDescriptorSelectionMenu DescriptorsSelectionMenu { get; set; }
        public ObservableCollection<FormKey> AllowedRaces { get; set; }
        public ObservableCollection<FormKey> DisallowedRaces { get; set; }
        public VM_RaceGroupingCheckboxList AllowedRaceGroupings { get; set; }
        public VM_RaceGroupingCheckboxList DisallowedRaceGroupings { get; set; }
        public ObservableCollection<VM_NPCAttribute> AllowedAttributes { get; set; } // keeping as array to allow deserialization of original zEBD settings files
        public ObservableCollection<VM_NPCAttribute> DisallowedAttributes { get; set; }
        public ObservableCollection<VM_NPCAttribute> ForceIfAttributes { get; set; }
        public bool bAllowUnique { get; set; }
        public bool bAllowNonUnique { get; set; }
        public bool bAllowRandom { get; set; }
        public int ProbabilityWeighting { get; set; }
        public ObservableCollection<VM_CollectionMemberString> RequiredTemplates { get; set; }
        public NPCWeightRange WeightRange { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public string Caption_MemberOfTemplateGroups { get; set; }
        public string Caption_MorphDescriptors { get; set; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> RacePickerFormKeys { get; set; }

        public RelayCommand AddAllowedAttribute { get; }
        public RelayCommand AddDisallowedAttribute { get; }
        public RelayCommand AddForceIfAttribute { get; }
        public RelayCommand AddRequiredTemplate { get; }
        public RelayCommand DeleteMe { get; }

        public ObservableCollection<VM_BodyGenTemplate> ParentCollection {get; set;}
        public ObservableCollection<VM_BodyGenTemplate> OtherGroupsTemplateCollection { get; set; }

        public static void GetViewModelFromModel(BodyGenConfig.BodyGenTemplate model, VM_BodyGenTemplate viewModel, VM_BodyGenMorphDescriptorMenu descriptorMenu, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
        {
            viewModel.Label = model.Label;
            viewModel.Notes = model.Notes;
            viewModel.Specs = model.Specs;
            viewModel.GroupSelectionCheckList.InitializeFromHashSet(model.MemberOfTemplateGroups);
            viewModel.DescriptorsSelectionMenu = VM_BodyGenMorphDescriptorSelectionMenu.InitializeFromTemplate(model, descriptorMenu);
            viewModel.AllowedRaces = new ObservableCollection<FormKey>(model.AllowedRaces);
            viewModel.AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
            foreach (var grouping in viewModel.AllowedRaceGroupings.RaceGroupingSelections)
            {
                if (model.AllowedRaceGroupings.Contains(grouping.Label))
                {
                    grouping.IsSelected = true;
                }
                else { grouping.IsSelected = false; }
            }

            viewModel.DisallowedRaces = new ObservableCollection<FormKey>(model.DisallowedRaces);
            viewModel.DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
            
            foreach (var grouping in viewModel.DisallowedRaceGroupings.RaceGroupingSelections)
            {
                if (model.DisallowedRaceGroupings.Contains(grouping.Label))
                {
                    grouping.IsSelected = true;
                }
                else { grouping.IsSelected = false; }
            }

            viewModel.AllowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.AllowedAttributes);
            viewModel.DisallowedAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.DisallowedAttributes);
            viewModel.ForceIfAttributes = VM_NPCAttribute.GetViewModelsFromModels(model.ForceIfAttributes);
            viewModel.bAllowUnique = model.bAllowUnique;
            viewModel.bAllowNonUnique = model.bAllowNonUnique;
            viewModel.bAllowRandom = model.bAllowRandom;
            viewModel.ProbabilityWeighting = model.ProbabilityWeighting;
            viewModel.RequiredTemplates = VM_CollectionMemberString.InitializeCollectionFromHashSet(model.RequiredTemplates);
            viewModel.WeightRange = model.WeightRange;
        }

        public void UpdateOtherGroupsTemplateCollection(object sender, NotifyCollectionChangedEventArgs e)
        {
            var excludedCollection = this.UpdateThisOtherGroupsTemplateCollection();
            foreach(var template in excludedCollection)
            {
                template.UpdateThisOtherGroupsTemplateCollection();
            }
        }

        public void UpdateOtherGroupsTemplateCollectionP(object sender, PropertyChangedEventArgs e)
        {
            var excludedCollection = this.UpdateThisOtherGroupsTemplateCollection();
            foreach (var template in excludedCollection)
            {
                template.UpdateThisOtherGroupsTemplateCollection();
            }
        }

        public ObservableCollection<VM_BodyGenTemplate> UpdateThisOtherGroupsTemplateCollection()
        {
            var updatedCollection = new ObservableCollection<VM_BodyGenTemplate>();
            var excludedCollection = new ObservableCollection<VM_BodyGenTemplate>();

            foreach (var template in this.ParentCollection)
            {
                bool inGroup = false;
                foreach (var group in template.GroupSelectionCheckList.CollectionMemberStrings)
                {
                    if (group.IsSelected == false) { continue; }

                    foreach (var thisGroup in this.GroupSelectionCheckList.CollectionMemberStrings)
                    {
                        if (thisGroup.IsSelected == false) { continue; }
                        
                        if (group.SubscribedString == thisGroup.SubscribedString)
                        {
                            inGroup = true;
                            break;
                        }
                    }
                    if (inGroup == true) { break; }
                }

                if (inGroup == false)
                {
                    updatedCollection.Add(template);
                }
                else
                {
                    excludedCollection.Add(template);
                }    
            }

            this.OtherGroupsTemplateCollection = updatedCollection;

            return excludedCollection;
        }
    }
}
