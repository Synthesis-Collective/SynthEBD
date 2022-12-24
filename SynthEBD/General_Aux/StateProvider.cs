using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using System.Reflection;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis;
using Noggog;

namespace SynthEBD;

public interface IStateProvider
{
    ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> LoadOrder { get; }
    ILinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache { get; }
    DirectoryPath ExtraSettingsDataPath { get; }
    DirectoryPath InternalDataPath { get; }
}

public interface IOutputStateProvider : IStateProvider
{
    ISkyrimMod OutputMod { get; }
}

public class StandaloneRunStateProvider : IOutputStateProvider
{
    private readonly IGameEnvironment<ISkyrimMod, ISkyrimModGetter> _environment;
    public ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> LoadOrder => _environment.LoadOrder;
    public ILinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache => _environment.LinkCache;
    public GameRelease GameRelease => _environment.GameRelease;
    public DirectoryPath ExtraSettingsDataPath { get; set; }
    public DirectoryPath InternalDataPath { get; set; }
    public ISkyrimMod OutputMod { get; }

    public StandaloneRunStateProvider()
    {
        string? exeLocation = null;
        var assembly = Assembly.GetEntryAssembly();
        if (assembly != null)
        {
            exeLocation = System.IO.Path.GetDirectoryName(assembly.Location);
        }

        ExtraSettingsDataPath = exeLocation ?? throw new Exception("Could not locate running assembly");
        InternalDataPath = System.IO.Path.Combine(ExtraSettingsDataPath, "InternalData");

        OutputMod = new SkyrimMod(ModKey.FromName("HunterBornExtender", ModType.Plugin), SkyrimRelease.SkyrimSE);

        var builder = GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(GameRelease.SkyrimSE);
        _environment = builder
            .TransformModListings(x =>
                x.OnlyEnabledAndExisting())
            .WithOutputMod(OutputMod)
            .Build();
    }
}

public class OpenForSettingsWrapper : IStateProvider
{
    private readonly IOpenForSettingsState _state;
    private readonly Lazy<IGameEnvironment<ISkyrimMod, ISkyrimModGetter>> _env;

    public OpenForSettingsWrapper(IOpenForSettingsState state)
    {
        _state = state;
        _env = new Lazy<IGameEnvironment<ISkyrimMod, ISkyrimModGetter>>(
            () => state.GetEnvironmentState<ISkyrimMod, ISkyrimModGetter>());
    }

    public ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> LoadOrder => _env.Value.LoadOrder;
    public ILinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache => _env.Value.LinkCache;
    public DirectoryPath ExtraSettingsDataPath => _state.ExtraSettingsDataPath ?? throw new Exception("Could not locate Extra Settings Data Path");
    public DirectoryPath InternalDataPath => _state.InternalDataPath ?? throw new Exception("Could not locate Extra Settings Data Path");
}
