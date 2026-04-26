namespace CharacterViewer.Rendering.Offscreen;

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
    /// <see cref="OpenTK.Windowing.Desktop.GameWindow"/> + FBO pipeline.
    /// All dependencies are the same set <see cref="VM_CharacterViewer"/>
    /// takes via DI; in NPC Plugin Chooser 2 these come out of the host
    /// container, in SynthEBD they come out of MainModule's existing
    /// registrations.
    ///
    /// The instance must be disposed when the host no longer needs it.
    /// One renderer instance is designed to handle many requests serially
    /// — the GL context, FBO allocation, and shared
    /// <see cref="CharacterPreviewCache"/> are amortized.
    ///
    /// <para><b>Thread:</b> must be called from the process's main thread
    /// (GLFW initializes its event hook on the first thread that touches
    /// it). Subsequent renders run on thread-pool threads internally.</para>
    /// </summary>
    public static IOffscreenRenderer Create(
        CharacterPreviewCache previewCache,
        BodySlideDeformer bodySlideDeformer,
        BsdFileParser bsdParser,
        BodyTriFileParser triParser,
        GameAssetResolver assets,
        ICharacterViewerSettings settings,
        CharacterViewerLogGate logGate,
        ICharacterViewerLogger logger)
        => new GameWindowOffscreenRenderer(
            previewCache, bodySlideDeformer, bsdParser, triParser,
            assets, settings, logGate, logger);
}
