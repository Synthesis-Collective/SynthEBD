using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.ComponentModel;
using Noggog.WPF;
using ReactiveUI;

namespace SynthEBD;

public class VM_BlockedNPC : ViewModel
{
    public VM_BlockedNPC()
    {
        this.PropertyChanged += TriggerDispNameUpdate;
        
        _linkCache = PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
            .ToProperty(this, nameof(lk), default(ILinkCache))
            .DisposeWith(this);
    }
    // Caption
    public string DispName { get; set; } = "New NPC";
    public FormKey FormKey { get; set; } = new();
    public bool Assets { get; set; } = true;
    public bool Height { get; set; } = false;
    public bool BodyShape { get; set; } = false;


    private readonly ObservableAsPropertyHelper<ILinkCache> _linkCache;
    public ILinkCache lk => _linkCache.Value;
    public IEnumerable<Type> NPCFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();

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