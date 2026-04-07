namespace SynthEBD;

/// <summary>
/// Placeholder Vortex implementation of <see cref="ISourceResolver"/>.
///
/// Vortex uses hardlinks into the Data directory and maintains a deployment manifest
/// (vortex.deployment.json or equivalent) mapping deployed files back to source mods.
/// The manifest location and format need to be determined before this can be implemented.
///
/// For now, this always returns unavailable, causing FaceGenPatcher to fall back to
/// the abort-and-notify path for Vortex users on re-runs.
/// </summary>
public class VortexSourceResolver : ISourceResolver
{
    private readonly Logger _logger;

    public bool IsAvailable => false;

    public VortexSourceResolver(Logger logger)
    {
        _logger = logger;
    }

    public void Initialize()
    {
        _logger.LogMessage("VortexSourceResolver: Vortex source resolution is not yet implemented. " +
                           "Vortex users must manually delete the SynthEBD output FaceGen directory before re-running.");
    }

    public bool TryResolve(string relativeDataPath, out string absolutePath, bool verbose = false)
    {
        absolutePath = null;
        return false;
    }
}
