namespace SynthEBD;

/// <summary>
/// Adapts SynthEBD's <see cref="IEnvironmentStateProvider"/> to the slimmer
/// <see cref="IDataFolderProvider"/> the viewer asset resolver needs. The
/// invalidation token is the active LinkCache instance — Mutagen replaces it
/// when the load order is rebuilt, which is exactly when downstream caches
/// (loose-file resolutions, BSA-extraction map) need to drop and re-resolve.
/// </summary>
public sealed class SynthEbdDataFolderAdapter : IDataFolderProvider
{
    private readonly IEnvironmentStateProvider _env;

    public SynthEbdDataFolderAdapter(IEnvironmentStateProvider env) => _env = env;

    public string DataFolderPath => _env.DataFolderPath;

    public object? CurrentLoadOrderToken => _env.LinkCache;
}
