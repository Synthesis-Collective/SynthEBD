using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_HeightConfig
    {
        public VM_HeightConfig()
        {
            this.Label = "";
            this.HeightAssignments = new ObservableCollection<VM_HeightAssignment>();
            this.SubscribedHeightConfig = new HeightConfig();
            this.SourcePath = "";
        }

        public string Label { get; set; }
        public ObservableCollection<VM_HeightAssignment> HeightAssignments { get; set; }

        public HeightConfig SubscribedHeightConfig { get; set; }
        public string SourcePath { get; set; }
        public RelayCommand AddHeightAssignment { get; }

        public static void GetViewModelsFromModels(ObservableCollection<VM_HeightConfig> viewModels, List<HeightConfig> models, List<string> loadedPaths)
        {
            for (int i = 0; i < models.Count; i++)
            {
                var vm = new VM_HeightConfig();
                vm.Label = models[i].Label;
                vm.HeightAssignments = VM_HeightAssignment.GetViewModelsFromModels(models[i].HeightAssignments);
                vm.SubscribedHeightConfig = models[i];
                vm.SourcePath = loadedPaths[i];

                viewModels.Add(vm);
            }
        }

        public static List<string> DumpViewModelsToModels(ObservableCollection<VM_HeightConfig> viewModels, List<HeightConfig> models)
        {
            List<string> filePaths = new List<string>();
            models.Clear();
            foreach (var vm in viewModels)
            {
                var m = new HeightConfig();
                m.Label = vm.Label;
                VM_HeightAssignment.DumpViewModelsToModels(m.HeightAssignments, vm.HeightAssignments);
                models.Add(m);
                filePaths.Add(vm.SourcePath);
            }
            return filePaths;
        }
    }
    public class VM_HeightAssignment
    {
        public VM_HeightAssignment(ObservableCollection<VM_HeightAssignment> parentCollection)
        {
            this.Label = "";
            this.Races = new ObservableCollection<FormKey>();
            this.DistributionMode = DistMode.uniform;
            this.MaleHeightBase = "1.000000";
            this.MaleHeightRange = "0.020000";
            this.FemaleHeightBase = "1.000000";
            this.FemaleHeightRange = "0.020000";

            this.FormKeyPickerTypes = typeof(IRaceGetter).AsEnumerable();
            this.lk = GameEnvironmentProvider.Instance.MyEnvironment.LinkCache;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
        }

        public string Label { get; set; }
        public ObservableCollection<FormKey> Races { get; set; }
        public DistMode DistributionMode { get; set; }
        public string MaleHeightBase { get; set; }
        public string MaleHeightRange { get; set; }
        public string FemaleHeightBase { get; set; }
        public string FemaleHeightRange { get; set; }

        public IEnumerable<Type> FormKeyPickerTypes { get; set; }
        public ILinkCache lk { get; set; }
        public RelayCommand DeleteCommand { get; }

        public List<DistMode> DistModes
        {
            get
            {
                List < DistMode > list = new List<DistMode>();
                list.Add(DistMode.uniform);
                list.Add(DistMode.bellCurve);
                return list;
            }
        }

        public static ObservableCollection<VM_HeightAssignment> GetViewModelsFromModels(HashSet<HeightAssignment> models)
        {
            ObservableCollection<VM_HeightAssignment> viewModels = new ObservableCollection<VM_HeightAssignment>();
            foreach (var model in models)
            {
                var vm = new VM_HeightAssignment(viewModels);
                vm.Label = model.Label;
                vm.Races = new ObservableCollection<FormKey>(model.Races);
                vm.MaleHeightBase = model.HeightMale;
                vm.MaleHeightRange = model.HeightMaleRange;
                vm.FemaleHeightBase = model.HeightFemale;
                vm.FemaleHeightRange = model.HeightFemaleRange;

                viewModels.Add(vm);
            }

            return viewModels;
        }

        public static HashSet<HeightAssignment> DumpViewModelsToModels(HashSet<HeightAssignment> models, ObservableCollection<VM_HeightAssignment> viewModels)
        {
            foreach (var vm in viewModels)
            {
                HeightAssignment ha = new HeightAssignment();
                ha.Label = vm.Label;
                ha.Races = vm.Races.ToHashSet();
                ha.HeightMale = vm.MaleHeightBase;
                ha.HeightMaleRange = vm.MaleHeightRange;
                ha.HeightFemale = vm.FemaleHeightBase;
                ha.HeightFemaleRange = vm.FemaleHeightRange;

                models.Add(ha);
            }

            return models;
        }
    }
}
