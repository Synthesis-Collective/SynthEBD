using System;
using System.Threading;

namespace SkyRender.Offscreen;

/// <summary>
/// Builds the scene for one offscreen render. The offscreen renderer owns the GL context, render
/// thread, framebuffers, readback and encoding; a scene builder owns what is drawn and where the
/// camera looks. <see cref="Build"/> runs on the renderer's render thread with its context current.
/// </summary>
public interface IOffscreenSceneBuilder
{
    /// <summary>
    /// Loads and installs everything the request describes and positions the camera. The returned
    /// scene is drawn once into the renderer's framebuffer and then disposed on the render thread.
    /// Throw <see cref="OperationCanceledException"/> to abandon a cancelled request.
    /// </summary>
    IOffscreenScene Build(OffscreenSceneContext context, OffscreenRenderRequest request);

    /// <summary>
    /// Optional GL-free warm-up (parse, decode) for a later render of the same request. Runs on a
    /// thread-pool worker with the request's asset scopes already pushed; failures are swallowed.
    /// </summary>
    void Prewarm(OffscreenRenderRequest request, CancellationToken cancellation) { }

    /// <summary>
    /// Optional: drop the builder's own CPU-side caches. Called on the render thread by
    /// <see cref="IOffscreenRenderer.InvalidateCaches"/>, after the resident GL textures are cleared
    /// and before the renderer clears its <see cref="AssetCache"/> and the resolver's resolve cache.
    /// </summary>
    void InvalidateCaches() { }
}

/// <summary>A built offscreen scene: the renderer that draws it and the camera to draw from.</summary>
public interface IOffscreenScene : IDisposable
{
    GlRenderer Renderer { get; }
    OrbitCamera Camera { get; }
}

/// <summary>Render-thread resources the offscreen renderer shares with a scene builder.</summary>
public sealed class OffscreenSceneContext
{
    public OffscreenSceneContext(int width, int height, ResidentTextureCache? residentTextures)
    {
        Width = width;
        Height = height;
        ResidentTextures = residentTextures;
    }

    /// <summary>Framebuffer size of this render, in pixels.</summary>
    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// GL texture cache shared across every render of this renderer, so the same textures upload
    /// once instead of per render. Its render pass is already open for this render. Null when the
    /// renderer could not create one.
    /// </summary>
    public ResidentTextureCache? ResidentTextures { get; }
}
