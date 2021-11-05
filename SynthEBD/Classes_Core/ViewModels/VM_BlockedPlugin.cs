using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_BlockedPlugin : INotifyPropertyChanged
    {
        public VM_BlockedPlugin()
        {
            this.PropertyChanged += TriggerDispNameUpdate;
            this.DispName = "New Plugin";
            this.ModKey = new ModKey();
            this.Assets = true;
            this.Height = false;
            this.BodyGen = false;

            this.lk = GameEnvironmentProvider.MyEnvironment.LinkCache;
        }

        // Caption
        public string DispName { get; set; }
        public ModKey ModKey { get; set; }
        public bool Assets { get; set; }
        public bool Height { get; set; }
        public bool BodyGen { get; set; }

        public ILinkCache lk { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;

        public void TriggerDispNameUpdate(object sender, PropertyChangedEventArgs e)
        {
            if (this.ModKey.IsNull == false)
            {
                this.DispName = this.ModKey.FileName;
            }
        }

        public static VM_BlockedPlugin GetViewModelFromModel(BlockedPlugin model)
        {
            VM_BlockedPlugin viewModel = new VM_BlockedPlugin();
            viewModel.DispName = model.ModKey.FileName;
            viewModel.ModKey = model.ModKey;
            viewModel.Assets = model.Assets;
            viewModel.Height = model.Height;
            viewModel.BodyGen = model.BodyGen;
            return viewModel;
        }
    }
}
