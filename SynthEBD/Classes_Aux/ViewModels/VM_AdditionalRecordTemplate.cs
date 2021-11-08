using Mutagen.Bethesda.Cache.Implementations;
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
    public class VM_AdditionalRecordTemplate : INotifyPropertyChanged
    {
        public VM_AdditionalRecordTemplate(ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, ObservableCollection<VM_AdditionalRecordTemplate> parentCollection)
        {
            this.RaceFormKeys = new ObservableCollection<FormKey>();
            this.TemplateNPC = new FormKey();
            this.RecordTemplateLinkCache = recordTemplateLinkCache;
            this.NPCFormKeyTypes = typeof(INpcGetter).AsEnumerable();
            this.lk = GameEnvironmentProvider.MyEnvironment.LinkCache;
            this.RacePickerFormKeys = typeof(IRaceGetter).AsEnumerable();
            this.ParentCollection = parentCollection;

            DeleteCommand = new SynthEBD.RelayCommand(
                    canExecute: _ => true,
                    execute: _ => { this.ParentCollection.Remove(this); }
                    );
        }

        public static AdditionalRecordTemplate DumpViewModelToModel(VM_AdditionalRecordTemplate viewModel)
        {
            return new AdditionalRecordTemplate() { Races = viewModel.RaceFormKeys.ToHashSet(), TemplateNPC = viewModel.TemplateNPC };
        }

        public ObservableCollection<FormKey> RaceFormKeys { get; set; }

        public ILinkCache lk { get; set; }
        public IEnumerable<Type> RacePickerFormKeys { get; set; }

        public FormKey TemplateNPC { get; set; }

        public IEnumerable<Type> NPCFormKeyTypes { get; set; }

        public ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> RecordTemplateLinkCache { get; set; }

        public ObservableCollection<VM_AdditionalRecordTemplate> ParentCollection { get; set; }

        public RelayCommand DeleteCommand { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
