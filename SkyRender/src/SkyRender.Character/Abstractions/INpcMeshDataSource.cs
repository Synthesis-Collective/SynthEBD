namespace SkyRender.Character;

/// <summary>
/// Resolves an <see cref="NpcIdentity"/> to a <see cref="ResolvedNpcMeshPaths"/>
/// without the cache (or other rendering-tier code) needing to know how the
/// resolution actually happens. A host implements this against its own
/// record source.
///
/// Cache invalidation: <see cref="CurrentInvalidationToken"/> is consulted by
/// <see cref="CharacterPreviewCache"/> via reference equality — when the token
/// changes the cache drops every resolved entry, since the same NPC FormKey may
/// now resolve to different mesh paths under the new load order.
/// </summary>
public interface INpcMeshDataSource
{
    ResolvedNpcMeshPaths? Resolve(NpcIdentity identity);

    /// <summary>Opaque reference-equality token; a new instance signals cache
    /// invalidation downstream, e.g. the host's current record-cache instance.</summary>
    object? CurrentInvalidationToken { get; }
}
