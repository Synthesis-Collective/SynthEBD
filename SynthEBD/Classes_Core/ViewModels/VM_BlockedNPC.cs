using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.ComponentModel;
using ReactiveUI;
using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_BlockedNPC : VM
{
    private readonly IEnvironmentStateProvider _stateProvider;
    private readonly Converters _converters;
    public delegate VM_BlockedNPC Factory();
    public VM_BlockedNPC(IEnvironmentStateProvider stateProvider, Converters converters)
    {
        _stateProvider = stateProvider;
        _converters = converters;
        this.WhenAnyValue(x => x.FormKey).Subscribe(x =>
        {
            if (!FormKey.IsNull)
            {
                DispName = _converters.CreateNPCDispNameFromFormKey(FormKey);
            }
        });

        _stateProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        this.WhenAnyValue(x => x.HeadParts).Subscribe(x =>
        {
            for (int i = 0; i < HeadPartTypes.Count; i++) { HeadPartTypes[i].Block = HeadParts; }
        });
    }
    // Caption
    public string DispName { get; set; } = "New NPC";
    public FormKey FormKey { get; set; } = new();
    public bool Assets { get; set; } = true;
    public bool Height { get; set; } = false;
    public bool BodyShape { get; set; } = false;
    public bool HeadParts { get; set; } = false;
    public ObservableCollection<VM_HeadPartBlock> HeadPartTypes { get; set; } = new()
    {
        new(HeadPart.TypeEnum.Eyebrows, false),
        new(HeadPart.TypeEnum.Eyes, false),
        new(HeadPart.TypeEnum.Face, false),
        new(HeadPart.TypeEnum.FacialHair, false),
        new(HeadPart.TypeEnum.Hair, false),
        new(HeadPart.TypeEnum.Misc, false),
        new(HeadPart.TypeEnum.Scars, false)
    };

    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> NPCFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();

    public static VM_BlockedNPC GetViewModelFromModel(BlockedNPC model, VM_BlockedNPC.Factory factory)
    {
        VM_BlockedNPC viewModel = factory();
        //viewModel.DispName = CreateNPCDispNameFromFormKey(model.FormKey, converters);
        viewModel.FormKey = model.FormKey;
        viewModel.Assets = model.Assets;
        viewModel.Height = model.Height;
        viewModel.BodyShape = model.BodyShape;
        viewModel.HeadParts = model.HeadParts;
        foreach (var type in model.HeadPartTypes.Keys) { viewModel.HeadPartTypes.Where(x => x.Type == type).First().Block = model.HeadPartTypes[type]; }
        return viewModel;
    }

    public static BlockedNPC DumpViewModelToModel(VM_BlockedNPC viewModel)
    {
        BlockedNPC model = new BlockedNPC();
        model.FormKey = viewModel.FormKey;
        model.Assets = viewModel.Assets;
        model.Height = viewModel.Height;
        model.BodyShape = viewModel.BodyShape;
        model.HeadParts = viewModel.HeadParts;
        foreach (var type in model.HeadPartTypes.Keys) { model.HeadPartTypes[type] = viewModel.HeadPartTypes.Where(x => x.Type == type).First().Block; }
        return model;
    }
}