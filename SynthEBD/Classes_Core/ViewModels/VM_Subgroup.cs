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

namespace SynthEBD
{
    public class VM_Subgroup : INotifyPropertyChanged
    {
        public VM_Subgroup(ObservableCollection<VM_RaceGrouping> RaceGroupingVMs)
        {
            this.id = "";
            this.name = "";
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
            this.excludedSubgroups = new ObservableCollection<string>();
            this.addKeywords = new ObservableCollection<string>();
            this.probabilityWeighting = 1;
            this.paths = new ObservableCollection<VM_FilePathReplacement>();
            this.allowedBodyGenDescriptors = new ObservableCollection<string>();
            this.disallowedBodyGenDescriptors = new ObservableCollection<string>();
            this.weightRange = new NPCWeightRange();
            this.subgroups = new ObservableCollection<VM_Subgroup>();

            //UI-related
            this.lk = new GameEnvironmentProvider().MyEnvironment.LinkCache;
            this.RacePickerFormKeys = typeof(IRaceGetter).AsEnumerable();

            AddAllowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.allowedAttributes.Add(new VM_NPCAttribute(this.allowedAttributes))
                );

            AddDisallowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.disallowedAttributes.Add(new VM_NPCAttribute(this.disallowedAttributes))
                );

            AddForceIfAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.forceIfAttributes.Add(new VM_NPCAttribute(this.forceIfAttributes))
                );

            AddPath = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.paths.Add(new VM_FilePathReplacement(this.paths))
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
        public ObservableCollection<string> excludedSubgroups { get; set; }
        public ObservableCollection<string> addKeywords { get; set; }
        public int probabilityWeighting { get; set; }
        public ObservableCollection<VM_FilePathReplacement> paths { get; set; }
        public ObservableCollection<string> allowedBodyGenDescriptors { get; set; }
        public ObservableCollection<string> disallowedBodyGenDescriptors { get; set; }
        public NPCWeightRange weightRange { get; set; }
        public ObservableCollection<VM_Subgroup> subgroups { get; set; }

        //UI-related
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> RacePickerFormKeys { get; set; }

        public RelayCommand AddAllowedAttribute { get; }
        public RelayCommand AddDisallowedAttribute { get; }
        public RelayCommand AddForceIfAttribute { get; }

        public RelayCommand AddPath { get; }


        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_Subgroup GetViewModelFromModel(AssetPack.Subgroup model, VM_Settings_General generalSettingsVM)
        {
            var viewModel = new VM_Subgroup(generalSettingsVM.RaceGroupings);

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
            //viewModel.requiredSubgroups = new ObservableCollection<string>(model.requiredSubgroups);
            viewModel.requiredSubgroups = new ObservableCollection<VM_Subgroup>();

            viewModel.excludedSubgroups = new ObservableCollection<string>(model.excludedSubgroups);
            viewModel.addKeywords = new ObservableCollection<string>(model.addKeywords);
            viewModel.probabilityWeighting = model.probabilityWeighting;
            viewModel.paths = VM_FilePathReplacement.GetViewModelsFromModels(model.paths);
            viewModel.allowedBodyGenDescriptors = new ObservableCollection<string>(model.allowedBodyGenDescriptors);
            viewModel.disallowedBodyGenDescriptors = new ObservableCollection<string>(model.disallowedBodyGenDescriptors);
            viewModel.weightRange = model.weightRange;

            foreach (var sg in model.subgroups)
            {
                viewModel.subgroups.Add(GetViewModelFromModel(sg, generalSettingsVM));
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
    }
}
