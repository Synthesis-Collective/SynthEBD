using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using System.ComponentModel;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

public class VM_BlockedPlugin : VM
{
    public VM_BlockedPlugin()
    {
        this.PropertyChanged += TriggerDispNameUpdate;
        
        PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
    }

    // Caption
    public string DispName { get; set; } = "New Plugin";
    public ModKey ModKey { get; set; } = new();
    public bool Assets { get; set; } = true;
    public bool Height { get; set; } = false;
    public bool BodyShape { get; set; } = false;

    public ILinkCache lk { get; private set; }

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