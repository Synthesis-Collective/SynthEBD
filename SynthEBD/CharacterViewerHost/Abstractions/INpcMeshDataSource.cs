namespace SynthEBD;

/// <summary>
/// Resolves an <see cref="NpcIdentity"/> to a <see cref="ResolvedNpcMeshPaths"/>
/// without the cache (or other rendering-tier code) needing to know how the
/// resolution actually happens. SynthEBD's adapter wraps
/// <see cref="NpcMeshResolver"/> + the current Mutagen LinkCache; NPC2 supplies
/// its own implementation against its own record cache.
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
    /// invalidation downstream. SynthEBD returns the active <c>ILinkCache</c>.</summary>
    object? CurrentInvalidationToken { get; }
}
