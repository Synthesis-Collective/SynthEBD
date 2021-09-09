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

namespace SynthEBD.Internal_Data_Classes.ViewModels
{
    public class VM_raceAlias : INotifyPropertyChanged
    {
        public VM_raceAlias(raceAlias alias, IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> env)
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

        public event PropertyChangedEventHandler PropertyChanged;

        public static ObservableCollection<VM_raceAlias> GetViewModelsFromModels(List<raceAlias> models, IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> env)
        {
            var RAVM = new ObservableCollection<VM_raceAlias>();

            foreach (var x in models)
            {
                var y = new VM_raceAlias(x, env);
                RAVM.Add(y);
            }

            return RAVM;
        }

        public static raceAlias DumpViewModelToModel(VM_raceAlias viewModel)
        {
            raceAlias model = new raceAlias();
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
