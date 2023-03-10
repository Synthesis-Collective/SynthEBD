using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using System.ComponentModel;
using System.Collections.ObjectModel;
using Noggog;
using ReactiveUI;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Order;

namespace SynthEBD;

public class VM_BlockedPlugin : VM
{
    private IEnvironmentStateProvider _environmentProvider;
    public delegate VM_BlockedPlugin Factory(VM_BlockedPluginPlaceHolder associatedPlaceHolder);
    public VM_BlockedPlugin(VM_BlockedPluginPlaceHolder associatedPlaceHolder, IEnvironmentStateProvider environmentProvider)
    {
        _environmentProvider = environmentProvider;

        AssociatedPlaceHolder = associatedPlaceHolder;
        AssociatedPlaceHolder.AssociatedViewModel = this;

        this.WhenAnyValue(x => x.ModKey).Subscribe(x =>
        {
            if (!ModKey.IsNull)
            {
                DispName = ModKey.FileName;
            }
        }).DisposeWith(this);

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        _environmentProvider.WhenAnyValue(x => x.LoadOrder)
            .Subscribe(x => LoadOrder = x)
            .DisposeWith(this);

        this.WhenAnyValue(x => x.HeadParts).Subscribe(x =>
        {
            for (int i = 0; i < HeadPartTypes.Count; i++) { HeadPartTypes[i].Block = HeadParts; }
        }).DisposeWith(this);
    }

    // Caption
    public string DispName { get; set; } = "New Plugin";
    public ModKey ModKey { get; set; } = new();
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

    public VM_BlockedPluginPlaceHolder AssociatedPlaceHolder { get; }

    public ILinkCache lk { get; private set; }
    public ILoadOrderGetter LoadOrder { get; private set; }

    public static VM_BlockedPlugin CreateViewModel(VM_BlockedPluginPlaceHolder placeHolder, VM_BlockedPlugin.Factory factory)
    {
        VM_BlockedPlugin viewModel = factory(placeHolder);
        viewModel.DispName = placeHolder.AssociatedModel.ModKey.FileName;
        viewModel.ModKey = placeHolder.AssociatedModel.ModKey;
        viewModel.Assets = placeHolder.AssociatedModel.Assets;
        viewModel.Height = placeHolder.AssociatedModel.Height;
        viewModel.BodyShape = placeHolder.AssociatedModel.BodyShape;
        viewModel.HeadParts = placeHolder.AssociatedModel.HeadParts;
        foreach (var type in placeHolder.AssociatedModel.HeadPartTypes.Keys) 
        { 
            viewModel.HeadPartTypes.Where(x => x.Type == type).First().Block = placeHolder.AssociatedModel.HeadPartTypes[type]; 
        }
        return viewModel;
    }

    public BlockedPlugin DumpViewModelToModel()
    {
        BlockedPlugin model = new BlockedPlugin();
        model.ModKey = ModKey;
        model.Assets = Assets;
        model.Height = Height;
        model.BodyShape = BodyShape;
        model.HeadParts = HeadParts;
        foreach (var type in model.HeadPartTypes.Keys) 
        { 
            model.HeadPartTypes[type] = HeadPartTypes.Where(x => x.Type == type).First().Block; 
        }
        return model;
    }
}

public class VM_BlockedPluginPlaceHolder : VM
{
    public VM_BlockedPluginPlaceHolder(BlockedPlugin associatedModel)
    {
        AssociatedModel = associatedModel;
        DispName = associatedModel.ModKey.FileName;
        this.WhenAnyValue(x => x.AssociatedViewModel.DispName).Subscribe(y => DispName = y).DisposeWith(this);
    }
    public string DispName { get; set; }
    public BlockedPlugin AssociatedModel { get; set; }
    public VM_BlockedPlugin? AssociatedViewModel { get; set; }
}