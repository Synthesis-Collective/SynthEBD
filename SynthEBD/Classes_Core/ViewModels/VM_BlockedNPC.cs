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
    public VM_BlockedNPC()
    {
        this.WhenAnyValue(x => x.FormKey).Subscribe(x =>
        {
            if (!FormKey.IsNull)
            {
                DispName = Converters.CreateNPCDispNameFromFormKey(FormKey);
            }
        });

        PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
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

    public static VM_BlockedNPC GetViewModelFromModel(BlockedNPC model)
    {
        VM_BlockedNPC viewModel = new VM_BlockedNPC();
        viewModel.DispName = Converters.CreateNPCDispNameFromFormKey(model.FormKey);
        viewModel.FormKey = model.FormKey;
        viewModel.Assets = model.Assets;
        viewModel.Height = model.Height;
        viewModel.BodyShape = model.BodyShape;
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
        foreach (var type in model.HeadPartTypes.Keys) { model.HeadPartTypes[type] = viewModel.HeadPartTypes.Where(x => x.Type == type).First().Block; }
        return model;
    }
}