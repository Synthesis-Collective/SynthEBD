namespace SkyRender.Character;

/// <summary>
/// Opaque NPC handle that the CharacterViewer subsystem uses to key its caches
/// and labels its status text. <see cref="CacheKey"/> must be stable and unique
/// per NPC within a host process, e.g. a plugin-qualified form ID or any
/// other deterministic string.
/// </summary>
public readonly record struct NpcIdentity(string CacheKey, string DisplayLabel);
