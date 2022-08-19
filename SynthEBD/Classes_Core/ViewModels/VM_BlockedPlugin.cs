using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using System.ComponentModel;
using Noggog;
using ReactiveUI;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class VM_BlockedPlugin : VM
{
    public VM_BlockedPlugin()
    {
        this.WhenAnyValue(x => x.ModKey).Subscribe(x =>
        {
            if (!ModKey.IsNull)
            {
                DispName = ModKey.FileName;
            }
        });

        PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LoadOrder)
            .Subscribe(x => LoadOrder = x)
            .DisposeWith(this);

        this.WhenAnyValue(x => x.HeadParts).Subscribe(x =>
        {
            if (HeadParts)
            {
                HeadPartsMisc = true;
                HeadPartsBeard = true;
                HeadPartsBrows = true;
                HeadPartsEyes = true;
                HeadPartsScars = true;
                HeadPartsHair = true;
                HeadPartsFace = true;
            }
            else
            {
                HeadPartsMisc = false;
                HeadPartsBeard = false;
                HeadPartsBrows = false;
                HeadPartsEyes = false;
                HeadPartsScars = false;
                HeadPartsHair = false;
                HeadPartsFace = false;
            }
        });
    }

    // Caption
    public string DispName { get; set; } = "New Plugin";
    public ModKey ModKey { get; set; } = new();
    public bool Assets { get; set; } = true;
    public bool Height { get; set; } = false;
    public bool BodyShape { get; set; } = false;
    public bool HeadParts { get; set; } = false;
    public bool HeadPartsMisc { get; set; } = false;
    public bool HeadPartsFace { get; set; } = false;
    public bool HeadPartsEyes { get; set; } = false;
    public bool HeadPartsBeard { get; set; } = false;
    public bool HeadPartsScars { get; set; } = false;
    public bool HeadPartsBrows { get; set; } = false;
    public bool HeadPartsHair { get; set; } = false;

    public ILinkCache lk { get; private set; }
    public Mutagen.Bethesda.Plugins.Order.ILoadOrder<Mutagen.Bethesda.Plugins.Order.IModListing<ISkyrimModGetter>> LoadOrder { get; private set; }

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