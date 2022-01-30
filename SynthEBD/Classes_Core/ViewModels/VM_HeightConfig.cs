using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
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
    public class VM_HeightConfig : INotifyPropertyChanged
    {
        public VM_HeightConfig()
        {
            this.Label = "New Height Configuration";
            this.HeightAssignments = new ObservableCollection<VM_HeightAssignment>();
            this.SubscribedHeightConfig = new HeightConfig();
            this.SourcePath = "";
            this.GlobalDistMode = DistMode.uniform;

            AddHeightAssignment = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.HeightAssignments.Add(new VM_HeightAssignment(this.HeightAssignments))
                );

            SetAllDistModes = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    foreach (var assignment in this.HeightAssignments)
                    {
                        assignment.DistributionMode = this.GlobalDistMode;
                    }
                }
                );
        }

        public string Label { get; set; }
        public ObservableCollection<VM_HeightAssignment> HeightAssignments { get; set; }
        public DistMode GlobalDistMode { get; set; }
        public HeightConfig SubscribedHeightConfig { get; set; }
        public string SourcePath { get; set; }
        public RelayCommand AddHeightAssignment { get; }
        public RelayCommand SetAllDistModes { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static void GetViewModelsFromModels(ObservableCollection<VM_HeightConfig> viewModels, List<HeightConfig> models)
        {
            for (int i = 0; i < models.Count; i++)
            {
                var vm = new VM_HeightConfig();
                vm.Label = models[i].Label;
                vm.HeightAssignments = VM_HeightAssignment.GetViewModelsFromModels(models[i].HeightAssignments);
                vm.SubscribedHeightConfig = models[i];
                vm.SourcePath = models[i].FilePath;

                viewModels.Add(vm);
            }
        }

        public static void DumpViewModelsToModels(ObservableCollection<VM_HeightConfig> viewModels, List<HeightConfig> models)
        {
            models.Clear();
            foreach (var vm in viewModels)
            {
                var m = new HeightConfig();
                m.Label = vm.Label;
                VM_HeightAssignment.DumpViewModelsToModels(m.HeightAssignments, vm.HeightAssignments);
                m.FilePath = vm.SourcePath;
                models.Add(m);
            }
        }
    }
    public class VM_HeightAssignment : INotifyPropertyChanged
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
            this.lk = PatcherEnvironmentProvider.Environment.LinkCache;
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

        public event PropertyChangedEventHandler PropertyChanged;

        public static ObservableCollection<VM_HeightAssignment> GetViewModelsFromModels(HashSet<HeightAssignment> models)
        {
            ObservableCollection<VM_HeightAssignment> viewModels = new ObservableCollection<VM_HeightAssignment>();
            foreach (var model in models)
            {
                var vm = new VM_HeightAssignment(viewModels);
                vm.Label = model.Label;
                vm.Races = new ObservableCollection<FormKey>(model.Races);
                vm.MaleHeightBase = model.HeightMale.ToString();
                vm.MaleHeightRange = model.HeightMaleRange.ToString();
                vm.FemaleHeightBase = model.HeightFemale.ToString();
                vm.FemaleHeightRange = model.HeightFemaleRange.ToString();

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

                if (float.TryParse(vm.MaleHeightBase, out var maleHeight))
                {
                    ha.HeightMale = maleHeight;
                }
                else
                {
                    Logger.LogError("Cannot parse male height " + vm.MaleHeightBase + " for Height Assignment: " + ha.Label);
                }

                if (float.TryParse(vm.FemaleHeightBase, out var femaleHeight))
                {
                    ha.HeightFemale = femaleHeight;
                }
                else
                {
                    Logger.LogError("Cannot parse female height " + vm.FemaleHeightBase + " for Height Assignment: " + ha.Label);
                }

                if (float.TryParse(vm.MaleHeightRange, out var maleHeightRange))
                {
                    ha.HeightMaleRange = maleHeightRange;
                }
                else
                {
                    Logger.LogError("Cannot parse male height range " + vm.MaleHeightRange + " for Height Assignment: " + ha.Label);
                }

                if (float.TryParse(vm.FemaleHeightRange, out var femaleHeightRange))
                {
                    ha.HeightFemaleRange = femaleHeightRange;
                }
                else
                {
                    Logger.LogError("Cannot parse female height range " + vm.FemaleHeightRange + " for Height Assignment: " + ha.Label);
                }

                models.Add(ha);
            }

            return models;
        }
    }
}
