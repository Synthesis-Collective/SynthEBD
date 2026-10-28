namespace SkyRender.Offscreen;

/// <summary>
/// Constructs <see cref="IOffscreenRenderer"/> instances. Hosts call this
/// once at startup (or lazily on first mugshot request) and reuse the
/// returned renderer across many requests.
///
/// The factory exists as a separation point so future implementations
/// (e.g. a Direct3D-based renderer) can be swapped in without touching
/// call sites.
/// </summary>
public static class OffscreenRendererFactory
{
    /// <summary>Creates the default offscreen renderer — a hidden
    /// <see cref="OpenTK.Windowing.Desktop.GameWindow"/> + FBO pipeline — around a host-supplied
    /// <see cref="IOffscreenSceneBuilder"/>, which decides what each render draws.
    ///
    /// The instance must be disposed when the host no longer needs it.
    /// One renderer instance is designed to handle many requests serially
    /// — the GL context, FBO allocation, and shared <see cref="AssetCache"/>
    /// are amortized.
    ///
    /// <para><b>Thread:</b> must be called from the process's main thread
    /// (GLFW initializes its event hook on the first thread that touches
    /// it). Subsequent renders run on thread-pool threads internally.</para>
    /// </summary>
    public static IOffscreenRenderer Create(
        IOffscreenSceneBuilder sceneBuilder,
        AssetCache assetCache,
        GameAssetResolver assets,
        RenderLogGate logGate,
        IRenderLogger logger)
        => new GameWindowOffscreenRenderer(sceneBuilder, assetCache, assets, logGate, logger);
}
