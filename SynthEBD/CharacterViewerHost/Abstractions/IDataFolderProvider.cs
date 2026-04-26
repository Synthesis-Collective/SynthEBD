namespace SynthEBD;

/// <summary>
/// Minimal slice of the host environment that <see cref="GameAssetResolver"/>
/// needs to resolve game-relative paths to disk: the data folder root, plus an
/// opaque token whose reference identity changes when the load order is
/// rebuilt so caches can invalidate.
///
/// SynthEBD adapts <see cref="IEnvironmentStateProvider"/> behind this
/// interface (see <see cref="SynthEbdDataFolderAdapter"/>).
/// </summary>
public interface IDataFolderProvider
{
    /// <summary>Absolute path to the game's Data folder.</summary>
    string DataFolderPath { get; }

    /// <summary>Opaque reference-equality token for cache invalidation. When the
    /// underlying load order is rebuilt this returns a new instance, signaling
    /// downstream caches that previously-resolved asset paths may now resolve
    /// differently.</summary>
    object? CurrentLoadOrderToken { get; }
}
