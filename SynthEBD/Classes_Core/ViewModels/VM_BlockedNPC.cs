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
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Converters _converters;
    public delegate VM_BlockedNPC Factory(VM_BlockedNPCPlaceHolder associatedPlaceHolder);
    public VM_BlockedNPC(VM_BlockedNPCPlaceHolder associatedPlaceHolder, IEnvironmentStateProvider environmentProvider, Converters converters)
    {
        _environmentProvider = environmentProvider;
        _converters = converters;

        AssociatedPlaceHolder = associatedPlaceHolder;
        AssociatedPlaceHolder.AssociatedViewModel = this;

        this.WhenAnyValue(x => x.FormKey).Subscribe(x =>
        {
            if (!FormKey.IsNull)
            {
                DispName = _converters.CreateNPCDispNameFromFormKey(FormKey);
            }
        }).DisposeWith(this);

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        this.WhenAnyValue(x => x.HeadParts).Subscribe(x =>
        {
            for (int i = 0; i < HeadPartTypes.Count; i++) { HeadPartTypes[i].Block = HeadParts; }
        }).DisposeWith(this);
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

    public VM_BlockedNPCPlaceHolder AssociatedPlaceHolder { get; }

    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> NPCFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();

    public static VM_BlockedNPC CreateViewModel(VM_BlockedNPCPlaceHolder placeHolder, VM_BlockedNPC.Factory factory)
    {
        VM_BlockedNPC viewModel = factory(placeHolder);
        viewModel.FormKey = placeHolder.AssociatedModel.FormKey;
        viewModel.Assets = placeHolder.AssociatedModel.Assets;
        viewModel.Height = placeHolder.AssociatedModel.Height;
        viewModel.BodyShape = placeHolder.AssociatedModel.BodyShape;
        viewModel.HeadParts = placeHolder.AssociatedModel.HeadParts;
        foreach (var type in placeHolder.AssociatedModel.HeadPartTypes.Keys) { viewModel.HeadPartTypes.Where(x => x.Type == type).First().Block = placeHolder.AssociatedModel.HeadPartTypes[type]; }
        return viewModel;
    }

    public BlockedNPC DumpViewModelToModel()
    {
        BlockedNPC model = new BlockedNPC();
        model.FormKey = FormKey;
        model.Assets = Assets;
        model.Height = Height;
        model.BodyShape = BodyShape;
        model.HeadParts = HeadParts;
        foreach (var type in model.HeadPartTypes.Keys) { model.HeadPartTypes[type] = HeadPartTypes.Where(x => x.Type == type).First().Block; }
        return model;
    }
}

public class VM_BlockedNPCPlaceHolder: VM
{
    public delegate VM_BlockedNPCPlaceHolder Factory(BlockedNPC associatedModel);
    public VM_BlockedNPCPlaceHolder(BlockedNPC associatedModel, Converters converters)
    {
        AssociatedModel = associatedModel;
        if (associatedModel.FormKey != null && !associatedModel.FormKey.IsNull)
        {
            DispName = converters.CreateNPCDispNameFromFormKey(associatedModel.FormKey);
        }
        this.WhenAnyValue(x => x.AssociatedViewModel.DispName).Subscribe(y => DispName = y).DisposeWith(this);
    }
    public string DispName { get; set; }
    public BlockedNPC AssociatedModel { get; set; }
    public VM_BlockedNPC? AssociatedViewModel { get; set; }
}