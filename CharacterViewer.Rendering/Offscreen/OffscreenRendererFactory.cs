namespace CharacterViewer.Rendering.Offscreen;

/// <summary>
/// Constructs <see cref="IOffscreenRenderer"/> instances. Hosts call this
/// once at startup (or lazily on first mugshot request) and reuse the
/// returned renderer across many requests.
///
/// The factory exists as a separation point so future implementations
/// (e.g. a Direct3D-based renderer for environments where OpenTK's
/// <see cref="OpenTK.Windowing.Desktop.GameWindow"/> isn't appropriate)
/// can be swapped in without touching call sites.
/// </summary>
public static class OffscreenRendererFactory
{
    /// <summary>Creates the default offscreen renderer — a hidden
    /// <see cref="OpenTK.Windowing.Desktop.GameWindow"/> + FBO pipeline.
    /// The instance must be disposed when the host no longer needs it.</summary>
    public static IOffscreenRenderer Create() => new GameWindowOffscreenRenderer();
}
