using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SynthEBD
{
    public class VM_raceAlias : INotifyPropertyChanged
    {
        public VM_raceAlias(RaceAlias alias, IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> env, VM_Settings_General parentVM)
        {
            this.race = alias.race;
            this.aliasRace = alias.aliasRace;
            this.bMale = alias.bMale;
            this.bFemale = alias.bFemale;
            this.bApplyToAssets = alias.bApplyToAssets;
            this.bApplyToBodyGen = alias.bApplyToBodyGen;
            this.bApplyToHeight = alias.bApplyToHeight;
            this.FormKeyPickerTypes = typeof(IRaceGetter).AsEnumerable();
            this.lk = env.LinkCache;

            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentVM.raceAliases.Remove(this));
        }

        public FormKey race { get; set; }
        public FormKey aliasRace { get; set; }
        public bool bMale { get; set; }
        public bool bFemale { get; set; }

        public bool bApplyToAssets { get; set; }
        public bool bApplyToBodyGen { get; set; }
        public bool bApplyToHeight { get; set; }

        public IEnumerable<Type> FormKeyPickerTypes { get; set; }

        public ILinkCache lk { get; set; }

        public VM_Settings_General ParentVM { get; set; }

        public RelayCommand DeleteCommand { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static ObservableCollection<VM_raceAlias> GetViewModelsFromModels(List<RaceAlias> models, IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> env, VM_Settings_General parentVM)
        {
            var RAVM = new ObservableCollection<VM_raceAlias>();

            foreach (var x in models)
            {
                var y = new VM_raceAlias(x, env, parentVM);
                RAVM.Add(y);
            }

            return RAVM;
        }

        public static RaceAlias DumpViewModelToModel(VM_raceAlias viewModel)
        {
            RaceAlias model = new RaceAlias();
            model.race = viewModel.race;
            model.aliasRace = viewModel.aliasRace;
            model.bMale = viewModel.bMale;
            model.bFemale = viewModel.bFemale;
            model.bApplyToAssets = viewModel.bApplyToAssets;
            model.bApplyToBodyGen = viewModel.bApplyToBodyGen;
            model.bApplyToHeight = viewModel.bApplyToHeight;

            return model;
        }
    }
}
