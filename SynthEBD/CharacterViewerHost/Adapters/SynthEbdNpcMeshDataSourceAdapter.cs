using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>
/// Adapts SynthEBD's Mutagen-aware <see cref="NpcMeshResolver"/> to the
/// rendering tier's neutral <see cref="INpcMeshDataSource"/>. The
/// <see cref="NpcIdentity.CacheKey"/> carries a string-encoded
/// <see cref="FormKey"/>; the adapter parses it back, looks up the current
/// LinkCache from the host environment, and converts the resolver's
/// <see cref="NpcMeshResolver.NpcMeshPaths"/> into the neutral
/// <see cref="ResolvedNpcMeshPaths"/> POCO.
///
/// <see cref="CurrentInvalidationToken"/> exposes the active LinkCache as the
/// reference-equality token — when Mutagen builds a new environment the
/// CharacterPreviewCache automatically drops every entry on the next access.
/// </summary>
public sealed class SynthEbdNpcMeshDataSourceAdapter : INpcMeshDataSource
{
    private readonly NpcMeshResolver _resolver;
    private readonly IEnvironmentStateProvider _env;

    public SynthEbdNpcMeshDataSourceAdapter(NpcMeshResolver resolver, IEnvironmentStateProvider env)
    {
        _resolver = resolver;
        _env = env;
    }

    public ResolvedNpcMeshPaths? Resolve(NpcIdentity identity)
    {
        if (!FormKey.TryFactory(identity.CacheKey, out var formKey)) return null;
        var paths = _resolver.ResolveMeshPaths(formKey, _env.LinkCache);
        return paths == null ? null : Convert(paths);
    }

    public object? CurrentInvalidationToken => _env.LinkCache;

    /// <summary>Phase A glue: copies field-by-field from the resolver's still-Mutagen-coupled
    /// inner POCO into the neutral one. Phase C will fold this into <see cref="NpcMeshResolver"/>
    /// itself once it lives in the Skyrim tier and produces <see cref="ResolvedNpcMeshPaths"/>
    /// directly.</summary>
    internal static ResolvedNpcMeshPaths Convert(NpcMeshResolver.NpcMeshPaths src) =>
        new()
        {
            BodyMeshPath = src.BodyMeshPath,
            HandsMeshPath = src.HandsMeshPath,
            FeetMeshPath = src.FeetMeshPath,
            HeadMeshPath = src.HeadMeshPath,
            Gender = src.Gender,
            SkeletonPath = src.SkeletonPath,
            ResolutionChains = src.ResolutionChains,
            TxstTextures = src.TxstTextures,
            FaceTintPath = src.FaceTintPath,
            TextureLightingColor = src.TextureLightingColor,
        };
}
