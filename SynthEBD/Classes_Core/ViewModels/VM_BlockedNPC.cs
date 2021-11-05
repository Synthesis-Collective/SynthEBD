using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_BlockedNPC : INotifyPropertyChanged
    {
        public VM_BlockedNPC()
        {
            this.PropertyChanged += TriggerDispNameUpdate;
            this.DispName = "New NPC";
            this.FormKey = new FormKey();
            this.Assets = true;
            this.Height = false;
            this.BodyGen = false;

            this.lk = GameEnvironmentProvider.MyEnvironment.LinkCache;
            this.NPCFormKeyTypes = typeof(INpcGetter).AsEnumerable();
        }
        // Caption
        public string DispName { get; set; }
        public FormKey FormKey { get; set; }
        public bool Assets { get; set; }
        public bool Height { get; set; }
        public bool BodyGen { get; set; }

        public ILinkCache lk { get; set; }
        public IEnumerable<Type> NPCFormKeyTypes { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public void TriggerDispNameUpdate(object sender, PropertyChangedEventArgs e)
        {
            if (this.FormKey.IsNull == false)
            {
                this.DispName = Converters.CreateNPCDispNameFromFormKey(this.FormKey);
            }
        }

        public static VM_BlockedNPC GetViewModelFromModel(BlockedNPC model)
        {
            VM_BlockedNPC viewModel = new VM_BlockedNPC();
            viewModel.DispName = Converters.CreateNPCDispNameFromFormKey(model.FormKey);
            viewModel.FormKey = model.FormKey;
            viewModel.Assets = model.Assets;
            viewModel.Height = model.Height;
            viewModel.BodyGen = model.BodyGen;
            return viewModel;
        }
    }
}
