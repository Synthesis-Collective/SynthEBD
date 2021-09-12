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
        }

        public string groupName { get; set; }
        public Gender gender { get; set; }
        public bool displayAlerts { get; set; }
        public string userAlert { get; set; }
        public ObservableCollection<VM_Subgroup> subgroups { get; set; }

        public string FilePath { get; set; }

        public Dictionary<Gender, string> GenderEnumDict { get; } = new Dictionary<Gender, string>()
        {
            {Gender.male, "Male"},
            {Gender.female, "Female"},
        };

        public static ObservableCollection<VM_AssetPack> GetViewModelsFromModels(List<AssetPack> assetPacks, List<string> paths)
        {
            ObservableCollection<VM_AssetPack> viewModels = new ObservableCollection<VM_AssetPack>();

            for (int i = 0; i < assetPacks.Count; i++)
            {
                var viewModel = GetViewModelFromModel(assetPacks[i]);
                viewModel.FilePath = paths[i];
                viewModels.Add(viewModel);
            }
            return viewModels;
        }
        public static VM_AssetPack GetViewModelFromModel(AssetPack model)
        {
            var viewModel = new VM_AssetPack();
            viewModel.groupName = model.groupName;
            viewModel.gender = model.gender;
            viewModel.displayAlerts = model.displayAlerts;
            viewModel.userAlert = model.userAlert;

            foreach (var sg in model.subgroups)
            {
                viewModel.subgroups.Add(VM_Subgroup.GetViewModelFromModel(sg));
            }
            return viewModel;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
    public class VM_Subgroup : INotifyPropertyChanged
    {
        public VM_Subgroup()
        {
            this.id = "";
            this.name = "";
            this.enabled = true;
            this.distributionEnabled = true;
            this.allowedRaces = new ObservableCollection<FormKey>();
            this.allowedRaceGroupings = new ObservableCollection<RaceGrouping>();
            this.disallowedRaces = new ObservableCollection<FormKey>();
            this.disallowedRaceGroupings = new ObservableCollection<RaceGrouping>();
            this.allowedAttributes = new ObservableCollection<string[]>();
            this.disallowedAttributes = new ObservableCollection<string[]>();
            this.forceIfAttributes = new ObservableCollection<string[]>();
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

            this.lk = new GameEnvironmentProvider().MyEnvironment.LinkCache;
            this.RacePickerFormKeys = typeof(IRaceGetter).AsEnumerable();
        }

        public string id { get; set; }
        public string name { get; set; }
        public bool enabled { get; set; }
        public bool distributionEnabled { get; set; }
        public ObservableCollection<FormKey> allowedRaces { get; set; }
        public ObservableCollection<RaceGrouping> allowedRaceGroupings { get; set; }
        public ObservableCollection<FormKey> disallowedRaces { get; set; }
        public ObservableCollection<RaceGrouping> disallowedRaceGroupings { get; set; }
        public ObservableCollection<string[]> allowedAttributes { get; set; } // keeping as array to allow deserialization of original zEBD settings files
        public ObservableCollection<string[]> disallowedAttributes { get; set; }
        public ObservableCollection<string[]> forceIfAttributes { get; set; }
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
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> RacePickerFormKeys { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_Subgroup GetViewModelFromModel(AssetPack.Subgroup model)
        {
            var viewModel = new VM_Subgroup();

            viewModel.id = model.id;
            viewModel.name = model.name;
            viewModel.enabled = model.enabled;
            viewModel.distributionEnabled = model.distributionEnabled;
            viewModel.allowedRaces = new ObservableCollection<FormKey>(model.allowedRaces);
            viewModel.allowedRaceGroupings = new ObservableCollection<RaceGrouping>(model.allowedRaceGroupings);
            viewModel.disallowedRaces = new ObservableCollection<FormKey>(model.disallowedRaces);
            viewModel.disallowedRaceGroupings = new ObservableCollection<RaceGrouping>(model.disallowedRaceGroupings);
            viewModel.allowedAttributes = new ObservableCollection<string[]>(model.allowedAttributes);
            viewModel.disallowedAttributes = new ObservableCollection<string[]>(model.disallowedAttributes);
            viewModel.forceIfAttributes = new ObservableCollection<string[]>(model.forceIfAttributes);
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
                viewModel.subgroups.Add(GetViewModelFromModel(sg));
            }

            return viewModel;
        }
    }
}
