namespace SynthEBD;

/// <summary>
/// Selects the appropriate <see cref="ISourceResolver"/> based on the user's
/// configured mod manager type. Injected into <see cref="FaceGenPatcher"/> so
/// it doesn't need to know about specific resolver implementations.
/// </summary>
public class SourceResolverProvider
{
    private readonly PatcherState _patcherState;
    private readonly MO2SourceResolver _mo2Resolver;
    private readonly VortexSourceResolver _vortexResolver;

    public SourceResolverProvider(
        PatcherState patcherState,
        MO2SourceResolver mo2Resolver,
        VortexSourceResolver vortexResolver)
    {
        _patcherState = patcherState;
        _mo2Resolver = mo2Resolver;
        _vortexResolver = vortexResolver;
    }

    /// <summary>
    /// Returns the resolver for the user's configured mod manager, or null if
    /// no supported mod manager is configured.
    /// </summary>
    public ISourceResolver GetResolver()
    {
        var modManagerType = _patcherState.ModManagerSettings?.ModManagerType ?? ModManager.None;
        return modManagerType switch
        {
            ModManager.ModOrganizer2 => _mo2Resolver,
            ModManager.Vortex => _vortexResolver,
            _ => null
        };
    }
}
