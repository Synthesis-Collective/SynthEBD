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
    class VM_HeightConfig
    {
        public VM_HeightConfig(ObservableCollection<VM_HeightConfig> parentCollection, ILinkCache linkCache)
        {
            this.Label = "";
            this.Races = new ObservableCollection<FormKey>();
            this.DistributionMode = DistMode.uniform;
            this.MaleHeightBase = "1.000000";
            this.MaleHeightRange = "0.020000";
            this.FemaleHeightBase = "1.000000";
            this.FemaleHeightRange = "0.020000";

            this.FormKeyPickerTypes = typeof(IRaceGetter).AsEnumerable();
            this.lk = linkCache;
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

        public static ObservableCollection<VM_HeightConfig> GetViewModelsFromModels(HashSet<HeightConfig> models, ILinkCache lk)
        {
            ObservableCollection<VM_HeightConfig> viewModels = new ObservableCollection<VM_HeightConfig>();
            foreach (var model in models)
            {
                var vm = new VM_HeightConfig(viewModels, lk);
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

        public static HashSet<HeightConfig> DumpViewModelsToModels(HashSet<HeightConfig> models, ObservableCollection<VM_HeightConfig> viewModels)
        {
            models.Clear();
            foreach (var vm in viewModels)
            {
                HeightConfig hc = new HeightConfig();
                hc.Label = vm.Label;
                hc.Races = vm.Races.ToHashSet();
                hc.HeightMale = vm.MaleHeightBase;
                hc.HeightMaleRange = vm.MaleHeightRange;
                hc.HeightFemale = vm.FemaleHeightBase;
                hc.HeightFemaleRange = vm.FemaleHeightRange;

                models.Add(hc);
            }

            return models;
        }
    }
}
