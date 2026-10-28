namespace SkyRender.Character.Offscreen;

/// <summary>
/// Constructs the offscreen renderer for NPC portraits: the core
/// <see cref="OffscreenRendererFactory"/> renderer driven by an <see cref="NpcSceneBuilder"/>.
/// </summary>
public static class NpcOffscreenRendererFactory
{
    /// <summary>Creates the default offscreen renderer — a hidden
    /// <see cref="OpenTK.Windowing.Desktop.GameWindow"/> + FBO pipeline.
    /// Takes the same dependencies a <see cref="VM_CharacterViewer"/> does, typically
    /// from the host's DI container.
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
        GameAssetResolver assets,
        ICharacterViewerSettings settings,
        RenderLogGate logGate,
        IRenderLogger logger)
        => OffscreenRendererFactory.Create(
            new NpcSceneBuilder(previewCache, assets, settings, logGate, logger),
            previewCache.Assets, assets, logGate, logger);
}
