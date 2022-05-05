using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using System.ComponentModel;

namespace SynthEBD;

public class VM_BlockedPlugin : INotifyPropertyChanged
{
    public VM_BlockedPlugin()
    {
        this.PropertyChanged += TriggerDispNameUpdate;
        this.DispName = "New Plugin";
        this.ModKey = new ModKey();
        this.Assets = true;
        this.Height = false;
        this.BodyShape = false;

        this.lk = PatcherEnvironmentProvider.Environment.LinkCache;
    }

    // Caption
    public string DispName { get; set; }
    public ModKey ModKey { get; set; }
    public bool Assets { get; set; }
    public bool Height { get; set; }
    public bool BodyShape { get; set; }

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
        viewModel.BodyShape = model.BodyShape;
        return viewModel;
    }

    public static BlockedPlugin DumpViewModelToModel(VM_BlockedPlugin viewModel)
    {
        BlockedPlugin model = new BlockedPlugin();
        model.ModKey = viewModel.ModKey;
        model.Assets = viewModel.Assets;
        model.Height = viewModel.Height;
        model.BodyShape = viewModel.BodyShape; 
        return model;
    }
}