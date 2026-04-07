namespace SynthEBD;

/// <summary>
/// Abstracts mod-manager-specific file priority resolution. Given a relative Data path,
/// returns the absolute path of the highest-priority file that provides that asset,
/// excluding the SynthEBD output directory.
///
/// Used by <see cref="FaceGenPatcher"/> to resolve the true upstream FaceGen NIF when
/// a re-run detects its own previous output via the metadata tag.
/// </summary>
public interface ISourceResolver
{
    /// <summary>
    /// Whether this resolver is configured and ready to use.
    /// Returns false if the user hasn't set up their mod manager path in settings.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Initializes the resolver at patcher startup (e.g., parsing modlist.txt).
    /// Call once before the FaceGen patching loop begins.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Attempts to resolve the true upstream source for a given relative Data path,
    /// excluding the SynthEBD output directory.
    /// </summary>
    /// <param name="relativeDataPath">
    ///   The path relative to the game's Data folder
    ///   (e.g., "meshes\actors\character\facegendata\facegeom\Skyrim.esm\00013BBE.nif").
    /// </param>
    /// <param name="absolutePath">
    ///   On success, the absolute filesystem path to the highest-priority non-SynthEBD
    ///   file providing this asset. Null on failure.
    /// </param>
    /// <param name="verbose">
    ///   When true and debug logging is enabled, logs each candidate path checked
    ///   during resolution. Callers gate this per-NPC via a FormKey debug set.
    /// </param>
    /// <returns>True if a source was found; false otherwise.</returns>
    bool TryResolve(string relativeDataPath, out string absolutePath, bool verbose = false);
}
