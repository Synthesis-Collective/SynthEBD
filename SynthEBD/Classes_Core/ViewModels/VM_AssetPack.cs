using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_AssetPack : INotifyPropertyChanged
    {
        public VM_AssetPack()
        {
            this.groupName = "";
            this.gender = Gender.male;
            this.displayAlerts = true;
            this.userAlert = "";
            this.subgroups = new ObservableCollection<VM_Subgroup>();
            this.FilePath = "";

            this.RaceGroupingList = new ObservableCollection<VM_RaceGrouping>();
        }

        public string groupName { get; set; }
        public Gender gender { get; set; }
        public bool displayAlerts { get; set; }
        public string userAlert { get; set; }
        public ObservableCollection<VM_Subgroup> subgroups { get; set; }

        public string FilePath { get; set; }
        public ObservableCollection<VM_RaceGrouping> RaceGroupingList { get; set; }

        public Dictionary<Gender, string> GenderEnumDict { get; } = new Dictionary<Gender, string>()
        {
            {Gender.male, "Male"},
            {Gender.female, "Female"},
        };

        public static ObservableCollection<VM_AssetPack> GetViewModelsFromModels(List<AssetPack> assetPacks, List<string> paths, VM_Settings_General generalSettingsVM)
        {
            ObservableCollection<VM_AssetPack> viewModels = new ObservableCollection<VM_AssetPack>();

            for (int i = 0; i < assetPacks.Count; i++)
            {
                var viewModel = GetViewModelFromModel(assetPacks[i], generalSettingsVM);
                viewModel.FilePath = paths[i];
                viewModels.Add(viewModel);
            }
            return viewModels;
        }
        public static VM_AssetPack GetViewModelFromModel(AssetPack model, VM_Settings_General generalSettingsVM)
        {
            var viewModel = new VM_AssetPack();
            viewModel.groupName = model.groupName;
            viewModel.gender = model.gender;
            viewModel.displayAlerts = model.displayAlerts;
            viewModel.userAlert = model.userAlert;

            viewModel.RaceGroupingList = new ObservableCollection<VM_RaceGrouping>(generalSettingsVM.RaceGroupings);

            foreach (var sg in model.subgroups)
            {
                viewModel.subgroups.Add(VM_Subgroup.GetViewModelFromModel(sg, generalSettingsVM));
            }
            return viewModel;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
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
            this.requiredSubgroups = new ObservableCollection<string>();
            this.excludedSubgroups = new ObservableCollection<string>();
            this.addKeywords = new ObservableCollection<string>();
            this.probabilityWeighting = 1;
            this.paths = new ObservableCollection<string[]>();
            this.allowedBodyGenDescriptors = new ObservableCollection<string>();
            this.disallowedBodyGenDescriptors = new ObservableCollection<string>();
            this.weightRange = new int[2];
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
        public ObservableCollection<string> requiredSubgroups { get; set; }
        public ObservableCollection<string> excludedSubgroups { get; set; }
        public ObservableCollection<string> addKeywords { get; set; }
        public int probabilityWeighting { get; set; }
        public ObservableCollection<string[]> paths { get; set; }
        public ObservableCollection<string> allowedBodyGenDescriptors { get; set; }
        public ObservableCollection<string> disallowedBodyGenDescriptors { get; set; }
        public int[] weightRange { get; set; }
        public ObservableCollection<VM_Subgroup> subgroups { get; set; }
        
        //UI-related
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> RacePickerFormKeys { get; set; }

        public RelayCommand AddAllowedAttribute { get; }
        public RelayCommand AddDisallowedAttribute { get; }
        public RelayCommand AddForceIfAttribute { get; }




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
            viewModel.requiredSubgroups = new ObservableCollection<string>(model.requiredSubgroups);
            viewModel.excludedSubgroups = new ObservableCollection<string>(model.excludedSubgroups);
            viewModel.addKeywords = new ObservableCollection<string>(model.addKeywords);
            viewModel.probabilityWeighting = model.probabilityWeighting;
            viewModel.paths = new ObservableCollection<string[]>(model.paths);
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
