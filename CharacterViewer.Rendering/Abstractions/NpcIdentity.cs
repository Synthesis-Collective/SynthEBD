namespace CharacterViewer.Rendering;

/// <summary>
/// Opaque NPC handle that the CharacterViewer subsystem uses to key its caches
/// and labels its status text. <see cref="CacheKey"/> must be stable and unique
/// per NPC within a host process; the Skyrim adapter encodes a Mutagen FormKey,
/// other hosts can supply any deterministic string.
/// </summary>
public readonly record struct NpcIdentity(string CacheKey, string DisplayLabel);
