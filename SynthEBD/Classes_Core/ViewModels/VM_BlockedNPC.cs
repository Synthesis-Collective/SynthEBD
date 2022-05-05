using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.ComponentModel;
using Noggog.WPF;

namespace SynthEBD;

public class VM_BlockedNPC : ViewModel
{
    public VM_BlockedNPC()
    {
        this.PropertyChanged += TriggerDispNameUpdate;
        this.DispName = "New NPC";
        this.FormKey = new FormKey();
        this.Assets = true;
        this.Height = false;
        this.BodyShape = false;

        this.lk = PatcherEnvironmentProvider.Environment.LinkCache;
        this.NPCFormKeyTypes = typeof(INpcGetter).AsEnumerable();
    }
    // Caption
    public string DispName { get; set; }
    public FormKey FormKey { get; set; }
    public bool Assets { get; set; }
    public bool Height { get; set; }
    public bool BodyShape { get; set; }

    public ILinkCache lk { get; set; }
    public IEnumerable<Type> NPCFormKeyTypes { get; set; }

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
        viewModel.BodyShape = model.BodyShape;
        return viewModel;
    }

    public static BlockedNPC DumpViewModelToModel(VM_BlockedNPC viewModel)
    {
        BlockedNPC model = new BlockedNPC();
        model.FormKey = viewModel.FormKey;
        model.Assets = viewModel.Assets;
        model.Height = viewModel.Height;
        model.BodyShape = viewModel.BodyShape;
        return model;
    }
}